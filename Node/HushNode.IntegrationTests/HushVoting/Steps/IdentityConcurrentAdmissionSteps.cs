using FluentAssertions;
using HushNetwork.proto;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-072 -> Phase 7 Task 7.2.
// The transport barrier releases eight real RPCs into ordinary node admission.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityConcurrentAdmissionSteps(HushVotingScenario scenario,
    HushVotingIdentityJourney identity, IdentitySubmissionSteps submission)
{
    private const int RequestCount = 8;
    private string _exact = "";

    [Given("Alice's reviewed browser transaction will compete with seven exact retransmissions at the real node")]
    public async Task ReviewAsync() => await submission.ReviewAsync();

    [When("all eight identity RPCs arrive before their admission barrier is released")]
    public async Task SubmitTogetherAsync()
    {
        scenario.Faults.HoldSubmissionBatch(RequestCount);
        Task<SubmitSignedTransactionReply>[] duplicates = [];
        using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        try
        {
            await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true }).ClickAsync();
            await scenario.Faults.SubmissionArrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            _exact = scenario.Faults.SubmittedTransactions.Single();
            duplicates = Enumerable.Range(0, RequestCount - 1).Select(_ =>
                scenario.Blockchain.SubmitSignedTransactionAsync(new() { SignedTransaction = _exact },
                    deadline: DateTime.UtcNow.AddSeconds(25)).ResponseAsync).ToArray();
            await scenario.Faults.SubmissionBatchArrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            scenario.Faults.SubmittedTransactions.Count.Should().Be(RequestCount);
            scenario.Faults.Submissions.Should().BeEmpty("all real requests must overlap before any admission runs");
        }
        finally { scenario.Faults.ReleaseSubmissionBatch(); }
        await Task.WhenAll(duplicates);
        await received.WaitAsync();
        await submission.WaitingAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (scenario.Faults.Submissions.Count < RequestCount) await Task.Delay(25, deadline.Token);
    }

    [Then("the node returns one ACCEPTED and seven PENDING replies for those exact signed bytes")]
    public void AdmissionOutcomes()
    {
        var replies = scenario.Faults.Submissions.ToArray();
        replies.Should().HaveCount(RequestCount);
        replies.Count(reply => reply.Status == TransactionStatus.Accepted).Should().Be(1);
        replies.Count(reply => reply.Status == TransactionStatus.Pending).Should().Be(RequestCount - 1);
        scenario.Faults.SubmittedTransactions.All(value => value == _exact).Should().BeTrue();
    }

    [Then("Alice retains the same pending keys until identity and licence indexing unlock the browser shell")]
    public async Task ConfirmAsync()
    {
        var pending = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys,
            HushVotingIdentityJourney.Alias, false, expectedTransaction: _exact);
        pending.KeysMatch.Should().BeTrue();
        pending.PendingTransactionMatches.Should().BeTrue();
        pending.PendingRegistration.Should().BeTrue();
        pending.Active.Should().BeFalse();
        var absent = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = identity.Keys.SigningPublicKey },
            deadline: DateTime.UtcNow.AddSeconds(10));
        absent.Successfull.Should().BeFalse();
        await submission.ConfirmAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(RequestCount + 1, "only the root-owned baseline licence is added");
        var active = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys,
            HushVotingIdentityJourney.Alias, false);
        active.KeysMatch.Should().BeTrue();
        active.Active.Should().BeTrue();
        active.PendingTransactionCleared.Should().BeTrue();
    }
}
