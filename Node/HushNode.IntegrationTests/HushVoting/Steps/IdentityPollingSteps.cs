using System.Diagnostics;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityPollingSteps(HushVotingScenario scenario, IdentitySubmissionSteps submission)
{
    private IPage Page => scenario.Page;
    private int _before;

    [Given("the waiting gate is foregrounded, online, visible, and authority-valid")]
    [Given("the waiting gate with a Check again control")]
    public async Task WaitingAsync()
    {
        await submission.PendingAsync();
        // Initial eligibility lookup, then immediate post-admission reconciliation.
        await WaitForRepliesAsync(2);
        _before = scenario.Faults.IdentityQueryCount;
    }

    private async Task WaitForRepliesAsync(int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (scenario.Faults.IdentityLookups.Count < count) await Task.Delay(30, deadline.Token);
    }

    [When("polling runs")]
    public async Task PollAsync() => await WaitForRepliesAsync(_before + 2);

    [Then("GetIdentity is called every three seconds through one coalesced loop")]
    public void ThreeSecondLoop()
    {
        var calls = scenario.Faults.IdentityQueryStartedTicks.ToArray();
        calls.Should().HaveCount(_before + 2);
        Stopwatch.GetElapsedTime(calls[^2], calls[^1]).TotalSeconds.Should().BeInRange(2.5, 3.75);
        scenario.Faults.IdentityLookups.All(result => !result.Reply.Successfull).Should().BeTrue("every pre-index lookup must report absence");
    }

    [Then("submission never happens on a poll")]
    public void NoSubmission() => submission.NoPeriodicSubmission();

    [When("the user selects Check again")]
    public async Task ManualAsync()
    {
        scenario.Faults.IdentityResponseDelay = TimeSpan.FromMilliseconds(500);
        var elapsed = Stopwatch.StartNew();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Check again", Exact = true }).DblClickAsync();
        await WaitForRepliesAsync(_before + 1);
        elapsed.Stop();
        elapsed.Elapsed.TotalSeconds.Should().BeLessThan(2);
        scenario.Faults.IdentityQueryCount.Should().Be(_before + 1);
        scenario.Faults.IdentityResponseDelay = TimeSpan.Zero;
    }

    [Then("one immediate coalesced lookup runs")]
    public void OneImmediateLookup() => scenario.Faults.IdentityQueryCount.Should().Be(_before + 1);

    [Then("no new poll loop or submission is created")]
    public async Task NoExtraLoopAsync()
    {
        await WaitForRepliesAsync(_before + 2);
        scenario.Faults.IdentityQueryCount.Should().Be(_before + 2);
        submission.NoPeriodicSubmission();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
    }

    [Then("indexed identity confirmation automatically advances to the root licence gate")]
    public async Task AutomaticConfirmationAsync() => await submission.ConfirmAsync();
}
