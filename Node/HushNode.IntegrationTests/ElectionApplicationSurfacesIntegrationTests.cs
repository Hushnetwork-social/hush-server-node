using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Elections;
using HushNode.IntegrationTests.Infrastructure;
using HushNode.Reactions.Crypto;
using HushServerNode;
using HushServerNode.Testing;
using HushServerNode.Testing.Elections;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Olimpo;
using ReactionECPoint = HushShared.Reactions.Model.ECPoint;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("Integration Tests")]
[Trait("Category", "FEAT-103")]
[Trait("Category", "NON_E2E")]
public sealed class ElectionApplicationSurfacesIntegrationTests : IAsyncLifetime
{
    private static readonly TestIdentity Delta = TestIdentities.GenerateFromSeed("TEST_DELTA_V1", "Delta");
    private static readonly TestIdentity Echo = TestIdentities.GenerateFromSeed("TEST_ECHO_V1", "Echo");
    private static readonly TestIdentity Foxtrot = TestIdentities.GenerateFromSeed("TEST_FOXTROT_V1", "Foxtrot");
    private static readonly TestIdentity Guest = TestIdentities.GenerateFromSeed("TEST_GUEST_V1", "Guest");
    private static readonly IReadOnlyList<TestIdentity> RolloutTrustees =
    [
        TestIdentities.Bob,
        TestIdentities.Charlie,
        Delta,
        Echo,
        Foxtrot,
    ];

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
    public async Task GetElectionHubView_WithMixedTrusteeAndVoterRoles_ReturnsSingleCombinedEntryWithoutRosterAccess()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-103 Mixed Trustee Voter Workspace",
            claimsBeforeOpen:
            [
                new ClaimSetup(TestIdentities.Bob, "voter-bob"),
            ],
            castWithAlice: true);

        await FinalizeElectionAsync(client, context.ElectionId);

        var hubView = await GetElectionHubViewAsync(client, TestIdentities.Bob);
        hubView.Success.Should().BeTrue();
        hubView.HasAnyElectionRoles.Should().BeTrue();
        hubView.Elections.Should().ContainSingle();

        var entry = hubView.Elections[0];
        entry.Election.ElectionId.Should().Be(context.ElectionId);
        entry.ActorRoles.IsOwnerAdmin.Should().BeFalse();
        entry.ActorRoles.IsTrustee.Should().BeTrue();
        entry.ActorRoles.IsVoter.Should().BeTrue();
        entry.ActorRoles.IsDesignatedAuditor.Should().BeFalse();
        entry.CanViewNamedParticipationRoster.Should().BeFalse();
        entry.CanViewReportPackage.Should().BeTrue();
        entry.CanViewParticipantResults.Should().BeTrue();
        entry.HasUnofficialResult.Should().BeTrue();
        entry.HasOfficialResult.Should().BeTrue();
        entry.SuggestedAction.Should().Be(ElectionHubNextActionHintProto.ElectionHubActionVoterReviewResult);

        var eligibilityView = await GetElectionEligibilityViewAsync(client, context.ElectionId, TestIdentities.Bob);
        eligibilityView.ActorRole.Should().Be(ElectionEligibilityActorRoleProto.EligibilityActorLinkedVoter);
        eligibilityView.CanReviewRestrictedRoster.Should().BeFalse();
        eligibilityView.RestrictedRosterEntries.Should().BeEmpty();

        var resultView = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            TestIdentities.Bob,
            waitForOfficialResult: true);
        resultView.CanViewReportPackage.Should().BeTrue();
        resultView.VisibleReportArtifacts.Should().NotContain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanNamedParticipationRoster);
        resultView.VisibleReportArtifacts.Should().NotContain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactMachineNamedParticipationRosterProjection);
    }

    [Fact]
    public async Task ClaimRosterEntry_AfterFinalize_SucceedsAndAddsLateLinkedVoterHubSurface()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-103 Late Claim Linking",
            castWithAlice: true);

        await FinalizeElectionAsync(client, context.ElectionId);

        var beforeClaim = await GetElectionEligibilityViewAsync(client, context.ElectionId, Guest);
        beforeClaim.Success.Should().BeTrue();
        beforeClaim.ActorRole.Should().Be(ElectionEligibilityActorRoleProto.EligibilityActorReadOnly);
        beforeClaim.CanClaimIdentity.Should().BeTrue();
        beforeClaim.CanReviewRestrictedRoster.Should().BeFalse();
        beforeClaim.SelfRosterEntry.Should().BeNull();
        beforeClaim.RestrictedRosterEntries.Should().BeEmpty();

        await ClaimRosterEntryAsync(context.ElectionId, Guest, "voter-guest");

        var afterClaim = await GetElectionEligibilityViewAsync(client, context.ElectionId, Guest);
        afterClaim.Success.Should().BeTrue();
        afterClaim.ActorRole.Should().Be(ElectionEligibilityActorRoleProto.EligibilityActorLinkedVoter);
        afterClaim.CanClaimIdentity.Should().BeFalse();
        afterClaim.SelfRosterEntry.Should().NotBeNull();
        afterClaim.SelfRosterEntry!.OrganizationVoterId.Should().Be("voter-guest");
        afterClaim.SelfRosterEntry.VotingRightStatus.Should().Be(ElectionVotingRightStatusProto.VotingRightActive);
        afterClaim.SelfRosterEntry.ParticipationStatus.Should().Be(
            ElectionParticipationStatusProto.ParticipationDidNotVote);

        var hubView = await GetElectionHubViewAsync(client, Guest);
        hubView.Success.Should().BeTrue();
        hubView.HasAnyElectionRoles.Should().BeTrue();
        hubView.Elections.Should().ContainSingle();
        hubView.Elections[0].ActorRoles.IsVoter.Should().BeTrue();
        hubView.Elections[0].CanViewParticipantResults.Should().BeTrue();
        hubView.Elections[0].HasOfficialResult.Should().BeTrue();
        hubView.Elections[0].SuggestedAction.Should().Be(
            ElectionHubNextActionHintProto.ElectionHubActionVoterReviewResult);

        var resultView = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            Guest,
            waitForOfficialResult: true);
        resultView.CanViewParticipantEncryptedResults.Should().BeTrue();
        resultView.UnofficialResult.Should().NotBeNull();
        resultView.OfficialResult.Should().NotBeNull();
    }

    [Fact]
    public async Task FinalizeElection_AfterTrusteeClose_KeepsStoredFinalizationSharesRedactedAndPreservesHashes()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-106 Finalization Share Redaction",
            castWithAlice: true);

        await FinalizeElectionAsync(client, context.ElectionId);

        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var storedShares = await dbContext.Set<ElectionFinalizationShareRecord>()
            .Where(x => x.ElectionId == new ElectionId(Guid.Parse(context.ElectionId)))
            .OrderBy(x => x.SubmittedAt)
            .ToListAsync();

        storedShares.Should().HaveCount(3);
        storedShares.Should().OnlyContain(x =>
            x.ShareMaterial == ElectionFinalizationShareStorageConstants.RedactedStoredShareMaterial);
        storedShares.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.ShareMaterialHash));
    }

    [Fact]
    public async Task LinkedVoter_CannotReadElectionEnvelopeAccess_AfterClaimLinking()
    {
        var client = await StartClientAsync();
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-103 Voter Envelope Denial");
        var electionId = createResponse.Election.ElectionId;

        await ClaimRosterEntryAsync(electionId, Guest, "voter-guest");

        var envelopeAccess = await GetElectionEnvelopeAccessAsync(client, electionId, Guest);

        envelopeAccess.Success.Should().BeFalse();
        envelopeAccess.ErrorMessage.Should().Contain("not available for actor");
    }

    [Fact]
    public async Task CreateReportAccessGrant_AfterFinalize_GrantsAuditorHubAndRestrictedArtifactAccessOnNextRead()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-103 Auditor Package Review",
            castWithAlice: true);

        await FinalizeElectionAsync(client, context.ElectionId);
        await CreateReportAccessGrantAsync(context.ElectionId, Guest);

        var grantsResponse = await GetElectionReportAccessGrantsAsync(client, context.ElectionId, TestIdentities.Alice);
        grantsResponse.Success.Should().BeTrue();
        grantsResponse.CanManageGrants.Should().BeTrue();
        grantsResponse.DeniedReason.Should().BeEmpty();
        grantsResponse.Grants.Should().ContainSingle(x =>
            x.ActorPublicAddress == Guest.PublicSigningAddress &&
            x.GrantRole == ElectionReportAccessGrantRoleProto.ReportAccessGrantDesignatedAuditor);

        var hubView = await GetElectionHubViewAsync(client, Guest);
        hubView.Success.Should().BeTrue();
        hubView.HasAnyElectionRoles.Should().BeTrue();
        hubView.Elections.Should().ContainSingle();

        var entry = hubView.Elections[0];
        entry.ActorRoles.IsDesignatedAuditor.Should().BeTrue();
        entry.ActorRoles.IsOwnerAdmin.Should().BeFalse();
        entry.ActorRoles.IsTrustee.Should().BeFalse();
        entry.ActorRoles.IsVoter.Should().BeFalse();
        entry.CanViewNamedParticipationRoster.Should().BeTrue();
        entry.CanViewReportPackage.Should().BeTrue();
        entry.CanViewParticipantResults.Should().BeTrue();
        entry.SuggestedAction.Should().Be(ElectionHubNextActionHintProto.ElectionHubActionAuditorReviewPackage);

        var resultView = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            Guest,
            waitForOfficialResult: true);
        resultView.CanViewParticipantEncryptedResults.Should().BeTrue();
        resultView.CanViewReportPackage.Should().BeTrue();
        resultView.UnofficialResult.Should().NotBeNull();
        resultView.OfficialResult.Should().NotBeNull();
        resultView.VisibleReportArtifacts.Should().Contain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanManifest);
        resultView.VisibleReportArtifacts.Should().Contain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanNamedParticipationRoster);
    }

    [Fact]
    public async Task GovernedOpenApproval_BelowThreshold_StaysPendingAndExposesTrusteeHubPrompt()
    {
        var client = await StartClientAsync();
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-096 Pending Governed Open");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await CompleteReadyThresholdAsync(electionId, ceremonyVersionId, requiredCompletionCount: 3);

        var readiness = await client.GetElectionOpenReadinessAsync(new GetElectionOpenReadinessRequest
        {
            ElectionId = electionId,
        });
        readiness.IsReadyToOpen.Should().BeTrue(string.Join(" | ", readiness.ValidationErrors));

        var proposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Open);

        var afterStart = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        var pendingProposal = afterStart.GovernedProposals.Single(x => x.Id == proposalId.ToString());
        afterStart.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Draft);
        pendingProposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.WaitingForApprovals);
        afterStart.GovernedProposalApprovals.Should().BeEmpty();

        var trusteeHub = await GetElectionHubViewAsync(client, TestIdentities.Bob);
        var trusteeEntry = trusteeHub.Elections.Single(x => x.Election.ElectionId == electionId);
        trusteeEntry.ActorRoles.IsTrustee.Should().BeTrue();
        trusteeEntry.SuggestedAction.Should().Be(ElectionHubNextActionHintProto.ElectionHubActionTrusteeApproveGovernedAction);
        trusteeEntry.SuggestedActionReason.Should().Be("A governed open request is awaiting your approval.");

        await ApproveProposalAsync(electionId, proposalId, TestIdentities.Bob);

        var afterFirstApproval = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        pendingProposal = afterFirstApproval.GovernedProposals.Single(x => x.Id == proposalId.ToString());
        afterFirstApproval.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Draft);
        pendingProposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.WaitingForApprovals);
        afterFirstApproval.GovernedProposalApprovals
            .Where(x => x.ProposalId == proposalId.ToString())
            .Select(x => x.TrusteeUserAddress)
            .Should()
            .Equal(TestIdentities.Bob.PublicSigningAddress);
    }

    [Fact]
    public async Task GovernedOpenApproval_AtThreshold_AutoOpensElectionWithoutSecondOwnerTransaction()
    {
        var client = await StartClientAsync();
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-096 Threshold Governed Open");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await CompleteReadyThresholdAsync(electionId, ceremonyVersionId, requiredCompletionCount: 3);

        var readiness = await client.GetElectionOpenReadinessAsync(new GetElectionOpenReadinessRequest
        {
            ElectionId = electionId,
        });
        readiness.IsReadyToOpen.Should().BeTrue(string.Join(" | ", readiness.ValidationErrors));

        var proposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Open);

        await ApproveProposalAsync(electionId, proposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, proposalId, TestIdentities.Charlie);

        var beforeThreshold = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        var pendingProposal = beforeThreshold.GovernedProposals.Single(x => x.Id == proposalId.ToString());
        beforeThreshold.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Draft);
        pendingProposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.WaitingForApprovals);
        beforeThreshold.GovernedProposalApprovals.Should().HaveCount(2);

        await ApproveProposalAsync(electionId, proposalId, Delta);

        var afterThreshold = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        var executedProposal = afterThreshold.GovernedProposals.Single(x => x.Id == proposalId.ToString());
        afterThreshold.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Open);
        afterThreshold.Election.OpenArtifactId.Should().NotBeNullOrWhiteSpace();
        executedProposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatusProto.ExecutionSucceeded);
        afterThreshold.GovernedProposalApprovals
            .Where(x => x.ProposalId == proposalId.ToString())
            .Select(x => x.TrusteeUserAddress)
            .Should()
            .Equal(
                TestIdentities.Bob.PublicSigningAddress,
                TestIdentities.Charlie.PublicSigningAddress,
                Delta.PublicSigningAddress);
    }

    [Fact]
    public async Task GovernedClose_AfterThresholdAndBeforeShares_AllowsImmediateOwnerAndTrusteeReads()
    {
        var client = await StartClientAsync();
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-103 Post-Close Trustee Read");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await CompleteReadyThresholdAsync(electionId, ceremonyVersionId, requiredCompletionCount: 3);

        var openProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Open);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, openProposalId, Delta);

        await ClaimRosterEntryAsync(electionId, TestIdentities.Alice, "voter-alice");
        await RegisterVotingCommitmentAsync(client, electionId, TestIdentities.Alice, "feat103-post-close-commitment");
        var castSubmitResponse = await SubmitAcceptedBallotCastViaBlockchainAsync(
            client,
            electionId,
            TestIdentities.Alice,
            "feat103-post-close-cast");
        castSubmitResponse.Successfull.Should().BeTrue(castSubmitResponse.Message);

        var closeProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Close);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, closeProposalId, Delta);

        var ownerReadTask = ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        var trusteeReadTask = ReloadElectionAsync(client, electionId, Echo);
        var trusteeHubTask = GetElectionHubViewAsync(client, Echo);

        await Task.WhenAll(ownerReadTask, trusteeReadTask, trusteeHubTask);

        var ownerRead = await ownerReadTask;
        ownerRead.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        ownerRead.Election.ClosedProgressStatus.Should().BeOneOf(
            ElectionClosedProgressStatusProto.ClosedProgressTallyCalculationInProgress,
            ElectionClosedProgressStatusProto.ClosedProgressWaitingForTrusteeShares);

        var trusteeRead = await trusteeReadTask;
        trusteeRead.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);

        var trusteeHub = await trusteeHubTask;
        trusteeHub.Success.Should().BeTrue();
        trusteeHub.Elections.Should().ContainSingle(x => x.Election.ElectionId == electionId);
    }

    [Fact]
    public async Task GovernedClose_WhenCloseCountingSessionCreated_ProjectsExecutorSessionMetadata()
    {
        var client = await StartClientAsync();
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-104 Close Counting Executor Metadata");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await CompleteReadyThresholdAsync(electionId, ceremonyVersionId, requiredCompletionCount: 3);

        var openProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Open);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, openProposalId, Delta);

        await ClaimRosterEntryAsync(electionId, TestIdentities.Alice, "voter-alice");
        await RegisterVotingCommitmentAsync(client, electionId, TestIdentities.Alice, "feat104-executor-metadata-commitment");
        var castSubmitResponse = await SubmitAcceptedBallotCastViaBlockchainAsync(
            client,
            electionId,
            TestIdentities.Alice,
            "feat104-executor-metadata-cast",
            useDevModePayload: true);
        castSubmitResponse.Successfull.Should().BeTrue(castSubmitResponse.Message);

        var closeProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Close);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, closeProposalId, Delta);

        var closedElection = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        closedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        var closeCountingSession = closedElection.FinalizationSessions.Should()
            .ContainSingle(x =>
                x.SessionPurpose == ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting &&
                x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares)
            .Subject;
        closeCountingSession.CloseCountingJobId.Should().NotBeNullOrWhiteSpace();
        closeCountingSession.CloseCountingJobStatus.Should().Be(ElectionCloseCountingJobStatusProto.CloseCountingJobAwaitingShares);
        closeCountingSession.ExecutorSessionPublicKey.Should().NotBeNullOrWhiteSpace();
        closeCountingSession.ExecutorKeyAlgorithm.Should().Be("ecies-secp256k1-v1");
    }

    [Fact]
    public async Task GovernedClose_WithDevModeBallotPayload_CompletesCloseCountingAndKeepsReadsHealthy()
    {
        var client = await StartClientAsync();
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-103 Dev-Mode Trustee Close");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await CompleteReadyThresholdAsync(electionId, ceremonyVersionId, requiredCompletionCount: 3);

        var openProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Open);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, openProposalId, Delta);

        await ClaimRosterEntryAsync(electionId, TestIdentities.Alice, "voter-alice");
        await RegisterVotingCommitmentAsync(client, electionId, TestIdentities.Alice, "feat103-dev-close-commitment");
        var castSubmitResponse = await SubmitAcceptedBallotCastViaBlockchainAsync(
            client,
            electionId,
            TestIdentities.Alice,
            "feat103-dev-close-cast",
            useDevModePayload: true);
        castSubmitResponse.Successfull.Should().BeTrue(castSubmitResponse.Message);

        var closeProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Close);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, closeProposalId, Delta);

        var closedElection = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        closedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        closedElection.Election.TallyReadyArtifactId.Should().BeNullOrWhiteSpace();
        closedElection.FinalizationSessions.Should().ContainSingle(x =>
            x.SessionPurpose == ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting &&
            x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares);

        var trusteeReadAfterClose = await ReloadElectionAsync(client, electionId, Echo);
        trusteeReadAfterClose.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        var trusteeHubAfterClose = await GetElectionHubViewAsync(client, Echo);
        trusteeHubAfterClose.Elections.Should().ContainSingle(x => x.Election.ElectionId == electionId);
        var eligibleTrusteeHubAfterClose = await GetElectionHubViewAsync(client, Delta);
        var trusteeHubEntryAfterClose = eligibleTrusteeHubAfterClose.Elections.Should()
            .ContainSingle(x => x.Election.ElectionId == electionId)
            .Subject;
        trusteeHubEntryAfterClose.SuggestedActionReason.Should().Be(
            "Submit the bound trustee tally share for close-counting.");

        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, TestIdentities.Bob);
        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, TestIdentities.Charlie);
        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, Delta);

        var ownerAfterShares = await WaitForTallyReadyElectionAsync(client, electionId, TestIdentities.Alice);
        ownerAfterShares.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        ownerAfterShares.Election.TallyReadyArtifactId.Should().NotBeNullOrWhiteSpace();
        ownerAfterShares.Election.UnofficialResultArtifactId.Should().NotBeNullOrWhiteSpace();
        ownerAfterShares.FinalizationSessions.Should().NotContain(x =>
            x.SessionPurpose == ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting &&
            x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares);

        var trusteeAfterShares = await ReloadElectionAsync(client, electionId, Echo);
        trusteeAfterShares.Election.TallyReadyArtifactId.Should().NotBeNullOrWhiteSpace();
        trusteeAfterShares.Election.UnofficialResultArtifactId.Should().NotBeNullOrWhiteSpace();
        var trusteeHubAfterShares = await GetElectionHubViewAsync(client, Echo);
        trusteeHubAfterShares.Elections.Should().ContainSingle(x => x.Election.ElectionId == electionId);
    }

    [Fact]
    public async Task GovernedClose_WithDevModeBallotPayload_AndPlaceholderTrusteeShares_CompletesCloseCountingAndKeepsReadsHealthy()
    {
        var client = await StartClientAsync();
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-103 Placeholder Trustee Share");
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await CompleteReadyThresholdAsync(electionId, ceremonyVersionId, requiredCompletionCount: 3);

        var openProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Open);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, openProposalId, Delta);

        await ClaimRosterEntryAsync(electionId, TestIdentities.Alice, "voter-alice");
        await RegisterVotingCommitmentAsync(client, electionId, TestIdentities.Alice, "feat103-placeholder-share-commitment");
        var castSubmitResponse = await SubmitAcceptedBallotCastViaBlockchainAsync(
            client,
            electionId,
            TestIdentities.Alice,
            "feat103-placeholder-share-cast",
            useDevModePayload: true);
        castSubmitResponse.Successfull.Should().BeTrue(castSubmitResponse.Message);

        var closeProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Close);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, closeProposalId, Delta);

        var closedElection = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        closedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        closedElection.FinalizationSessions.Should().ContainSingle(x =>
            x.SessionPurpose == ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting &&
            x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares);

        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, TestIdentities.Bob, "aggregate-finalization-share");
        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, TestIdentities.Charlie, "aggregate-finalization-share");
        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, Delta, "aggregate-finalization-share");

        await using (var scope = _node!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
            var storedShares = await dbContext.Set<ElectionFinalizationShareRecord>()
                .Where(x => x.ElectionId == new ElectionId(Guid.Parse(electionId)))
                .OrderBy(x => x.SubmittedAt)
                .ToListAsync();

            storedShares.Should().HaveCount(3);
            storedShares.Should().OnlyContain(x => x.CloseCountingJobId.HasValue);
            storedShares.Should().OnlyContain(x => x.ExecutorKeyAlgorithm == "ecies-secp256k1-v1");
            storedShares.Should().OnlyContain(x => x.ShareMaterial != "aggregate-finalization-share");
        }

        var ownerAfterShares = await WaitForTallyReadyElectionAsync(client, electionId, TestIdentities.Alice);
        ownerAfterShares.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        ownerAfterShares.Election.TallyReadyArtifactId.Should().NotBeNullOrWhiteSpace();
        ownerAfterShares.Election.UnofficialResultArtifactId.Should().NotBeNullOrWhiteSpace();

        await using (var scope = _node!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
            var closeCountingJob = await dbContext.Set<ElectionCloseCountingJobRecord>()
                .SingleAsync(x => x.ElectionId == new ElectionId(Guid.Parse(electionId)));
            var executorEnvelope = await dbContext.Set<ElectionExecutorSessionKeyEnvelopeRecord>()
                .SingleAsync(x => x.CloseCountingJobId == closeCountingJob.Id);
            var storedShares = await dbContext.Set<ElectionFinalizationShareRecord>()
                .Where(x => x.ElectionId == new ElectionId(Guid.Parse(electionId)))
                .OrderBy(x => x.SubmittedAt)
                .ToListAsync();

            executorEnvelope.SealedExecutorSessionPrivateKey
                .Should().Be(CloseCountingExecutorKeyRegistryConstants.DestroyedEnvelopeMarker);
            executorEnvelope.DestroyedAt.Should().NotBeNull();
            executorEnvelope.ExpiresAt.Should().NotBeNull();
            storedShares.Should().HaveCount(3);
            storedShares.Should().OnlyContain(x =>
                x.ShareMaterial == ElectionFinalizationShareStorageConstants.RedactedStoredShareMaterial);
            storedShares.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.ShareMaterialHash));
        }

        var trusteeAfterShares = await ReloadElectionAsync(client, electionId, Echo);
        trusteeAfterShares.Election.TallyReadyArtifactId.Should().NotBeNullOrWhiteSpace();
        trusteeAfterShares.Election.UnofficialResultArtifactId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RevokedPendingTrusteeInvitation_CannotReadElectionEnvelopeAccess_BeforeOrAfterRevocation()
    {
        var client = await StartClientAsync();
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-096 Revoke Envelope Access");
        var electionId = createResponse.Election.ElectionId;
        var parsedElectionId = new ElectionId(Guid.Parse(electionId));

        var (inviteTransaction, invitationId) = TestTransactionFactory.CreateElectionTrusteeInvitation(
            TestIdentities.Alice,
            parsedElectionId,
            Guest);
        (await SubmitBlockchainTransactionAsync(inviteTransaction)).Successfull.Should().BeTrue();

        var accessBeforeRevoke = await GetElectionEnvelopeAccessAsync(client, electionId, Guest);
        accessBeforeRevoke.Success.Should().BeFalse();
        accessBeforeRevoke.ErrorMessage.Should().Contain("not available for actor");

        var revokeResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RevokeElectionTrusteeInvitation(
                TestIdentities.Alice,
                parsedElectionId,
                invitationId));
        revokeResponse.Successfull.Should().BeTrue(revokeResponse.Message);

        var accessAfterRevoke = await GetElectionEnvelopeAccessAsync(client, electionId, Guest);
        accessAfterRevoke.Success.Should().BeFalse();
        accessAfterRevoke.ErrorMessage.Should().Contain("not available for actor");
    }

    [Fact]
    public async Task RejectedPendingTrusteeInvitation_CannotReadElectionEnvelopeAccess_BeforeOrAfterRejection()
    {
        var client = await StartClientAsync();
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, "FEAT-096 Reject Envelope Access");
        var electionId = createResponse.Election.ElectionId;
        var parsedElectionId = new ElectionId(Guid.Parse(electionId));

        var (inviteTransaction, invitationId) = TestTransactionFactory.CreateElectionTrusteeInvitation(
            TestIdentities.Alice,
            parsedElectionId,
            Guest);
        (await SubmitBlockchainTransactionAsync(inviteTransaction)).Successfull.Should().BeTrue();

        var accessBeforeReject = await GetElectionEnvelopeAccessAsync(client, electionId, Guest);
        accessBeforeReject.Success.Should().BeFalse();
        accessBeforeReject.ErrorMessage.Should().Contain("not available for actor");

        var rejectResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RejectElectionTrusteeInvitation(
                Guest,
                parsedElectionId,
                invitationId));
        rejectResponse.Successfull.Should().BeTrue(rejectResponse.Message);

        var accessAfterReject = await GetElectionEnvelopeAccessAsync(client, electionId, Guest);
        accessAfterReject.Success.Should().BeFalse();
        accessAfterReject.ErrorMessage.Should().Contain("not available for actor");
    }

    [Fact]
    public async Task GetElection_UnsignedRead_HidesGovernedAndFinalizationMetadata_WhileSignedOwnerReadKeepsIt()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-096 Signed Operational Metadata",
            castWithAlice: true);

        var publicDetail = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = context.ElectionId,
        });

        publicDetail.Success.Should().BeTrue(publicDetail.ErrorMessage);
        publicDetail.GovernedProposals.Should().BeEmpty();
        publicDetail.GovernedProposalApprovals.Should().BeEmpty();
        publicDetail.FinalizationSessions.Should().BeEmpty();
        publicDetail.FinalizationShares.Should().BeEmpty();
        publicDetail.FinalizationReleaseEvidenceRecords.Should().BeEmpty();

        var signedOwnerDetail = await ReloadElectionAsync(client, context.ElectionId, TestIdentities.Alice);
        signedOwnerDetail.GovernedProposals.Should().NotBeEmpty();
        signedOwnerDetail.FinalizationSessions.Should().ContainSingle(x =>
            x.SessionPurpose == ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting);
        signedOwnerDetail.FinalizationShares.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    private async Task<HushElections.HushElectionsClient> StartClientAsync()
    {
        await DisposeNodeAsync();
        await _fixture!.ResetAllAsync();
        (_node, _blockControl, _grpcFactory) = await _fixture.StartNodeAsync();
        return _grpcFactory.CreateClient<HushElections.HushElectionsClient>();
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

    private async Task<ClosedElectionReadyContext> CreateClosedElectionReadyForFinalizeAsync(
        HushElections.HushElectionsClient client,
        string title,
        IReadOnlyList<ClaimSetup>? claimsBeforeOpen = null,
        bool castWithAlice = false)
    {
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, title);
        var electionId = createResponse.Election.ElectionId;

        if (claimsBeforeOpen is not null)
        {
            foreach (var claim in claimsBeforeOpen)
            {
                await ClaimRosterEntryAsync(electionId, claim.Actor, claim.OrganizationVoterId);
            }
        }

        await InviteAndAcceptRolloutTrusteesAsync(electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await CompleteReadyThresholdAsync(electionId, ceremonyVersionId, requiredCompletionCount: 3);

        var readiness = await client.GetElectionOpenReadinessAsync(new GetElectionOpenReadinessRequest
        {
            ElectionId = electionId,
        });
        readiness.IsReadyToOpen.Should().BeTrue(string.Join(" | ", readiness.ValidationErrors));

        var openProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Open);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, openProposalId, Delta);

        var openElection = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        openElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Open);

        if (castWithAlice)
        {
            await ClaimRosterEntryAsync(electionId, TestIdentities.Alice, "voter-alice");
            await RegisterVotingCommitmentAsync(client, electionId, TestIdentities.Alice, "feat103-commitment-001");
            var castSubmitResponse = await SubmitAcceptedBallotCastViaBlockchainAsync(
                client,
                electionId,
                TestIdentities.Alice,
                "feat103-cast-001");
            castSubmitResponse.Successfull.Should().BeTrue(castSubmitResponse.Message);
        }

        var closeProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Close);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, closeProposalId, Delta);

        var closedElection = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        closedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        closedElection.Election.TallyReadyArtifactId.Should().BeNullOrWhiteSpace();
        closedElection.FinalizationSessions.Should().ContainSingle(x =>
            x.SessionPurpose == ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting &&
            x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares);

        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, TestIdentities.Bob);
        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, TestIdentities.Charlie);
        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, Delta);

        var tallyReadyElection = await WaitForTallyReadyElectionAsync(client, electionId, TestIdentities.Alice);
        tallyReadyElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        tallyReadyElection.Election.TallyReadyArtifactId.Should().NotBeNullOrWhiteSpace();
        tallyReadyElection.Election.UnofficialResultArtifactId.Should().NotBeNullOrWhiteSpace();

        return new ClosedElectionReadyContext(electionId);
    }

    private async Task FinalizeElectionAsync(
        HushElections.HushElectionsClient client,
        string electionId)
    {
        var finalizeProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Finalize);
        await ApproveProposalAsync(electionId, finalizeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, finalizeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, finalizeProposalId, Delta);

        var finalizedElection = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        finalizedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);
        finalizedElection.Election.FinalizeArtifactId.Should().NotBeNullOrWhiteSpace();
        finalizedElection.Election.OfficialResultArtifactId.Should().NotBeNullOrWhiteSpace();
        finalizedElection.FinalizationSessions.Should().NotContain(x =>
            x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares);
    }

    private async Task CreateReportAccessGrantAsync(string electionId, TestIdentity designatedAuditor)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.CreateElectionReportAccessGrant(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                designatedAuditor.PublicSigningAddress));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
    }

    private async Task<ElectionCommandResponse> CreateTrusteeThresholdDraftAsync(
        HushElections.HushElectionsClient client,
        string title)
    {
        var (signedTransaction, electionId) = TestTransactionFactory.CreateElectionDraft(
            TestIdentities.Alice,
            "feat-103 integration draft",
            BuildTrusteeThresholdDraftSpecification(title));
        var submitResponse = await SubmitBlockchainTransactionAsync(signedTransaction);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var importRosterResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.ImportElectionRoster(
                TestIdentities.Alice,
                electionId,
                BuildOpenReadyRosterEntries()));
        importRosterResponse.Successfull.Should().BeTrue(importRosterResponse.Message);

        var response = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId.ToString(),
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
        response.LatestDraftSnapshot.Should().NotBeNull();

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            DraftSnapshot = response.LatestDraftSnapshot,
        };
    }

    private async Task InviteAndAcceptRolloutTrusteesAsync(string electionId)
    {
        foreach (var trustee in RolloutTrustees)
        {
            var (inviteTransaction, invitationId) = TestTransactionFactory.CreateElectionTrusteeInvitation(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                trustee);
            var inviteSubmitResponse = await SubmitBlockchainTransactionAsync(inviteTransaction);
            inviteSubmitResponse.Successfull.Should().BeTrue(inviteSubmitResponse.Message);

            var acceptSubmitResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.AcceptElectionTrusteeInvitation(
                    trustee,
                    new ElectionId(Guid.Parse(electionId)),
                    invitationId));
            acceptSubmitResponse.Successfull.Should().BeTrue(acceptSubmitResponse.Message);
        }
    }

    private async Task<string> StartCeremonyAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string profileId)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.StartElectionCeremony(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                profileId));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        response.Success.Should().BeTrue(response.ErrorMessage);
        return response.CeremonyVersions
            .Where(x => x.ProfileId == profileId)
            .OrderByDescending(x => x.VersionNumber)
            .First()
            .Id;
    }

    private async Task CompleteReadyThresholdAsync(
        string electionId,
        string ceremonyVersionId,
        int requiredCompletionCount)
    {
        const string tallyFingerprint = "feat103-ready-tally-fingerprint";

        for (var index = 0; index < requiredCompletionCount; index++)
        {
            var trustee = RolloutTrustees[index];
            await PublishJoinAndSelfTestAsync(electionId, ceremonyVersionId, trustee, index);

            var submitMaterialResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.SubmitElectionCeremonyMaterial(
                    trustee,
                    new ElectionId(Guid.Parse(electionId)),
                    Guid.Parse(ceremonyVersionId),
                    recipientTrusteeUserAddress: null,
                    messageType: "self_share_package",
                    payloadVersion: "feat103-material-v1",
                    encryptedPayload: $"encrypted-payload-{trustee.DisplayName.ToLowerInvariant()}",
                    payloadFingerprint: $"fingerprint-{trustee.DisplayName.ToLowerInvariant()}"));
            submitMaterialResponse.Successfull.Should().BeTrue(submitMaterialResponse.Message);

            var completeResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.CompleteElectionCeremonyTrustee(
                    TestIdentities.Alice,
                    new ElectionId(Guid.Parse(electionId)),
                    Guid.Parse(ceremonyVersionId),
                    trustee.PublicSigningAddress,
                    shareVersion: $"share-v1-{trustee.DisplayName.ToLowerInvariant()}",
                    tallyPublicKeyFingerprint: tallyFingerprint));
            completeResponse.Successfull.Should().BeTrue(completeResponse.Message);
        }
    }

    private async Task PublishJoinAndSelfTestAsync(
        string electionId,
        string ceremonyVersionId,
        TestIdentity trustee,
        int trusteeIndex)
    {
        var publishResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.PublishElectionCeremonyTransportKey(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                transportPublicKeyFingerprint: $"feat103-transport-{trusteeIndex + 1}"));
        publishResponse.Successfull.Should().BeTrue(publishResponse.Message);

        var joinResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.JoinElectionCeremony(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));
        joinResponse.Successfull.Should().BeTrue(joinResponse.Message);

        var selfTestResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RecordElectionCeremonySelfTestSuccess(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));
        selfTestResponse.Successfull.Should().BeTrue(selfTestResponse.Message);
    }

    private async Task<Guid> StartGovernedProposalAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        ElectionGovernedActionType actionType)
    {
        var (signedTransaction, proposalId) = TestTransactionFactory.StartElectionGovernedProposal(
            TestIdentities.Alice,
            new ElectionId(Guid.Parse(electionId)),
            actionType);
        var submitResponse = await SubmitBlockchainTransactionAsync(signedTransaction);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        response.GovernedProposals.Should().ContainSingle(x => x.Id == proposalId.ToString());
        return proposalId;
    }

    private async Task ApproveProposalAsync(
        string electionId,
        Guid proposalId,
        TestIdentity trustee)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.ApproveElectionGovernedProposal(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                proposalId,
                approvalNote: null));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
    }

    private async Task ClaimRosterEntryAsync(
        string electionId,
        TestIdentity actor,
        string organizationVoterId)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.ClaimElectionRosterEntry(
                actor,
                new ElectionId(Guid.Parse(electionId)),
                organizationVoterId,
                "1111"));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
    }

    private async Task RegisterVotingCommitmentAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string commitmentHash)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RegisterElectionVotingCommitment(
                actor,
                new ElectionId(Guid.Parse(electionId)),
                commitmentHash));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await GetElectionVotingViewAsync(client, electionId, actor);
        response.CommitmentRegistered.Should().BeTrue();
    }

    private async Task<SubmitSignedTransactionReply> SubmitAcceptedBallotCastViaBlockchainAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey,
        bool useDevModePayload = false)
    {
        var signedTransaction = await BuildAcceptedBallotCastTransactionAsync(
            client,
            electionId,
            actor,
            submissionIdempotencyKey,
            useDevModePayload);

        return await SubmitBlockchainTransactionAsync(signedTransaction);
    }

    private async Task<string> BuildAcceptedBallotCastTransactionAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey,
        bool useDevModePayload = false)
    {
        var votingView = await GetElectionVotingViewAsync(client, electionId, actor);

        votingView.CommitmentRegistered.Should().BeTrue();
        votingView.OpenArtifactId.Should().NotBeNullOrWhiteSpace();
        votingView.EligibleSetHash.Should().NotBeNullOrWhiteSpace();
        votingView.CeremonyVersionId.Should().NotBeNullOrWhiteSpace();
        votingView.DkgProfileId.Should().NotBeNullOrWhiteSpace();
        votingView.TallyPublicKeyFingerprint.Should().NotBeNullOrWhiteSpace();

        var selectionCount = votingView.Election.Options.Count;
        selectionCount.Should().BeGreaterThan(0);
        var nonBlankChoiceIndexes = votingView.Election.Options
            .Select((option, index) => new { option, index })
            .Where(x => !x.option.IsBlankOption)
            .Select(x => x.index)
            .ToArray();
        nonBlankChoiceIndexes.Should().NotBeEmpty();

        var choiceIndex = ResolveChoiceIndex(actor, submissionIdempotencyKey, nonBlankChoiceIndexes);
        var selectedOption = votingView.Election.Options[choiceIndex];
        var devArtifactSeed = Guid.NewGuid().ToString();
        var devModeBallotPackage = useDevModePayload
            ? BuildDevModeEncryptedBallotPackage(
                electionId,
                selectedOption.OptionId,
                selectedOption.DisplayLabel,
                selectedOption.ShortDescription,
                selectedOption.BallotOrder,
                selectedOption.IsBlankOption)
            : null;

        return TestTransactionFactory.AcceptElectionBallotCast(
            actor,
            new ElectionId(Guid.Parse(electionId)),
            submissionIdempotencyKey,
            useDevModePayload
                ? devModeBallotPackage!
                : BuildEncryptedBallotPackage(electionId, actor, submissionIdempotencyKey, selectionCount, choiceIndex),
            useDevModePayload
                ? BuildDevModeProofBundle(votingView, selectedOption.OptionId, devModeBallotPackage!)
                : BuildProofBundle(electionId, actor, submissionIdempotencyKey),
            useDevModePayload
                ? BuildDevModeBallotNullifier(electionId, devArtifactSeed)
                : BuildBallotNullifier(electionId, actor, submissionIdempotencyKey),
            Guid.Parse(votingView.OpenArtifactId),
            Convert.FromBase64String(votingView.EligibleSetHash),
            Guid.Parse(votingView.CeremonyVersionId),
            votingView.DkgProfileId,
            votingView.TallyPublicKeyFingerprint);
    }

    private async Task SubmitFinalizationShareViaBlockchainAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity trustee,
        string? shareMaterialOverride = null)
    {
        var currentResponse = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        var session = currentResponse.FinalizationSessions
            .Single(x => x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares);
        var shareIndex = session.EligibleTrustees
            .Select((reference, index) => new { reference.TrusteeUserAddress, ShareIndex = index + 1 })
            .Single(x => x.TrusteeUserAddress == trustee.PublicSigningAddress)
            .ShareIndex;
        var ceremonyVersionId = string.IsNullOrWhiteSpace(session.CeremonySnapshot?.CeremonyVersionId)
            ? (Guid?)null
            : Guid.Parse(session.CeremonySnapshot.CeremonyVersionId);
        var shareMaterial = shareMaterialOverride ?? BuildFinalizationShareMaterial(electionId, trustee, session);

        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionFinalizationShare(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(session.Id),
                shareIndex,
                $"feat103-share-v1-{trustee.DisplayName.ToLowerInvariant()}",
                ElectionFinalizationTargetType.AggregateTally,
                Guid.Parse(session.CloseArtifactId),
                session.AcceptedBallotSetHash.ToByteArray(),
                session.FinalEncryptedTallyHash.ToByteArray(),
                session.TargetTallyId,
                ceremonyVersionId,
                session.CeremonySnapshot?.TallyPublicKeyFingerprint,
                shareMaterial,
                string.IsNullOrWhiteSpace(session.CloseCountingJobId)
                    ? null
                    : Guid.Parse(session.CloseCountingJobId),
                string.IsNullOrWhiteSpace(session.ExecutorSessionPublicKey)
                    ? null
                    : session.ExecutorSessionPublicKey,
                string.IsNullOrWhiteSpace(session.ExecutorKeyAlgorithm)
                    ? null
                    : session.ExecutorKeyAlgorithm));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
    }

    private async Task<GetElectionHubViewResponse> GetElectionHubViewAsync(
        HushElections.HushElectionsClient client,
        TestIdentity actor)
    {
        var request = new GetElectionHubViewRequest
        {
            ActorPublicAddress = actor.PublicSigningAddress,
        };
        var response = await client.GetElectionHubViewAsync(
            request,
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.GetElectionHubView),
                actor,
                new Dictionary<string, object?>
                {
                    ["ActorPublicAddress"] = request.ActorPublicAddress,
                }));

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response;
    }

    private async Task<GetElectionEligibilityViewResponse> GetElectionEligibilityViewAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor)
    {
        var request = new GetElectionEligibilityViewRequest
        {
            ElectionId = electionId,
            ActorPublicAddress = actor.PublicSigningAddress,
        };
        var response = await client.GetElectionEligibilityViewAsync(
            request,
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.GetElectionEligibilityView),
                actor,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = request.ElectionId,
                    ["ActorPublicAddress"] = request.ActorPublicAddress,
                }));

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response;
    }

    private async Task<GetElectionReportAccessGrantsResponse> GetElectionReportAccessGrantsAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor)
    {
        var request = new GetElectionReportAccessGrantsRequest
        {
            ElectionId = electionId,
            ActorPublicAddress = actor.PublicSigningAddress,
        };
        var response = await client.GetElectionReportAccessGrantsAsync(
            request,
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.GetElectionReportAccessGrants),
                actor,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = request.ElectionId,
                    ["ActorPublicAddress"] = request.ActorPublicAddress,
                }));

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response;
    }

    private async Task<GetElectionVotingViewResponse> GetElectionVotingViewAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey = "")
    {
        async Task<GetElectionVotingViewResponse> QueryAsync()
        {
            var request = new GetElectionVotingViewRequest
            {
                ElectionId = electionId,
                ActorPublicAddress = actor.PublicSigningAddress,
                SubmissionIdempotencyKey = submissionIdempotencyKey,
            };

            return await client.GetElectionVotingViewAsync(
                request,
                headers: CreateSignedElectionQueryHeaders(
                    nameof(HushElections.HushElectionsClient.GetElectionVotingView),
                    actor,
                    new Dictionary<string, object?>
                    {
                        ["ElectionId"] = request.ElectionId,
                        ["ActorPublicAddress"] = request.ActorPublicAddress,
                        ["SubmissionIdempotencyKey"] = request.SubmissionIdempotencyKey,
                    }));
        }

        var response = await QueryAsync();
        for (var attempt = 0; attempt < 20 && !response.Success; attempt++)
        {
            await Task.Delay(100);
            response = await QueryAsync();
        }

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response;
    }

    private async Task<GetElectionResultViewResponse> GetElectionResultViewAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        bool waitForOfficialResult = false)
    {
        async Task<GetElectionResultViewResponse> QueryAsync()
        {
            var request = new GetElectionResultViewRequest
            {
                ElectionId = electionId,
                ActorPublicAddress = actor.PublicSigningAddress,
            };

            return await client.GetElectionResultViewAsync(
                request,
                headers: CreateSignedElectionQueryHeaders(
                    nameof(HushElections.HushElectionsClient.GetElectionResultView),
                    actor,
                    new Dictionary<string, object?>
                    {
                        ["ElectionId"] = request.ElectionId,
                        ["ActorPublicAddress"] = request.ActorPublicAddress,
                    }));
        }

        var response = await QueryAsync();
        for (var attempt = 0;
             attempt < 20 &&
             (waitForOfficialResult && string.IsNullOrWhiteSpace(response.OfficialResult?.Id));
             attempt++)
        {
            await Task.Delay(100);
            response = await QueryAsync();
        }

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response;
    }

    private async Task<GetElectionEnvelopeAccessResponse> GetElectionEnvelopeAccessAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor)
    {
        var request = new GetElectionEnvelopeAccessRequest
        {
            ElectionId = electionId,
            ActorPublicAddress = actor.PublicSigningAddress,
        };

        return await client.GetElectionEnvelopeAccessAsync(
            request,
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.GetElectionEnvelopeAccess),
                actor,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = request.ElectionId,
                    ["ActorPublicAddress"] = request.ActorPublicAddress,
                }));
    }

    private async Task<GetElectionResponse> ReloadElectionAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity? actor = null)
    {
        var request = new GetElectionRequest
        {
            ElectionId = electionId,
        };
        GetElectionResponse response;
        if (actor is null)
        {
            response = await client.GetElectionAsync(request);
        }
        else
        {
            response = await client.GetElectionAsync(
                request,
                headers: CreateSignedElectionQueryHeaders(
                    nameof(HushElections.HushElectionsClient.GetElection),
                    actor,
                    new Dictionary<string, object?>
                    {
                        ["ElectionId"] = request.ElectionId,
                    }));
        }

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response;
    }

    private async Task<GetElectionResponse> WaitForTallyReadyElectionAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor)
    {
        var response = await ReloadElectionAsync(client, electionId, actor);

        for (var attempt = 0;
             attempt < 20 &&
             string.IsNullOrWhiteSpace(response.Election.TallyReadyArtifactId);
             attempt++)
        {
            await Task.Delay(100);
            response = await ReloadElectionAsync(client, electionId, actor);
        }

        return response;
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

    private static Metadata CreateSignedElectionQueryHeaders(
        string method,
        TestIdentity actor,
        IReadOnlyDictionary<string, object?> request)
    {
        var signedAt = DateTimeOffset.UtcNow.ToString("O");
        var payload = BuildSignedElectionQueryPayload(
            method,
            actor.PublicSigningAddress,
            signedAt,
            request);

        return new Metadata
        {
            { "x-hush-election-query-signatory", actor.PublicSigningAddress },
            { "x-hush-election-query-signed-at", signedAt },
            { "x-hush-election-query-signature", DigitalSignature.SignMessageCompactBase64(payload, actor.PrivateSigningKey) },
        };
    }

    private static string BuildSignedElectionQueryPayload(
        string method,
        string actorAddress,
        string signedAt,
        IReadOnlyDictionary<string, object?> request)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["actorAddress"] = actorAddress,
            ["method"] = method,
            ["request"] = DeepSortElectionQueryValue(request),
            ["signedAt"] = signedAt,
        };

        return JsonSerializer.Serialize(payload);
    }

    private static object? DeepSortElectionQueryValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            var sortedDictionary = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in readOnlyDictionary)
            {
                sortedDictionary[entry.Key] = DeepSortElectionQueryValue(entry.Value);
            }

            return sortedDictionary;
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            var sortedDictionary = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in dictionary)
            {
                sortedDictionary[entry.Key] = DeepSortElectionQueryValue(entry.Value);
            }

            return sortedDictionary;
        }

        if (value is IEnumerable<object?> sequence && value is not string)
        {
            return sequence.Select(DeepSortElectionQueryValue).ToArray();
        }

        return value;
    }

    private static string BuildEncryptedBallotPackage(
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey,
        int selectionCount,
        int choiceIndex)
    {
        var curve = new BabyJubJubCurve();
        var publicKeySeed = ParseSeedToScalar($"feat103:public-key:{electionId}", curve.Order);
        var nonceSeed = ParseSeedToScalar(
            $"feat103:nonces:{electionId}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}",
            curve.Order);
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(publicKeySeed, curve);
        var ballot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: $"feat103-ballot:{electionId}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}",
            choiceIndex: choiceIndex,
            publicKey: keyPair.PublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(
                nonceSeed,
                selectionCount,
                curve),
            selectionCount: selectionCount,
            curve: curve);

        var payload = new PublishedElectionBallotPackage(
            Version: "election-ballot.v1",
            PublicKey: ToPublishedPoint(keyPair.PublicKey),
            SelectionCount: selectionCount,
            Ciphertext: new PublishedElectionCiphertext(
                ballot.Slots.Select(slot => ToPublishedPoint(slot.C1)).ToArray(),
                ballot.Slots.Select(slot => ToPublishedPoint(slot.C2)).ToArray()));

        return JsonSerializer.Serialize(payload);
    }

    private static string BuildProofBundle(
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey) =>
        JsonSerializer.Serialize(new PublishedElectionProofBundle(
            Version: "integration-proof-bundle.v1",
            Actor: actor.PublicSigningAddress,
            ElectionId: electionId,
            SubmissionIdempotencyKey: submissionIdempotencyKey.Trim()));

    private static string BuildDevModeEncryptedBallotPackage(
        string electionId,
        string optionId,
        string optionLabel,
        string? optionDescription,
        int ballotOrder,
        bool isBlankOption)
    {
        var selectionFingerprint = ComputeLowerHexSha256(
            $"election-dev-selection:v1:{electionId}:{optionId}:{optionLabel}");

        return JsonSerializer.Serialize(new
        {
            mode = "election-dev-mode-v1",
            packageType = "dev-protected-ballot",
            electionId,
            optionId,
            optionLabel,
            optionDescription = optionDescription ?? string.Empty,
            ballotOrder,
            isBlankOption,
            selectionFingerprint,
        });
    }

    private static string BuildDevModeProofBundle(
        GetElectionVotingViewResponse votingView,
        string optionId,
        string ballotPackage)
    {
        var electionId = votingView.Election.ElectionId;
        var ballotPackageHash = ComputeLowerHexSha256(ballotPackage);

        return JsonSerializer.Serialize(new
        {
            mode = "election-dev-mode-v1",
            proofType = "dev-election-proof",
            electionId,
            optionId,
            ballotPackageHash,
            openArtifactId = votingView.OpenArtifactId,
            eligibleSetHash = votingView.EligibleSetHash,
            ceremonyVersionId = votingView.CeremonyVersionId,
            dkgProfileId = votingView.DkgProfileId,
            tallyPublicKeyFingerprint = votingView.TallyPublicKeyFingerprint,
        });
    }

    private static string BuildBallotNullifier(
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"feat103:ballot-nullifier:{electionId}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}")));

    private static string BuildDevModeBallotNullifier(string electionId, string devArtifactSeed) =>
        ComputeLowerHexSha256($"election-dev-nullifier:v2:{electionId}:{devArtifactSeed}:nullifier");

    private static int ResolveChoiceIndex(
        TestIdentity actor,
        string submissionIdempotencyKey,
        IReadOnlyList<int> availableChoiceIndexes)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"feat103:choice-index:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}"));
        var scalar = new BigInteger(digest, isUnsigned: true, isBigEndian: true);
        return availableChoiceIndexes[(int)(scalar % availableChoiceIndexes.Count)];
    }

    private static string ComputeLowerHexSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private static string BuildFinalizationShareMaterial(
        string electionId,
        TestIdentity trustee,
        ElectionFinalizationSession session)
    {
        var curve = new BabyJubJubCurve();
        var publicKeySeed = ParseSeedToScalar($"feat103:public-key:{electionId}", curve.Order);
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(publicKeySeed, curve);
        var thresholdSeed = keyPair.PrivateKey - 7920;
        var trusteeIds = ImmutableArray.CreateRange(session.EligibleTrustees.Select(x => x.TrusteeUserAddress));
        var thresholdDefinition = new ControlledElectionThresholdDefinition(
            electionId,
            trusteeIds,
            session.RequiredShareCount);
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            thresholdDefinition,
            session.Id,
            session.TargetTallyId,
            thresholdSeed,
            curve);

        return thresholdSetup.Shares
            .Single(x => string.Equals(x.TrusteeId, trustee.PublicSigningAddress, StringComparison.Ordinal))
            .ShareMaterial;
    }

    private static BigInteger ParseSeedToScalar(string seed, BigInteger order)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var scalar = new BigInteger(digest, isUnsigned: true, isBigEndian: true) % order;
        return scalar == BigInteger.Zero ? BigInteger.One : scalar;
    }

    private static PublishedElectionPointPayload ToPublishedPoint(ReactionECPoint point) =>
        new(
            point.X.ToString(CultureInfo.InvariantCulture),
            point.Y.ToString(CultureInfo.InvariantCulture));

    private static IReadOnlyList<ElectionRosterImportItem> BuildOpenReadyRosterEntries() =>
    [
        new ElectionRosterImportItem("voter-alice", ElectionRosterContactType.Email, "alice.eligibility@hush.test"),
        new ElectionRosterImportItem("voter-bob", ElectionRosterContactType.Phone, "+15550001002", IsInitiallyActive: false),
        new ElectionRosterImportItem("voter-charlie", ElectionRosterContactType.Email, "charlie.eligibility@hush.test"),
        new ElectionRosterImportItem("voter-guest", ElectionRosterContactType.Email, "guest.eligibility@hush.test"),
    ];

    private static ElectionDraftSpecification BuildTrusteeThresholdDraftSpecification(string title) =>
        new(
            Title: title,
            ShortDescription: "Shared HushVoting surface validation",
            ExternalReferenceCode: "REF-2026-103",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: ElectionBindingStatus.Binding,
            GovernanceMode: ElectionGovernanceMode.TrusteeThreshold,
            DisclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            OutcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.PassFail,
                "pass_fail_yes_no",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: true,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "simple_majority_of_non_blank_votes"),
            ApprovedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            ProtocolOmegaVersion: "omega-v1.0.0",
            ReportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            OwnerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve the proposal", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject the proposal", 2, false),
            ],
            AcknowledgedWarningCodes:
            [
                ElectionWarningCode.AllTrusteesRequiredFragility,
            ],
            RequiredApprovalCount: 3,
            OfficialResultVisibilityPolicy: OfficialResultVisibilityPolicy.ParticipantEncryptedOnly);

    private sealed record ClaimSetup(TestIdentity Actor, string OrganizationVoterId);

    private sealed record ClosedElectionReadyContext(string ElectionId);

    private sealed record PublishedElectionBallotPackage(
        string Version,
        PublishedElectionPointPayload PublicKey,
        int SelectionCount,
        PublishedElectionCiphertext Ciphertext);

    private sealed record PublishedElectionCiphertext(
        PublishedElectionPointPayload[] C1,
        PublishedElectionPointPayload[] C2);

    private sealed record PublishedElectionPointPayload(
        string X,
        string Y);

    private sealed record PublishedElectionProofBundle(
        string Version,
        string Actor,
        string ElectionId,
        string SubmissionIdempotencyKey);
}
