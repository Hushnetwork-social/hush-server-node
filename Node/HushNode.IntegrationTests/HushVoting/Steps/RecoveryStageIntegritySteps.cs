using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-053 -> Phase 3 Tasks 3.5/3.6,
// Phase 7 Tasks 7.1/7.2. Web device-password staging, no native qualification.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryStageIntegritySteps(HushVotingScenario scenario, RecoveryProfileSteps recovery,
    RecoveryProtectionSteps protection, AuthenticationSteps authentication, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;

    [Given("Alice selects her real registered recovery candidate for atomic password protected staging")]
    public async Task ReadyAsync()
    {
        await protection.ReadyAsync();
    }

    [When("recovery waits for its actual IndexedDB read back before committing the selected keys")]
    public async Task HoldReadBackAsync()
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
                                    event.stopImmediatePropagation(); held = true;
                                    const callback = tx.oncomplete;
                                    release = () => { callback?.call(tx, event); release = null; };
                                }
                            });
                        }
                        return request;
                    };
                    globalThis.hvRecoveryStageHeld = () => written && held && release !== null && switched === 0;
                    globalThis.hvRecoveryStageCommitted = () => held && release === null && switched === 1;
                    globalThis.hvReleaseRecoveryStage = () => { release?.(); return true; };
                    globalThis.hvRestoreRecoveryStage = () => {
                        IDBObjectStore.prototype.get = get; IDBObjectStore.prototype.put = put;
                        release?.(); return true;
                    };
                    return true;
                })()
                """)).Should().BeTrue();
            await SubmitProtectionAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await worker.EvaluateBooleanAsync("globalThis.hvRecoveryStageHeld()")) await Task.Delay(25, deadline.Token);
            await NoAccessAsync();
            (await JournalAbsentAsync()).Should().BeTrue();
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
            (await worker.EvaluateBooleanAsync("globalThis.hvReleaseRecoveryStage()")).Should().BeTrue();
            await Expect(Page.Locator("#rw-quarantine")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            (await worker.EvaluateBooleanAsync("globalThis.hvRecoveryStageCommitted()")).Should().BeTrue();
            scenario.Faults.RejectedIdentityQueries.Should().BeGreaterThan(0);
            var stored = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
            stored.KeysMatch.Should().BeTrue();
            stored.ConcreteKeysOnly.Should().BeTrue();
            stored.MetadataMatches.Should().BeTrue();
            stored.NetworkMatches.Should().BeTrue();
            stored.DevicePasswordProtected.Should().BeTrue();
            stored.PendingRegistration.Should().BeTrue();
            stored.Active.Should().BeFalse();
            // First staging has one slot. The inspector above proves its keys;
            // the all-retained-slots helper additionally requires Active.
            (await Page.EvaluateAsync<int>("""
                async () => {
                    const db = await new Promise((resolve, reject) => { const r = indexedDB.open('hushvoting-vault'); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Slot inspection failed')); });
                    try { return await new Promise((resolve, reject) => {
                        const r = db.transaction('vaultSlots').objectStore('vaultSlots').count();
                        r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Slot inspection failed'));
                    }); } finally { db.close(); }
                }
                """)).Should().Be(1);
        }
        finally { await worker.EvaluateBooleanAsync("globalThis.hvRestoreRecoveryStage?.() === true"); }
    }

    [Then("only the selected encrypted recovery keys survive and fresh node verification activates them")]
    public async Task ActivateAsync()
    {
        await protection.OnlineAsync();
        var stored = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.Active.Should().BeTrue();
        stored.KeysMatch.Should().BeTrue();
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
    }

    [When("Alice restores again and the actual recovery journal transaction aborts")]
    public async Task AbortCommitAsync()
    {
        await authentication.LockAsync();
        await authentication.RemoveAsync();
        await authentication.StorageRemovedAsync();
        await recovery.RestoreAsync();
        await recovery.ConfirmAsync();
        var queries = scenario.Faults.IdentityQueryCount;
        await using var worker = await HushVotingWorkerProbe.AttachAsync(scenario);
        try
        {
            (await worker.EvaluateBooleanAsync("""
                (() => {
                    const put = IDBObjectStore.prototype.put, remove = Map.prototype.delete;
                    let aborted = false, discarded = 0;
                    Map.prototype.delete = function(key) {
                        if (aborted && this.get(key)?.kind === 'words') discarded++;
                        return Reflect.apply(remove, this, [key]);
                    };
                    IDBObjectStore.prototype.put = function(...args) {
                        const request = Reflect.apply(put, this, args);
                        if (this.name === 'vaultJournal' && args[1] === 'current' && !aborted) {
                            aborted = true; this.transaction.abort();
                        }
                        return request;
                    };
                    globalThis.hvRecoveryStageAborted = () => aborted;
                    globalThis.hvRecoveryFailureDiscarded = () => aborted && discarded === 1;
                    globalThis.hvRestoreRecoveryPut = () => { IDBObjectStore.prototype.put = put; Map.prototype.delete = remove; return true; };
                    return true;
                })()
                """)).Should().BeTrue();
            await SubmitProtectionAsync();
            // Worker Lock revokes the child and the existing root rereads custody.
            // The aborted write's slot residue must remain blocked, never first-run.
            await Expect(Page.Locator(".error-surface")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Create User|^Restore Recovery Words") })).ToHaveCountAsync(0);
            (await worker.EvaluateBooleanAsync("globalThis.hvRecoveryStageAborted()")).Should().BeTrue();
            (await worker.EvaluateBooleanAsync("globalThis.hvRecoveryFailureDiscarded()")).Should().BeTrue("failed staging must discard the selected recovery material before reporting failure");
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
        }
        finally { await worker.EvaluateBooleanAsync("globalThis.hvRestoreRecoveryPut?.() === true"); }
    }

    [Then("an aborted recovery commit leaves no active journal and cannot start online activation")]
    public async Task RejectedAsync()
    {
        (await JournalAbsentAsync()).Should().BeTrue();
        await NoAccessAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }

    private async Task SubmitProtectionAsync()
    {
        await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
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
