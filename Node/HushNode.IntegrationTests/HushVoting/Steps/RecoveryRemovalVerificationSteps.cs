using FluentAssertions;
using System.Text.Json;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-068 -> Phase 3 Tasks 3.9/3.10,
// Phase 7 Tasks 7.1/7.2. Interrupted-resume consent remains FEAT-022.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryRemovalVerificationSteps(HushVotingScenario scenario, RecoveryProfileSteps recovery,
    AuthenticationSteps authentication, HushVotingIdentityJourney identity, RecoveryWordEntrySteps entry)
{
    private IPage Page => scenario.Page;

    [Given("Alice has a restored and locked identity with a real vault and owned journal residue")]
    public async Task ReadyAsync()
    {
        await recovery.RegisteredAsync();
        await recovery.RestoreAsync();
        await recovery.ConfirmAsync();
        await recovery.ProtectAsync();
        await authentication.LockAsync();
        await authentication.SafeLockedPreviewAsync();
        // A resolved licence can already have cleared its pending journal.
        // Seed only opaque test-owned residue in its three managed keys so
        // removal must actually delete them; this is not an active licence.
        await Page.EvaluateAsync("""
            async () => {
                const db = await new Promise((resolve, reject) => { const r = indexedDB.open('hushvoting-vault'); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Owned residue setup failed')); });
                try { await new Promise((resolve, reject) => {
                    const tx = db.transaction('licenceJournal', 'readwrite');
                    for (const key of ['slot-a','slot-b','pointer']) tx.objectStore('licenceJournal').put({ ownedCleanupResidue: true }, key);
                    tx.oncomplete = resolve; tx.onerror = () => reject(new Error('Owned residue setup failed'));
                }); } finally { db.close(); }
            }
            """);
        var counts = await StoreCountsAsync();
        counts["vaultSlots"].Should().BeGreaterThan(0);
        counts["vaultJournal"].Should().BeGreaterThan(0);
        counts["licenceJournal"].Should().BeGreaterThan(0);
    }

    [When("confirmed removal waits for the real final storage absence acknowledgement")]
    public async Task RemoveWithBarrierAsync()
    {
        await using var worker = await HushVotingWorkerProbe.AttachAsync(scenario);
        try
        {
            (await worker.EvaluateBooleanAsync("""
                (() => {
                    const get = IDBObjectStore.prototype.get;
                    let release = null, held = false;
                    IDBObjectStore.prototype.get = function(key) {
                        const request = Reflect.apply(get, this, [key]);
                        if (this.name === 'operationalSidecars' && key === 'removalTombstone') {
                            const transaction = this.transaction;
                            transaction.addEventListener('complete', event => {
                                if (!held && request.result === undefined) {
                                    // Preserve the real request result and completed transaction.
                                    // Delay only delivery of the application's completion callback.
                                    event.stopImmediatePropagation();
                                    held = true;
                                    const callback = transaction.oncomplete;
                                    release = () => { callback?.call(transaction, event); release = null; };
                                }
                            });
                        }
                        return request;
                    };
                    globalThis.hvRemovalVerificationHeld = () => held && release !== null;
                    globalThis.hvReleaseRemovalVerification = () => {
                        IDBObjectStore.prototype.get = get;
                        release?.();
                        delete globalThis.hvRemovalVerificationHeld;
                        delete globalThis.hvReleaseRemovalVerification;
                    };
                    return true;
                })()
                """)).Should().BeTrue();
            await authentication.RemoveAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await worker.EvaluateBooleanAsync("globalThis.hvRemovalVerificationHeld()"))
                await Task.Delay(25, deadline.Token);
            await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Create User|Restore Credential File|Restore Recovery Words)") })).ToHaveCountAsync(0);
            await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
            var counts = await StoreCountsAsync();
            counts.Values.All(count => count == 0).Should().BeTrue("first-run must still wait for acknowledged verification even after deletion");
            scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        }
        finally
        {
            await worker.EvaluateBooleanAsync("(() => { globalThis.hvReleaseRemovalVerification?.(); return true; })()");
        }
    }

    [Then("verified empty custody restores first-run while the same blockchain identity remains")]
    public async Task VerifiedAsync()
    {
        await authentication.FirstRunChoicesAsync();
        await authentication.StorageRemovedAsync();
        var profile = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = identity.Keys.SigningPublicKey }, deadline: DateTime.UtcNow.AddSeconds(10));
        (profile.Successfull && profile.PublicSigningAddress == identity.Keys.SigningPublicKey && profile.PublicEncryptAddress == identity.Keys.EncryptPublicKey).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        await entry.EntryAsync();
        await Expect(Page.GetByTestId("word-grid").Locator("input")).ToHaveCountAsync(24);
        (await Page.GetByTestId("word-grid").Locator("input").EvaluateAllAsync<bool>("inputs => inputs.every(input => input.value === '')")).Should().BeTrue();
    }

    private async Task<Dictionary<string, int>> StoreCountsAsync() => JsonSerializer.Deserialize<Dictionary<string, int>>(await Page.EvaluateAsync<string>("""
        async () => {
            const db = await new Promise((resolve, reject) => { const r = indexedDB.open('hushvoting-vault'); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Owned storage count unavailable')); });
            try {
                const counts = await Promise.all([...db.objectStoreNames].map(name => new Promise((resolve, reject) => {
                    const r = db.transaction(name).objectStore(name).count();
                    r.onsuccess = () => resolve([name, r.result]); r.onerror = () => reject(new Error('Owned storage count unavailable'));
                })));
                return JSON.stringify(Object.fromEntries(counts));
            } finally { db.close(); }
        }
        """))!;
}
