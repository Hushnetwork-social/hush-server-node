using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushNode.Reactions.Crypto;
using HushServerNode;
using HushServerNode.Testing;
using HushServerNode.Testing.Elections;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReactionECPoint = HushShared.Reactions.Model.ECPoint;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("Integration Tests")]
[Trait("Category", "FEAT-102")]
public sealed class ElectionReportPackageIntegrationTests : IAsyncLifetime
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
    public async Task FinalizeElection_WithSealedPackage_ExposesRoleScopedArtifactsAcrossResultViews()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-102 Sealed Review Package");

        var finalizeProposalId = await StartGovernedProposalAsync(
            client,
            context.ElectionId,
            ElectionGovernedActionType.Finalize);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, Delta);

        var finalizedElection = await ReloadElectionAsync(client, context.ElectionId);
        finalizedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);
        finalizedElection.Election.FinalizeArtifactId.Should().NotBeNullOrWhiteSpace();
        finalizedElection.Election.OfficialResultArtifactId.Should().NotBeNullOrWhiteSpace();

        var ownerResult = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            waitForOfficialResult: true);
        ownerResult.CanViewReportPackage.Should().BeTrue();
        ownerResult.CanRetryFailedPackageFinalization.Should().BeFalse();
        ownerResult.LatestReportPackage.Should().NotBeNull();
        ownerResult.LatestReportPackage!.Status.Should().Be(ElectionReportPackageStatusProto.ReportPackageSealed);
        ownerResult.LatestReportPackage.ArtifactCount.Should().Be(13);
        ownerResult.VisibleReportArtifacts.Should().HaveCount(13);
        ownerResult.VisibleReportArtifacts.Should().Contain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanNamedParticipationRoster);
        ownerResult.VisibleReportArtifacts.Should().Contain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactMachineNamedParticipationRosterProjection);
        var ownerAuditArtifact = ownerResult.VisibleReportArtifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanAuditProvenanceReport);
        ownerAuditArtifact.Content.Should().Contain("AllTrusteesRequiredFragility");
        ownerAuditArtifact.Content.Should().Contain("Tally public key fingerprint");
        ownerAuditArtifact.Content.Should().Contain("Governed Finalization Approvals");
        ownerAuditArtifact.Content.Should().Contain("Finalization Share Evidence");
        ownerAuditArtifact.Content.Should().Contain("Official result hash");

        var trusteeResult = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            TestIdentities.Bob,
            waitForOfficialResult: true);
        trusteeResult.CanViewReportPackage.Should().BeTrue();
        trusteeResult.VisibleReportArtifacts.Should().HaveCount(11);
        trusteeResult.VisibleReportArtifacts.Should().NotContain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanNamedParticipationRoster);
        trusteeResult.VisibleReportArtifacts.Should().NotContain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactMachineNamedParticipationRosterProjection);
        trusteeResult.VisibleReportArtifacts.Should().OnlyContain(x =>
            x.AccessScope == ElectionReportArtifactAccessScopeProto.ReportArtifactOwnerAuditorTrustee);

        var participantResult = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            Guest,
            waitForOfficialResult: true);
        participantResult.CanViewReportPackage.Should().BeFalse();
        participantResult.CanRetryFailedPackageFinalization.Should().BeFalse();
        participantResult.VisibleReportArtifacts.Should().BeEmpty();
        participantResult.OfficialResult.Should().NotBeNull();

        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var packages = await dbContext.Set<ElectionReportPackageRecord>()
            .Where(x => x.ElectionId == new ElectionId(Guid.Parse(context.ElectionId)))
            .OrderBy(x => x.AttemptNumber)
            .ToListAsync();
        packages.Should().ContainSingle();
        packages.Single().Status.Should().Be(ElectionReportPackageStatus.Sealed);
        packages.Single().ArtifactCount.Should().Be(13);

        var artifacts = await dbContext.Set<ElectionReportArtifactRecord>()
            .Where(x => x.ReportPackageId == packages.Single().Id)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();
        artifacts.Should().HaveCount(13);
        artifacts.Should().Contain(x => x.ArtifactKind == ElectionReportArtifactKind.HumanManifest);
        artifacts.Should().Contain(x => x.ArtifactKind == ElectionReportArtifactKind.MachineEvidenceGraph);
        artifacts.Should().Contain(x => x.ArtifactKind == ElectionReportArtifactKind.HumanNamedParticipationRoster);
    }

    [Fact]
    public async Task FinalizeElection_WhenPackageGenerationFails_PersistsFailedAttemptAndRetrySealsANewAttempt()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-102 Failed Attempt Retry");

        await CorruptCloseEligibilitySnapshotBoundaryAsync(context.ElectionId);

        var finalizeProposalId = await StartGovernedProposalAsync(
            client,
            context.ElectionId,
            ElectionGovernedActionType.Finalize);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, Delta);

        var failedElection = await ReloadElectionAsync(client, context.ElectionId);
        failedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        failedElection.Election.FinalizeArtifactId.Should().BeNullOrWhiteSpace();
        failedElection.GovernedProposals.Should().ContainSingle(x =>
            x.Id == finalizeProposalId.ToString() &&
            x.ActionType == ElectionGovernedActionTypeProto.GovernedActionFinalize &&
            x.ExecutionStatus == ElectionGovernedProposalExecutionStatusProto.ExecutionFailed);

        var failedOwnerResult = await GetElectionResultViewAsync(client, context.ElectionId, TestIdentities.Alice);
        failedOwnerResult.CanViewReportPackage.Should().BeTrue();
        failedOwnerResult.CanRetryFailedPackageFinalization.Should().BeTrue();
        failedOwnerResult.LatestReportPackage.Should().NotBeNull();
        failedOwnerResult.LatestReportPackage!.Status.Should().Be(
            ElectionReportPackageStatusProto.ReportPackageGenerationFailed);
        failedOwnerResult.VisibleReportArtifacts.Should().BeEmpty();

        await using (var scope = _node!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
            var failedPackages = await dbContext.Set<ElectionReportPackageRecord>()
                .Where(x => x.ElectionId == new ElectionId(Guid.Parse(context.ElectionId)))
                .OrderBy(x => x.AttemptNumber)
                .ToListAsync();
            failedPackages.Should().ContainSingle();
            failedPackages.Single().Status.Should().Be(ElectionReportPackageStatus.GenerationFailed);
            failedPackages.Single().FailureReason.Should().Contain("Close eligibility snapshot");
        }

        var electionAfterFailure = await ReloadElectionAsync(client, context.ElectionId);
        await RestoreCloseEligibilitySnapshotBoundaryAsync(
            context.ElectionId,
            Guid.Parse(electionAfterFailure.Election.CloseArtifactId));

        await RetryProposalExecutionAsync(client, context.ElectionId, finalizeProposalId);

        var finalizedElection = await ReloadElectionAsync(client, context.ElectionId);
        finalizedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);
        finalizedElection.Election.FinalizeArtifactId.Should().NotBeNullOrWhiteSpace();

        var retriedOwnerResult = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            waitForOfficialResult: true);
        retriedOwnerResult.CanRetryFailedPackageFinalization.Should().BeFalse();
        retriedOwnerResult.LatestReportPackage.Should().NotBeNull();
        retriedOwnerResult.LatestReportPackage!.Status.Should().Be(
            ElectionReportPackageStatusProto.ReportPackageSealed);
        retriedOwnerResult.VisibleReportArtifacts.Should().HaveCount(13);

        await using (var scope = _node!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
            var packages = await dbContext.Set<ElectionReportPackageRecord>()
                .Where(x => x.ElectionId == new ElectionId(Guid.Parse(context.ElectionId)))
                .OrderBy(x => x.AttemptNumber)
                .ToListAsync();
            packages.Should().HaveCount(2);
            packages[0].Status.Should().Be(ElectionReportPackageStatus.GenerationFailed);
            packages[1].Status.Should().Be(ElectionReportPackageStatus.Sealed);
            packages[1].AttemptNumber.Should().Be(2);
            packages[1].PreviousAttemptId.Should().Be(packages[0].Id);
            packages[1].FrozenEvidenceHash.Should().Equal(packages[0].FrozenEvidenceHash);
        }
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
        string title)
    {
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, title);
        var electionId = createResponse.Election.ElectionId;

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

        var openElection = await ReloadElectionAsync(client, electionId);
        openElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Open);

        await ClaimRosterEntryAsync(electionId, TestIdentities.Alice, "voter-alice");
        await ClaimRosterEntryAsync(electionId, Guest, "voter-guest");
        await RegisterVotingCommitmentAsync(client, electionId, TestIdentities.Alice, "feat102-commitment-001");
        var castSubmitResponse = await SubmitAcceptedBallotCastViaBlockchainAsync(
            client,
            electionId,
            TestIdentities.Alice,
            "feat102-cast-001");
        castSubmitResponse.Successfull.Should().BeTrue(castSubmitResponse.Message);

        var closeProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Close);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, closeProposalId, Delta);

        var closedElection = await ReloadElectionAsync(client, electionId);
        closedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        closedElection.Election.TallyReadyArtifactId.Should().BeNullOrWhiteSpace();
        closedElection.FinalizationSessions.Should().ContainSingle(x =>
            x.SessionPurpose == ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting &&
            x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares);

        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, TestIdentities.Bob);
        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, TestIdentities.Charlie);
        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, Delta);

        var tallyReadyElection = await ReloadElectionAsync(client, electionId);
        tallyReadyElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        tallyReadyElection.Election.TallyReadyArtifactId.Should().NotBeNullOrWhiteSpace();
        tallyReadyElection.Election.UnofficialResultArtifactId.Should().NotBeNullOrWhiteSpace();

        return new ClosedElectionReadyContext(electionId);
    }

    private async Task<ElectionCommandResponse> CreateTrusteeThresholdDraftAsync(
        HushElections.HushElectionsClient client,
        string title)
    {
        var (signedTransaction, electionId) = TestTransactionFactory.CreateElectionDraft(
            TestIdentities.Alice,
            "feat-102 integration draft",
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

        var response = await ReloadElectionAsync(client, electionId);
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
        const string tallyFingerprint = "feat102-ready-tally-fingerprint";

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
                    messageType: "dkg-share-package",
                    payloadVersion: "omega-v1.0.0",
                    encryptedPayload: $"feat102-payload-{index}",
                    payloadFingerprint: $"feat102-payload-fingerprint-{index}"));
            submitMaterialResponse.Successfull.Should().BeTrue(submitMaterialResponse.Message);

            var completeTrusteeResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.CompleteElectionCeremonyTrustee(
                    TestIdentities.Alice,
                    new ElectionId(Guid.Parse(electionId)),
                    Guid.Parse(ceremonyVersionId),
                    trustee.PublicSigningAddress,
                    $"feat102-share-v1-{index}",
                    tallyFingerprint));
            completeTrusteeResponse.Successfull.Should().BeTrue(completeTrusteeResponse.Message);
        }
    }

    private async Task PublishJoinAndSelfTestAsync(
        string electionId,
        string ceremonyVersionId,
        TestIdentity trustee,
        int index)
    {
        var publishResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.PublishElectionCeremonyTransportKey(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                $"feat102-transport-fingerprint-{index}"));
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

        var response = await ReloadElectionAsync(client, electionId);
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

    private async Task RetryProposalExecutionAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        Guid proposalId)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RetryElectionGovernedProposalExecution(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                proposalId));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await ReloadElectionAsync(client, electionId);
        response.GovernedProposals.Should().ContainSingle(x => x.Id == proposalId.ToString());
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
        string submissionIdempotencyKey)
    {
        var signedTransaction = await BuildAcceptedBallotCastTransactionAsync(
            client,
            electionId,
            actor,
            submissionIdempotencyKey);

        return await SubmitBlockchainTransactionAsync(signedTransaction);
    }

    private async Task<string> BuildAcceptedBallotCastTransactionAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey)
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

        return TestTransactionFactory.AcceptElectionBallotCast(
            actor,
            new ElectionId(Guid.Parse(electionId)),
            submissionIdempotencyKey,
            BuildEncryptedBallotPackage(electionId, actor, submissionIdempotencyKey, selectionCount, choiceIndex),
            BuildProofBundle(electionId, actor, submissionIdempotencyKey),
            BuildBallotNullifier(electionId, actor, submissionIdempotencyKey),
            Guid.Parse(votingView.OpenArtifactId),
            Convert.FromBase64String(votingView.EligibleSetHash),
            Guid.Parse(votingView.CeremonyVersionId),
            votingView.DkgProfileId,
            votingView.TallyPublicKeyFingerprint);
    }

    private async Task SubmitFinalizationShareViaBlockchainAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity trustee)
    {
        var currentResponse = await ReloadElectionAsync(client, electionId);
        var session = currentResponse.FinalizationSessions
            .Single(x => x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares);
        var shareIndex = session.EligibleTrustees
            .Select((reference, index) => new { reference.TrusteeUserAddress, ShareIndex = index + 1 })
            .Single(x => x.TrusteeUserAddress == trustee.PublicSigningAddress)
            .ShareIndex;
        var ceremonyVersionId = string.IsNullOrWhiteSpace(session.CeremonySnapshot?.CeremonyVersionId)
            ? (Guid?)null
            : Guid.Parse(session.CeremonySnapshot.CeremonyVersionId);
        var shareMaterial = BuildFinalizationShareMaterial(electionId, trustee, session);

        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionFinalizationShare(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(session.Id),
                shareIndex,
                $"feat102-share-v1-{trustee.DisplayName.ToLowerInvariant()}",
                ElectionFinalizationTargetType.AggregateTally,
                Guid.Parse(session.CloseArtifactId),
                session.AcceptedBallotSetHash.ToByteArray(),
                session.FinalEncryptedTallyHash.ToByteArray(),
                session.TargetTallyId,
                ceremonyVersionId,
                session.CeremonySnapshot?.TallyPublicKeyFingerprint,
                shareMaterial));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
    }

    private async Task<GetElectionVotingViewResponse> GetElectionVotingViewAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey = "")
    {
        async Task<GetElectionVotingViewResponse> QueryAsync() =>
            await client.GetElectionVotingViewAsync(new GetElectionVotingViewRequest
            {
                ElectionId = electionId,
                ActorPublicAddress = actor.PublicSigningAddress,
                SubmissionIdempotencyKey = submissionIdempotencyKey,
            });

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
        async Task<GetElectionResultViewResponse> QueryAsync() =>
            await client.GetElectionResultViewAsync(new GetElectionResultViewRequest
            {
                ElectionId = electionId,
                ActorPublicAddress = actor.PublicSigningAddress,
            });

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

    private async Task<GetElectionResponse> ReloadElectionAsync(
        HushElections.HushElectionsClient client,
        string electionId)
    {
        var response = await client.GetElectionAsync(new GetElectionRequest
        {
            ElectionId = electionId,
        });

        response.Success.Should().BeTrue(response.ErrorMessage);
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

    private async Task CorruptCloseEligibilitySnapshotBoundaryAsync(string electionId)
    {
        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var electionKey = new ElectionId(Guid.Parse(electionId));
        var snapshot = await dbContext.Set<ElectionEligibilitySnapshotRecord>()
            .SingleAsync(x =>
                x.ElectionId == electionKey &&
                x.SnapshotType == ElectionEligibilitySnapshotType.Close);

        var updated = snapshot with
        {
            BoundaryArtifactId = Guid.NewGuid(),
        };

        dbContext.Entry(snapshot).State = EntityState.Detached;
        dbContext.Update(updated);
        await dbContext.SaveChangesAsync();
    }

    private async Task RestoreCloseEligibilitySnapshotBoundaryAsync(string electionId, Guid closeArtifactId)
    {
        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var electionKey = new ElectionId(Guid.Parse(electionId));
        var snapshot = await dbContext.Set<ElectionEligibilitySnapshotRecord>()
            .SingleAsync(x =>
                x.ElectionId == electionKey &&
                x.SnapshotType == ElectionEligibilitySnapshotType.Close);

        var updated = snapshot with
        {
            BoundaryArtifactId = closeArtifactId,
        };

        dbContext.Entry(snapshot).State = EntityState.Detached;
        dbContext.Update(updated);
        await dbContext.SaveChangesAsync();
    }

    private static string BuildEncryptedBallotPackage(
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey,
        int selectionCount,
        int choiceIndex)
    {
        var curve = new BabyJubJubCurve();
        var publicKeySeed = ParseSeedToScalar($"feat100:public-key:{electionId}", curve.Order);
        var nonceSeed = ParseSeedToScalar(
            $"feat100:nonces:{electionId}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}",
            curve.Order);
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(publicKeySeed, curve);
        var ballot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: $"feat100-ballot:{electionId}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}",
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

    private static string BuildBallotNullifier(
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"feat099:ballot-nullifier:{electionId}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}")));

    private static int ResolveChoiceIndex(
        TestIdentity actor,
        string submissionIdempotencyKey,
        IReadOnlyList<int> availableChoiceIndexes)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"feat100:choice-index:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}"));
        var scalar = new BigInteger(digest, isUnsigned: true, isBigEndian: true);
        return availableChoiceIndexes[(int)(scalar % availableChoiceIndexes.Count)];
    }

    private static string BuildFinalizationShareMaterial(
        string electionId,
        TestIdentity trustee,
        ElectionFinalizationSession session)
    {
        var curve = new BabyJubJubCurve();
        var publicKeySeed = ParseSeedToScalar($"feat100:public-key:{electionId}", curve.Order);
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
            ShortDescription: "Governed report package vote",
            ExternalReferenceCode: "REF-2026-102",
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
            OfficialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

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
