using FluentAssertions;
using HushNetwork.proto;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 / FEAT-007 / Phase 7 Task 7.2 / AC-007-032 and AC-007-033.
// Product policy: FEAT-007 Phase 3 Task 3.5; schema implementation: FEAT-011 Phase 4 Task 4.1.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityWaitingLifecycleSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    IdentitySubmissionSteps submission)
{
    private string _browserTransaction = "";
    private string _expectedLifecycle = "";
    private int _expectedRequests;

    [Given("submission returns ACCEPTED")]
    public async Task AcceptedAsync()
    {
        await submission.PendingAsync();
        scenario.Faults.Submissions.Single().Status.Should().Be(TransactionStatus.Accepted);
        _browserTransaction = scenario.Faults.SubmittedTransactions.Single();
        _expectedLifecycle = "waitingAccepted";
        _expectedRequests = 1;
    }

    [When("the lifecycle is promoted")]
    public async Task WaitingAsync() => await submission.WaitingAsync();

    [Then("the provisional lifecycle becomes saved-user waiting")]
    [Then("HushVoting retains a saved waiting identity after the genuine PENDING reply")]
    public async Task SavedWaitingAsync()
    {
        await submission.WaitingAsync();
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys,
            HushVotingIdentityJourney.Alias, false, expectedTransaction: _browserTransaction,
            expectedPendingLifecycle: _expectedLifecycle);
        stored.KeysMatch.Should().BeTrue();
        stored.MetadataMatches.Should().BeTrue();
        stored.PendingRegistration.Should().BeTrue();
        stored.Active.Should().BeFalse();
        stored.PendingTransactionMatches.Should().BeTrue();
        stored.PendingLifecycleMatches.Should().BeTrue("the committed encrypted transaction must record the real admission outcome");
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true })).ToBeEnabledAsync();
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true })).ToHaveCountAsync(0);
    }

    [Then("no block confirmation is claimed")]
    public async Task NotConfirmedAsync()
    {
        await submission.NoShellAsync();
        var reply = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = identity.Keys.SigningPublicKey },
            deadline: DateTime.UtcNow.AddSeconds(10));
        reply.Successfull.Should().BeFalse("mempool admission must not publish an indexed identity");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_expectedRequests);
        await submission.ConfirmAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_expectedRequests + 1, "only the baseline licence is added after identity confirmation");
    }

    [Given("Alice's reviewed identity is ready for controlled duplicate delivery of its exact transaction")]
    public async Task ConcurrentAdmissionAsync()
    {
        await submission.ReviewAsync();
        scenario.Faults.HoldNextSubmission();
        _expectedLifecycle = "waitingPending";
        _expectedRequests = 2;
    }

    [When("Alice submits through the browser and the node responds PENDING for the same signing key")]
    public async Task PendingAsync()
    {
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true }).ClickAsync();
        try
        {
            await scenario.Faults.SubmissionArrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            _browserTransaction = scenario.Faults.SubmittedTransactions.Single();
            // Delay the browser's genuine RPC before admission. Deliver its exact
            // signed bytes through a second real gRPC request, then release the
            // first. Both results come from ordinary server validation/reservation.
            using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
            var accepted = await scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = _browserTransaction },
                deadline: DateTime.UtcNow.AddSeconds(15));
            accepted.Status.Should().Be(TransactionStatus.Accepted);
            await received.WaitAsync();
        }
        finally { scenario.Faults.ReleaseSubmission(); }
        try { await submission.WaitingAsync(); }
        catch (PlaywrightException)
        {
            throw new InvalidOperationException("Waiting gate did not appear. Node admission states: "
                + string.Join(',', scenario.Faults.Submissions.Select(reply => reply.Status))
                + "; RPC outcomes: " + string.Join(';', scenario.Faults.Outcomes));
        }
        scenario.Faults.Submissions.Count.Should().Be(2);
        scenario.Faults.Submissions.Last().Status.Should().Be(TransactionStatus.Pending);
        scenario.Faults.SubmittedTransactions.All(value => value == _browserTransaction).Should().BeTrue(
            "genuine pending admission requires the exact same signed transaction, not a different same-key request");
    }

    [Then("polling preserves those exact bytes without another submission until real block confirmation")]
    public async Task PreserveWhilePendingAsync()
    {
        var queries = scenario.Faults.IdentityLookups.Count;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (scenario.Faults.IdentityLookups.Count < queries + 2) await Task.Delay(50, deadline.Token);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_expectedRequests);
        await SavedWaitingAsync();
        await NotConfirmedAsync();
    }
}
