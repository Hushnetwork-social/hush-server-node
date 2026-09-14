using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-009 AC-009-062 -> Phase 3 Tasks 3.7/3.8, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialResumeLookupSteps(HushVotingScenario scenario, CredentialFileSteps file,
    CredentialProtectionSteps protection, AuthenticationSteps authentication, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;
    private int _requests;

    [Given("Alice stages imported credentials in a restartable browser before its source becomes unavailable")]
    public async Task StageAsync()
    {
        await scenario.UseRestartableBrowserAsync();
        await file.SourceWithWordsAsync();
        await file.DecryptAsync();
        await file.ImportedAsync();
        await protection.OfflineStagingAsync();
        await protection.PendingVaultAsync();
        file.MakeOwnedSourceUnavailable();
    }

    [When("the browser crashes and credential restoration restarts without the source or backup password")]
    public async Task RestartAsync()
    {
        await scenario.CrashAndRestartBrowserAsync();
        await Page.AddInitScriptAsync("""
            (() => {
                const facts = { sourceReads: 0, sourceTransfers: 0, imports: 0 };
                window.hvResumeSource = facts;
                const slice = File.prototype.slice;
                File.prototype.slice = function(...args) { facts.sourceReads++; return Reflect.apply(slice, this, args); };
                const send = MessagePort.prototype.postMessage;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'secret-transfer' && ['fileBytes','filePassword','mnemonic'].includes(message.purpose)) facts.sourceTransfers++;
                    if (message?.kind === 'operation' && ['importFileCandidate','deriveRecoveryCandidates','createCandidate'].includes(message.operation)) facts.imports++;
                    return Reflect.apply(send, this, args);
                };
            })();
            """);
        await Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
        await NoSourceAsync();
        _requests = scenario.Faults.RequestMethods.Count;
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }

    [Then("the selected device password resumes exact lookup and durable activation without importing again")]
    public async Task ActivateAsync()
    {
        scenario.Faults.IdentityUnavailable = false;
        await identity.UnlockAndBootstrapAsync();
        scenario.Faults.RequestMethods.Skip(_requests).First(method => method is "GetIdentity" or "SubmitSignedTransaction")
            .Should().Be("GetIdentity", "identity reconciliation must precede transaction submission; read-only height queries are independent");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "only the original identity and the baseline licence may be submitted");
        var lookup = scenario.Faults.IdentityLookups.Last();
        (lookup.Reply.Successfull && lookup.SigningAddress == identity.Keys.SigningPublicKey
            && lookup.Reply.PublicSigningAddress == identity.Keys.SigningPublicKey
            && lookup.Reply.PublicEncryptAddress == identity.Keys.EncryptPublicKey).Should().BeTrue();
        await NoSourceAsync();
        var facts = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        (facts.KeysMatch && facts.MetadataMatches && facts.DevicePasswordProtected && facts.ConcreteKeysOnly
            && facts.Active && facts.PendingTransactionCleared).Should().BeTrue();
    }

    private async Task NoSourceAsync()
    {
        foreach (var id in new[] { "credential-file-input", "selected-file-name", "backup-password-input", "word-grid", "recovery-list" })
            await Expect(Page.GetByTestId(id)).ToHaveCountAsync(0);
        (await Page.EvaluateAsync<bool>("() => Object.values(window.hvResumeSource).every(count => count === 0)")).Should().BeTrue();
    }
}
