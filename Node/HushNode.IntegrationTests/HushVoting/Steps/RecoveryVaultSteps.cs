using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryVaultSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity, AuthenticationSteps authentication)
{
    [Then("independent inspection of every retained recovery vault slot finds only the selected concrete keys")]
    [Then("independent inspection and a fresh unlock find only the imported concrete keys and no recovery reveal")]
    public async Task ConcreteKeysOnlyAsync()
    {
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
        await authentication.LockAsync();
        await scenario.Page.ReloadAsync();
        await HushVotingIdentityJourney.FillSecretAsync(scenario.Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await scenario.Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Unlock HushVoting") }).ClickAsync();
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = HushVotingIdentityJourney.Alias, Exact = true }).ClickAsync();
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("recovery|mnemonic", System.Text.RegularExpressions.RegexOptions.IgnoreCase) })).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }
}
