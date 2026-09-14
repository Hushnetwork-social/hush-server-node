using FluentAssertions;
using HushNetwork.proto;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 / FEAT-007 / Phase 7 Task 7.2 / AC-007-034 and AC-007-042.
// Policy: FEAT-007 Phase 3 Task 3.5; browser composition: FEAT-011 Phase 4 Tasks 4.1/4.3.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityConfirmationSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    IdentitySubmissionSteps submission)
{
    private const string IndexedAlias = "Second device profile";
    private int _lookupReplyOffset;
    private bool _blockedBeforeLookup;

    [Given("Alice has reviewed a Private identity and its first submission can be delayed before admission")]
    public async Task ReviewAsync()
    {
        await submission.ReviewAsync();
        scenario.Faults.HoldNextSubmission();
    }

    [When("another device indexes the same keys with a different Public profile before Alice's delayed submission completes")]
    public async Task IndexCompetingProfileAsync()
    {
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true }).ClickAsync();
        try
        {
            await scenario.Faults.SubmissionArrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var retained = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys,
                HushVotingIdentityJourney.Alias, false, expectedTransaction: scenario.Faults.SubmittedTransactions.Single());
            retained.PendingTransactionMatches.Should().BeTrue();
            retained.PendingRegistration.Should().BeTrue();
            retained.MetadataMatches.Should().BeTrue();
            // A genuine independent device wins the race while the browser RPC
            // is delayed. No indexed records or positive replies are fabricated.
            using (var accepted = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20)))
            {
                var reply = await scenario.Blockchain.SubmitSignedTransactionAsync(new()
                {
                    SignedTransaction = HushVotingServerIdentity.Sign(identity.Keys, IndexedAlias, true)
                }, deadline: DateTime.UtcNow.AddSeconds(15));
                reply.Status.Should().Be(TransactionStatus.Accepted);
                await accepted.WaitAsync();
            }
            await scenario.Blocks.ProduceBlockAsync();
            _lookupReplyOffset = scenario.Faults.IdentityLookups.Count;
            scenario.Faults.IdentityResponseDelay = TimeSpan.FromSeconds(3);
        }
        catch { scenario.Faults.ReleaseSubmission(); throw; }

        using var licence = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        scenario.Faults.ReleaseSubmission();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!scenario.Faults.Submissions.Any(reply => reply.Status == TransactionStatus.AlreadyExists))
                await Task.Delay(30, deadline.Token);
            scenario.Faults.IdentityLookups.Count.Should().Be(_lookupReplyOffset,
                "the controlled delay keeps fresh indexed lookup replies unavailable at this checkpoint");
            await submission.NoShellAsync();
            scenario.Faults.SubmittedTransactions.Count.Should().Be(2,
                "ALREADY_EXISTS alone must not start the authenticated licence bootstrap");
            _blockedBeforeLookup = true;
        }
        finally { scenario.Faults.IdentityResponseDelay = TimeSpan.Zero; }
        await licence.WaitAsync();
        await Expect(scenario.Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync();
    }

    [Then("fresh exact indexed lookup is required after the real ALREADY_EXISTS response")]
    public void VerifyExactLookup()
    {
        _blockedBeforeLookup.Should().BeTrue();
        var replies = scenario.Faults.IdentityLookups.Skip(_lookupReplyOffset).ToArray();
        replies.Length.Should().BeGreaterThan(0);
        replies.All(item => item.SigningAddress == identity.Keys.SigningPublicKey && item.Reply.Successfull
            && item.Reply.PublicSigningAddress == identity.Keys.SigningPublicKey
            && item.Reply.PublicEncryptAddress == identity.Keys.EncryptPublicKey
            && item.Reply.ProfileName == IndexedAlias && item.Reply.IsPublic).Should().BeTrue();
    }

    [Then("the active encrypted record atomically adopts the indexed alias and visibility and clears its pending transaction")]
    public async Task SynchronisedAsync()
    {
        VerifyExactLookup();
        var active = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, IndexedAlias, true);
        active.Active.Should().BeTrue();
        active.MetadataMatches.Should().BeTrue("both encrypted metadata and preview must identify the indexed profile");
        active.KeysMatch.Should().BeTrue();
        active.ConcreteKeysOnly.Should().BeTrue();
        active.PendingTransactionCleared.Should().BeTrue("active identity work and its digest must be retired together");
        await submission.NoShellAsync();
    }

    [Then("authenticated access follows exact identity verification and separate licence indexing without another identity submission")]
    public async Task CompleteAsync()
    {
        _blockedBeforeLookup.Should().BeTrue();
        await submission.NoShellAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { Name = IndexedAlias, Exact = true })).ToBeVisibleAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(3,
            "two competing identity requests and one licence are the only submissions");
    }
}
