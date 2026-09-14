using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryLookupSteps(HushVotingScenario scenario, RecoveryWordEntrySteps entry)
{
    private IPage Page => scenario.Page;
    private readonly System.Diagnostics.Stopwatch _lookupClock = new();

    [Given("the second real recovery candidate lookup stalls beyond its transport deadline")]
    public async Task StalledLookupAsync()
    {
        await entry.EntryAsync();
        scenario.Faults.IdentityResponseDelay = TimeSpan.FromMilliseconds(750);
        scenario.Faults.StallIdentityQueryNumber = 2;
    }

    [When("Alice observes the counted progress of sequential recovery lookups")]
    public async Task CountedLookupsAsync()
    {
        _lookupClock.Start();
        await VerifyAsync();
        await Expect(Page.GetByTestId("recovery-status")).ToContainTextAsync("Checking identity formats 0 of 2");
        await Expect(Page.GetByTestId("recovery-status")).ToContainTextAsync("Checking identity formats 1 of 2");
        scenario.Faults.IdentityLookups.Count.Should().Be(1);
        await Expect(Page.GetByTestId("candidate-list")).ToHaveCountAsync(0);
    }

    [Then("the unresolved lookup times out after ten seconds without becoming an absent profile")]
    public async Task BoundedLookupAsync()
    {
        await Expect(Page.Locator("#rw-lookup-error")).ToContainTextAsync("Retry the unresolved identity checks", new() { Timeout = 13_000 });
        _lookupClock.Stop();
        // Includes derivation, the first 750 ms lookup and browser scheduling.
        _lookupClock.Elapsed.TotalSeconds.Should().BeInRange(10, 14);
        scenario.Faults.IdentityQueryCount.Should().Be(2);
        scenario.Faults.IdentityLookups.Count.Should().Be(1);
        await Expect(Page.GetByTestId("candidate-list")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("recreate-alias")).ToHaveCountAsync(0);
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        await RetryAsync();
    }

    [Given("the node will fail one of Alice's two recovery candidate lookups")]
    public async Task PartialOutageAsync()
    {
        await entry.EntryAsync();
        scenario.Faults.RejectIdentityQueryNumber = 2;
    }

    [When("Alice verifies her recovery phrase")]
    public async Task VerifyAsync()
    {
        for (var position = 1; position <= 24; position++)
            await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + position), position == 24 ? "art" : "abandon");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
    }

    [Then("the partial result permits neither candidate selection nor profile creation")]
    public async Task FailClosedAsync()
    {
        await Expect(Page.Locator("#rw-lookup-error")).ToContainTextAsync("Retry the unresolved identity checks");
        await Expect(Page.GetByTestId("recovery-status")).ToContainTextAsync("Checking identity formats 1 of 2");
        await Expect(Page.GetByTestId("candidate-list")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("recreate-alias")).ToHaveCountAsync(0);
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveCountAsync(0);
        scenario.Faults.IdentityQueryCount.Should().Be(2);
        scenario.Faults.RejectedIdentityQueries.Should().Be(1);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [Then("retry queries only the unresolved candidate before exposing the complete outcome")]
    public async Task RetryAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Retry unresolved checks", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("candidate-list").Locator("li")).ToHaveCountAsync(2);
        scenario.Faults.IdentityQueryCount.Should().Be(3);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Selected", Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue to review profile", Exact = true })).ToBeDisabledAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }
}
