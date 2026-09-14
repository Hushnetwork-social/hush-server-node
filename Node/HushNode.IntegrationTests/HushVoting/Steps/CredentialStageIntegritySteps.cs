using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-009 AC-009-057 -> Phase 3 Tasks 3.7/3.8,
// Phase 7 Tasks 7.1/7.2. Shared journal: FEAT-004 Task 3.6.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialStageIntegritySteps(HushVotingScenario scenario, CredentialFileSteps file,
    CredentialProtectionSteps protection, AuthenticationSteps authentication)
{
    private IPage Page => scenario.Page;

    [Given("Alice imports her real registered source and reaches the atomic staging boundary")]
    public async Task ReadyAsync()
    {
        await file.SourceWithWordsAsync();
        await file.DecryptAsync();
        await file.ImportedAsync();
    }

    [When("the worker waits for the actual encrypted slot read back before switching its journal")]
    public async Task HeldReadBackAsync()
    {
        var queries = scenario.Faults.IdentityQueryCount;
        scenario.Faults.IdentityUnavailable = true;
        await using var worker = await HushVotingWorkerProbe.AttachAsync(scenario);
        try
        {
            (await worker.EvaluateBooleanAsync("""
                (() => {
                    const get = IDBObjectStore.prototype.get, put = IDBObjectStore.prototype.put;
                    let written = false, held = false, release = null, switched = 0;
                    IDBObjectStore.prototype.put = function(...args) {
                        if (this.name === 'vaultSlots') written = true;
                        if (this.name === 'vaultJournal' && args[1] === 'current') switched++;
                        return Reflect.apply(put, this, args);
                    };
                    IDBObjectStore.prototype.get = function(...args) {
                        const request = Reflect.apply(get, this, args);
                        if (written && this.name === 'vaultSlots') {
                            const tx = this.transaction;
                            tx.addEventListener('complete', event => {
                                if (!held) {
                                    event.stopImmediatePropagation();
                                    held = true;
                                    const callback = tx.oncomplete;
                                    release = () => { callback?.call(tx, event); release = null; };
                                }
                            });
                        }
                        return request;
                    };
                    globalThis.hvStageHeld = () => written && held && release !== null && switched === 0;
                    globalThis.hvStageCommittedOnce = () => held && release === null && switched === 1;
                    globalThis.hvReleaseStageRead = () => { release?.(); return true; };
                    globalThis.hvRestoreStageRead = () => {
                        IDBObjectStore.prototype.get = get; IDBObjectStore.prototype.put = put;
                        release?.(); return true;
                    };
                    return true;
                })()
                """)).Should().BeTrue();
            await SubmitProtectionAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await worker.EvaluateBooleanAsync("globalThis.hvStageHeld()")) await Task.Delay(25, deadline.Token);
            await NoAccessAsync();
            (await JournalAbsentAsync()).Should().BeTrue();
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
            (await worker.EvaluateBooleanAsync("globalThis.hvReleaseStageRead()")).Should().BeTrue();
            await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToBeVisibleAsync(new() { Timeout = 30_000 });
            (await worker.EvaluateBooleanAsync("globalThis.hvStageCommittedOnce()")).Should().BeTrue();
            scenario.Faults.RejectedIdentityQueries.Should().BeGreaterThan(0);
            await protection.PendingVaultAsync();
        }
        finally { await worker.EvaluateBooleanAsync("globalThis.hvRestoreStageRead?.() === true"); }
    }

    [Then("the exact encrypted stage stays pending until fresh real node verification activates it")]
    public async Task VerifiedStageAsync()
    {
        await protection.OnlineRequiredAsync();
        await protection.DefaultPasswordVerifiedAsync();
        await file.VerifySourceBytesAsync();
    }

    [When("Alice restores again and ciphertext changes during the worker's actual IndexedDB write")]
    public async Task ChangedCiphertextAsync()
    {
        await authentication.LockAsync();
        await authentication.RemoveAsync();
        await authentication.StorageRemovedAsync();
        await file.OpenAsync();
        await file.DecryptAsync();
        await file.ImportedWithTransactionCountAsync(2);
        var queries = scenario.Faults.IdentityQueryCount;
        await using var worker = await HushVotingWorkerProbe.AttachAsync(scenario);
        try
        {
            (await worker.EvaluateBooleanAsync("""
                (() => {
                    const put = IDBObjectStore.prototype.put;
                    let changed = false, switched = 0;
                    IDBObjectStore.prototype.put = function(...args) {
                        if (this.name === 'vaultSlots' && !changed) {
                            const slot = structuredClone(args[0]);
                            const envelope = JSON.parse(new TextDecoder().decode(slot.bytes));
                            const record = envelope.records.ordinary;
                            record.ciphertext = (record.ciphertext[0] === 'A' ? 'B' : 'A') + record.ciphertext.slice(1);
                            slot.bytes = new TextEncoder().encode(JSON.stringify(envelope));
                            args[0] = slot;
                            changed = true;
                        }
                        if (this.name === 'vaultJournal' && args[1] === 'current') switched++;
                        return Reflect.apply(put, this, args);
                    };
                    globalThis.hvStageCorruptionRejected = () => changed && switched === 0;
                    globalThis.hvRestoreStagePut = () => { IDBObjectStore.prototype.put = put; return true; };
                    return true;
                })()
                """)).Should().BeTrue();
            await SubmitProtectionAsync();
            await Expect(Page.GetByTestId("restore-error")).ToHaveTextAsync("Something went wrong; please try again.", new() { Timeout = 30_000 });
            (await worker.EvaluateBooleanAsync("globalThis.hvStageCorruptionRejected()")).Should().BeTrue();
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
        }
        finally { await worker.EvaluateBooleanAsync("globalThis.hvRestoreStagePut?.() === true"); }
    }

    [Then("changed staged bytes cannot commit a journal or initiate online activation")]
    public async Task RejectedAsync()
    {
        (await JournalAbsentAsync()).Should().BeTrue();
        await NoAccessAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }

    private async Task SubmitProtectionAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password"), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password-confirmation"), HushVotingScenario.DevicePassword);
        await Page.GetByTestId("submit-protection").ClickAsync();
    }

    private async Task NoAccessAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
    }

    private async Task<bool> JournalAbsentAsync() => await Page.EvaluateAsync<bool>("""
        async () => {
            const db = await new Promise((resolve, reject) => { const r = indexedDB.open('hushvoting-vault'); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Journal inspection failed')); });
            try { return await new Promise((resolve, reject) => {
                const request = db.transaction('vaultJournal').objectStore('vaultJournal').get('current');
                request.onsuccess = () => resolve(request.result === undefined);
                request.onerror = () => reject(new Error('Journal inspection failed'));
            }); } finally { db.close(); }
        }
        """);
}
