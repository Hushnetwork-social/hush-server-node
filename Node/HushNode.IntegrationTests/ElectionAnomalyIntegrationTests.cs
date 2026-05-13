using FluentAssertions;
using HushNetwork.proto;
using HushNode.Elections.gRPC;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;
using HushServerNode.Testing.Elections;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("Integration Tests")]
[Trait("Category", "FEAT-123")]
[Trait("Category", "NON_E2E")]
public sealed class ElectionAnomalyIntegrationTests : IAsyncLifetime
{
    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private BlockProductionControl? _blockControl;
    private GrpcClientFactory? _grpcFactory;

    public async Task InitializeAsync()
    {
        _fixture = new HushTestFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await DisposeNodeAsync();

        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task SignedAnomalyTransactions_CreateThreadAndRestrictedProjections()
    {
        await StartNodeAsync();
        var electionId = await CreateDraftElectionAsync("FEAT-123 Anomaly Integration");
        (await SubmitBlockchainTransactionAsync(TestTransactionFactory.CreateIdentityRegistration(TestIdentities.Charlie)))
            .Successfull
            .Should()
            .BeTrue();
        var (inviteTransaction, invitationId) = TestTransactionFactory.CreateElectionTrusteeInvitation(
            TestIdentities.Alice,
            electionId,
            TestIdentities.Charlie);
        var inviteResponse = await SubmitBlockchainTransactionAsync(inviteTransaction);
        inviteResponse.Successfull.Should().BeTrue(inviteResponse.Message);
        var acceptResponse = await SubmitBlockchainTransactionAsync(TestTransactionFactory.AcceptElectionTrusteeInvitation(
            TestIdentities.Charlie,
            electionId,
            invitationId));
        acceptResponse.Successfull.Should().BeTrue(acceptResponse.Message);

        var (submissionTransaction, anomalyThreadId) = TestTransactionFactory.SubmitElectionAnomalyThread(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId);
        var submitResponse = await SubmitBlockchainTransactionAsync(submissionTransaction);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var duplicateTransaction = TestTransactionFactory.SubmitElectionAnomalyThread(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId).Transaction;
        var duplicateResponse = await SubmitBlockchainTransactionAsync(duplicateTransaction);
        duplicateResponse.Successfull.Should().BeFalse("one person can submit only one anomaly thread per election");

        var (requestTransaction, clarificationRequestId) = TestTransactionFactory.RequestElectionAnomalyInformation(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId,
            anomalyThreadId);
        (await SubmitBlockchainTransactionAsync(requestTransaction)).Successfull.Should().BeTrue();

        var clarificationTransaction = TestTransactionFactory.SubmitElectionAnomalyInformation(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId,
            anomalyThreadId,
            clarificationRequestId);
        (await SubmitBlockchainTransactionAsync(clarificationTransaction)).Successfull.Should().BeTrue();

        var authorityResponseTransaction = TestTransactionFactory.RecordElectionAnomalyAuthorityResponse(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId,
            anomalyThreadId);
        (await SubmitBlockchainTransactionAsync(authorityResponseTransaction)).Successfull.Should().BeTrue();

        var grantTransaction = TestTransactionFactory.CreateElectionReportAccessGrant(
            TestIdentities.Alice,
            electionId,
            TestIdentities.Bob.PublicSigningAddress);
        (await SubmitBlockchainTransactionAsync(grantTransaction)).Successfull.Should().BeTrue();

        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var threads = await dbContext.Set<ElectionAnomalyThreadRecord>()
            .Where(x => x.ElectionId == electionId)
            .ToListAsync();
        threads.Should().ContainSingle();
        threads[0].Id.Should().Be(anomalyThreadId);

        var messages = await dbContext.Set<ElectionAnomalyMessageEnvelopeRecord>()
            .Where(x => x.AnomalyThreadId == anomalyThreadId)
            .OrderBy(x => x.RecordedAt)
            .ToListAsync();
        messages.Should().HaveCount(4);

        var wraps = await dbContext.Set<ElectionAnomalyRecipientWrapRecord>()
            .Where(x => x.AnomalyThreadId == anomalyThreadId)
            .ToListAsync();
        foreach (var message in messages)
        {
            wraps.Where(x => x.MessageEnvelopeId == message.Id)
                .Select(x => x.RecipientRoleId)
                .Should()
                .Contain([
                    ElectionAnomalyRecipientRoleIds.Submitter,
                    ElectionAnomalyRecipientRoleIds.ElectionOwner,
                    ElectionAnomalyRecipientRoleIds.DesignatedAuditor,
                ]);
        }

        wraps.Where(x => x.RecipientRoleId == ElectionAnomalyRecipientRoleIds.DesignatedAuditor)
            .Should()
            .HaveCount(messages.Count)
            .And.OnlyContain(x => x.WrapStatusId == ElectionAnomalyRecipientWrapStatusIds.PendingBackfill);

        var queryService = scope.ServiceProvider.GetRequiredService<IElectionQueryApplicationService>();
        var ownProjection = await queryService.GetElectionAnomalyOwnThreadAsync(
            electionId,
            TestIdentities.Alice.PublicSigningAddress);
        ownProjection.Should().NotBeNull();
        ownProjection!.Messages.Should().HaveCount(4);

        var peerProjection = await queryService.GetElectionAnomalyOwnThreadAsync(
            electionId,
            TestIdentities.Bob.PublicSigningAddress);
        peerProjection.Should().BeNull();

        var ownerTriage = await queryService.GetElectionAnomalyOwnerTriageAsync(
            electionId,
            TestIdentities.Alice.PublicSigningAddress);
        ownerTriage.Should().ContainSingle();
        ownerTriage[0].SubmitterActorPublicAddress.Should().Be(TestIdentities.Alice.PublicSigningAddress);

        var trusteeCounts = await queryService.GetElectionAnomalyTrusteeCountsAsync(
            electionId,
            TestIdentities.Charlie.PublicSigningAddress);
        trusteeCounts.Should().NotBeNull();
        trusteeCounts!.TotalThreadCount.Should().Be(1);
        trusteeCounts.GetType().GetProperties()
            .Should()
            .NotContain(property => property.Name.Contains("Message", StringComparison.OrdinalIgnoreCase));

        var trusteeRestrictedReview = await queryService.GetElectionAnomalyAuditorRestrictedReviewAsync(
            electionId,
            TestIdentities.Charlie.PublicSigningAddress);
        trusteeRestrictedReview.Should().BeNull();

        var auditorReview = await queryService.GetElectionAnomalyAuditorRestrictedReviewAsync(
            electionId,
            TestIdentities.Bob.PublicSigningAddress);
        auditorReview.Should().NotBeNull();
        auditorReview!.Threads.Should().ContainSingle();
        auditorReview.Threads[0].Messages.Should().HaveCount(4);
        auditorReview.Threads[0].Messages[0].RecipientWraps
            .Should()
            .OnlyContain(x => !string.IsNullOrWhiteSpace(x.RecipientRoleId));
    }

    [Fact]
    public async Task SignedAnomalySubmission_WithClosedSubmissionWindow_IsRejected()
    {
        await StartNodeAsync();
        var electionId = await CreateDraftElectionAsync("FEAT-123 Closed Anomaly Window");

        await using (var scope = _node!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
            var election = await dbContext.Set<ElectionRecord>()
                .AsNoTracking()
                .SingleAsync(x => x.ElectionId == electionId);
            dbContext.Update(election with { AnomalySubmissionWindowClosesAt = DateTime.UtcNow.AddMinutes(-1) });
            await dbContext.SaveChangesAsync();
        }

        var (submissionTransaction, _) = TestTransactionFactory.SubmitElectionAnomalyThread(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId);
        var submitResponse = await SubmitBlockchainTransactionAsync(submissionTransaction);
        submitResponse.Successfull.Should().BeFalse("the anomaly submission window is closed");

        await using var verificationScope = _node!.Services.CreateAsyncScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var threadCount = await verificationDbContext.Set<ElectionAnomalyThreadRecord>()
            .CountAsync(x => x.ElectionId == electionId);
        threadCount.Should().Be(0);
    }

    private async Task StartNodeAsync()
    {
        await DisposeNodeAsync();
        await _fixture!.ResetAllAsync();
        (_node, _blockControl, _grpcFactory) = await _fixture.StartNodeAsync();
    }

    private async Task DisposeNodeAsync()
    {
        _grpcFactory?.Dispose();
        _grpcFactory = null;
        _blockControl = null;

        if (_node is not null)
        {
            await _node.DisposeAsync();
            _node = null;
        }
    }

    private async Task<ElectionId> CreateDraftElectionAsync(string title)
    {
        var (signedTransaction, electionId) = TestTransactionFactory.CreateElectionDraft(
            TestIdentities.Alice,
            "feat-123 anomaly integration draft",
            CreateDraftSpecification(title));
        var submitResponse = await SubmitBlockchainTransactionAsync(signedTransaction);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
        return electionId;
    }

    private async Task<SubmitSignedTransactionReply> SubmitBlockchainTransactionAsync(string signedTransaction)
    {
        var blockchainClient = _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();
        using var waiter = _node!.StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        if (submitResponse.Successfull)
        {
            await waiter.WaitAsync();
            await _blockControl!.ProduceBlockAsync();
        }

        return submitResponse;
    }

    private static ElectionDraftSpecification CreateDraftSpecification(string title) =>
        new(
            Title: title,
            ShortDescription: "Anomaly integration validation",
            ExternalReferenceCode: "FEAT-123",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: ElectionBindingStatus.Binding,
            SelectedProfileId: "dkg-prod-3of5",
            GovernanceMode: ElectionGovernanceMode.TrusteeThreshold,
            DisclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            OutcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.SingleWinner,
                "single_winner",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: false,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "highest_non_blank_votes"),
            ApprovedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            ProtocolOmegaVersion: "omega-v1.0.0",
            ReportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            OwnerOptions:
            [
                new ElectionOptionDefinition("option-a", "Alice", "First option", 1, false),
                new ElectionOptionDefinition("option-b", "Bob", "Second option", 2, false),
            ],
            AcknowledgedWarningCodes:
            [
                ElectionWarningCode.AllTrusteesRequiredFragility,
            ],
            RequiredApprovalCount: 3);
}
