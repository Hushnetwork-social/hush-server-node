// EPIC-001 -> FEAT-007 AC-007-052 / FEAT-008 AC-008-062 /
// FEAT-009 AC-009-061 -> Phase 7 Tasks 7.1/7.2.
// Startup contract refinement: FEAT-030; migration: FEAT-011 Tasks 7.M1–7.M3.
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class StagedStartupPresentationSteps(HushVotingScenario scenario,
    IdentityPersistenceSteps creation, RecoveryResumeLookupSteps recovery,
    CredentialResumeLookupSteps file, HushVotingIdentityJourney identity)
{
    private bool _creation;
    private int _requests;

    [Given("Alice has genuinely staged creation in a browser that will lose its process")]
    public async Task CreationAsync() { _creation = true; await creation.PendingAsync(); }

    [Given("Alice has genuinely staged recovery words in a browser that will lose its process")]
    public Task RecoveryAsync() => recovery.StageAsync();

    [Given("Alice has genuinely staged a credential file in a browser that will lose its process")]
    public Task FileAsync() => file.StageAsync();

    [When("the browser restarts with only its persistent staged vault and no onboarding authority")]
    public async Task RestartAsync()
    {
        await scenario.CrashAndRestartBrowserAsync();
        await scenario.Page.AddInitScriptAsync("""
            (() => {
                window.hvStagedInputCount = 0;
                const send = MessagePort.prototype.postMessage;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'secret-transfer' && ['mnemonic','fileBytes','filePassword'].includes(message.purpose)) window.hvStagedInputCount++;
                    if (message?.kind === 'operation' && ['createCandidate','deriveRecoveryCandidates','importFileCandidate'].includes(message.operation)) window.hvStagedInputCount++;
                    return Reflect.apply(send, this, args);
                };
            })();
            """);
        await scenario.Page.ReloadAsync();
        _requests = scenario.Faults.RequestMethods.Count;
    }

    [Then("the required staged-resume heading appears before password unlock and exact online activation")]
    public async Task ResumeAsync()
    {
        await Expect(scenario.Page.GetByRole(AriaRole.Heading, new()
        {
            Name = _creation ? "Finish creating your identity" : "Finish restoring your identity", Exact = true
        })).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await NoSourceAsync();
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new()
        {
            NameRegex = new System.Text.RegularExpressions.Regex("^Create User|^Restore Credential File|^Restore Recovery Words")
        })).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByLabel("Device password", new() { Exact = true })).ToBeVisibleAsync();
        var pending = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        (pending.KeysMatch && pending.ConcreteKeysOnly && pending.PendingRegistration && !pending.Active).Should().BeTrue();
        if (_creation) await scenario.Blocks.ProduceBlockAsync();
        scenario.Faults.IdentityUnavailable = false;
        await identity.UnlockAndBootstrapAsync();
        scenario.Faults.RequestMethods.Skip(_requests).First(method => method is "GetIdentity" or "SubmitSignedTransaction")
            .Should().Be("GetIdentity");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(scenario.Page, identity.Keys,
            HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
        await NoSourceAsync();
    }

    private async Task NoSourceAsync()
    {
        foreach (var id in new[] { "word-grid", "recovery-list", "credential-file-input", "backup-password-input" })
            await Expect(scenario.Page.GetByTestId(id)).ToHaveCountAsync(0);
        (await scenario.Page.EvaluateAsync<int>("() => window.hvStagedInputCount")).Should().Be(0);
    }
}
