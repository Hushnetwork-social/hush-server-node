using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionLifecycleServiceTests
{
    [Fact]
    public async Task CreateDraftAsync_WithValidRequest_PersistsDraftHistoryAndWarnings()
    {
        var store = new ElectionStore();
        var service = CreateService(store);

        var result = await service.CreateDraftAsync(new CreateElectionDraftRequest(
            OwnerPublicAddress: "owner-address",
            ActorPublicAddress: "owner-address",
            SnapshotReason: "initial draft",
            Draft: CreateAdminDraftSpecification(
                acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet])));

        result.IsSuccess.Should().BeTrue();
        result.Election.Should().NotBeNull();
        result.DraftSnapshot.Should().NotBeNull();
        store.Elections.Should().ContainSingle();
        store.DraftSnapshots.Should().ContainSingle();
        store.WarningAcknowledgements.Should().ContainSingle();
        store.WarningAcknowledgements[0].WarningCode.Should().Be(ElectionWarningCode.LowAnonymitySet);
        store.DraftSnapshots[0].DraftRevision.Should().Be(1);
    }

    [Fact]
    public async Task CreateDraftAsync_WithNonOwnerActor_ReturnsForbidden()
    {
        var service = CreateService(new ElectionStore());

        var result = await service.CreateDraftAsync(new CreateElectionDraftRequest(
            OwnerPublicAddress: "owner-address",
            ActorPublicAddress: "not-owner",
            SnapshotReason: "initial draft",
            Draft: CreateAdminDraftSpecification()));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.Forbidden);
    }

    [Fact]
    public async Task CreateDraftAsync_WithUnsupportedPolicy_ReturnsValidationFailed()
    {
        var service = CreateService(new ElectionStore());

        var result = await service.CreateDraftAsync(new CreateElectionDraftRequest(
            OwnerPublicAddress: "owner-address",
            ActorPublicAddress: "owner-address",
            SnapshotReason: "initial draft",
            Draft: CreateAdminDraftSpecification(
                disclosureMode: ElectionDisclosureMode.SeparatedParticipationAndResultReports)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        result.ValidationErrors.Should().Contain(x => x.Contains("final-results-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UpdateDraftAsync_WithValidOwner_PersistsNextRevisionHistory()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var createResult = await service.CreateDraftAsync(new CreateElectionDraftRequest(
            OwnerPublicAddress: "owner-address",
            ActorPublicAddress: "owner-address",
            SnapshotReason: "initial draft",
            Draft: CreateAdminDraftSpecification(
                title: "Board Election",
                acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet])));

        var updateResult = await service.UpdateDraftAsync(new UpdateElectionDraftRequest(
            ElectionId: createResult.Election!.ElectionId,
            ActorPublicAddress: "owner-address",
            SnapshotReason: "owner updated title",
            Draft: CreateAdminDraftSpecification(
                title: "Board Election 2026",
                acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet])));

        updateResult.IsSuccess.Should().BeTrue();
        updateResult.Election.Should().NotBeNull();
        updateResult.Election!.CurrentDraftRevision.Should().Be(2);
        updateResult.Election.Title.Should().Be("Board Election 2026");
        store.DraftSnapshots.Should().HaveCount(2);
        store.DraftSnapshots.Select(x => x.DraftRevision).Should().Equal(1, 2);
        store.WarningAcknowledgements.Count(x => x.DraftRevision == 2).Should().Be(1);
    }

    [Fact]
    public async Task UpdateDraftAsync_WithNonOwnerActor_ReturnsForbidden()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateAdminElection();
        store.Elections[election.ElectionId] = election;

        var result = await service.UpdateDraftAsync(new UpdateElectionDraftRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "not-owner",
            SnapshotReason: "attempted update",
            Draft: CreateAdminDraftSpecification(title: "Updated")));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.Forbidden);
    }

    [Fact]
    public async Task UpdateDraftAsync_AfterElectionOpened_ReturnsInvalidState()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateOpenElection();
        store.Elections[election.ElectionId] = election;

        var result = await service.UpdateDraftAsync(new UpdateElectionDraftRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            SnapshotReason: "attempted update",
            Draft: CreateAdminDraftSpecification(title: "Updated")));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.InvalidState);
    }

    [Fact]
    public async Task InviteTrusteeAsync_WithActiveInvitation_ReturnsConflict()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection();
        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[invitation.Id] = invitation;

        var result = await service.InviteTrusteeAsync(new InviteElectionTrusteeRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            TrusteeUserAddress: "trustee-a",
            TrusteeDisplayName: "Alice"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.Conflict);
    }

    [Fact]
    public async Task InviteTrusteeAsync_AfterRevocation_AllowsExplicitReinvite()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection();
        var revokedInvitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision).Revoke(
                DateTime.UtcNow,
                election.CurrentDraftRevision,
                ElectionLifecycleState.Draft);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[revokedInvitation.Id] = revokedInvitation;

        var result = await service.InviteTrusteeAsync(new InviteElectionTrusteeRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            TrusteeUserAddress: "trustee-a",
            TrusteeDisplayName: "Alice"));

        result.IsSuccess.Should().BeTrue();
        result.TrusteeInvitation.Should().NotBeNull();
        result.TrusteeInvitation!.Status.Should().Be(ElectionTrusteeInvitationStatus.Pending);
        result.TrusteeInvitation.Id.Should().NotBe(revokedInvitation.Id);
        store.TrusteeInvitations.Values.Count(x =>
            string.Equals(x.TrusteeUserAddress, "trustee-a", StringComparison.OrdinalIgnoreCase))
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task AcceptTrusteeInvitation_AfterElectionOpened_ReturnsInvalidState()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection() with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = DateTime.UtcNow,
            OpenArtifactId = Guid.NewGuid(),
            LastUpdatedAt = DateTime.UtcNow,
        };
        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: 1);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[invitation.Id] = invitation;

        var result = await service.AcceptTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest(
            ElectionId: election.ElectionId,
            InvitationId: invitation.Id,
            ActorPublicAddress: "trustee-a"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.InvalidState);
    }

    [Fact]
    public async Task AcceptTrusteeInvitation_WithResolvedInvitation_ReturnsConflict()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection();
        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                election.CurrentDraftRevision,
                ElectionLifecycleState.Draft);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[invitation.Id] = invitation;

        var result = await service.AcceptTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest(
            ElectionId: election.ElectionId,
            InvitationId: invitation.Id,
            ActorPublicAddress: "trustee-a"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.Conflict);
    }

    [Fact]
    public async Task EvaluateOpenReadinessAsync_WithMissingCurrentRevisionWarningAcknowledgement_ReturnsNotReady()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateAdminElection(
            acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet]);

        store.Elections[election.ElectionId] = election;

        var result = await service.EvaluateOpenReadinessAsync(new EvaluateElectionOpenReadinessRequest(
            election.ElectionId,
            RequiredWarningCodes: [ElectionWarningCode.LowAnonymitySet]));

        result.IsReadyToOpen.Should().BeFalse();
        result.MissingWarningAcknowledgements.Should().Contain(ElectionWarningCode.LowAnonymitySet);
        result.ValidationErrors.Should().Contain(x => x.Contains("LowAnonymitySet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateOpenReadinessAsync_WithPendingTrusteeAndExactThreshold_RequiresFragilityWarningAndBlocksOpen()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection() with
        {
            RequiredApprovalCount = 1,
        };
        var acceptedInvitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                election.CurrentDraftRevision,
                ElectionLifecycleState.Draft);
        var pendingInvitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-b",
            trusteeDisplayName: "Bob",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedInvitation.Id] = acceptedInvitation;
        store.TrusteeInvitations[pendingInvitation.Id] = pendingInvitation;

        var result = await service.EvaluateOpenReadinessAsync(new EvaluateElectionOpenReadinessRequest(
            election.ElectionId,
            RequiredWarningCodes: []));

        result.IsReadyToOpen.Should().BeFalse();
        result.RequiredWarningCodes.Should().Contain(ElectionWarningCode.AllTrusteesRequiredFragility);
        result.MissingWarningAcknowledgements.Should().Contain(ElectionWarningCode.AllTrusteesRequiredFragility);
        result.ValidationErrors.Should().Contain(x =>
            x.Contains("remain pending", StringComparison.OrdinalIgnoreCase));
        result.ValidationErrors.Should().Contain(x => x.Contains("FEAT-096", StringComparison.Ordinal));
        result.ValidationErrors.Should().Contain(x =>
            x.Contains("AllTrusteesRequiredFragility", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartGovernedProposalAsync_WithValidOpenRequest_PersistsPendingProposal()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection() with
        {
            RequiredApprovalCount = 1,
        };
        var acceptedTrusteeA = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                election.CurrentDraftRevision,
                ElectionLifecycleState.Draft);
        var acceptedTrusteeB = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-b",
            trusteeDisplayName: "Bob",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                election.CurrentDraftRevision,
                ElectionLifecycleState.Draft);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrusteeA.Id] = acceptedTrusteeA;
        store.TrusteeInvitations[acceptedTrusteeB.Id] = acceptedTrusteeB;

        var result = await service.StartGovernedProposalAsync(new StartElectionGovernedProposalRequest(
            election.ElectionId,
            ElectionGovernedActionType.Open,
            "owner-address"));

        result.IsSuccess.Should().BeTrue();
        result.GovernedProposal.Should().NotBeNull();
        result.GovernedProposal!.ActionType.Should().Be(ElectionGovernedActionType.Open);
        result.GovernedProposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.WaitingForApprovals);
        store.GovernedProposals.Should().ContainSingle();
    }

    [Fact]
    public async Task StartGovernedProposalAsync_WithValidCloseRequest_LocksVoteAcceptanceImmediately()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection() with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = DateTime.UtcNow,
            OpenArtifactId = Guid.NewGuid(),
            LastUpdatedAt = DateTime.UtcNow,
        };

        store.Elections[election.ElectionId] = election;

        var result = await service.StartGovernedProposalAsync(new StartElectionGovernedProposalRequest(
            election.ElectionId,
            ElectionGovernedActionType.Close,
            "owner-address"));

        result.IsSuccess.Should().BeTrue();
        result.Election.Should().NotBeNull();
        result.Election!.VoteAcceptanceLockedAt.Should().NotBeNull();
        result.GovernedProposal.Should().NotBeNull();
        store.Elections[election.ElectionId].VoteAcceptanceLockedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateDraftAsync_WithPendingGovernedOpenProposal_ReturnsInvalidState()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection();
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            election,
            ElectionGovernedActionType.Open,
            proposedByPublicAddress: "owner-address");

        store.Elections[election.ElectionId] = election;
        store.GovernedProposals[proposal.Id] = proposal;

        var result = await service.UpdateDraftAsync(new UpdateElectionDraftRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            SnapshotReason: "attempted update while governed open pending",
            Draft: CreateTrusteeDraftSpecification()));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.InvalidState);
        result.ErrorMessage.Should().Contain("governed open proposal");
    }

    [Fact]
    public async Task OpenElectionAsync_WithValidAdminOnlyDraft_PersistsOpenBoundary()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateAdminElection(
            acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet]);
        var warning = ElectionModelFactory.CreateWarningAcknowledgement(
            election.ElectionId,
            ElectionWarningCode.LowAnonymitySet,
            election.CurrentDraftRevision,
            acknowledgedByPublicAddress: "owner-address");
        var frozenRosterHash = new byte[] { 1, 2, 3, 4 };

        store.Elections[election.ElectionId] = election;
        store.WarningAcknowledgements.Add(warning);

        var result = await service.OpenElectionAsync(new OpenElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            RequiredWarningCodes: [ElectionWarningCode.LowAnonymitySet],
            FrozenEligibleVoterSetHash: frozenRosterHash,
            TrusteePolicyExecutionReference: "n/a",
            ReportingPolicyExecutionReference: "reporting-v1",
            ReviewWindowExecutionReference: "no-review"));

        result.IsSuccess.Should().BeTrue();
        result.BoundaryArtifact.Should().NotBeNull();
        result.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Open);
        result.BoundaryArtifact!.ArtifactType.Should().Be(ElectionBoundaryArtifactType.Open);
        result.BoundaryArtifact.FrozenEligibleVoterSetHash.Should().Equal(frozenRosterHash);
        store.BoundaryArtifacts.Should().ContainSingle();
        store.Elections[election.ElectionId].OpenArtifactId.Should().Be(result.BoundaryArtifact.Id);
    }

    [Fact]
    public async Task OpenElectionAsync_WithTrusteeThresholdElection_ReturnsDependencyBlocked()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection(
            acknowledgedWarningCodes: [ElectionWarningCode.AllTrusteesRequiredFragility]);
        var warning = ElectionModelFactory.CreateWarningAcknowledgement(
            election.ElectionId,
            ElectionWarningCode.AllTrusteesRequiredFragility,
            election.CurrentDraftRevision,
            acknowledgedByPublicAddress: "owner-address");
        var acceptedTrusteeA = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                election.CurrentDraftRevision,
                ElectionLifecycleState.Draft);
        var acceptedTrusteeB = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-b",
            trusteeDisplayName: "Bob",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                election.CurrentDraftRevision,
                ElectionLifecycleState.Draft);

        store.Elections[election.ElectionId] = election;
        store.WarningAcknowledgements.Add(warning);
        store.TrusteeInvitations[acceptedTrusteeA.Id] = acceptedTrusteeA;
        store.TrusteeInvitations[acceptedTrusteeB.Id] = acceptedTrusteeB;

        var result = await service.OpenElectionAsync(new OpenElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            RequiredWarningCodes: [ElectionWarningCode.AllTrusteesRequiredFragility]));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.NotSupported);
        result.ErrorMessage.Should().Contain("governed proposal workflow");
        store.BoundaryArtifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveGovernedProposalAsync_AtThreshold_AutoExecutesProposal()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection() with
        {
            RequiredApprovalCount = 1,
        };
        var acceptedTrusteeA = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                election.CurrentDraftRevision,
                ElectionLifecycleState.Draft);
        var acceptedTrusteeB = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-b",
            trusteeDisplayName: "Bob",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                election.CurrentDraftRevision,
                ElectionLifecycleState.Draft);
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            election,
            ElectionGovernedActionType.Open,
            proposedByPublicAddress: "owner-address");

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrusteeA.Id] = acceptedTrusteeA;
        store.TrusteeInvitations[acceptedTrusteeB.Id] = acceptedTrusteeB;
        store.GovernedProposals[proposal.Id] = proposal;

        var result = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            election.ElectionId,
            proposal.Id,
            "trustee-a",
            "Ready."));

        result.IsSuccess.Should().BeTrue();
        result.Election.Should().NotBeNull();
        result.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Open);
        result.GovernedProposal.Should().NotBeNull();
        result.GovernedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionSucceeded);
        result.GovernedProposalApproval.Should().NotBeNull();
        store.GovernedProposalApprovals.Should().ContainSingle();
        store.BoundaryArtifacts.Should().ContainSingle();
        store.Elections[election.ElectionId].OpenArtifactId.Should().Be(result.BoundaryArtifact!.Id);
    }

    [Fact]
    public async Task ApproveGovernedProposalAsync_WithExistingApproval_ReturnsConflict()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection() with
        {
            RequiredApprovalCount = 2,
        };
        var acceptedTrustee = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                election.CurrentDraftRevision,
                ElectionLifecycleState.Draft);
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            election,
            ElectionGovernedActionType.Open,
            proposedByPublicAddress: "owner-address");
        var existingApproval = ElectionModelFactory.CreateGovernedProposalApproval(
            proposal,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            approvalNote: null);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrustee.Id] = acceptedTrustee;
        store.GovernedProposals[proposal.Id] = proposal;
        store.GovernedProposalApprovals.Add(existingApproval);

        var result = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            election.ElectionId,
            proposal.Id,
            "trustee-a"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.Conflict);
    }

    [Fact]
    public async Task RetryGovernedProposalExecutionAsync_AfterRecordedFailure_ReusesExistingApproval()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var openElection = CreateTrusteeElection() with
        {
            RequiredApprovalCount = 1,
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = DateTime.UtcNow,
            OpenArtifactId = Guid.NewGuid(),
            LastUpdatedAt = DateTime.UtcNow,
        };
        var draftElection = openElection with
        {
            LifecycleState = ElectionLifecycleState.Draft,
            OpenedAt = null,
            OpenArtifactId = null,
        };
        var acceptedTrusteeA = ElectionModelFactory.CreateTrusteeInvitation(
            openElection.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: openElection.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                openElection.CurrentDraftRevision,
                ElectionLifecycleState.Draft);
        var acceptedTrusteeB = ElectionModelFactory.CreateTrusteeInvitation(
            openElection.ElectionId,
            trusteeUserAddress: "trustee-b",
            trusteeDisplayName: "Bob",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: openElection.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                openElection.CurrentDraftRevision,
                ElectionLifecycleState.Draft);
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            openElection,
            ElectionGovernedActionType.Close,
            proposedByPublicAddress: "owner-address");

        store.Elections[openElection.ElectionId] = draftElection;
        store.TrusteeInvitations[acceptedTrusteeA.Id] = acceptedTrusteeA;
        store.TrusteeInvitations[acceptedTrusteeB.Id] = acceptedTrusteeB;
        store.GovernedProposals[proposal.Id] = proposal;

        var approvalResult = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            openElection.ElectionId,
            proposal.Id,
            "trustee-a"));

        approvalResult.IsSuccess.Should().BeTrue();
        approvalResult.GovernedProposal.Should().NotBeNull();
        approvalResult.GovernedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionFailed);
        approvalResult.GovernedProposal.ExecutionFailureReason.Should().Contain("close is only allowed from the open state");

        store.Elections[openElection.ElectionId] = openElection;

        var retryResult = await service.RetryGovernedProposalExecutionAsync(new RetryElectionGovernedProposalExecutionRequest(
            openElection.ElectionId,
            proposal.Id,
            "owner-address"));

        retryResult.IsSuccess.Should().BeTrue();
        retryResult.Election.Should().NotBeNull();
        retryResult.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        retryResult.GovernedProposal.Should().NotBeNull();
        retryResult.GovernedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionSucceeded);
        store.GovernedProposalApprovals.Should().ContainSingle();
        store.BoundaryArtifacts.Should().ContainSingle();
    }

    [Fact]
    public async Task CloseAndFinalizeAsync_WithValidOrdering_PersistsCanonicalBoundaryArtifacts()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateOpenElection();
        var closeBallotHash = new byte[] { 7, 8 };
        var closeTallyHash = new byte[] { 9, 10 };
        var finalizeBallotHash = new byte[] { 11, 12 };
        var finalizeTallyHash = new byte[] { 13, 14 };

        store.Elections[election.ElectionId] = election;

        var closeResult = await service.CloseElectionAsync(new CloseElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            AcceptedBallotSetHash: closeBallotHash,
            FinalEncryptedTallyHash: closeTallyHash));

        var finalizeResult = await service.FinalizeElectionAsync(new FinalizeElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            AcceptedBallotSetHash: finalizeBallotHash,
            FinalEncryptedTallyHash: finalizeTallyHash));

        closeResult.IsSuccess.Should().BeTrue();
        finalizeResult.IsSuccess.Should().BeTrue();
        closeResult.Election!.VoteAcceptanceLockedAt.Should().NotBeNull();
        finalizeResult.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Finalized);
        store.BoundaryArtifacts.Select(x => x.ArtifactType).Should().Equal(
            ElectionBoundaryArtifactType.Close,
            ElectionBoundaryArtifactType.Finalize);
        store.BoundaryArtifacts[0].AcceptedBallotSetHash.Should().Equal(closeBallotHash);
        store.BoundaryArtifacts[1].FinalEncryptedTallyHash.Should().Equal(finalizeTallyHash);
        store.Elections[election.ElectionId].VoteAcceptanceLockedAt.Should().NotBeNull();
        store.Elections[election.ElectionId].CloseArtifactId.Should().Be(closeResult.BoundaryArtifact!.Id);
        store.Elections[election.ElectionId].FinalizeArtifactId.Should().Be(finalizeResult.BoundaryArtifact!.Id);
    }

    [Fact]
    public async Task FinalizeElectionAsync_FromOpenState_ReturnsInvalidState()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateOpenElection();
        store.Elections[election.ElectionId] = election;

        var result = await service.FinalizeElectionAsync(new FinalizeElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.InvalidState);
    }

    private static ElectionLifecycleService CreateService(ElectionStore store) =>
        new(new FakeUnitOfWorkProvider(store), NullLogger<ElectionLifecycleService>.Instance);

    private static ElectionRecord CreateAdminElection(
        string title = "Board Election",
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null) =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: title,
            shortDescription: "Annual board vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "ORG-2026-01",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: CreateSingleWinnerRule(),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("alice", "Alice", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("bob", "Bob", null, 2, IsBlankOption: false),
            ],
            acknowledgedWarningCodes: acknowledgedWarningCodes);

    private static ElectionRecord CreateOpenElection() =>
        CreateAdminElection(acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet]) with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = DateTime.UtcNow,
            OpenArtifactId = Guid.NewGuid(),
            LastUpdatedAt = DateTime.UtcNow,
        };

    private static ElectionRecord CreateTrusteeElection(
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null) =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Referendum",
            shortDescription: "Policy vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "REF-2026-01",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.TrusteeThreshold,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: CreatePassFailRule(),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("no", "No", null, 2, IsBlankOption: false),
            ],
            acknowledgedWarningCodes: acknowledgedWarningCodes,
            requiredApprovalCount: 2);

    private static ElectionDraftSpecification CreateAdminDraftSpecification(
        string title = "Board Election",
        ElectionDisclosureMode disclosureMode = ElectionDisclosureMode.FinalResultsOnly,
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null) =>
        new(
            Title: title,
            ShortDescription: "Annual board vote",
            ExternalReferenceCode: "ORG-2026-01",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: ElectionBindingStatus.Binding,
            GovernanceMode: ElectionGovernanceMode.AdminOnly,
            DisclosureMode: disclosureMode,
            ParticipationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            OutcomeRule: CreateSingleWinnerRule(),
            ApprovedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            ProtocolOmegaVersion: "omega-v1.0.0",
            ReportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            OwnerOptions:
            [
                new ElectionOptionDefinition("alice", "Alice", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("bob", "Bob", null, 2, IsBlankOption: false),
            ],
            AcknowledgedWarningCodes: acknowledgedWarningCodes);

    private static ElectionDraftSpecification CreateTrusteeDraftSpecification(
        string title = "Governed Referendum",
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null,
        int requiredApprovalCount = 2) =>
        new(
            Title: title,
            ShortDescription: "Policy vote",
            ExternalReferenceCode: "REF-2026-01",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: ElectionBindingStatus.Binding,
            GovernanceMode: ElectionGovernanceMode.TrusteeThreshold,
            DisclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            OutcomeRule: CreatePassFailRule(),
            ApprovedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            ProtocolOmegaVersion: "omega-v1.0.0",
            ReportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            OwnerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("no", "No", null, 2, IsBlankOption: false),
            ],
            AcknowledgedWarningCodes: acknowledgedWarningCodes,
            RequiredApprovalCount: requiredApprovalCount);

    private static OutcomeRuleDefinition CreateSingleWinnerRule() =>
        new(
            OutcomeRuleKind.SingleWinner,
            "single_winner",
            SeatCount: 1,
            BlankVoteCountsForTurnout: true,
            BlankVoteExcludedFromWinnerSelection: true,
            BlankVoteExcludedFromThresholdDenominator: false,
            TieResolutionRule: "tie_unresolved",
            CalculationBasis: "highest_non_blank_votes");

    private static OutcomeRuleDefinition CreatePassFailRule() =>
        new(
            OutcomeRuleKind.PassFail,
            "pass_fail_yes_no",
            SeatCount: 1,
            BlankVoteCountsForTurnout: true,
            BlankVoteExcludedFromWinnerSelection: true,
            BlankVoteExcludedFromThresholdDenominator: true,
            TieResolutionRule: "tie_unresolved",
            CalculationBasis: "simple_majority_of_non_blank_votes");

    private sealed class ElectionStore
    {
        public Dictionary<ElectionId, ElectionRecord> Elections { get; } = [];
        public List<ElectionDraftSnapshotRecord> DraftSnapshots { get; } = [];
        public List<ElectionBoundaryArtifactRecord> BoundaryArtifacts { get; } = [];
        public List<ElectionWarningAcknowledgementRecord> WarningAcknowledgements { get; } = [];
        public Dictionary<Guid, ElectionTrusteeInvitationRecord> TrusteeInvitations { get; } = [];
        public Dictionary<Guid, ElectionGovernedProposalRecord> GovernedProposals { get; } = [];
        public List<ElectionGovernedProposalApprovalRecord> GovernedProposalApprovals { get; } = [];
        public Dictionary<string, ElectionCeremonyProfileRecord> CeremonyProfiles { get; } = [];
        public Dictionary<Guid, ElectionCeremonyVersionRecord> CeremonyVersions { get; } = [];
        public List<ElectionCeremonyTranscriptEventRecord> CeremonyTranscriptEvents { get; } = [];
        public List<ElectionCeremonyMessageEnvelopeRecord> CeremonyMessageEnvelopes { get; } = [];
        public Dictionary<Guid, ElectionCeremonyTrusteeStateRecord> CeremonyTrusteeStates { get; } = [];
        public Dictionary<Guid, ElectionCeremonyShareCustodyRecord> CeremonyShareCustodyRecords { get; } = [];
    }

    private sealed class FakeUnitOfWorkProvider(ElectionStore store) : IUnitOfWorkProvider<ElectionsDbContext>
    {
        public IReadOnlyUnitOfWork<ElectionsDbContext> CreateReadOnly() =>
            new FakeReadOnlyUnitOfWork(new FakeElectionsRepository(store));

        public IWritableUnitOfWork<ElectionsDbContext> CreateWritable() =>
            new FakeWritableUnitOfWork(new FakeElectionsRepository(store));

        public IWritableUnitOfWork<ElectionsDbContext> CreateWritable(System.Data.IsolationLevel isolationLevel) =>
            new FakeWritableUnitOfWork(new FakeElectionsRepository(store));
    }

    private sealed class FakeReadOnlyUnitOfWork(FakeElectionsRepository repository) : IReadOnlyUnitOfWork<ElectionsDbContext>
    {
        public ElectionsDbContext Context => null!;

        public TRepository GetRepository<TRepository>()
            where TRepository : IRepository =>
            typeof(TRepository) == typeof(IElectionsRepository)
                ? (TRepository)(object)repository
                : throw new InvalidOperationException($"Repository {typeof(TRepository).Name} is not supported by this test harness.");

        public void Dispose()
        {
        }
    }

    private sealed class FakeWritableUnitOfWork(FakeElectionsRepository repository) : IWritableUnitOfWork<ElectionsDbContext>
    {
        public ElectionsDbContext Context => null!;

        public Task CommitAsync() => Task.CompletedTask;

        public TRepository GetRepository<TRepository>()
            where TRepository : IRepository =>
            typeof(TRepository) == typeof(IElectionsRepository)
                ? (TRepository)(object)repository
                : throw new InvalidOperationException($"Repository {typeof(TRepository).Name} is not supported by this test harness.");

        public Task RollbackAsync() => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FakeElectionsRepository(ElectionStore store) : IElectionsRepository
    {
        public Task<ElectionRecord?> GetElectionAsync(ElectionId electionId) =>
            Task.FromResult(store.Elections.GetValueOrDefault(electionId));

        public Task<ElectionRecord?> GetElectionForUpdateAsync(ElectionId electionId) =>
            Task.FromResult(store.Elections.GetValueOrDefault(electionId));

        public Task<IReadOnlyList<ElectionRecord>> GetElectionsByOwnerAsync(string ownerPublicAddress) =>
            Task.FromResult<IReadOnlyList<ElectionRecord>>(
                store.Elections.Values
                    .Where(x => x.OwnerPublicAddress == ownerPublicAddress)
                    .OrderByDescending(x => x.LastUpdatedAt)
                    .ToArray());

        public Task SaveElectionAsync(ElectionRecord election)
        {
            store.Elections[election.ElectionId] = election;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionDraftSnapshotRecord>> GetDraftSnapshotsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionDraftSnapshotRecord>>(
                store.DraftSnapshots
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.DraftRevision)
                    .ThenBy(x => x.RecordedAt)
                    .ToArray());

        public Task<ElectionDraftSnapshotRecord?> GetLatestDraftSnapshotAsync(ElectionId electionId) =>
            Task.FromResult(
                store.DraftSnapshots
                    .Where(x => x.ElectionId == electionId)
                    .OrderByDescending(x => x.DraftRevision)
                    .ThenByDescending(x => x.RecordedAt)
                    .FirstOrDefault());

        public Task SaveDraftSnapshotAsync(ElectionDraftSnapshotRecord snapshot)
        {
            store.DraftSnapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionBoundaryArtifactRecord>> GetBoundaryArtifactsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionBoundaryArtifactRecord>>(
                store.BoundaryArtifacts
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.RecordedAt)
                    .ToArray());

        public Task SaveBoundaryArtifactAsync(ElectionBoundaryArtifactRecord artifact)
        {
            store.BoundaryArtifacts.Add(artifact);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionWarningAcknowledgementRecord>> GetWarningAcknowledgementsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionWarningAcknowledgementRecord>>(
                store.WarningAcknowledgements
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.DraftRevision)
                    .ThenBy(x => x.AcknowledgedAt)
                    .ToArray());

        public Task SaveWarningAcknowledgementAsync(ElectionWarningAcknowledgementRecord acknowledgement)
        {
            store.WarningAcknowledgements.Add(acknowledgement);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionTrusteeInvitationRecord>> GetTrusteeInvitationsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionTrusteeInvitationRecord>>(
                store.TrusteeInvitations.Values
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.SentAt)
                    .ToArray());

        public Task<ElectionTrusteeInvitationRecord?> GetTrusteeInvitationAsync(Guid invitationId) =>
            Task.FromResult(store.TrusteeInvitations.GetValueOrDefault(invitationId));

        public Task SaveTrusteeInvitationAsync(ElectionTrusteeInvitationRecord invitation)
        {
            store.TrusteeInvitations[invitation.Id] = invitation;
            return Task.CompletedTask;
        }

        public Task UpdateTrusteeInvitationAsync(ElectionTrusteeInvitationRecord invitation)
        {
            store.TrusteeInvitations[invitation.Id] = invitation;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionGovernedProposalRecord>> GetGovernedProposalsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionGovernedProposalRecord>>(
                store.GovernedProposals.Values
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.CreatedAt)
                    .ToArray());

        public Task<ElectionGovernedProposalRecord?> GetGovernedProposalAsync(Guid proposalId) =>
            Task.FromResult(store.GovernedProposals.GetValueOrDefault(proposalId));

        public Task<ElectionGovernedProposalRecord?> GetPendingGovernedProposalAsync(ElectionId electionId)
        {
            var pending = store.GovernedProposals.Values
                .Where(x =>
                    x.ElectionId == electionId &&
                    x.ExecutionStatus != ElectionGovernedProposalExecutionStatus.ExecutionSucceeded)
                .OrderBy(x => x.CreatedAt)
                .ToArray();

            return pending.Length switch
            {
                0 => Task.FromResult<ElectionGovernedProposalRecord?>(null),
                1 => Task.FromResult<ElectionGovernedProposalRecord?>(pending[0]),
                _ => throw new InvalidOperationException(
                    $"Election {electionId} has multiple pending governed proposals, which violates the FEAT-096 invariant."),
            };
        }

        public Task SaveGovernedProposalAsync(ElectionGovernedProposalRecord proposal)
        {
            store.GovernedProposals[proposal.Id] = proposal;
            return Task.CompletedTask;
        }

        public Task UpdateGovernedProposalAsync(ElectionGovernedProposalRecord proposal)
        {
            store.GovernedProposals[proposal.Id] = proposal;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionGovernedProposalApprovalRecord>> GetGovernedProposalApprovalsAsync(Guid proposalId) =>
            Task.FromResult<IReadOnlyList<ElectionGovernedProposalApprovalRecord>>(
                store.GovernedProposalApprovals
                    .Where(x => x.ProposalId == proposalId)
                    .OrderBy(x => x.ApprovedAt)
                    .ToArray());

        public Task<ElectionGovernedProposalApprovalRecord?> GetGovernedProposalApprovalAsync(
            Guid proposalId,
            string trusteeUserAddress) =>
            Task.FromResult(
                store.GovernedProposalApprovals.FirstOrDefault(x =>
                    x.ProposalId == proposalId &&
                    x.TrusteeUserAddress == trusteeUserAddress));

        public Task SaveGovernedProposalApprovalAsync(ElectionGovernedProposalApprovalRecord approval)
        {
            store.GovernedProposalApprovals.Add(approval);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionCeremonyProfileRecord>> GetCeremonyProfilesAsync() =>
            Task.FromResult<IReadOnlyList<ElectionCeremonyProfileRecord>>(
                store.CeremonyProfiles.Values
                    .OrderBy(x => x.RequiredApprovalCount)
                    .ThenBy(x => x.TrusteeCount)
                    .ThenBy(x => x.ProfileId)
                    .ToArray());

        public Task<ElectionCeremonyProfileRecord?> GetCeremonyProfileAsync(string profileId) =>
            Task.FromResult(store.CeremonyProfiles.GetValueOrDefault(profileId));

        public Task SaveCeremonyProfileAsync(ElectionCeremonyProfileRecord profile)
        {
            store.CeremonyProfiles[profile.ProfileId] = profile;
            return Task.CompletedTask;
        }

        public Task UpdateCeremonyProfileAsync(ElectionCeremonyProfileRecord profile)
        {
            store.CeremonyProfiles[profile.ProfileId] = profile;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionCeremonyVersionRecord>> GetCeremonyVersionsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionCeremonyVersionRecord>>(
                store.CeremonyVersions.Values
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.VersionNumber)
                    .ToArray());

        public Task<ElectionCeremonyVersionRecord?> GetCeremonyVersionAsync(Guid ceremonyVersionId) =>
            Task.FromResult(store.CeremonyVersions.GetValueOrDefault(ceremonyVersionId));

        public Task<ElectionCeremonyVersionRecord?> GetActiveCeremonyVersionAsync(ElectionId electionId)
        {
            var active = store.CeremonyVersions.Values
                .Where(x =>
                    x.ElectionId == electionId &&
                    x.Status != ElectionCeremonyVersionStatus.Superseded)
                .OrderBy(x => x.VersionNumber)
                .ToArray();

            return active.Length switch
            {
                0 => Task.FromResult<ElectionCeremonyVersionRecord?>(null),
                1 => Task.FromResult<ElectionCeremonyVersionRecord?>(active[0]),
                _ => throw new InvalidOperationException(
                    $"Election {electionId} has multiple active ceremony versions, which violates the FEAT-097 invariant."),
            };
        }

        public Task SaveCeremonyVersionAsync(ElectionCeremonyVersionRecord version)
        {
            store.CeremonyVersions[version.Id] = version;
            return Task.CompletedTask;
        }

        public Task UpdateCeremonyVersionAsync(ElectionCeremonyVersionRecord version)
        {
            store.CeremonyVersions[version.Id] = version;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionCeremonyTranscriptEventRecord>> GetCeremonyTranscriptEventsAsync(Guid ceremonyVersionId) =>
            Task.FromResult<IReadOnlyList<ElectionCeremonyTranscriptEventRecord>>(
                store.CeremonyTranscriptEvents
                    .Where(x => x.CeremonyVersionId == ceremonyVersionId)
                    .OrderBy(x => x.OccurredAt)
                    .ThenBy(x => x.Id)
                    .ToArray());

        public Task SaveCeremonyTranscriptEventAsync(ElectionCeremonyTranscriptEventRecord transcriptEvent)
        {
            store.CeremonyTranscriptEvents.Add(transcriptEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionCeremonyMessageEnvelopeRecord>> GetCeremonyMessageEnvelopesAsync(Guid ceremonyVersionId) =>
            Task.FromResult<IReadOnlyList<ElectionCeremonyMessageEnvelopeRecord>>(
                store.CeremonyMessageEnvelopes
                    .Where(x => x.CeremonyVersionId == ceremonyVersionId)
                    .OrderBy(x => x.SubmittedAt)
                    .ThenBy(x => x.Id)
                    .ToArray());

        public Task<IReadOnlyList<ElectionCeremonyMessageEnvelopeRecord>> GetCeremonyMessageEnvelopesForRecipientAsync(
            Guid ceremonyVersionId,
            string trusteeUserAddress) =>
            Task.FromResult<IReadOnlyList<ElectionCeremonyMessageEnvelopeRecord>>(
                store.CeremonyMessageEnvelopes
                    .Where(x =>
                        x.CeremonyVersionId == ceremonyVersionId &&
                        x.RecipientTrusteeUserAddress == trusteeUserAddress)
                    .OrderBy(x => x.SubmittedAt)
                    .ThenBy(x => x.Id)
                    .ToArray());

        public Task SaveCeremonyMessageEnvelopeAsync(ElectionCeremonyMessageEnvelopeRecord messageEnvelope)
        {
            store.CeremonyMessageEnvelopes.Add(messageEnvelope);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionCeremonyTrusteeStateRecord>> GetCeremonyTrusteeStatesAsync(Guid ceremonyVersionId) =>
            Task.FromResult<IReadOnlyList<ElectionCeremonyTrusteeStateRecord>>(
                store.CeremonyTrusteeStates.Values
                    .Where(x => x.CeremonyVersionId == ceremonyVersionId)
                    .OrderBy(x => x.TrusteeUserAddress)
                    .ToArray());

        public Task<ElectionCeremonyTrusteeStateRecord?> GetCeremonyTrusteeStateAsync(Guid ceremonyVersionId, string trusteeUserAddress) =>
            Task.FromResult(
                store.CeremonyTrusteeStates.Values.FirstOrDefault(x =>
                    x.CeremonyVersionId == ceremonyVersionId &&
                    x.TrusteeUserAddress == trusteeUserAddress));

        public Task SaveCeremonyTrusteeStateAsync(ElectionCeremonyTrusteeStateRecord trusteeState)
        {
            store.CeremonyTrusteeStates[trusteeState.Id] = trusteeState;
            return Task.CompletedTask;
        }

        public Task UpdateCeremonyTrusteeStateAsync(ElectionCeremonyTrusteeStateRecord trusteeState)
        {
            store.CeremonyTrusteeStates[trusteeState.Id] = trusteeState;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionCeremonyShareCustodyRecord>> GetCeremonyShareCustodyRecordsAsync(Guid ceremonyVersionId) =>
            Task.FromResult<IReadOnlyList<ElectionCeremonyShareCustodyRecord>>(
                store.CeremonyShareCustodyRecords.Values
                    .Where(x => x.CeremonyVersionId == ceremonyVersionId)
                    .OrderBy(x => x.TrusteeUserAddress)
                    .ToArray());

        public Task<ElectionCeremonyShareCustodyRecord?> GetCeremonyShareCustodyRecordAsync(Guid ceremonyVersionId, string trusteeUserAddress) =>
            Task.FromResult(
                store.CeremonyShareCustodyRecords.Values.FirstOrDefault(x =>
                    x.CeremonyVersionId == ceremonyVersionId &&
                    x.TrusteeUserAddress == trusteeUserAddress));

        public Task SaveCeremonyShareCustodyRecordAsync(ElectionCeremonyShareCustodyRecord shareCustodyRecord)
        {
            store.CeremonyShareCustodyRecords[shareCustodyRecord.Id] = shareCustodyRecord;
            return Task.CompletedTask;
        }

        public Task UpdateCeremonyShareCustodyRecordAsync(ElectionCeremonyShareCustodyRecord shareCustodyRecord)
        {
            store.CeremonyShareCustodyRecords[shareCustodyRecord.Id] = shareCustodyRecord;
            return Task.CompletedTask;
        }
    }
}
