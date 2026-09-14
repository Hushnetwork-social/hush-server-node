using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-009 AC-009-072 -> Phase 3 Tasks 3.9/3.10, Phase 7 Task 7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialCleanupFailureSteps(HushVotingScenario scenario, CredentialFileSteps file,
    AuthenticationSteps authentication, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;

    [Given("Alice restores a real registered backup while worker cleanup can encounter actual contention")]
    public async Task ReadyAsync()
    {
        await scenario.UseRestartableBrowserAsync();
        await file.SourceWithWordsAsync();
        await Page.EvaluateAsync("""
            () => {
                const send = MessagePort.prototype.postMessage, listening = new WeakSet();
                let armed = null, blocked = null, blockerOutcome = null, rejected = false, discarded = false, attempts = 0;
                const observe = port => {
                    if (listening.has(port)) return;
                    listening.add(port);
                    port.addEventListener('message', event => {
                        const result = event.data;
                        if (result?.kind !== 'operation-outcome' || !blocked) return;
                        if (result.operationId === blocked.blockerId) blockerOutcome = result.outcome;
                        if (result.operationId === blocked.cleanupId && result.outcome === 'AUTHORITY_BUSY') rejected = true;
                        if (result.operationId === blocked.retryId && result.outcome === 'OK') discarded = true;
                    });
                };
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation' && message.operation === armed) {
                        observe(this); attempts++;
                        if (blocked === null) {
                            // Hold an actual closed worker operation waiting for a public test password.
                            // The following cleanup must receive the real authority's BUSY rejection.
                            const blockerId = 'cleanup-contention-' + crypto.randomUUID();
                            blocked = { port: this, blockerId, cleanupId: message.operationId,
                                channel: message.clientChannel, epoch: message.authorityEpoch };
                            Reflect.apply(send, this, [{ kind: 'operation', operation: 'unlockPassword', operationVersion: 1,
                                operationId: blockerId, clientChannel: message.clientChannel, authorityEpoch: message.authorityEpoch }]);
                        } else blocked.retryId = message.operationId;
                    }
                    return Reflect.apply(send, this, args);
                };
                window.__hvArmCleanupContention = operation => {
                    armed = operation; blocked = null; blockerOutcome = null; rejected = false; discarded = false; attempts = 0;
                };
                window.__hvCleanupContentionFacts = () => ({ rejected, discarded, attempts });
                window.__hvReleaseCleanupContention = async expectedOutcome => {
                    if (!blocked) return false;
                    Reflect.apply(send, blocked.port, [{ kind: 'secret-transfer', operationId: blocked.blockerId,
                        clientChannel: blocked.channel, authorityEpoch: blocked.epoch, purpose: 'devicePassword',
                        value: 'public-test-contention-release' }]);
                    const deadline = Date.now() + 5000;
                    while (blockerOutcome === null && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 20));
                    return blockerOutcome === expectedOutcome;
                };
            }
            """);
    }

    [When("real worker contention rejects source cleanup and then validated-candidate cleanup")]
    public async Task FailAndRetryAsync()
    {
        await file.ChoosePreparedAsync();
        await FailAndRetryOneAsync("discardSecretTransfers");
        await authentication.FirstRunChoicesAsync();
        await file.OpenAsync();
        await file.DecryptAsync();
        await file.ImportedAsync();
        await FailAndRetryOneAsync("destroyCandidate");
        await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveValueAsync("");
    }

    private async Task FailAndRetryOneAsync(string operation)
    {
        var queries = scenario.Faults.IdentityQueryCount;
        await Page.EvaluateAsync("operation => window.__hvArmCleanupContention(operation)", operation);
        await Page.GoBackAsync();
        await Expect(Page.Locator(".error-surface")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync();
        foreach (var id in new[] { "choose-file", "credential-file-input", "backup-password-input", "restore-device-password", "authenticated-shell" })
            await Expect(Page.GetByTestId(id)).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Create User|Restore Credential File|Restore Recovery Words)") })).ToHaveCountAsync(0);
        (await Page.EvaluateAsync<bool>("() => { const f = window.__hvCleanupContentionFacts(); return f.rejected && !f.discarded && f.attempts === 1; }")).Should().BeTrue();
        scenario.Faults.IdentityQueryCount.Should().Be(queries);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        // No vault exists yet; the held unlock cannot unlock or stage one.
        (await Page.EvaluateAsync<bool>("() => window.__hvReleaseCleanupContention('CORRUPT_VAULT')")).Should().BeTrue();
        // Removing contention does not automatically bypass the blocked cleanup.
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await Page.WaitForFunctionAsync("() => { const f = window.__hvCleanupContentionFacts(); return f.discarded && f.attempts === 2; }");
        scenario.Faults.IdentityQueryCount.Should().Be(queries);
    }

    [Then("only acknowledged cleanup permits fresh selection and the unchanged source restores through the live node")]
    public async Task CompleteAsync()
    {
        await file.DecryptAsync();
        await file.ImportedAsync();
        await file.ProtectAsync();
        await file.SourceUnchangedAsync();
        await file.NoPersistentSourceAsync();
    }

    [When("real worker contention rejects removal of Alice's restored local identity")]
    public async Task RemovalFailureAsync()
    {
        await authentication.LockAsync();
        await authentication.SafeLockedPreviewAsync();
        await Page.EvaluateAsync("() => window.__hvArmCleanupContention('removeLocalUser')");
        await authentication.RemoveAsync();
        await Expect(Page.Locator(".error-surface")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Try again", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Create User|Restore Credential File|Restore Recovery Words)") })).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        (await Page.EvaluateAsync<bool>("() => { const f = window.__hvCleanupContentionFacts(); return f.rejected && !f.discarded && f.attempts === 1; }")).Should().BeTrue();
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }

    [Then("removal failure preserves the vault and only a fresh confirmed removal returns to verified empty custody")]
    public async Task RetryRemovalAsync()
    {
        // The existing vault rejects the public test password; no real password is used by the fault.
        (await Page.EvaluateAsync<bool>("() => window.__hvReleaseCleanupContention('WRONG_PASSWORD_OR_DAMAGED')")).Should().BeTrue();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Try again", Exact = true }).ClickAsync();
        await authentication.SafeLockedPreviewAsync();
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveCountAsync(0);
        // An aborted schema upgrade closes the worker's actual IndexedDB connection
        // through versionchange without changing the database version or its records.
        (await Page.EvaluateAsync<bool>("""
            async () => {
                const before = (await indexedDB.databases()).find(db => db.name === 'hushvoting-vault');
                if (!before) return false;
                const aborted = await new Promise(resolve => {
                    const request = indexedDB.open(before.name, before.version + 1);
                    request.onupgradeneeded = () => request.transaction.abort();
                    request.onerror = event => { event.preventDefault(); resolve(request.error?.name === 'AbortError'); };
                    request.onsuccess = () => { request.result.close(); resolve(false); };
                });
                const after = (await indexedDB.databases()).find(db => db.name === before.name);
                return aborted && after?.version === before.version;
            }
            """)).Should().BeTrue();
        await authentication.RemoveAsync();
        await Expect(Page.Locator(".error-surface")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Create User|Restore Credential File|Restore Recovery Words)") })).ToHaveCountAsync(0);
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
        await scenario.CrashAndRestartBrowserAsync();
        await authentication.SafeLockedPreviewAsync();
        await authentication.RemoveAsync();
        await authentication.FirstRunChoicesAsync();
        await authentication.StorageRemovedAsync();
        await file.RemovedSourcePreservedAsync();
    }
}
