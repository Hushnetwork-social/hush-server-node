using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-039 -> Phase 7 Task 7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityPollingLifecycleSteps(HushVotingScenario scenario,
    HushVotingIdentityJourney identity, IdentitySubmissionSteps submission, IdentityPollingSteps polling)
{
    [Given("Alice's unindexed browser identity has an active real three-second confirmation loop")]
    public async Task WaitingAsync()
    {
        await scenario.Page.AddInitScriptAsync("""
            (() => {
                const original = MessagePort.prototype.postMessage;
                let operations = 0, inputs = 0;
                MessagePort.prototype.postMessage = function(...args) {
                    if (args[0]?.kind === 'operation') operations++;
                    return Reflect.apply(original, this, args);
                };
                for (const event of ['pointerdown', 'keydown', 'input'])
                    document.addEventListener(event, () => inputs++, true);
                window.__hvPollingActivity = () => ({ operations, inputs });
            })();
            """);
        await polling.WaitingAsync();
    }

    [When("the waiting page receives a controlled visibility loss and recovery then goes offline and online")]
    public async Task SuspendAndDisconnectAsync()
    {
        // Explicit negative browser-environment fault. This is not a claim that
        // headless Chromium implements physical background/window lifecycle.
        await scenario.Page.EvaluateAsync("""
            () => {
                Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' });
                document.dispatchEvent(new Event('visibilitychange'));
            }
            """);
        try
        {
            (await scenario.Page.EvaluateAsync<string>("() => document.visibilityState")).Should().Be("hidden");
            await NoQueriesAcrossTwoIntervalsAsync();
        }
        finally
        {
            await scenario.Page.EvaluateAsync("() => { delete document.visibilityState; document.dispatchEvent(new Event('visibilitychange')); }");
        }
        (await scenario.Page.EvaluateAsync<string>("() => document.visibilityState")).Should().Be("visible");
        await ResumedQueryAsync();

        await scenario.Page.Context.SetOfflineAsync(true);
        try
        {
            (await scenario.Page.EvaluateAsync<bool>("() => navigator.onLine")).Should().BeFalse();
            await NoQueriesAcrossTwoIntervalsAsync();
        }
        finally { await scenario.Page.Context.SetOfflineAsync(false); }
        await ResumedQueryAsync();
        await submission.NoShellAsync();
        submission.NoPeriodicSubmission();
    }

    private async Task NoQueriesAcrossTwoIntervalsAsync()
    {
        var before = scenario.Faults.IdentityQueryCount;
        await Task.Delay(6_500);
        scenario.Faults.IdentityQueryCount.Should().Be(before, "suspended, offline or revoked creation must not issue confirmation RPCs");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }

    private async Task ResumedQueryAsync()
    {
        var before = scenario.Faults.IdentityLookups.Count;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (scenario.Faults.IdentityLookups.Count <= before) await Task.Delay(40, deadline.Token);
        scenario.Faults.IdentityLookups.All(value => !value.Reply.Successfull).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }

    [Then("resumed confirmation polls issue no worker operations or user-input events")]
    public async Task NoActivityAsync()
    {
        var before = await scenario.Page.EvaluateAsync<string>("() => JSON.stringify(window.__hvPollingActivity())");
        await ResumedQueryAsync();
        await ResumedQueryAsync();
        var after = await scenario.Page.EvaluateAsync<string>("() => JSON.stringify(window.__hvPollingActivity())");
        after.Should().Be(before, "lookup-only polling must not manufacture input or invoke worker operations");
    }

    [Then("Lock revokes the waiting authority and stops its RPCs until fresh unlock after real indexing")]
    public async Task LockAndConfirmAsync()
    {
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true }).ClickAsync();
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Unlock HushVoting!", Exact = true })).ToBeVisibleAsync();
        await NoQueriesAcrossTwoIntervalsAsync();
        await submission.NoShellAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await identity.UnlockAndBootstrapAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
    }
}
