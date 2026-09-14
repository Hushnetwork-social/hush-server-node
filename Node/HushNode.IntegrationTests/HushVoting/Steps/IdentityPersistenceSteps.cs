using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityPersistenceSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    IdentitySubmissionSteps submission, AuthenticationSteps authentication)
{
    private string _exact = "";

    [Given("Alice's restartable browser has sealed and submitted an identity that is not yet indexed")]
    public async Task PendingAsync()
    {
        await scenario.UseRestartableBrowserAsync();
        await submission.PendingAsync();
        _exact = scenario.Faults.SubmittedTransactions.Single();
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false, expectedTransaction: _exact);
        stored.PendingTransactionMatches.Should().BeTrue();
        stored.PendingRegistration.Should().BeTrue();
    }

    [When("the owned browser process is killed and restarted using its existing encrypted storage")]
    public async Task RestartAsync()
    {
        await scenario.CrashAndRestartBrowserAsync();
        await authentication.SafeLockedPreviewAsync();
    }

    [Then("the exact signed bytes survive and ordinary unlock completes only after real identity and licence indexing")]
    public async Task ExactSurvivesAsync()
    {
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false, expectedTransaction: _exact);
        stored.PendingTransactionMatches.Should().BeTrue();
        stored.KeysMatch.Should().BeTrue();
        stored.ConcreteKeysOnly.Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        await Expect(scenario.Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
        await scenario.Blocks.ProduceBlockAsync();
        await identity.UnlockAndBootstrapAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "only the original identity and its baseline licence are submitted");
    }
}
