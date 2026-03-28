using System.Security.Cryptography;
using System.Text;
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
    public async Task CreateDraftAsync_WithPreassignedElectionIdAndTransactionSource_PersistsBoundIdentifiers()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var electionId = ElectionId.NewElectionId;
        var transactionId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        var result = await service.CreateDraftAsync(new CreateElectionDraftRequest(
            OwnerPublicAddress: "owner-address",
            ActorPublicAddress: "owner-address",
            SnapshotReason: "initial draft",
            Draft: CreateAdminDraftSpecification(
                acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet]),
            PreassignedElectionId: electionId,
            SourceTransactionId: transactionId,
            SourceBlockHeight: 17,
            SourceBlockId: blockId));

        result.IsSuccess.Should().BeTrue();
        result.Election.Should().NotBeNull();
        result.Election!.ElectionId.Should().Be(electionId);
        result.DraftSnapshot.Should().NotBeNull();
        result.DraftSnapshot!.SourceTransactionId.Should().Be(transactionId);
        result.DraftSnapshot.SourceBlockHeight.Should().Be(17);
        result.DraftSnapshot.SourceBlockId.Should().Be(blockId);
        store.WarningAcknowledgements.Should().ContainSingle();
        store.WarningAcknowledgements[0].SourceTransactionId.Should().Be(transactionId);
        store.WarningAcknowledgements[0].SourceBlockHeight.Should().Be(17);
        store.WarningAcknowledgements[0].SourceBlockId.Should().Be(blockId);
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
    public async Task CreateDraftAsync_WithLateActivationPolicy_PersistsDraft()
    {
        var service = CreateService(new ElectionStore());

        var result = await service.CreateDraftAsync(new CreateElectionDraftRequest(
            OwnerPublicAddress: "owner-address",
            ActorPublicAddress: "owner-address",
            SnapshotReason: "initial draft",
            Draft: CreateAdminDraftSpecification(
                eligibilityMutationPolicy: EligibilityMutationPolicy.LateActivationForRosteredVotersOnly)));

        result.IsSuccess.Should().BeTrue();
        result.Election.Should().NotBeNull();
        result.Election!.EligibilityMutationPolicy.Should().Be(EligibilityMutationPolicy.LateActivationForRosteredVotersOnly);
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
    public async Task UpdateDraftAsync_WithTransactionSource_PersistsSnapshotAndWarningEvidence()
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
        var transactionId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        var updateResult = await service.UpdateDraftAsync(new UpdateElectionDraftRequest(
            ElectionId: createResult.Election!.ElectionId,
            ActorPublicAddress: "owner-address",
            SnapshotReason: "owner updated title",
            Draft: CreateAdminDraftSpecification(
                title: "Board Election 2026",
                acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet]),
            SourceTransactionId: transactionId,
            SourceBlockHeight: 19,
            SourceBlockId: blockId));

        updateResult.IsSuccess.Should().BeTrue();
        updateResult.DraftSnapshot.Should().NotBeNull();
        updateResult.DraftSnapshot!.SourceTransactionId.Should().Be(transactionId);
        updateResult.DraftSnapshot.SourceBlockHeight.Should().Be(19);
        updateResult.DraftSnapshot.SourceBlockId.Should().Be(blockId);
        store.WarningAcknowledgements.Count(x => x.DraftRevision == 2).Should().Be(1);
        store.WarningAcknowledgements.Single(x => x.DraftRevision == 2).SourceTransactionId.Should().Be(transactionId);
        store.WarningAcknowledgements.Single(x => x.DraftRevision == 2).SourceBlockHeight.Should().Be(19);
        store.WarningAcknowledgements.Single(x => x.DraftRevision == 2).SourceBlockId.Should().Be(blockId);
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
    public async Task ImportRosterAsync_WithDraftElection_ReplacesExistingRosterEntries()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateAdminElection();

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(
            store,
            CreateRosterEntry(election, "1001"),
            CreateRosterEntry(election, "1002"));

        var result = await service.ImportRosterAsync(new ImportElectionRosterRequest(
            election.ElectionId,
            "owner-address",
            [
                CreateRosterImportItem("2001"),
                CreateRosterImportItem("2002", isInitiallyActive: false, contactType: ElectionRosterContactType.Phone),
            ]));

        result.IsSuccess.Should().BeTrue();
        result.RosterEntries.Should().HaveCount(2);
        store.RosterEntries
            .Where(x => x.ElectionId == election.ElectionId)
            .Select(x => x.OrganizationVoterId)
            .Should()
            .BeEquivalentTo("2001", "2002");
        store.RosterEntries
            .Single(x =>
                x.ElectionId == election.ElectionId &&
                string.Equals(x.OrganizationVoterId, "2002", StringComparison.OrdinalIgnoreCase))
            .VotingRightStatus
            .Should()
            .Be(ElectionVotingRightStatus.Inactive);
    }

    [Fact]
    public async Task ClaimRosterEntryAsync_WithTemporaryMasterCode_LinksRosterEntry()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateAdminElection();

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(store, CreateRosterEntry(election, "2001"));

        var result = await service.ClaimRosterEntryAsync(new ClaimElectionRosterEntryRequest(
            election.ElectionId,
            "voter-address",
            "2001",
            "1111"));

        result.IsSuccess.Should().BeTrue();
        result.RosterEntry.Should().NotBeNull();
        result.RosterEntry!.IsLinked.Should().BeTrue();
        result.RosterEntry.LinkedActorPublicAddress.Should().Be("voter-address");
        store.RosterEntries.Single(x => x.ElectionId == election.ElectionId).LinkedActorPublicAddress.Should().Be("voter-address");
    }

    [Fact]
    public async Task ClaimRosterEntryAsync_WithWrongVerificationCode_ReturnsValidationFailed()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateAdminElection();

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(store, CreateRosterEntry(election, "2001"));

        var result = await service.ClaimRosterEntryAsync(new ClaimElectionRosterEntryRequest(
            election.ElectionId,
            "voter-address",
            "2001",
            "9999"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        result.ErrorMessage.Should().Contain("1111");
        store.RosterEntries.Single(x => x.ElectionId == election.ElectionId).IsLinked.Should().BeFalse();
    }

    [Fact]
    public async Task ActivateRosterEntryAsync_WithLateActivationPolicy_ActivatesLinkedFrozenVoter()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var openedAt = DateTime.UtcNow.AddMinutes(-5);
        var election = CreateAdminElection() with
        {
            EligibilityMutationPolicy = EligibilityMutationPolicy.LateActivationForRosteredVotersOnly,
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            OpenArtifactId = Guid.NewGuid(),
            LastUpdatedAt = openedAt,
        };
        var rosterEntry = CreateRosterEntry(
                election,
                "3001",
                ElectionVotingRightStatus.Inactive)
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-address", openedAt.AddMinutes(1));

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(store, rosterEntry);

        var result = await service.ActivateRosterEntryAsync(new ActivateElectionRosterEntryRequest(
            election.ElectionId,
            "owner-address",
            "3001"));

        result.IsSuccess.Should().BeTrue();
        result.RosterEntry.Should().NotBeNull();
        result.RosterEntry!.VotingRightStatus.Should().Be(ElectionVotingRightStatus.Active);
        result.EligibilityActivationEvent.Should().NotBeNull();
        result.EligibilityActivationEvent!.Outcome.Should().Be(ElectionEligibilityActivationOutcome.Activated);
        store.EligibilityActivationEvents.Should().ContainSingle();
        store.EligibilityActivationEvents[0].BlockReason.Should().Be(ElectionEligibilityActivationBlockReason.None);
    }

    [Fact]
    public async Task ActivateRosterEntryAsync_WithFrozenPolicy_ReturnsInvalidStateAndPersistsBlockedEvent()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var openedAt = DateTime.UtcNow.AddMinutes(-5);
        var election = CreateOpenElection() with
        {
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
        var rosterEntry = CreateRosterEntry(
                election,
                "3001",
                ElectionVotingRightStatus.Inactive)
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-address", openedAt.AddMinutes(1));

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(store, rosterEntry);

        var result = await service.ActivateRosterEntryAsync(new ActivateElectionRosterEntryRequest(
            election.ElectionId,
            "owner-address",
            "3001"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.InvalidState);
        store.EligibilityActivationEvents.Should().ContainSingle();
        store.EligibilityActivationEvents[0].Outcome.Should().Be(ElectionEligibilityActivationOutcome.Blocked);
        store.EligibilityActivationEvents[0].BlockReason.Should().Be(ElectionEligibilityActivationBlockReason.PolicyDisallowsLateActivation);
    }

    [Fact]
    public async Task ActivateRosterEntryAsync_WithMissingRosterEntry_ReturnsNotFoundAndPersistsBlockedEvent()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateOpenElection() with
        {
            EligibilityMutationPolicy = EligibilityMutationPolicy.LateActivationForRosteredVotersOnly,
        };

        store.Elections[election.ElectionId] = election;

        var result = await service.ActivateRosterEntryAsync(new ActivateElectionRosterEntryRequest(
            election.ElectionId,
            "owner-address",
            "9999"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.NotFound);
        store.EligibilityActivationEvents.Should().ContainSingle();
        store.EligibilityActivationEvents[0].BlockReason.Should().Be(ElectionEligibilityActivationBlockReason.RosterEntryNotFound);
    }

    [Fact]
    public async Task RegisterVotingCommitmentAsync_WithEligibleLinkedVoter_PersistsRegistrationWithoutParticipation()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(store);

        var result = await service.RegisterVotingCommitmentAsync(new RegisterElectionVotingCommitmentRequest(
            scenario.Election.ElectionId,
            "voter-address",
            "commitment-hash-1"));

        result.IsSuccess.Should().BeTrue();
        result.CommitmentRegistration.Should().NotBeNull();
        result.CommitmentRegistration!.OrganizationVoterId.Should().Be(scenario.RosterEntry.OrganizationVoterId);
        result.CommitmentRegistration.LinkedActorPublicAddress.Should().Be("voter-address");
        store.CommitmentRegistrations.Should().ContainSingle();
        store.ParticipationRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterVotingCommitmentAsync_WithFrozenPolicyInactiveAtOpen_ReturnsNotActive()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(
            store,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            wasActiveAtOpen: false,
            currentlyActive: false,
            createCommitmentRegistration: false);

        var result = await service.RegisterVotingCommitmentAsync(new RegisterElectionVotingCommitmentRequest(
            scenario.Election.ElectionId,
            "voter-address",
            "commitment-hash-1"));

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(ElectionCommitmentRegistrationFailureReason.NotActive);
        store.CommitmentRegistrations.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterVotingCommitmentAsync_WhenAlreadyRegistered_ReturnsAlreadyRegistered()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(store, createCommitmentRegistration: true);

        var result = await service.RegisterVotingCommitmentAsync(new RegisterElectionVotingCommitmentRequest(
            scenario.Election.ElectionId,
            "voter-address",
            "commitment-hash-2"));

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(ElectionCommitmentRegistrationFailureReason.AlreadyRegistered);
        store.CommitmentRegistrations.Should().ContainSingle();
    }

    [Fact]
    public async Task AcceptBallotCastAsync_WithEligibleCommittedVoter_WritesMinimalArtifactsAndMarksParticipation()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(store, createCommitmentRegistration: true);

        var result = await service.AcceptBallotCastAsync(CreateCastRequest(
            scenario,
            idempotencyKey: "cast-key-1",
            ballotNullifier: "nullifier-1"));

        result.IsSuccess.Should().BeTrue();
        result.ParticipationRecord.Should().NotBeNull();
        result.ParticipationRecord!.ParticipationStatus.Should().Be(ElectionParticipationStatus.CountedAsVoted);
        result.CheckoffConsumption.Should().NotBeNull();
        result.CheckoffConsumption!.OrganizationVoterId.Should().Be(scenario.RosterEntry.OrganizationVoterId);
        result.AcceptedBallot.Should().NotBeNull();
        result.AcceptedBallot!.BallotNullifier.Should().Be("nullifier-1");
        result.CastIdempotencyRecord.Should().NotBeNull();
        store.ParticipationRecords.Should().ContainSingle();
        store.CheckoffConsumptions.Should().ContainSingle();
        store.AcceptedBallots.Should().ContainSingle();
        store.CastIdempotencyRecords.Should().ContainSingle();
    }

    [Fact]
    public async Task AcceptBallotCastAsync_WithWrongBoundaryContext_ReturnsWrongElectionContext()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(store, createCommitmentRegistration: true);
        var request = CreateCastRequest(scenario) with
        {
            TallyPublicKeyFingerprint = "wrong-tally",
        };

        var result = await service.AcceptBallotCastAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(ElectionCastAcceptanceFailureReason.WrongElectionContext);
        store.CheckoffConsumptions.Should().BeEmpty();
        store.AcceptedBallots.Should().BeEmpty();
        store.CastIdempotencyRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptBallotCastAsync_WithDuplicateNullifier_ReturnsDuplicateNullifier()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(store, createCommitmentRegistration: true);
        store.AcceptedBallots.Add(ElectionModelFactory.CreateAcceptedBallotRecord(
            scenario.Election.ElectionId,
            encryptedBallotPackage: "existing-ciphertext",
            proofBundle: "existing-proof",
            ballotNullifier: "nullifier-1",
            acceptedAt: DateTime.UtcNow.AddMinutes(-1)));

        var result = await service.AcceptBallotCastAsync(CreateCastRequest(
            scenario,
            idempotencyKey: "cast-key-2",
            ballotNullifier: "nullifier-1"));

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(ElectionCastAcceptanceFailureReason.DuplicateNullifier);
        store.CheckoffConsumptions.Should().BeEmpty();
        store.CastIdempotencyRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptBallotCastAsync_WithCommittedSameElectionKey_ReturnsAlreadyUsed()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(store, createCommitmentRegistration: true);
        store.CastIdempotencyRecords.Add(ElectionModelFactory.CreateCastIdempotencyRecord(
            scenario.Election.ElectionId,
            ComputeScopedHash("cast-key-3"),
            DateTime.UtcNow.AddMinutes(-1)));

        var result = await service.AcceptBallotCastAsync(CreateCastRequest(
            scenario,
            idempotencyKey: "cast-key-3",
            ballotNullifier: "nullifier-3"));

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(ElectionCastAcceptanceFailureReason.AlreadyUsed);
        store.CheckoffConsumptions.Should().BeEmpty();
        store.AcceptedBallots.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptBallotCastAsync_WithPendingSameElectionKey_ReturnsStillProcessing()
    {
        var store = new ElectionStore
        {
            GetElectionForUpdateEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseGetElectionForUpdate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(store, createCommitmentRegistration: true);
        var request = CreateCastRequest(
            scenario,
            idempotencyKey: "cast-key-pending",
            ballotNullifier: "nullifier-pending");

        var firstAttempt = service.AcceptBallotCastAsync(request);
        await store.GetElectionForUpdateEntered.Task;

        var secondAttempt = await service.AcceptBallotCastAsync(request);

        secondAttempt.IsSuccess.Should().BeFalse();
        secondAttempt.FailureReason.Should().Be(ElectionCastAcceptanceFailureReason.StillProcessing);

        store.ReleaseGetElectionForUpdate.SetResult(true);
        var firstResult = await firstAttempt;
        firstResult.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcceptBallotCastAsync_WhenCloseBoundaryIsPersisted_ReturnsClosePersisted()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(
            store,
            createCommitmentRegistration: true,
            voteAcceptanceLockedAt: DateTime.UtcNow.AddSeconds(-5));

        var result = await service.AcceptBallotCastAsync(CreateCastRequest(
            scenario,
            idempotencyKey: "cast-key-closed",
            ballotNullifier: "nullifier-closed"));

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(ElectionCastAcceptanceFailureReason.ClosePersisted);
        store.CheckoffConsumptions.Should().BeEmpty();
        store.AcceptedBallots.Should().BeEmpty();
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
    public async Task InviteTrusteeAsync_WithPreassignedInvitationIdAndTransactionSource_PersistsBoundIdentifiers()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection();
        var invitationId = Guid.NewGuid();
        var sourceTransactionId = Guid.NewGuid();
        var sourceBlockId = Guid.NewGuid();

        store.Elections[election.ElectionId] = election;

        var result = await service.InviteTrusteeAsync(new InviteElectionTrusteeRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            TrusteeUserAddress: "trustee-a",
            TrusteeDisplayName: "Alice",
            PreassignedInvitationId: invitationId,
            SourceTransactionId: sourceTransactionId,
            SourceBlockHeight: 17,
            SourceBlockId: sourceBlockId));

        result.IsSuccess.Should().BeTrue();
        result.TrusteeInvitation.Should().NotBeNull();
        result.TrusteeInvitation!.Id.Should().Be(invitationId);
        result.TrusteeInvitation.LatestTransactionId.Should().Be(sourceTransactionId);
        result.TrusteeInvitation.LatestBlockHeight.Should().Be(17);
        result.TrusteeInvitation.LatestBlockId.Should().Be(sourceBlockId);
        store.TrusteeInvitations[invitationId].LatestTransactionId.Should().Be(sourceTransactionId);
        store.TrusteeInvitations[invitationId].LatestBlockHeight.Should().Be(17);
        store.TrusteeInvitations[invitationId].LatestBlockId.Should().Be(sourceBlockId);
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
    public async Task RevokeTrusteeInvitation_WithTransactionSource_PersistsLatestIdentifiers()
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
        var transactionId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[invitation.Id] = invitation;

        var result = await service.RevokeTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest(
            ElectionId: election.ElectionId,
            InvitationId: invitation.Id,
            ActorPublicAddress: "owner-address",
            SourceTransactionId: transactionId,
            SourceBlockHeight: 27,
            SourceBlockId: blockId));

        result.IsSuccess.Should().BeTrue();
        result.TrusteeInvitation.Should().NotBeNull();
        result.TrusteeInvitation!.Status.Should().Be(ElectionTrusteeInvitationStatus.Revoked);
        result.TrusteeInvitation.LatestTransactionId.Should().Be(transactionId);
        result.TrusteeInvitation.LatestBlockHeight.Should().Be(27);
        result.TrusteeInvitation.LatestBlockId.Should().Be(blockId);
    }

    [Fact]
    public async Task StartElectionCeremonyAsync_WithMatchingProfile_PersistsVersionAndAcceptedTrusteeStates()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection(requiredApprovalCount: 2);
        var acceptedTrusteeA = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var acceptedTrusteeB = CreateAcceptedTrusteeInvitation(election, "trustee-b", "Bob");
        var profile = RegisterCeremonyProfile(store, "dkg-prod-2of2", trusteeCount: 2, requiredApprovalCount: 2);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrusteeA.Id] = acceptedTrusteeA;
        store.TrusteeInvitations[acceptedTrusteeB.Id] = acceptedTrusteeB;

        var result = await service.StartElectionCeremonyAsync(new StartElectionCeremonyRequest(
            election.ElectionId,
            "owner-address",
            profile.ProfileId));

        result.IsSuccess.Should().BeTrue();
        result.CeremonyProfile.Should().NotBeNull();
        result.CeremonyVersion.Should().NotBeNull();
        result.CeremonyVersion!.ProfileId.Should().Be(profile.ProfileId);
        result.CeremonyVersion.BoundTrustees.Should().HaveCount(2);
        result.CeremonyTranscriptEvents.Should().ContainSingle(x =>
            x.EventType == ElectionCeremonyTranscriptEventType.VersionStarted);
        store.CeremonyVersions.Should().ContainSingle();
        store.CeremonyTrusteeStates.Values.Should().HaveCount(2);
        store.CeremonyTrusteeStates.Values.Should().OnlyContain(x =>
            x.State == ElectionTrusteeCeremonyState.AcceptedTrustee);
    }

    [Fact]
    public async Task StartElectionCeremonyAsync_WithDevProfileDisabled_ReturnsNotSupported()
    {
        var store = new ElectionStore();
        var service = CreateService(store, new ElectionCeremonyOptions(EnableDevCeremonyProfiles: false));
        var election = CreateTrusteeElection(requiredApprovalCount: 1);
        var acceptedTrustee = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var profile = RegisterCeremonyProfile(store, "dkg-dev-1of1", trusteeCount: 1, requiredApprovalCount: 1, devOnly: true);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrustee.Id] = acceptedTrustee;

        var result = await service.StartElectionCeremonyAsync(new StartElectionCeremonyRequest(
            election.ElectionId,
            "owner-address",
            profile.ProfileId));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.NotSupported);
        store.CeremonyVersions.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptTrusteeInvitation_WithAcceptedRosterChangeAfterCeremonyProgress_SupersedesActiveVersion()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection(requiredApprovalCount: 1);
        var acceptedTrustee = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var pendingTrustee = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-b",
            trusteeDisplayName: "Bob",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision);
        var profile = RegisterCeremonyProfile(store, "dkg-prod-1of1-progress", trusteeCount: 1, requiredApprovalCount: 1);
        var version = RegisterCeremonyVersion(
            store,
            election,
            profile,
            [acceptedTrustee],
            completedTrustees: [],
            ready: false);
        var progressedState = store.CeremonyTrusteeStates.Values.Single()
            .PublishTransportKey("transport-a", DateTime.UtcNow)
            .MarkJoined(DateTime.UtcNow);
        store.CeremonyTrusteeStates[progressedState.Id] = progressedState;
        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrustee.Id] = acceptedTrustee;
        store.TrusteeInvitations[pendingTrustee.Id] = pendingTrustee;

        var result = await service.AcceptTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest(
            election.ElectionId,
            pendingTrustee.Id,
            "trustee-b"));

        result.IsSuccess.Should().BeTrue();
        store.CeremonyVersions[version.Id].Status.Should().Be(ElectionCeremonyVersionStatus.Superseded);
        store.CeremonyTranscriptEvents.Should().Contain(x =>
            x.EventType == ElectionCeremonyTranscriptEventType.VersionSuperseded &&
            x.RestartReason!.Contains("changed the active ceremony roster", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SubmitElectionCeremonyMaterialAsync_WithoutSelfTest_ReturnsValidationFailed()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection(requiredApprovalCount: 1);
        var acceptedTrustee = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var profile = RegisterCeremonyProfile(store, "dkg-prod-1of1-submit", trusteeCount: 1, requiredApprovalCount: 1);
        var version = RegisterCeremonyVersion(
            store,
            election,
            profile,
            [acceptedTrustee],
            completedTrustees: [],
            ready: false);
        var joinedState = store.CeremonyTrusteeStates.Values.Single()
            .PublishTransportKey("transport-a", DateTime.UtcNow)
            .MarkJoined(DateTime.UtcNow);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrustee.Id] = acceptedTrustee;
        store.CeremonyTrusteeStates[joinedState.Id] = joinedState;

        var result = await service.SubmitElectionCeremonyMaterialAsync(new SubmitElectionCeremonyMaterialRequest(
            election.ElectionId,
            version.Id,
            "trustee-a",
            RecipientTrusteeUserAddress: null,
            MessageType: "share-package",
            PayloadVersion: "v1",
            EncryptedPayload: [1, 2, 3],
            PayloadFingerprint: "payload-fingerprint"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        store.CeremonyMessageEnvelopes.Should().BeEmpty();
        store.CeremonyTrusteeStates[joinedState.Id].State.Should().Be(ElectionTrusteeCeremonyState.CeremonyJoined);
    }

    [Fact]
    public async Task CompleteElectionCeremonyTrusteeAsync_WhenThresholdReached_MarksVersionReadyAndCreatesShareCustody()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection(requiredApprovalCount: 1);
        var acceptedTrustee = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var profile = RegisterCeremonyProfile(store, "dkg-prod-1of1-ready", trusteeCount: 1, requiredApprovalCount: 1);
        var version = RegisterCeremonyVersion(
            store,
            election,
            profile,
            [acceptedTrustee],
            completedTrustees: [],
            ready: false);
        var submittedState = store.CeremonyTrusteeStates.Values.Single()
            .PublishTransportKey("transport-a", DateTime.UtcNow)
            .MarkJoined(DateTime.UtcNow)
            .RecordSelfTestSuccess(DateTime.UtcNow)
            .RecordMaterialSubmitted(DateTime.UtcNow);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrustee.Id] = acceptedTrustee;
        store.CeremonyTrusteeStates[submittedState.Id] = submittedState;

        var result = await service.CompleteElectionCeremonyTrusteeAsync(new CompleteElectionCeremonyTrusteeRequest(
            election.ElectionId,
            version.Id,
            "owner-address",
            "trustee-a",
            "share-v1",
            TallyPublicKeyFingerprint: "tally-fingerprint-1"));

        result.IsSuccess.Should().BeTrue();
        result.CeremonyVersion.Should().NotBeNull();
        result.CeremonyVersion!.Status.Should().Be(ElectionCeremonyVersionStatus.Ready);
        result.CeremonyShareCustody.Should().NotBeNull();
        result.CeremonyShareCustody!.ShareVersion.Should().Be("share-v1");
        store.CeremonyTranscriptEvents.Should().Contain(x =>
            x.EventType == ElectionCeremonyTranscriptEventType.VersionReady);
    }

    [Fact]
    public async Task RecordElectionCeremonyShareImportAsync_WithMismatchedBinding_ReturnsValidationFailedAndPersistsFailure()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection(requiredApprovalCount: 1);
        var acceptedTrustee = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var profile = RegisterCeremonyProfile(store, "dkg-prod-1of1-import", trusteeCount: 1, requiredApprovalCount: 1);
        var version = RegisterCeremonyVersion(
            store,
            election,
            profile,
            [acceptedTrustee],
            completedTrustees: ["trustee-a"],
            ready: true);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrustee.Id] = acceptedTrustee;

        var result = await service.RecordElectionCeremonyShareImportAsync(new RecordElectionCeremonyShareImportRequest(
            election.ElectionId,
            version.Id,
            "trustee-a",
            ImportedElectionId: ElectionId.NewElectionId,
            ImportedCeremonyVersionId: version.Id,
            ImportedTrusteeUserAddress: "trustee-a",
            ImportedShareVersion: "share-v1"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        store.CeremonyShareCustodyRecords.Values.Single().Status.Should().Be(ElectionCeremonyShareCustodyStatus.ImportFailed);
    }

    [Fact]
    public async Task EvaluateOpenReadinessAsync_WithMissingCurrentRevisionWarningAcknowledgement_ReturnsNotReady()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateAdminElection(
            acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet]);

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(store, CreateRosterEntry(election, "4001"));

        var result = await service.EvaluateOpenReadinessAsync(new EvaluateElectionOpenReadinessRequest(
            election.ElectionId,
            RequiredWarningCodes: [ElectionWarningCode.LowAnonymitySet]));

        result.IsReadyToOpen.Should().BeFalse();
        result.MissingWarningAcknowledgements.Should().Contain(ElectionWarningCode.LowAnonymitySet);
        result.ValidationErrors.Should().Contain(x => x.Contains("LowAnonymitySet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateOpenReadinessAsync_WithReadyCeremonyAtExactThreshold_ReturnsReadyWithoutPendingInviteBlock()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection(requiredApprovalCount: 1);
        var acceptedInvitation = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var pendingInvitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-b",
            trusteeDisplayName: "Bob",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision);
        var warningAcknowledgement = ElectionModelFactory.CreateWarningAcknowledgement(
            election.ElectionId,
            ElectionWarningCode.AllTrusteesRequiredFragility,
            election.CurrentDraftRevision,
            acknowledgedByPublicAddress: "owner-address");
        var profile = RegisterCeremonyProfile(store, "dkg-prod-1of1", trusteeCount: 1, requiredApprovalCount: 1);
        RegisterCeremonyVersion(
            store,
            election,
            profile,
            [acceptedInvitation],
            completedTrustees: ["trustee-a"],
            ready: true);

        store.Elections[election.ElectionId] = election with
        {
            AcknowledgedWarningCodes = [ElectionWarningCode.AllTrusteesRequiredFragility],
        };
        store.TrusteeInvitations[acceptedInvitation.Id] = acceptedInvitation;
        store.TrusteeInvitations[pendingInvitation.Id] = pendingInvitation;
        store.WarningAcknowledgements.Add(warningAcknowledgement);
        AddRosterEntries(store, CreateRosterEntry(election, "4001"));

        var result = await service.EvaluateOpenReadinessAsync(new EvaluateElectionOpenReadinessRequest(
            election.ElectionId,
            RequiredWarningCodes: []));

        result.IsReadyToOpen.Should().BeTrue();
        result.RequiredWarningCodes.Should().Contain(ElectionWarningCode.AllTrusteesRequiredFragility);
        result.MissingWarningAcknowledgements.Should().BeEmpty();
        result.ValidationErrors.Should().BeEmpty();
        result.ValidationErrors.Should().NotContain(x =>
            x.Contains("remain pending", StringComparison.OrdinalIgnoreCase));
        result.CeremonySnapshot.Should().NotBeNull();
        result.CeremonySnapshot!.ActiveTrustees.Should().ContainSingle(x =>
            string.Equals(x.TrusteeUserAddress, "trustee-a", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateOpenReadinessAsync_WithoutImportedRoster_ReturnsNotReady()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateAdminElection();

        store.Elections[election.ElectionId] = election;

        var result = await service.EvaluateOpenReadinessAsync(new EvaluateElectionOpenReadinessRequest(
            election.ElectionId,
            RequiredWarningCodes: []));

        result.IsReadyToOpen.Should().BeFalse();
        result.ValidationErrors.Should().Contain(x =>
            x.Contains("imported election roster", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvaluateOpenReadinessAsync_WithFrozenPolicyAndNoActiveRosteredVoter_ReturnsNotReady()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateAdminElection();

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(
            store,
            CreateRosterEntry(
                election,
                "4002",
                ElectionVotingRightStatus.Inactive));

        var result = await service.EvaluateOpenReadinessAsync(new EvaluateElectionOpenReadinessRequest(
            election.ElectionId,
            RequiredWarningCodes: []));

        result.IsReadyToOpen.Should().BeFalse();
        result.ValidationErrors.Should().Contain(x =>
            x.Contains("at least one active rostered voter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StartGovernedProposalAsync_WithValidOpenRequest_PersistsPendingProposal()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection(requiredApprovalCount: 1);
        var proposalId = Guid.NewGuid();
        var sourceTransactionId = Guid.NewGuid();
        var acceptedTrusteeA = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var acceptedTrusteeB = CreateAcceptedTrusteeInvitation(election, "trustee-b", "Bob");
        var profile = RegisterCeremonyProfile(store, "dkg-prod-1of2", trusteeCount: 2, requiredApprovalCount: 1);
        RegisterCeremonyVersion(
            store,
            election,
            profile,
            [acceptedTrusteeA, acceptedTrusteeB],
            completedTrustees: ["trustee-a", "trustee-b"],
            ready: true);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrusteeA.Id] = acceptedTrusteeA;
        store.TrusteeInvitations[acceptedTrusteeB.Id] = acceptedTrusteeB;
        AddRosterEntries(store, CreateRosterEntry(election, "4001"));

        var result = await service.StartGovernedProposalAsync(new StartElectionGovernedProposalRequest(
            election.ElectionId,
            ElectionGovernedActionType.Open,
            "owner-address",
            PreassignedProposalId: proposalId,
            SourceTransactionId: sourceTransactionId,
            SourceBlockHeight: 43,
            SourceBlockId: Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.GovernedProposal.Should().NotBeNull();
        result.GovernedProposal!.Id.Should().Be(proposalId);
        result.GovernedProposal!.ActionType.Should().Be(ElectionGovernedActionType.Open);
        result.GovernedProposal.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.WaitingForApprovals);
        result.GovernedProposal.LatestTransactionId.Should().Be(sourceTransactionId);
        result.GovernedProposal.LatestBlockHeight.Should().Be(43);
        store.GovernedProposals.Should().ContainSingle();
        store.GovernedProposals.Values.Single().Id.Should().Be(proposalId);
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
        var sourceTransactionId = Guid.NewGuid();
        var warning = ElectionModelFactory.CreateWarningAcknowledgement(
            election.ElectionId,
            ElectionWarningCode.LowAnonymitySet,
            election.CurrentDraftRevision,
            acknowledgedByPublicAddress: "owner-address");
        var rosterEntry = CreateRosterEntry(election, "4001");

        store.Elections[election.ElectionId] = election;
        store.WarningAcknowledgements.Add(warning);
        AddRosterEntries(store, rosterEntry);

        var result = await service.OpenElectionAsync(new OpenElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            RequiredWarningCodes: [ElectionWarningCode.LowAnonymitySet],
            TrusteePolicyExecutionReference: "n/a",
            ReportingPolicyExecutionReference: "reporting-v1",
            ReviewWindowExecutionReference: "no-review",
            SourceTransactionId: sourceTransactionId,
            SourceBlockHeight: 41,
            SourceBlockId: Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.BoundaryArtifact.Should().NotBeNull();
        result.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Open);
        result.BoundaryArtifact!.ArtifactType.Should().Be(ElectionBoundaryArtifactType.Open);
        result.BoundaryArtifact.FrozenEligibleVoterSetHash.Should().NotBeNull().And.NotBeEmpty();
        result.EligibilitySnapshot.Should().NotBeNull();
        result.EligibilitySnapshot!.SnapshotType.Should().Be(ElectionEligibilitySnapshotType.Open);
        result.EligibilitySnapshot.ActiveDenominatorCount.Should().Be(1);
        result.EligibilitySnapshot.DidNotVoteCount.Should().Be(1);
        result.EligibilitySnapshot.ActiveDenominatorSetHash.Should().Equal(result.BoundaryArtifact.FrozenEligibleVoterSetHash);
        result.RosterEntries.Should().ContainSingle();
        result.RosterEntries[0].WasPresentAtOpen.Should().BeTrue();
        result.RosterEntries[0].WasActiveAtOpen.Should().BeTrue();
        result.BoundaryArtifact.SourceTransactionId.Should().Be(sourceTransactionId);
        result.BoundaryArtifact.SourceBlockHeight.Should().Be(41);
        store.BoundaryArtifacts.Should().ContainSingle();
        store.EligibilitySnapshots.Should().ContainSingle();
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
        AddRosterEntries(store, CreateRosterEntry(election, "4001"));

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
        var election = CreateTrusteeElection(requiredApprovalCount: 1);
        var approvalTransactionId = Guid.NewGuid();
        var acceptedTrusteeA = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var acceptedTrusteeB = CreateAcceptedTrusteeInvitation(election, "trustee-b", "Bob");
        var profile = RegisterCeremonyProfile(store, "dkg-prod-1of2-open", trusteeCount: 2, requiredApprovalCount: 1);
        RegisterCeremonyVersion(
            store,
            election,
            profile,
            [acceptedTrusteeA, acceptedTrusteeB],
            completedTrustees: ["trustee-a", "trustee-b"],
            ready: true);
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            election,
            ElectionGovernedActionType.Open,
            proposedByPublicAddress: "owner-address");

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrusteeA.Id] = acceptedTrusteeA;
        store.TrusteeInvitations[acceptedTrusteeB.Id] = acceptedTrusteeB;
        store.GovernedProposals[proposal.Id] = proposal;
        AddRosterEntries(store, CreateRosterEntry(election, "4001"));

        var result = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            election.ElectionId,
            proposal.Id,
            "trustee-a",
            "Ready.",
            SourceTransactionId: approvalTransactionId,
            SourceBlockHeight: 47,
            SourceBlockId: Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.Election.Should().NotBeNull();
        result.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Open);
        result.GovernedProposal.Should().NotBeNull();
        result.GovernedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionSucceeded);
        result.GovernedProposal.LatestTransactionId.Should().Be(approvalTransactionId);
        result.GovernedProposal.LatestBlockHeight.Should().Be(47);
        result.GovernedProposalApproval.Should().NotBeNull();
        result.GovernedProposalApproval!.SourceTransactionId.Should().Be(approvalTransactionId);
        result.GovernedProposalApproval.SourceBlockHeight.Should().Be(47);
        result.BoundaryArtifact.Should().NotBeNull();
        result.BoundaryArtifact!.CeremonySnapshot.Should().NotBeNull();
        result.BoundaryArtifact.CeremonySnapshot!.ProfileId.Should().Be(profile.ProfileId);
        result.BoundaryArtifact.CeremonySnapshot.ActiveTrustees.Should().HaveCount(2);
        result.EligibilitySnapshot.Should().NotBeNull();
        result.BoundaryArtifact.SourceTransactionId.Should().Be(approvalTransactionId);
        result.BoundaryArtifact.SourceBlockHeight.Should().Be(47);
        store.GovernedProposalApprovals.Should().ContainSingle();
        store.BoundaryArtifacts.Should().ContainSingle();
        store.EligibilitySnapshots.Should().ContainSingle();
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
    public async Task ApproveGovernedProposalAsync_ForCloseAtThreshold_MaterializesTallyReadyBoundary()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var approvalTransactionId = Guid.NewGuid();
        var openElection = CreateTrusteeElection(requiredApprovalCount: 1) with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = DateTime.UtcNow.AddMinutes(-10),
            OpenArtifactId = Guid.NewGuid(),
            VoteAcceptanceLockedAt = DateTime.UtcNow.AddMinutes(-1),
            LastUpdatedAt = DateTime.UtcNow.AddMinutes(-1),
        };
        var acceptedTrustee = ElectionModelFactory.CreateTrusteeInvitation(
            openElection.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: openElection.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                openElection.CurrentDraftRevision,
                ElectionLifecycleState.Draft);
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            openElection,
            ElectionGovernedActionType.Close,
            proposedByPublicAddress: "owner-address");

        store.Elections[openElection.ElectionId] = openElection;
        store.TrusteeInvitations[acceptedTrustee.Id] = acceptedTrustee;
        store.GovernedProposals[proposal.Id] = proposal;

        var result = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            openElection.ElectionId,
            proposal.Id,
            "trustee-a",
            "Close now.",
            SourceTransactionId: approvalTransactionId,
            SourceBlockHeight: 48,
            SourceBlockId: Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.Election.Should().NotBeNull();
        result.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        result.Election.TallyReadyAt.Should().NotBeNull();
        result.GovernedProposal.Should().NotBeNull();
        result.GovernedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionSucceeded);
        result.BoundaryArtifact.Should().NotBeNull();
        result.BoundaryArtifact!.ArtifactType.Should().Be(ElectionBoundaryArtifactType.Close);
        result.BoundaryArtifact.AcceptedBallotSetHash.Should().NotBeNull().And.NotBeEmpty();
        result.BoundaryArtifact.FinalEncryptedTallyHash.Should().NotBeNull().And.NotBeEmpty();
        result.BoundaryArtifact.SourceTransactionId.Should().Be(approvalTransactionId);
        result.BoundaryArtifact.SourceBlockHeight.Should().Be(48);
        store.Elections[openElection.ElectionId].CloseArtifactId.Should().Be(result.BoundaryArtifact.Id);
        store.Elections[openElection.ElectionId].TallyReadyAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RetryGovernedProposalExecutionAsync_AfterRecordedFailure_ReusesExistingApproval()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var approvalTransactionId = Guid.NewGuid();
        var retryTransactionId = Guid.NewGuid();
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
            "trustee-a",
            SourceTransactionId: approvalTransactionId,
            SourceBlockHeight: 51,
            SourceBlockId: Guid.NewGuid()));

        approvalResult.IsSuccess.Should().BeTrue();
        approvalResult.GovernedProposal.Should().NotBeNull();
        approvalResult.GovernedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionFailed);
        approvalResult.GovernedProposal.ExecutionFailureReason.Should().Contain("close is only allowed from the open state");
        approvalResult.GovernedProposal.LatestTransactionId.Should().Be(approvalTransactionId);
        approvalResult.GovernedProposal.LatestBlockHeight.Should().Be(51);

        store.Elections[openElection.ElectionId] = openElection;

        var retryResult = await service.RetryGovernedProposalExecutionAsync(new RetryElectionGovernedProposalExecutionRequest(
            openElection.ElectionId,
            proposal.Id,
            "owner-address",
            SourceTransactionId: retryTransactionId,
            SourceBlockHeight: 52,
            SourceBlockId: Guid.NewGuid()));

        retryResult.IsSuccess.Should().BeTrue();
        retryResult.Election.Should().NotBeNull();
        retryResult.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        retryResult.GovernedProposal.Should().NotBeNull();
        retryResult.GovernedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionSucceeded);
        retryResult.GovernedProposal.LatestTransactionId.Should().Be(retryTransactionId);
        retryResult.GovernedProposal.LatestBlockHeight.Should().Be(52);
        retryResult.BoundaryArtifact.Should().NotBeNull();
        retryResult.BoundaryArtifact!.SourceTransactionId.Should().Be(retryTransactionId);
        retryResult.BoundaryArtifact.SourceBlockHeight.Should().Be(52);
        store.GovernedProposalApprovals.Should().ContainSingle();
        store.BoundaryArtifacts.Should().ContainSingle();
    }

    [Fact]
    public async Task ApproveGovernedProposalAsync_ForFinalizeAtThreshold_StartsBoundFinalizationSessionAndKeepsElectionClosed()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedClosedTrusteeElectionForFinalization(store, requiredApprovalCount: 1);
        var approvalTransactionId = Guid.NewGuid();

        var result = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            scenario.Election.ElectionId,
            scenario.Proposal.Id,
            "trustee-a",
            "Ready to finalize.",
            SourceTransactionId: approvalTransactionId,
            SourceBlockHeight: 61,
            SourceBlockId: Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.Election.Should().NotBeNull();
        result.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        result.GovernedProposal.Should().NotBeNull();
        result.GovernedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionSucceeded);
        result.GovernedProposal.LatestTransactionId.Should().Be(approvalTransactionId);
        result.GovernedProposal.LatestBlockHeight.Should().Be(61);
        result.FinalizationSession.Should().NotBeNull();
        result.FinalizationSession!.Status.Should().Be(ElectionFinalizationSessionStatus.AwaitingShares);
        result.FinalizationSession.GovernedProposalId.Should().Be(scenario.Proposal.Id);
        result.FinalizationSession.CloseArtifactId.Should().Be(scenario.CloseArtifact.Id);
        result.FinalizationSession.CeremonySnapshot.Should().NotBeNull();
        result.FinalizationSession.CeremonySnapshot!.CeremonyVersionId.Should().Be(scenario.CeremonyVersion.Id);
        result.FinalizationSession.RequiredShareCount.Should().Be(1);
        result.BoundaryArtifact.Should().BeNull();
        result.FinalizationReleaseEvidence.Should().BeNull();
        store.FinalizationSessions.Should().ContainSingle();
        store.FinalizationReleaseEvidenceRecords.Should().BeEmpty();
        store.Elections[scenario.Election.ElectionId].FinalizeArtifactId.Should().BeNull();
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WhenThresholdReached_FinalizesElectionAndStoresReleaseEvidence()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedClosedTrusteeElectionForFinalization(store, requiredApprovalCount: 1);
        var approvalResult = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            scenario.Election.ElectionId,
            scenario.Proposal.Id,
            "trustee-a",
            "Ready to finalize."));
        var session = approvalResult.FinalizationSession!;
        var shareTransactionId = Guid.NewGuid();

        var result = await service.SubmitFinalizationShareAsync(new SubmitElectionFinalizationShareRequest(
            ElectionId: scenario.Election.ElectionId,
            FinalizationSessionId: session.Id,
            ActorPublicAddress: "trustee-a",
            ShareIndex: 1,
            ShareVersion: "share-v1",
            TargetType: ElectionFinalizationTargetType.AggregateTally,
            ClaimedCloseArtifactId: session.CloseArtifactId,
            ClaimedAcceptedBallotSetHash: scenario.AcceptedBallotSetHash,
            ClaimedFinalEncryptedTallyHash: scenario.FinalEncryptedTallyHash,
            ClaimedTargetTallyId: session.TargetTallyId,
            ClaimedCeremonyVersionId: scenario.CeremonyVersion.Id,
            ClaimedTallyPublicKeyFingerprint: scenario.CeremonyVersion.TallyPublicKeyFingerprint,
            ShareMaterial: "ciphertext-share-material",
            SourceTransactionId: shareTransactionId,
            SourceBlockHeight: 71,
            SourceBlockId: Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.Election.Should().NotBeNull();
        result.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Finalized);
        result.BoundaryArtifact.Should().NotBeNull();
        result.BoundaryArtifact!.ArtifactType.Should().Be(ElectionBoundaryArtifactType.Finalize);
        result.BoundaryArtifact.SourceTransactionId.Should().Be(shareTransactionId);
        result.BoundaryArtifact.SourceBlockHeight.Should().Be(71);
        result.FinalizationShare.Should().NotBeNull();
        result.FinalizationShare!.Status.Should().Be(ElectionFinalizationShareStatus.Accepted);
        result.FinalizationShare.SourceTransactionId.Should().Be(shareTransactionId);
        result.FinalizationSession.Should().NotBeNull();
        result.FinalizationSession!.Status.Should().Be(ElectionFinalizationSessionStatus.Completed);
        result.FinalizationReleaseEvidence.Should().NotBeNull();
        result.FinalizationReleaseEvidence!.AcceptedShareCount.Should().Be(1);
        result.FinalizationReleaseEvidence.SourceTransactionId.Should().Be(shareTransactionId);
        store.FinalizationShares.Should().ContainSingle();
        store.FinalizationReleaseEvidenceRecords.Should().ContainSingle();
        store.FinalizationReleaseEvidenceRecords[0].FinalizationSessionId.Should().Be(session.Id);
        store.Elections[scenario.Election.ElectionId].FinalizeArtifactId.Should().Be(result.BoundaryArtifact.Id);
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WithSingleBallotTarget_PersistsRejectedShareAndKeepsSessionWaiting()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedClosedTrusteeElectionForFinalization(store, requiredApprovalCount: 1);
        var approvalResult = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            scenario.Election.ElectionId,
            scenario.Proposal.Id,
            "trustee-a",
            "Ready to finalize."));
        var session = approvalResult.FinalizationSession!;

        var result = await service.SubmitFinalizationShareAsync(new SubmitElectionFinalizationShareRequest(
            ElectionId: scenario.Election.ElectionId,
            FinalizationSessionId: session.Id,
            ActorPublicAddress: "trustee-a",
            ShareIndex: 1,
            ShareVersion: "share-v1",
            TargetType: ElectionFinalizationTargetType.SingleBallot,
            ClaimedCloseArtifactId: session.CloseArtifactId,
            ClaimedAcceptedBallotSetHash: scenario.AcceptedBallotSetHash,
            ClaimedFinalEncryptedTallyHash: scenario.FinalEncryptedTallyHash,
            ClaimedTargetTallyId: session.TargetTallyId,
            ClaimedCeremonyVersionId: scenario.CeremonyVersion.Id,
            ClaimedTallyPublicKeyFingerprint: scenario.CeremonyVersion.TallyPublicKeyFingerprint,
            ShareMaterial: "ciphertext-share-material"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        result.ErrorMessage.Should().Contain("aggregate-tally");
        store.FinalizationShares.Should().ContainSingle();
        store.FinalizationShares[0].Status.Should().Be(ElectionFinalizationShareStatus.Rejected);
        store.FinalizationShares[0].FailureCode.Should().Be("SINGLE_BALLOT_RELEASE_FORBIDDEN");
        store.FinalizationReleaseEvidenceRecords.Should().BeEmpty();
        store.Elections[scenario.Election.ElectionId].LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        store.FinalizationSessions[session.Id].Status.Should().Be(ElectionFinalizationSessionStatus.AwaitingShares);
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WithWrongTargetClaim_PersistsRejectedShareAndLeavesElectionClosed()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedClosedTrusteeElectionForFinalization(store, requiredApprovalCount: 1);
        var approvalResult = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            scenario.Election.ElectionId,
            scenario.Proposal.Id,
            "trustee-a",
            "Ready to finalize."));
        var session = approvalResult.FinalizationSession!;

        var result = await service.SubmitFinalizationShareAsync(new SubmitElectionFinalizationShareRequest(
            ElectionId: scenario.Election.ElectionId,
            FinalizationSessionId: session.Id,
            ActorPublicAddress: "trustee-a",
            ShareIndex: 1,
            ShareVersion: "share-v1",
            TargetType: ElectionFinalizationTargetType.AggregateTally,
            ClaimedCloseArtifactId: session.CloseArtifactId,
            ClaimedAcceptedBallotSetHash: scenario.AcceptedBallotSetHash,
            ClaimedFinalEncryptedTallyHash: [99, 98, 97],
            ClaimedTargetTallyId: session.TargetTallyId,
            ClaimedCeremonyVersionId: scenario.CeremonyVersion.Id,
            ClaimedTallyPublicKeyFingerprint: scenario.CeremonyVersion.TallyPublicKeyFingerprint,
            ShareMaterial: "ciphertext-share-material"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        result.ErrorMessage.Should().Contain("exact close-boundary target");
        store.FinalizationShares.Should().ContainSingle();
        store.FinalizationShares[0].Status.Should().Be(ElectionFinalizationShareStatus.Rejected);
        store.FinalizationShares[0].FailureCode.Should().Be("WRONG_TARGET_SHARE");
        store.FinalizationReleaseEvidenceRecords.Should().BeEmpty();
        store.Elections[scenario.Election.ElectionId].LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        store.FinalizationSessions[session.Id].Status.Should().Be(ElectionFinalizationSessionStatus.AwaitingShares);
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WithDuplicateAcceptedShare_PersistsRejectedShareAndKeepsElectionClosed()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedClosedTrusteeElectionForFinalization(store, requiredApprovalCount: 2);
        var firstApprovalResult = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            scenario.Election.ElectionId,
            scenario.Proposal.Id,
            "trustee-a",
            "Ready to finalize."));
        firstApprovalResult.IsSuccess.Should().BeTrue();
        firstApprovalResult.FinalizationSession.Should().BeNull();

        var secondApprovalResult = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            scenario.Election.ElectionId,
            scenario.Proposal.Id,
            "trustee-b",
            "Ready to finalize."));
        var session = secondApprovalResult.FinalizationSession!;

        var firstResult = await service.SubmitFinalizationShareAsync(new SubmitElectionFinalizationShareRequest(
            ElectionId: scenario.Election.ElectionId,
            FinalizationSessionId: session.Id,
            ActorPublicAddress: "trustee-a",
            ShareIndex: 1,
            ShareVersion: "share-v1",
            TargetType: ElectionFinalizationTargetType.AggregateTally,
            ClaimedCloseArtifactId: session.CloseArtifactId,
            ClaimedAcceptedBallotSetHash: scenario.AcceptedBallotSetHash,
            ClaimedFinalEncryptedTallyHash: scenario.FinalEncryptedTallyHash,
            ClaimedTargetTallyId: session.TargetTallyId,
            ClaimedCeremonyVersionId: scenario.CeremonyVersion.Id,
            ClaimedTallyPublicKeyFingerprint: scenario.CeremonyVersion.TallyPublicKeyFingerprint,
            ShareMaterial: "ciphertext-share-material"));

        firstResult.IsSuccess.Should().BeTrue();
        firstResult.FinalizationShare.Should().NotBeNull();
        firstResult.FinalizationShare!.Status.Should().Be(ElectionFinalizationShareStatus.Accepted);
        store.Elections[scenario.Election.ElectionId].LifecycleState.Should().Be(ElectionLifecycleState.Closed);

        var duplicateResult = await service.SubmitFinalizationShareAsync(new SubmitElectionFinalizationShareRequest(
            ElectionId: scenario.Election.ElectionId,
            FinalizationSessionId: session.Id,
            ActorPublicAddress: "trustee-a",
            ShareIndex: 1,
            ShareVersion: "share-v1",
            TargetType: ElectionFinalizationTargetType.AggregateTally,
            ClaimedCloseArtifactId: session.CloseArtifactId,
            ClaimedAcceptedBallotSetHash: scenario.AcceptedBallotSetHash,
            ClaimedFinalEncryptedTallyHash: scenario.FinalEncryptedTallyHash,
            ClaimedTargetTallyId: session.TargetTallyId,
            ClaimedCeremonyVersionId: scenario.CeremonyVersion.Id,
            ClaimedTallyPublicKeyFingerprint: scenario.CeremonyVersion.TallyPublicKeyFingerprint,
            ShareMaterial: "ciphertext-share-material"));

        duplicateResult.IsSuccess.Should().BeFalse();
        duplicateResult.ErrorCode.Should().Be(ElectionCommandErrorCode.Conflict);
        duplicateResult.ErrorMessage.Should().Contain("already recorded");
        store.FinalizationShares.Should().HaveCount(2);
        store.FinalizationShares[1].Status.Should().Be(ElectionFinalizationShareStatus.Rejected);
        store.FinalizationShares[1].FailureCode.Should().Be("DUPLICATE_SHARE");
        store.FinalizationReleaseEvidenceRecords.Should().BeEmpty();
        store.Elections[scenario.Election.ElectionId].LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        store.FinalizationSessions[session.Id].Status.Should().Be(ElectionFinalizationSessionStatus.AwaitingShares);
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WithMalformedShare_PersistsRejectedShareAndLeavesElectionClosed()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedClosedTrusteeElectionForFinalization(store, requiredApprovalCount: 1);
        var approvalResult = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            scenario.Election.ElectionId,
            scenario.Proposal.Id,
            "trustee-a",
            "Ready to finalize."));
        var session = approvalResult.FinalizationSession!;

        var result = await service.SubmitFinalizationShareAsync(new SubmitElectionFinalizationShareRequest(
            ElectionId: scenario.Election.ElectionId,
            FinalizationSessionId: session.Id,
            ActorPublicAddress: "trustee-a",
            ShareIndex: 1,
            ShareVersion: "share-v1",
            TargetType: ElectionFinalizationTargetType.AggregateTally,
            ClaimedCloseArtifactId: session.CloseArtifactId,
            ClaimedAcceptedBallotSetHash: scenario.AcceptedBallotSetHash,
            ClaimedFinalEncryptedTallyHash: scenario.FinalEncryptedTallyHash,
            ClaimedTargetTallyId: session.TargetTallyId,
            ClaimedCeremonyVersionId: scenario.CeremonyVersion.Id,
            ClaimedTallyPublicKeyFingerprint: scenario.CeremonyVersion.TallyPublicKeyFingerprint,
            ShareMaterial: ""));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        result.ErrorMessage.Should().Contain("missing required share or target fields");
        store.FinalizationShares.Should().ContainSingle();
        store.FinalizationShares[0].Status.Should().Be(ElectionFinalizationShareStatus.Rejected);
        store.FinalizationShares[0].FailureCode.Should().Be("MALFORMED_SHARE");
        store.FinalizationReleaseEvidenceRecords.Should().BeEmpty();
        store.Elections[scenario.Election.ElectionId].LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        store.FinalizationSessions[session.Id].Status.Should().Be(ElectionFinalizationSessionStatus.AwaitingShares);
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WithWrongCeremonyBinding_PersistsRejectedShareAndLeavesElectionClosed()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedClosedTrusteeElectionForFinalization(store, requiredApprovalCount: 1);
        var approvalResult = await service.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            scenario.Election.ElectionId,
            scenario.Proposal.Id,
            "trustee-a",
            "Ready to finalize."));
        var session = approvalResult.FinalizationSession!;

        var result = await service.SubmitFinalizationShareAsync(new SubmitElectionFinalizationShareRequest(
            ElectionId: scenario.Election.ElectionId,
            FinalizationSessionId: session.Id,
            ActorPublicAddress: "trustee-a",
            ShareIndex: 1,
            ShareVersion: "share-v1",
            TargetType: ElectionFinalizationTargetType.AggregateTally,
            ClaimedCloseArtifactId: session.CloseArtifactId,
            ClaimedAcceptedBallotSetHash: scenario.AcceptedBallotSetHash,
            ClaimedFinalEncryptedTallyHash: scenario.FinalEncryptedTallyHash,
            ClaimedTargetTallyId: session.TargetTallyId,
            ClaimedCeremonyVersionId: Guid.NewGuid(),
            ClaimedTallyPublicKeyFingerprint: scenario.CeremonyVersion.TallyPublicKeyFingerprint,
            ShareMaterial: "ciphertext-share-material"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        result.ErrorMessage.Should().Contain("exact session ceremony binding");
        store.FinalizationShares.Should().ContainSingle();
        store.FinalizationShares[0].Status.Should().Be(ElectionFinalizationShareStatus.Rejected);
        store.FinalizationShares[0].FailureCode.Should().Be("WRONG_TARGET_SHARE");
        store.FinalizationReleaseEvidenceRecords.Should().BeEmpty();
        store.Elections[scenario.Election.ElectionId].LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        store.FinalizationSessions[session.Id].Status.Should().Be(ElectionFinalizationSessionStatus.AwaitingShares);
    }

    [Fact]
    public async Task CloseElectionAsync_WithFrozenPolicy_UsesOpenActiveDenominator()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var openedAt = DateTime.UtcNow.AddMinutes(-10);
        var election = CreateOpenElection() with
        {
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
        var activeAtOpenEntry = CreateRosterEntry(
                election,
                "5001",
                ElectionVotingRightStatus.Active)
            .FreezeAtOpen(openedAt);
        var laterActivatedEntry = CreateRosterEntry(
                election,
                "5002",
                ElectionVotingRightStatus.Inactive)
            .FreezeAtOpen(openedAt)
            .MarkVotingRightActive("owner-address", openedAt.AddMinutes(2));

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(store, activeAtOpenEntry, laterActivatedEntry);
        AddParticipationRecords(
            store,
            CreateParticipationRecord(
                election,
                "5001",
                ElectionParticipationStatus.CountedAsVoted,
                openedAt.AddMinutes(3)),
            CreateParticipationRecord(
                election,
                "5002",
                ElectionParticipationStatus.Blank,
                openedAt.AddMinutes(4)));

        var result = await service.CloseElectionAsync(new CloseElectionRequest(
            election.ElectionId,
            "owner-address"));

        result.IsSuccess.Should().BeTrue();
        result.EligibilitySnapshot.Should().NotBeNull();
        result.EligibilitySnapshot!.SnapshotType.Should().Be(ElectionEligibilitySnapshotType.Close);
        result.EligibilitySnapshot.ActiveDenominatorCount.Should().Be(1);
        result.EligibilitySnapshot.CountedParticipationCount.Should().Be(1);
        result.EligibilitySnapshot.BlankCount.Should().Be(0);
        result.EligibilitySnapshot.DidNotVoteCount.Should().Be(0);
    }

    [Fact]
    public async Task CloseElectionAsync_WithLateActivationPolicy_UsesFinalActiveDenominator()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var openedAt = DateTime.UtcNow.AddMinutes(-10);
        var election = CreateOpenElection() with
        {
            EligibilityMutationPolicy = EligibilityMutationPolicy.LateActivationForRosteredVotersOnly,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
        var activeAtOpenEntry = CreateRosterEntry(
                election,
                "5001",
                ElectionVotingRightStatus.Active)
            .FreezeAtOpen(openedAt);
        var laterActivatedEntry = CreateRosterEntry(
                election,
                "5002",
                ElectionVotingRightStatus.Inactive)
            .FreezeAtOpen(openedAt)
            .MarkVotingRightActive("owner-address", openedAt.AddMinutes(2));

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(store, activeAtOpenEntry, laterActivatedEntry);
        AddParticipationRecords(
            store,
            CreateParticipationRecord(
                election,
                "5001",
                ElectionParticipationStatus.CountedAsVoted,
                openedAt.AddMinutes(3)),
            CreateParticipationRecord(
                election,
                "5002",
                ElectionParticipationStatus.Blank,
                openedAt.AddMinutes(4)));

        var result = await service.CloseElectionAsync(new CloseElectionRequest(
            election.ElectionId,
            "owner-address"));

        result.IsSuccess.Should().BeTrue();
        result.EligibilitySnapshot.Should().NotBeNull();
        result.EligibilitySnapshot!.SnapshotType.Should().Be(ElectionEligibilitySnapshotType.Close);
        result.EligibilitySnapshot.ActiveDenominatorCount.Should().Be(2);
        result.EligibilitySnapshot.CountedParticipationCount.Should().Be(2);
        result.EligibilitySnapshot.BlankCount.Should().Be(1);
        result.EligibilitySnapshot.DidNotVoteCount.Should().Be(0);
    }

    [Fact]
    public async Task CloseAndFinalizeAsync_WithValidOrdering_PersistsCanonicalBoundaryArtifacts()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateOpenElection();
        var closeTransactionId = Guid.NewGuid();
        var finalizeTransactionId = Guid.NewGuid();
        var closeBallotHash = new byte[] { 7, 8 };
        var closeTallyHash = new byte[] { 9, 10 };

        store.Elections[election.ElectionId] = election;

        var closeResult = await service.CloseElectionAsync(new CloseElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            AcceptedBallotSetHash: closeBallotHash,
            FinalEncryptedTallyHash: closeTallyHash,
            SourceTransactionId: closeTransactionId,
            SourceBlockHeight: 52,
            SourceBlockId: Guid.NewGuid()));

        var finalizeResult = await service.FinalizeElectionAsync(new FinalizeElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            AcceptedBallotSetHash: closeBallotHash,
            FinalEncryptedTallyHash: closeTallyHash,
            SourceTransactionId: finalizeTransactionId,
            SourceBlockHeight: 53,
            SourceBlockId: Guid.NewGuid()));

        closeResult.IsSuccess.Should().BeTrue();
        finalizeResult.IsSuccess.Should().BeTrue();
        closeResult.Election!.VoteAcceptanceLockedAt.Should().NotBeNull();
        finalizeResult.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Finalized);
        store.BoundaryArtifacts.Select(x => x.ArtifactType).Should().Equal(
            ElectionBoundaryArtifactType.Close,
            ElectionBoundaryArtifactType.Finalize);
        store.BoundaryArtifacts[0].AcceptedBallotSetHash.Should().Equal(closeBallotHash);
        store.BoundaryArtifacts[0].SourceTransactionId.Should().Be(closeTransactionId);
        store.BoundaryArtifacts[0].SourceBlockHeight.Should().Be(52);
        store.BoundaryArtifacts[1].FinalEncryptedTallyHash.Should().Equal(closeTallyHash);
        store.BoundaryArtifacts[1].SourceTransactionId.Should().Be(finalizeTransactionId);
        store.BoundaryArtifacts[1].SourceBlockHeight.Should().Be(53);
        store.Elections[election.ElectionId].VoteAcceptanceLockedAt.Should().NotBeNull();
        store.Elections[election.ElectionId].CloseArtifactId.Should().Be(closeResult.BoundaryArtifact!.Id);
        store.Elections[election.ElectionId].FinalizeArtifactId.Should().Be(finalizeResult.BoundaryArtifact!.Id);
        finalizeResult.FinalizationSession.Should().NotBeNull();
        finalizeResult.FinalizationSession!.Status.Should().Be(ElectionFinalizationSessionStatus.Completed);
        finalizeResult.FinalizationReleaseEvidence.Should().NotBeNull();
        finalizeResult.FinalizationReleaseEvidence!.AcceptedShareCount.Should().Be(0);
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

    private static ElectionLifecycleService CreateService(
        ElectionStore store,
        ElectionCeremonyOptions? ceremonyOptions = null) =>
        new(
            new FakeUnitOfWorkProvider(store),
            NullLogger<ElectionLifecycleService>.Instance,
            ceremonyOptions ?? new ElectionCeremonyOptions());

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

    private static ElectionRosterImportItem CreateRosterImportItem(
        string organizationVoterId,
        bool isInitiallyActive = true,
        ElectionRosterContactType contactType = ElectionRosterContactType.Email,
        string? contactValue = null) =>
        new(
            organizationVoterId,
            contactType,
            contactValue ?? ResolveContactValue(organizationVoterId, contactType),
            isInitiallyActive);

    private static ElectionRosterEntryRecord CreateRosterEntry(
        ElectionRecord election,
        string organizationVoterId,
        ElectionVotingRightStatus votingRightStatus = ElectionVotingRightStatus.Active,
        ElectionRosterContactType contactType = ElectionRosterContactType.Email,
        string? contactValue = null) =>
        ElectionModelFactory.CreateRosterEntry(
            election.ElectionId,
            organizationVoterId,
            contactType,
            contactValue ?? ResolveContactValue(organizationVoterId, contactType),
            votingRightStatus);

    private static ElectionParticipationRecord CreateParticipationRecord(
        ElectionRecord election,
        string organizationVoterId,
        ElectionParticipationStatus participationStatus,
        DateTime? recordedAt = null) =>
        ElectionModelFactory.CreateParticipationRecord(
            election.ElectionId,
            organizationVoterId,
            participationStatus,
            recordedAt);

    private static AcceptElectionBallotCastRequest CreateCastRequest(
        CastAcceptanceScenario scenario,
        string idempotencyKey = "cast-key-1",
        string ballotNullifier = "nullifier-1") =>
        new(
            scenario.Election.ElectionId,
            "voter-address",
            idempotencyKey,
            EncryptedBallotPackage: "ciphertext-payload",
            ProofBundle: "proof-bundle",
            BallotNullifier: ballotNullifier,
            OpenArtifactId: scenario.OpenArtifact.Id,
            EligibleSetHash: scenario.EligibleSetHash.ToArray(),
            CeremonyVersionId: scenario.CeremonySnapshot.CeremonyVersionId,
            DkgProfileId: scenario.CeremonySnapshot.ProfileId,
            TallyPublicKeyFingerprint: scenario.CeremonySnapshot.TallyPublicKeyFingerprint);

    private static void AddRosterEntries(ElectionStore store, params ElectionRosterEntryRecord[] rosterEntries)
    {
        foreach (var rosterEntry in rosterEntries)
        {
            store.RosterEntries.RemoveAll(x =>
                x.ElectionId == rosterEntry.ElectionId &&
                string.Equals(x.OrganizationVoterId, rosterEntry.OrganizationVoterId, StringComparison.OrdinalIgnoreCase));
            store.RosterEntries.Add(rosterEntry);
        }
    }

    private static void AddParticipationRecords(ElectionStore store, params ElectionParticipationRecord[] participationRecords)
    {
        foreach (var participationRecord in participationRecords)
        {
            store.ParticipationRecords.RemoveAll(x =>
                x.ElectionId == participationRecord.ElectionId &&
                string.Equals(x.OrganizationVoterId, participationRecord.OrganizationVoterId, StringComparison.OrdinalIgnoreCase));
            store.ParticipationRecords.Add(participationRecord);
        }
    }

    private static string ResolveContactValue(
        string organizationVoterId,
        ElectionRosterContactType contactType) =>
        contactType switch
        {
            ElectionRosterContactType.Email => $"{organizationVoterId}@example.com",
            ElectionRosterContactType.Phone => $"+41{organizationVoterId.PadLeft(8, '0')}",
            _ => throw new ArgumentOutOfRangeException(nameof(contactType), contactType, "Unsupported contact type."),
        };

    private static CastAcceptanceScenario SeedOpenElectionForCast(
        ElectionStore store,
        EligibilityMutationPolicy eligibilityMutationPolicy = EligibilityMutationPolicy.FrozenAtOpen,
        bool wasActiveAtOpen = true,
        bool currentlyActive = true,
        bool createCommitmentRegistration = false,
        DateTime? voteAcceptanceLockedAt = null)
    {
        var openedAt = DateTime.UtcNow.AddMinutes(-10);
        var eligibleSetHash = new byte[] { 11, 12, 13, 14 };
        var ceremonySnapshot = ElectionModelFactory.CreateCeremonyBindingSnapshot(
            ceremonyVersionId: Guid.NewGuid(),
            ceremonyVersionNumber: 1,
            profileId: "dkg-feat-099",
            boundTrusteeCount: 1,
            requiredApprovalCount: 1,
            activeTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
            ],
            tallyPublicKeyFingerprint: "tally-feat-099");

        var election = CreateAdminElection(acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet]) with
        {
            EligibilityMutationPolicy = eligibilityMutationPolicy,
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            VoteAcceptanceLockedAt = voteAcceptanceLockedAt,
            LastUpdatedAt = voteAcceptanceLockedAt ?? openedAt,
        };

        var rosterEntry = CreateRosterEntry(
                election,
                organizationVoterId: "4001",
                votingRightStatus: wasActiveAtOpen ? ElectionVotingRightStatus.Active : ElectionVotingRightStatus.Inactive)
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-address", openedAt.AddMinutes(1));

        if (!wasActiveAtOpen && currentlyActive)
        {
            rosterEntry = rosterEntry.MarkVotingRightActive("owner-address", openedAt.AddMinutes(2));
        }

        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            recordedByPublicAddress: "owner-address",
            ceremonySnapshot: ceremonySnapshot,
            recordedAt: openedAt,
            frozenEligibleVoterSetHash: eligibleSetHash);

        election = election with
        {
            OpenArtifactId = openArtifact.Id,
        };

        ElectionCommitmentRegistrationRecord? commitmentRegistration = null;
        if (createCommitmentRegistration)
        {
            commitmentRegistration = ElectionModelFactory.CreateCommitmentRegistrationRecord(
                election.ElectionId,
                rosterEntry.OrganizationVoterId,
                "voter-address",
                "commitment-hash-seeded",
                openedAt.AddMinutes(3));
            store.CommitmentRegistrations.Add(commitmentRegistration);
        }

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(store, rosterEntry);
        store.BoundaryArtifacts.Add(openArtifact);

        return new CastAcceptanceScenario(
            election,
            openArtifact,
            rosterEntry,
            eligibleSetHash,
            ceremonySnapshot,
            commitmentRegistration);
    }

    private static string ComputeScopedHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));

    private static TrusteeFinalizationScenario SeedClosedTrusteeElectionForFinalization(
        ElectionStore store,
        int requiredApprovalCount)
    {
        var draftElection = CreateTrusteeElection(requiredApprovalCount: requiredApprovalCount);
        var trusteeA = CreateAcceptedTrusteeInvitation(draftElection, "trustee-a", "Alice");
        var trusteeB = CreateAcceptedTrusteeInvitation(draftElection, "trustee-b", "Bob");
        var profile = RegisterCeremonyProfile(
            store,
            $"dkg-prod-2of2-finalize-{requiredApprovalCount}",
            trusteeCount: 2,
            requiredApprovalCount: requiredApprovalCount);
        var ceremonyVersion = RegisterCeremonyVersion(
            store,
            draftElection,
            profile,
            [trusteeA, trusteeB],
            completedTrustees: ["trustee-a", "trustee-b"],
            ready: true,
            tallyFingerprint: $"finalize-tally-{requiredApprovalCount}");
        var ceremonySnapshot = ElectionModelFactory.CreateCeremonyBindingSnapshot(
            ceremonyVersion.Id,
            ceremonyVersion.VersionNumber,
            profile.ProfileId,
            profile.TrusteeCount,
            profile.RequiredApprovalCount,
            [
                new ElectionTrusteeReference(trusteeA.TrusteeUserAddress, trusteeA.TrusteeDisplayName),
                new ElectionTrusteeReference(trusteeB.TrusteeUserAddress, trusteeB.TrusteeDisplayName),
            ],
            ceremonyVersion.TallyPublicKeyFingerprint!);
        var openElection = draftElection with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = DateTime.UtcNow.AddMinutes(-10),
            LastUpdatedAt = DateTime.UtcNow.AddMinutes(-10),
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            openElection,
            recordedByPublicAddress: "owner-address",
            recordedAt: openElection.OpenedAt!.Value,
            ceremonySnapshot: ceremonySnapshot);
        var closeBallotHash = new byte[] { 41, 42, 43 };
        var closeTallyHash = new byte[] { 51, 52, 53 };
        var closedElection = openElection with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            OpenArtifactId = openArtifact.Id,
            ClosedAt = DateTime.UtcNow.AddMinutes(-5),
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-4),
            VoteAcceptanceLockedAt = DateTime.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTime.UtcNow.AddMinutes(-4),
        };
        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            closedElection,
            recordedByPublicAddress: "owner-address",
            recordedAt: closedElection.ClosedAt!.Value,
            acceptedBallotSetHash: closeBallotHash,
            finalEncryptedTallyHash: closeTallyHash);
        var storedElection = closedElection with
        {
            CloseArtifactId = closeArtifact.Id,
        };
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            storedElection,
            ElectionGovernedActionType.Finalize,
            proposedByPublicAddress: "owner-address");

        store.Elections[storedElection.ElectionId] = storedElection;
        store.TrusteeInvitations[trusteeA.Id] = trusteeA;
        store.TrusteeInvitations[trusteeB.Id] = trusteeB;
        store.BoundaryArtifacts.Add(openArtifact);
        store.BoundaryArtifacts.Add(closeArtifact);
        store.GovernedProposals[proposal.Id] = proposal;

        return new TrusteeFinalizationScenario(
            storedElection,
            proposal,
            ceremonyVersion,
            openArtifact,
            closeArtifact,
            closeBallotHash,
            closeTallyHash);
    }

    private static ElectionRecord CreateTrusteeElection(
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null,
        int requiredApprovalCount = 2) =>
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
            requiredApprovalCount: requiredApprovalCount);

    private static ElectionTrusteeInvitationRecord CreateAcceptedTrusteeInvitation(
        ElectionRecord election,
        string trusteeUserAddress,
        string trusteeDisplayName) =>
        ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: trusteeUserAddress,
            trusteeDisplayName: trusteeDisplayName,
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision).Accept(
                DateTime.UtcNow,
                election.CurrentDraftRevision,
                ElectionLifecycleState.Draft);

    private static ElectionCeremonyProfileRecord RegisterCeremonyProfile(
        ElectionStore store,
        string profileId,
        int trusteeCount,
        int requiredApprovalCount,
        bool devOnly = false)
    {
        var profile = ElectionModelFactory.CreateCeremonyProfile(
            profileId: profileId,
            displayName: profileId,
            description: $"Test profile {profileId}",
            providerKey: devOnly ? "hush-dev" : "hush-prod",
            profileVersion: "v1",
            trusteeCount: trusteeCount,
            requiredApprovalCount: requiredApprovalCount,
            devOnly: devOnly);
        store.CeremonyProfiles[profile.ProfileId] = profile;
        return profile;
    }

    private static ElectionCeremonyVersionRecord RegisterCeremonyVersion(
        ElectionStore store,
        ElectionRecord election,
        ElectionCeremonyProfileRecord profile,
        IReadOnlyList<ElectionTrusteeInvitationRecord> acceptedInvitations,
        IReadOnlyList<string>? completedTrustees = null,
        bool ready = false,
        string tallyFingerprint = "tally-fingerprint-v1")
    {
        var version = ElectionModelFactory.CreateCeremonyVersion(
            election.ElectionId,
            versionNumber: 1,
            profile.ProfileId,
            profile.RequiredApprovalCount,
            acceptedInvitations
                .Select(x => new ElectionTrusteeReference(x.TrusteeUserAddress, x.TrusteeDisplayName))
                .ToArray(),
            startedByPublicAddress: "owner-address");

        foreach (var invitation in acceptedInvitations)
        {
            var trusteeState = ElectionModelFactory.CreateCeremonyTrusteeState(
                election.ElectionId,
                version.Id,
                invitation.TrusteeUserAddress,
                invitation.TrusteeDisplayName,
                state: ElectionTrusteeCeremonyState.AcceptedTrustee);

            if (completedTrustees?.Contains(invitation.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase) == true)
            {
                trusteeState = trusteeState
                    .PublishTransportKey($"transport-{invitation.TrusteeUserAddress}", DateTime.UtcNow)
                    .MarkJoined(DateTime.UtcNow)
                    .RecordSelfTestSuccess(DateTime.UtcNow)
                    .RecordMaterialSubmitted(DateTime.UtcNow)
                    .MarkCompleted(DateTime.UtcNow, "share-v1");

                var custodyRecord = ElectionModelFactory.CreateCeremonyShareCustodyRecord(
                    election.ElectionId,
                    version.Id,
                    invitation.TrusteeUserAddress,
                    "share-v1");
                store.CeremonyShareCustodyRecords[custodyRecord.Id] = custodyRecord;
            }

            store.CeremonyTrusteeStates[trusteeState.Id] = trusteeState;
        }

        if (ready)
        {
            version = version.MarkReady(DateTime.UtcNow, tallyFingerprint);
        }

        store.CeremonyVersions[version.Id] = version;
        return version;
    }

    private static ElectionDraftSpecification CreateAdminDraftSpecification(
        string title = "Board Election",
        ElectionDisclosureMode disclosureMode = ElectionDisclosureMode.FinalResultsOnly,
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null,
        EligibilityMutationPolicy eligibilityMutationPolicy = EligibilityMutationPolicy.FrozenAtOpen) =>
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
            EligibilityMutationPolicy: eligibilityMutationPolicy,
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

    private sealed record TrusteeFinalizationScenario(
        ElectionRecord Election,
        ElectionGovernedProposalRecord Proposal,
        ElectionCeremonyVersionRecord CeremonyVersion,
        ElectionBoundaryArtifactRecord OpenArtifact,
        ElectionBoundaryArtifactRecord CloseArtifact,
        byte[] AcceptedBallotSetHash,
        byte[] FinalEncryptedTallyHash);

    private sealed record CastAcceptanceScenario(
        ElectionRecord Election,
        ElectionBoundaryArtifactRecord OpenArtifact,
        ElectionRosterEntryRecord RosterEntry,
        byte[] EligibleSetHash,
        ElectionCeremonyBindingSnapshot CeremonySnapshot,
        ElectionCommitmentRegistrationRecord? CommitmentRegistration);

    private sealed class ElectionStore
    {
        public Dictionary<ElectionId, ElectionRecord> Elections { get; } = [];
        public Dictionary<(ElectionId ElectionId, string ActorPublicAddress), ElectionEnvelopeAccessRecord> ElectionEnvelopeAccessRecords { get; } = [];
        public List<ElectionDraftSnapshotRecord> DraftSnapshots { get; } = [];
        public List<ElectionRosterEntryRecord> RosterEntries { get; } = [];
        public List<ElectionEligibilityActivationEventRecord> EligibilityActivationEvents { get; } = [];
        public List<ElectionParticipationRecord> ParticipationRecords { get; } = [];
        public List<ElectionCommitmentRegistrationRecord> CommitmentRegistrations { get; } = [];
        public List<ElectionCheckoffConsumptionRecord> CheckoffConsumptions { get; } = [];
        public List<ElectionEligibilitySnapshotRecord> EligibilitySnapshots { get; } = [];
        public List<ElectionBoundaryArtifactRecord> BoundaryArtifacts { get; } = [];
        public List<ElectionAcceptedBallotRecord> AcceptedBallots { get; } = [];
        public List<ElectionCastIdempotencyRecord> CastIdempotencyRecords { get; } = [];
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
        public Dictionary<Guid, ElectionFinalizationSessionRecord> FinalizationSessions { get; } = [];
        public List<ElectionFinalizationShareRecord> FinalizationShares { get; } = [];
        public List<ElectionFinalizationReleaseEvidenceRecord> FinalizationReleaseEvidenceRecords { get; } = [];
        public TaskCompletionSource<bool>? GetElectionForUpdateEntered { get; set; }
        public TaskCompletionSource<bool>? ReleaseGetElectionForUpdate { get; set; }
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

        public async Task<ElectionRecord?> GetElectionForUpdateAsync(ElectionId electionId)
        {
            store.GetElectionForUpdateEntered?.TrySetResult(true);

            if (store.ReleaseGetElectionForUpdate is not null)
            {
                await store.ReleaseGetElectionForUpdate.Task;
                store.ReleaseGetElectionForUpdate = null;
            }

            return store.Elections.GetValueOrDefault(electionId);
        }

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

        public Task<ElectionEnvelopeAccessRecord?> GetElectionEnvelopeAccessAsync(ElectionId electionId, string actorPublicAddress) =>
            Task.FromResult(
                store.ElectionEnvelopeAccessRecords.GetValueOrDefault((electionId, actorPublicAddress)));

        public Task SaveElectionEnvelopeAccessAsync(ElectionEnvelopeAccessRecord accessRecord)
        {
            store.ElectionEnvelopeAccessRecords[(accessRecord.ElectionId, accessRecord.ActorPublicAddress)] = accessRecord;
            return Task.CompletedTask;
        }

        public Task UpdateElectionEnvelopeAccessAsync(ElectionEnvelopeAccessRecord accessRecord)
        {
            store.ElectionEnvelopeAccessRecords[(accessRecord.ElectionId, accessRecord.ActorPublicAddress)] = accessRecord;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionRosterEntryRecord>> GetRosterEntriesAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionRosterEntryRecord>>(
                store.RosterEntries
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.OrganizationVoterId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        public Task<ElectionRosterEntryRecord?> GetRosterEntryAsync(ElectionId electionId, string organizationVoterId) =>
            Task.FromResult(
                store.RosterEntries.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    string.Equals(x.OrganizationVoterId, organizationVoterId, StringComparison.OrdinalIgnoreCase)));

        public Task<ElectionRosterEntryRecord?> GetRosterEntryByLinkedActorAsync(ElectionId electionId, string actorPublicAddress) =>
            Task.FromResult(
                store.RosterEntries.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    string.Equals(x.LinkedActorPublicAddress, actorPublicAddress, StringComparison.Ordinal)));

        public Task SaveRosterEntryAsync(ElectionRosterEntryRecord rosterEntry)
        {
            store.RosterEntries.RemoveAll(x =>
                x.ElectionId == rosterEntry.ElectionId &&
                string.Equals(x.OrganizationVoterId, rosterEntry.OrganizationVoterId, StringComparison.OrdinalIgnoreCase));
            store.RosterEntries.Add(rosterEntry);
            return Task.CompletedTask;
        }

        public Task UpdateRosterEntryAsync(ElectionRosterEntryRecord rosterEntry) =>
            SaveRosterEntryAsync(rosterEntry);

        public Task DeleteRosterEntriesAsync(ElectionId electionId)
        {
            store.RosterEntries.RemoveAll(x => x.ElectionId == electionId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionEligibilityActivationEventRecord>> GetEligibilityActivationEventsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionEligibilityActivationEventRecord>>(
                store.EligibilityActivationEvents
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.OccurredAt)
                    .ThenBy(x => x.Id)
                    .ToArray());

        public Task SaveEligibilityActivationEventAsync(ElectionEligibilityActivationEventRecord activationEvent)
        {
            store.EligibilityActivationEvents.Add(activationEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionParticipationRecord>> GetParticipationRecordsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionParticipationRecord>>(
                store.ParticipationRecords
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.OrganizationVoterId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        public Task<ElectionParticipationRecord?> GetParticipationRecordAsync(ElectionId electionId, string organizationVoterId) =>
            Task.FromResult(
                store.ParticipationRecords.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    string.Equals(x.OrganizationVoterId, organizationVoterId, StringComparison.OrdinalIgnoreCase)));

        public Task SaveParticipationRecordAsync(ElectionParticipationRecord participationRecord)
        {
            store.ParticipationRecords.RemoveAll(x =>
                x.ElectionId == participationRecord.ElectionId &&
                string.Equals(x.OrganizationVoterId, participationRecord.OrganizationVoterId, StringComparison.OrdinalIgnoreCase));
            store.ParticipationRecords.Add(participationRecord);
            return Task.CompletedTask;
        }

        public Task UpdateParticipationRecordAsync(ElectionParticipationRecord participationRecord) =>
            SaveParticipationRecordAsync(participationRecord);

        public Task<IReadOnlyList<ElectionCommitmentRegistrationRecord>> GetCommitmentRegistrationsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionCommitmentRegistrationRecord>>(
                store.CommitmentRegistrations
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.RegisteredAt)
                    .ToArray());

        public Task<ElectionCommitmentRegistrationRecord?> GetCommitmentRegistrationAsync(
            ElectionId electionId,
            string organizationVoterId) =>
            Task.FromResult(
                store.CommitmentRegistrations.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    string.Equals(x.OrganizationVoterId, organizationVoterId, StringComparison.OrdinalIgnoreCase)));

        public Task<ElectionCommitmentRegistrationRecord?> GetCommitmentRegistrationByLinkedActorAsync(
            ElectionId electionId,
            string actorPublicAddress) =>
            Task.FromResult(
                store.CommitmentRegistrations.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    string.Equals(x.LinkedActorPublicAddress, actorPublicAddress, StringComparison.Ordinal)));

        public Task SaveCommitmentRegistrationAsync(ElectionCommitmentRegistrationRecord commitmentRegistration)
        {
            store.CommitmentRegistrations.RemoveAll(x =>
                x.ElectionId == commitmentRegistration.ElectionId &&
                string.Equals(x.OrganizationVoterId, commitmentRegistration.OrganizationVoterId, StringComparison.OrdinalIgnoreCase));
            store.CommitmentRegistrations.Add(commitmentRegistration);
            return Task.CompletedTask;
        }

        public Task UpdateCommitmentRegistrationAsync(ElectionCommitmentRegistrationRecord commitmentRegistration) =>
            SaveCommitmentRegistrationAsync(commitmentRegistration);

        public Task<IReadOnlyList<ElectionCheckoffConsumptionRecord>> GetCheckoffConsumptionsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionCheckoffConsumptionRecord>>(
                store.CheckoffConsumptions
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.ConsumedAt)
                    .ToArray());

        public Task<ElectionCheckoffConsumptionRecord?> GetCheckoffConsumptionAsync(
            ElectionId electionId,
            string organizationVoterId) =>
            Task.FromResult(
                store.CheckoffConsumptions.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    string.Equals(x.OrganizationVoterId, organizationVoterId, StringComparison.OrdinalIgnoreCase)));

        public Task SaveCheckoffConsumptionAsync(ElectionCheckoffConsumptionRecord checkoffConsumption)
        {
            store.CheckoffConsumptions.RemoveAll(x =>
                x.ElectionId == checkoffConsumption.ElectionId &&
                string.Equals(x.OrganizationVoterId, checkoffConsumption.OrganizationVoterId, StringComparison.OrdinalIgnoreCase));
            store.CheckoffConsumptions.Add(checkoffConsumption);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionEligibilitySnapshotRecord>> GetEligibilitySnapshotsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionEligibilitySnapshotRecord>>(
                store.EligibilitySnapshots
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.RecordedAt)
                    .ThenBy(x => x.Id)
                    .ToArray());

        public Task<ElectionEligibilitySnapshotRecord?> GetEligibilitySnapshotAsync(
            ElectionId electionId,
            ElectionEligibilitySnapshotType snapshotType) =>
            Task.FromResult(
                store.EligibilitySnapshots
                    .Where(x => x.ElectionId == electionId && x.SnapshotType == snapshotType)
                    .OrderByDescending(x => x.RecordedAt)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefault());

        public Task SaveEligibilitySnapshotAsync(ElectionEligibilitySnapshotRecord snapshot)
        {
            store.EligibilitySnapshots.RemoveAll(x =>
                x.ElectionId == snapshot.ElectionId &&
                x.SnapshotType == snapshot.SnapshotType);
            store.EligibilitySnapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionAcceptedBallotRecord>> GetAcceptedBallotsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionAcceptedBallotRecord>>(
                store.AcceptedBallots
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.AcceptedAt)
                    .ToArray());

        public Task<ElectionAcceptedBallotRecord?> GetAcceptedBallotAsync(Guid acceptedBallotId) =>
            Task.FromResult(store.AcceptedBallots.FirstOrDefault(x => x.Id == acceptedBallotId));

        public Task<ElectionAcceptedBallotRecord?> GetAcceptedBallotByNullifierAsync(
            ElectionId electionId,
            string ballotNullifier) =>
            Task.FromResult(
                store.AcceptedBallots.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    string.Equals(x.BallotNullifier, ballotNullifier, StringComparison.Ordinal)));

        public Task SaveAcceptedBallotAsync(ElectionAcceptedBallotRecord acceptedBallot)
        {
            store.AcceptedBallots.RemoveAll(x => x.Id == acceptedBallot.Id);
            store.AcceptedBallots.Add(acceptedBallot);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionCastIdempotencyRecord>> GetCastIdempotencyRecordsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionCastIdempotencyRecord>>(
                store.CastIdempotencyRecords
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.RecordedAt)
                    .ToArray());

        public Task<ElectionCastIdempotencyRecord?> GetCastIdempotencyRecordAsync(
            ElectionId electionId,
            string idempotencyKeyHash) =>
            Task.FromResult(
                store.CastIdempotencyRecords.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    string.Equals(x.IdempotencyKeyHash, idempotencyKeyHash, StringComparison.Ordinal)));

        public Task SaveCastIdempotencyRecordAsync(ElectionCastIdempotencyRecord idempotencyRecord)
        {
            store.CastIdempotencyRecords.RemoveAll(x =>
                x.ElectionId == idempotencyRecord.ElectionId &&
                string.Equals(x.IdempotencyKeyHash, idempotencyRecord.IdempotencyKeyHash, StringComparison.Ordinal));
            store.CastIdempotencyRecords.Add(idempotencyRecord);
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

        public Task<IReadOnlyList<ElectionFinalizationSessionRecord>> GetFinalizationSessionsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionFinalizationSessionRecord>>(
                store.FinalizationSessions.Values
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.CreatedAt)
                    .ToArray());

        public Task<ElectionFinalizationSessionRecord?> GetFinalizationSessionAsync(Guid finalizationSessionId) =>
            Task.FromResult(store.FinalizationSessions.GetValueOrDefault(finalizationSessionId));

        public Task<ElectionFinalizationSessionRecord?> GetActiveFinalizationSessionAsync(ElectionId electionId)
        {
            var activeSessions = store.FinalizationSessions.Values
                .Where(x =>
                    x.ElectionId == electionId &&
                    x.Status != ElectionFinalizationSessionStatus.Completed)
                .OrderBy(x => x.CreatedAt)
                .ToArray();

            return activeSessions.Length switch
            {
                0 => Task.FromResult<ElectionFinalizationSessionRecord?>(null),
                1 => Task.FromResult<ElectionFinalizationSessionRecord?>(activeSessions[0]),
                _ => throw new InvalidOperationException(
                    $"Election {electionId} has multiple active finalization sessions, which violates the FEAT-098 invariant."),
            };
        }

        public Task SaveFinalizationSessionAsync(ElectionFinalizationSessionRecord session)
        {
            store.FinalizationSessions[session.Id] = session;
            return Task.CompletedTask;
        }

        public Task UpdateFinalizationSessionAsync(ElectionFinalizationSessionRecord session)
        {
            store.FinalizationSessions[session.Id] = session;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionFinalizationShareRecord>> GetFinalizationSharesAsync(Guid finalizationSessionId) =>
            Task.FromResult<IReadOnlyList<ElectionFinalizationShareRecord>>(
                store.FinalizationShares
                    .Where(x => x.FinalizationSessionId == finalizationSessionId)
                    .OrderBy(x => x.SubmittedAt)
                    .ThenBy(x => x.Id)
                    .ToArray());

        public Task<ElectionFinalizationShareRecord?> GetAcceptedFinalizationShareAsync(
            Guid finalizationSessionId,
            string trusteeUserAddress) =>
            Task.FromResult(
                store.FinalizationShares
                    .Where(x =>
                        x.FinalizationSessionId == finalizationSessionId &&
                        x.TrusteeUserAddress == trusteeUserAddress &&
                        x.Status == ElectionFinalizationShareStatus.Accepted)
                    .OrderByDescending(x => x.SubmittedAt)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefault());

        public Task SaveFinalizationShareAsync(ElectionFinalizationShareRecord shareRecord)
        {
            store.FinalizationShares.Add(shareRecord);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionFinalizationReleaseEvidenceRecord>> GetFinalizationReleaseEvidenceRecordsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionFinalizationReleaseEvidenceRecord>>(
                store.FinalizationReleaseEvidenceRecords
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.CompletedAt)
                    .ToArray());

        public Task<ElectionFinalizationReleaseEvidenceRecord?> GetFinalizationReleaseEvidenceRecordAsync(Guid finalizationSessionId) =>
            Task.FromResult(
                store.FinalizationReleaseEvidenceRecords.FirstOrDefault(x => x.FinalizationSessionId == finalizationSessionId));

        public Task SaveFinalizationReleaseEvidenceRecordAsync(ElectionFinalizationReleaseEvidenceRecord releaseEvidenceRecord)
        {
            store.FinalizationReleaseEvidenceRecords.Add(releaseEvidenceRecord);
            return Task.CompletedTask;
        }
    }
}
