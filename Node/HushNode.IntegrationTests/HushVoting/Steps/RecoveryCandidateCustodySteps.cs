using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryCandidateCustodySteps(HushVotingScenario scenario, RecoveryProfileSteps profile, AuthenticationSteps authentication)
{
    private int _queries;

    [When("Alice abandons the resolved recovery and retries after reload while the node is unavailable")]
    public async Task AbandonAsync()
    {
        await scenario.Page.GoBackAsync();
        await authentication.FirstRunChoicesAsync();
        await authentication.StorageRemovedAsync();
        (await scenario.Page.EvaluateAsync<bool>("async () => localStorage.length === 0 && sessionStorage.length === 0 && (await caches.keys()).length === 0")).Should().BeTrue();
        _queries = scenario.Faults.IdentityQueryCount;
        scenario.Faults.IdentityUnavailable = true;
        await profile.EnterWordsAndVerifyAsync();
        await Expect(scenario.Page.Locator("#rw-lookup-error")).ToContainTextAsync("Retry the unresolved identity checks");
    }

    [Then("previous profile outcomes cannot authorize recovery and only fresh live results restore Alice")]
    public async Task FreshResultsAsync()
    {
        scenario.Faults.IdentityQueryCount.Should().Be(_queries + 1, "lookup stops at the first transport failure without reusing a previous epoch's result");
        await Expect(scenario.Page.GetByTestId("candidate-list")).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByRole(AriaRole.Heading, new() { Name = "Confirm this identity", Exact = true })).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByLabel("Device password", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        scenario.Faults.IdentityUnavailable = false;
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Retry unresolved checks", Exact = true }).ClickAsync();
        await Expect(scenario.Page.GetByTestId("candidate-list").Locator("li")).ToHaveCountAsync(1);
        await Expect(scenario.Page.GetByTestId("safe-alias")).ToHaveTextAsync(HushVotingIdentityJourney.Alias);
        scenario.Faults.IdentityQueryCount.Should().Be(_queries + 3, "both unresolved formats must be queried afresh after connectivity recovers");
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Continue to protect this device", Exact = true }).ClickAsync();
        await profile.ProtectAsync();
    }
}
