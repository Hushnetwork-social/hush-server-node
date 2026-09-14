// EPIC-001 -> FEAT-008 AC-008-070 -> Phase 3 Tasks 3.9/3.10,
// Phase 7 Tasks 7.1/7.2. Live confirmed removal only; restart consent remains FEAT-022.
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryCleanupQuarantineSteps(HushVotingScenario scenario,
    RecoveryRemovalVerificationSteps removal, AuthenticationSteps authentication)
{
    private IPage Page => scenario.Page;

    [Given("Alice has restored her identity and locked its vault before confirmed cleanup")]
    public Task ReadyAsync() => removal.ReadyAsync();

    [When("a real managed-journal deletion aborts during removal and Alice retries inspection")]
    public async Task AbortDeletionAsync()
    {
        await using var worker = await HushVotingWorkerProbe.AttachAsync(scenario);
        try
        {
            (await worker.EvaluateBooleanAsync("""
                (() => {
                    const original = IDBObjectStore.prototype.delete;
                    let aborted = 0;
                    IDBObjectStore.prototype.delete = function(key) {
                        const request = Reflect.apply(original, this, [key]);
                        if (this.name === 'licenceJournal' && key === 'pointer') {
                            aborted++;
                            this.transaction.abort();
                        }
                        return request;
                    };
                    globalThis.hvCleanupDeletionAbortedOnce = () => aborted === 1;
                    globalThis.hvRestoreCleanupDeletion = () => {
                        IDBObjectStore.prototype.delete = original;
                        delete globalThis.hvCleanupDeletionAbortedOnce;
                        delete globalThis.hvRestoreCleanupDeletion;
                    };
                    return true;
                })()
                """)).Should().BeTrue();
            await authentication.RemoveAsync();
            await Expect(Page.Locator(".error-surface")).ToBeVisibleAsync();
            await BlockedAsync();
            (await worker.EvaluateBooleanAsync("globalThis.hvCleanupDeletionAbortedOnce()")).Should().BeTrue();
            (await ResidueAndMarkerRemainAsync()).Should().BeTrue("an aborted real transaction must not be reported as verified absence");
            // Same-session inspection cannot repeat deletion or supply fresh consent.
            await Page.GetByRole(AriaRole.Button, new() { Name = "Try again", Exact = true }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync();
            await BlockedAsync();
            (await ResidueAndMarkerRemainAsync()).Should().BeTrue();
            (await worker.EvaluateBooleanAsync("globalThis.hvCleanupDeletionAbortedOnce()")).Should().BeTrue("inspection cannot silently repeat deletion");
            scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        }
        finally
        {
            await worker.EvaluateBooleanAsync("(() => { globalThis.hvRestoreCleanupDeletion?.(); return true; })()");
        }
    }

    [Then("recovery stays quarantined until Alice freshly confirms removal and all managed custody is verified absent")]
    public async Task ConfirmedRecoveryAsync()
    {
        await BlockedAsync();
        (await ResidueAndMarkerRemainAsync()).Should().BeTrue();
        await authentication.RemoveAsync();
        await removal.VerifiedAsync();
    }

    private async Task BlockedAsync()
    {
        await Expect(Page.Locator(".error-surface")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new()
        {
            NameRegex = new System.Text.RegularExpressions.Regex("^(Create User|Restore Credential File|Restore Recovery Words)")
        })).ToHaveCountAsync(0);
        authentication.RootOnlyUrl();
    }

    private async Task<bool> ResidueAndMarkerRemainAsync() => await Page.EvaluateAsync<bool>("""
        async () => {
            const db = await new Promise((resolve, reject) => {
                const request = indexedDB.open('hushvoting-vault');
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(new Error('Owned cleanup inspection failed'));
            });
            const exists = (store, key) => new Promise((resolve, reject) => {
                const tx = db.transaction(store);
                const request = tx.objectStore(store).get(key);
                let present = false;
                request.onsuccess = () => { present = request.result !== undefined; };
                tx.oncomplete = () => resolve(present);
                tx.onabort = tx.onerror = () => reject(new Error('Owned cleanup inspection failed'));
            });
            try { return (await exists('licenceJournal', 'pointer')) && (await exists('operationalSidecars', 'removalTombstone')); }
            finally { db.close(); }
        }
        """);
}
