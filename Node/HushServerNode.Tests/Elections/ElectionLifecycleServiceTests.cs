using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Numerics;
using FluentAssertions;
using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushNode.Identity.Storage;
using HushNode.Reactions.Crypto;
using HushShared.Blockchain.BlockModel;
using HushShared.Elections.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Olimpo;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionLifecycleServiceTests
{
    private const string ExecutorKeyAlgorithm = "ecies-secp256k1-v1";
    private static readonly CredentialsProfile TestNodeCredentials = CreateTestNodeCredentials();

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
    public async Task ImportRosterAsync_WithDraftElection_AppendsNewRosterEntries()
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
            .BeEquivalentTo("1001", "1002", "2001", "2002");
        store.RosterEntries
            .Single(x =>
                x.ElectionId == election.ElectionId &&
                string.Equals(x.OrganizationVoterId, "2002", StringComparison.OrdinalIgnoreCase))
            .VotingRightStatus
            .Should()
            .Be(ElectionVotingRightStatus.Inactive);
    }

    [Fact]
    public async Task ImportRosterAsync_WithExistingOrganizationVoterIds_KeepsExistingRowsAndSkipsDuplicates()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateAdminElection();
        var linkedExistingEntry = CreateRosterEntry(election, "1001")
            .LinkToActor("linked-voter-address", DateTime.UtcNow.AddMinutes(-5));

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(store, linkedExistingEntry);

        var result = await service.ImportRosterAsync(new ImportElectionRosterRequest(
            election.ElectionId,
            "owner-address",
            [
                CreateRosterImportItem("1001", contactValue: "changed@example.org"),
                CreateRosterImportItem("2001"),
            ]));

        result.IsSuccess.Should().BeTrue();
        result.RosterEntries.Should().ContainSingle();
        result.RosterEntries[0].OrganizationVoterId.Should().Be("2001");
        store.RosterEntries
            .Where(x => x.ElectionId == election.ElectionId)
            .Select(x => x.OrganizationVoterId)
            .Should()
            .BeEquivalentTo("1001", "2001");

        var preservedEntry = store.RosterEntries.Single(x =>
            x.ElectionId == election.ElectionId &&
            string.Equals(x.OrganizationVoterId, "1001", StringComparison.OrdinalIgnoreCase));
        preservedEntry.LinkedActorPublicAddress.Should().Be("linked-voter-address");
        preservedEntry.ContactValue.Should().Be(linkedExistingEntry.ContactValue);
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
    public async Task ClaimRosterEntryAsync_AfterElectionFinalized_StillLinksRosterEntry()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var finalizedAt = DateTime.UtcNow.AddMinutes(-2);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = finalizedAt.AddMinutes(-3),
            FinalizedAt = finalizedAt,
            LastUpdatedAt = finalizedAt,
        };

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(store, CreateRosterEntry(election, "2001"));

        var result = await service.ClaimRosterEntryAsync(new ClaimElectionRosterEntryRequest(
            election.ElectionId,
            "voter-address",
            "2001",
            "1111"));

        result.IsSuccess.Should().BeTrue();
        result.RosterEntry.Should().NotBeNull();
        result.RosterEntry!.LinkedActorPublicAddress.Should().Be("voter-address");
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
        result.AcceptedBallot.AcceptedAt.Should().NotBe(result.CheckoffConsumption.ConsumedAt);
        result.AcceptedBallot.AcceptedAt.Should().Be(GetProtectedAcceptedBallotTimestamp(scenario.Election));
        result.CastIdempotencyRecord.Should().NotBeNull();
        store.ParticipationRecords.Should().ContainSingle();
        store.CheckoffConsumptions.Should().ContainSingle();
        store.AcceptedBallots.Should().ContainSingle();
        store.BallotMemPoolEntries.Should().ContainSingle();
        store.BallotMemPoolEntries[0].QueuedAt.Should().Be(result.AcceptedBallot.AcceptedAt);
        store.CastIdempotencyRecords.Should().ContainSingle();
    }

    [Fact]
    public async Task AcceptBallotCastAsync_OnBindingElection_RejectsDevModeBallotArtifacts()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(store, createCommitmentRegistration: true);

        var result = await service.AcceptBallotCastAsync(CreateCastRequest(
            scenario,
            encryptedBallotPackage: CreateDevModeAcceptedBallotPackageForTests(),
            proofBundle: CreateDevModeAcceptedProofBundleForTests()));

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be(ElectionCastAcceptanceFailureReason.ValidationFailed);
        result.ErrorMessage.Should().Contain("selected non-dev circuit");
        store.AcceptedBallots.Should().BeEmpty();
        store.BallotMemPoolEntries.Should().BeEmpty();
        store.CastIdempotencyRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptBallotCastAsync_OnNonBindingElection_AllowsDevModeBallotArtifacts()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(store, createCommitmentRegistration: true);
        var nonBindingElection = scenario.Election with
        {
            BindingStatus = ElectionBindingStatus.NonBinding,
            SelectedProfileId = "admin-dev-1of1",
            SelectedProfileDevOnly = true,
        };
        store.Elections[scenario.Election.ElectionId] = nonBindingElection;
        scenario = scenario with
        {
            Election = nonBindingElection,
        };

        var result = await service.AcceptBallotCastAsync(CreateCastRequest(
            scenario,
            encryptedBallotPackage: CreateDevModeAcceptedBallotPackageForTests(),
            proofBundle: CreateDevModeAcceptedProofBundleForTests()));

        result.IsSuccess.Should().BeTrue();
        result.AcceptedBallot.Should().NotBeNull();
        store.AcceptedBallots.Should().ContainSingle();
        store.BallotMemPoolEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task AcceptBallotCastAsync_WithMultipleVoters_UsesSharedPrivacyBucketForAcceptedAndQueuedArtifacts()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(store, createCommitmentRegistration: true);
        var protectedTimestamp = GetProtectedAcceptedBallotTimestamp(scenario.Election);
        var openedAt = scenario.Election.OpenedAt!.Value;
        var secondRosterEntry = CreateRosterEntry(
                scenario.Election,
                organizationVoterId: "4002",
                votingRightStatus: ElectionVotingRightStatus.Active)
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-address-2", openedAt.AddMinutes(1));
        AddRosterEntries(store, secondRosterEntry);
        store.CommitmentRegistrations.Add(ElectionModelFactory.CreateCommitmentRegistrationRecord(
            scenario.Election.ElectionId,
            secondRosterEntry.OrganizationVoterId,
            "voter-address-2",
            "commitment-hash-seeded-2",
            openedAt.AddMinutes(3)));

        var firstResult = await service.AcceptBallotCastAsync(CreateCastRequest(
            scenario,
            idempotencyKey: "cast-key-1",
            ballotNullifier: "nullifier-1"));
        var secondResult = await service.AcceptBallotCastAsync(new AcceptElectionBallotCastRequest(
            scenario.Election.ElectionId,
            "voter-address-2",
            "cast-key-2",
            EncryptedBallotPackage: "ciphertext-payload-2",
            ProofBundle: "proof-bundle-2",
            BallotNullifier: "nullifier-2",
            OpenArtifactId: scenario.OpenArtifact.Id,
            EligibleSetHash: scenario.EligibleSetHash.ToArray(),
            CeremonyVersionId: scenario.CeremonySnapshot.CeremonyVersionId,
            DkgProfileId: scenario.CeremonySnapshot.ProfileId,
            TallyPublicKeyFingerprint: scenario.CeremonySnapshot.TallyPublicKeyFingerprint));

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        store.CheckoffConsumptions.Should().HaveCount(2);
        store.CheckoffConsumptions.Select(x => x.ConsumedAt).Distinct().Should().HaveCount(2);
        store.AcceptedBallots.Should().HaveCount(2);
        store.AcceptedBallots.Select(x => x.AcceptedAt).Distinct().Should().ContainSingle().Which.Should().Be(protectedTimestamp);
        store.BallotMemPoolEntries.Should().HaveCount(2);
        store.BallotMemPoolEntries.Select(x => x.QueuedAt).Distinct().Should().ContainSingle().Which.Should().Be(protectedTimestamp);
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
    public async Task AcceptBallotCastAsync_WithLegacyAdminOnlyOpenBoundaryWithoutStoredSnapshot_UsesSyntheticProtectedTallyBinding()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var scenario = SeedOpenElectionForCast(store, createCommitmentRegistration: true);
        var legacyOpenArtifact = scenario.OpenArtifact with
        {
            CeremonySnapshot = null,
        };
        store.BoundaryArtifacts[0] = legacyOpenArtifact;

        var syntheticBinding = ElectionProtectedTallyBinding.BuildAdminOnlyProtectedTallyBindingSnapshot(scenario.Election);
        var request = CreateCastRequest(scenario) with
        {
            CeremonyVersionId = syntheticBinding.CeremonyVersionId,
            DkgProfileId = syntheticBinding.ProfileId,
            TallyPublicKeyFingerprint = syntheticBinding.TallyPublicKeyFingerprint,
        };

        var result = await service.AcceptBallotCastAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.AcceptedBallot.Should().NotBeNull();
        store.AcceptedBallots.Should().ContainSingle();
        store.CheckoffConsumptions.Should().ContainSingle();
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
    public async Task AcceptBallotCastAsync_OnSuccess_PopulatesCommittedIdempotencyCache()
    {
        var store = new ElectionStore();
        var cacheService = new Mock<IElectionCastIdempotencyCacheService>();
        var service = CreateService(store, castIdempotencyCacheService: cacheService.Object);
        var scenario = SeedOpenElectionForCast(store, createCommitmentRegistration: true);

        var result = await service.AcceptBallotCastAsync(CreateCastRequest(
            scenario,
            idempotencyKey: "cast-key-cache",
            ballotNullifier: "nullifier-cache"));

        result.IsSuccess.Should().BeTrue();
        cacheService.Verify(
            x => x.SetAsync(
                scenario.Election.ElectionId.ToString(),
                ComputeScopedHash("cast-key-cache")),
            Times.Once);
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
    public async Task CreateReportAccessGrantAsync_AfterFinalize_PersistsGrantAndUpdatesElection()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var finalizedAt = DateTime.UtcNow.AddMinutes(-5);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            FinalizedAt = finalizedAt,
            OfficialResultArtifactId = Guid.NewGuid(),
            LastUpdatedAt = finalizedAt,
        };

        store.Elections[election.ElectionId] = election;

        var result = await service.CreateReportAccessGrantAsync(new CreateElectionReportAccessGrantRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            DesignatedAuditorPublicAddress: "auditor-address"));

        result.IsSuccess.Should().BeTrue();
        result.ReportAccessGrant.Should().NotBeNull();
        result.ReportAccessGrant!.ActorPublicAddress.Should().Be("auditor-address");
        result.ReportAccessGrant.GrantedByPublicAddress.Should().Be("owner-address");
        result.Election.Should().NotBeNull();
        result.Election!.LastUpdatedAt.Should().Be(result.ReportAccessGrant.GrantedAt);
        result.Election.LastUpdatedAt.Should().BeAfter(finalizedAt);
        store.ReportAccessGrants.Should().ContainSingle();
        store.ReportAccessGrants[0].GrantRole.Should().Be(ElectionReportAccessGrantRole.DesignatedAuditor);
        store.Elections[election.ElectionId].LastUpdatedAt.Should().Be(result.ReportAccessGrant.GrantedAt);
    }

    [Fact]
    public async Task CreateReportAccessGrantAsync_WithAcceptedTrusteeTarget_ReturnsConflict()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection();
        var acceptedInvitation = ElectionModelFactory.CreateTrusteeInvitation(
                election.ElectionId,
                trusteeUserAddress: "trustee-a",
                trusteeDisplayName: "Alice",
                invitedByPublicAddress: "owner-address",
                sentAtDraftRevision: election.CurrentDraftRevision)
            .Accept(
                respondedAt: DateTime.UtcNow.AddMinutes(-2),
                resolvedAtDraftRevision: election.CurrentDraftRevision,
                lifecycleState: ElectionLifecycleState.Draft);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedInvitation.Id] = acceptedInvitation;

        var result = await service.CreateReportAccessGrantAsync(new CreateElectionReportAccessGrantRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            DesignatedAuditorPublicAddress: "trustee-a"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.Conflict);
        store.ReportAccessGrants.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateReportAccessGrantAsync_WithExistingGrant_ReturnsConflict()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateAdminElection();
        var existingGrant = ElectionModelFactory.CreateReportAccessGrant(
            election.ElectionId,
            actorPublicAddress: "auditor-address",
            grantedByPublicAddress: "owner-address",
            grantedAt: DateTime.UtcNow.AddMinutes(-3));

        store.Elections[election.ElectionId] = election;
        store.ReportAccessGrants.Add(existingGrant);

        var result = await service.CreateReportAccessGrantAsync(new CreateElectionReportAccessGrantRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            DesignatedAuditorPublicAddress: "auditor-address"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.Conflict);
        store.ReportAccessGrants.Should().ContainSingle();
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
    public async Task AcceptTrusteeInvitation_WhenActorAlreadyHasDesignatedAuditorGrant_ReturnsConflict()
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
        var existingGrant = ElectionModelFactory.CreateReportAccessGrant(
            election.ElectionId,
            actorPublicAddress: "trustee-a",
            grantedByPublicAddress: "owner-address",
            grantedAt: DateTime.UtcNow.AddMinutes(-1));

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[invitation.Id] = invitation;
        store.ReportAccessGrants.Add(existingGrant);

        var result = await service.AcceptTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest(
            ElectionId: election.ElectionId,
            InvitationId: invitation.Id,
            ActorPublicAddress: "trustee-a"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.Conflict);
        store.TrusteeInvitations[invitation.Id].Status.Should().Be(ElectionTrusteeInvitationStatus.Pending);
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
    public async Task RevokeTrusteeInvitation_WithExistingEnvelopeAccess_RemovesEnvelopeAccess()
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
        store.ElectionEnvelopeAccessRecords[(election.ElectionId, "trustee-a")] = new ElectionEnvelopeAccessRecord(
            election.ElectionId,
            "trustee-a",
            "node-wrapped-election-private-key",
            "wrapped-election-private-key",
            DateTime.UtcNow,
            Guid.NewGuid(),
            12,
            Guid.NewGuid());

        var result = await service.RevokeTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest(
            ElectionId: election.ElectionId,
            InvitationId: invitation.Id,
            ActorPublicAddress: "owner-address"));

        result.IsSuccess.Should().BeTrue();
        store.ElectionEnvelopeAccessRecords.Should().NotContainKey((election.ElectionId, "trustee-a"));
    }

    [Fact]
    public async Task RejectTrusteeInvitation_WithExistingEnvelopeAccess_RemovesEnvelopeAccess()
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
        store.ElectionEnvelopeAccessRecords[(election.ElectionId, "trustee-a")] = new ElectionEnvelopeAccessRecord(
            election.ElectionId,
            "trustee-a",
            "node-wrapped-election-private-key",
            "wrapped-election-private-key",
            DateTime.UtcNow,
            Guid.NewGuid(),
            12,
            Guid.NewGuid());

        var result = await service.RejectTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest(
            ElectionId: election.ElectionId,
            InvitationId: invitation.Id,
            ActorPublicAddress: "trustee-a"));

        result.IsSuccess.Should().BeTrue();
        store.ElectionEnvelopeAccessRecords.Should().NotContainKey((election.ElectionId, "trustee-a"));
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
    public async Task StartElectionCeremonyAsync_WithBindingElectionAndDevProfile_ReturnsValidationFailed()
    {
        var store = new ElectionStore();
        var service = CreateService(store, new ElectionCeremonyOptions(EnableDevCeremonyProfiles: true));
        var profile = RegisterCeremonyProfile(store, "dkg-dev-1of1", trusteeCount: 1, requiredApprovalCount: 1, devOnly: true);
        var election = CreateTrusteeElection(
            requiredApprovalCount: 1,
            selectedProfileId: profile.ProfileId,
            selectedProfileDevOnly: true);
        var acceptedTrustee = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrustee.Id] = acceptedTrustee;

        var result = await service.StartElectionCeremonyAsync(new StartElectionCeremonyRequest(
            election.ElectionId,
            "owner-address",
            profile.ProfileId));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        result.ErrorMessage.Should().Contain("Binding elections cannot use dev/open ceremony profiles");
        store.CeremonyVersions.Should().BeEmpty();
    }

    [Fact]
    public async Task StartElectionCeremonyAsync_WithNonBindingElectionAndProductionProfile_StartsCeremony()
    {
        var store = new ElectionStore();
        var service = CreateService(store, new ElectionCeremonyOptions(EnableDevCeremonyProfiles: true));
        var profile = RegisterCeremonyProfile(store, "dkg-prod-1of1", trusteeCount: 1, requiredApprovalCount: 1, devOnly: false);
        var election = CreateTrusteeElection(
            requiredApprovalCount: 1,
            bindingStatus: ElectionBindingStatus.NonBinding,
            selectedProfileId: profile.ProfileId,
            selectedProfileDevOnly: false);
        var acceptedTrustee = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrustee.Id] = acceptedTrustee;

        var result = await service.StartElectionCeremonyAsync(new StartElectionCeremonyRequest(
            election.ElectionId,
            "owner-address",
            profile.ProfileId));

        result.IsSuccess.Should().BeTrue();
        result.CeremonyVersion.Should().NotBeNull();
        result.CeremonyVersion!.ProfileId.Should().Be(profile.ProfileId);
        store.CeremonyVersions.Should().ContainSingle();
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
            PayloadFingerprint: "payload-fingerprint",
            ShareVersion: "share-v1",
            CloseCountingPublicCommitment: CeremonyTestKeyFixtures.PublicKeyBytes));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        store.CeremonyMessageEnvelopes.Should().BeEmpty();
        store.CeremonyTrusteeStates[joinedState.Id].State.Should().Be(ElectionTrusteeCeremonyState.CeremonyJoined);
    }

    [Fact]
    public async Task SubmitElectionCeremonyMaterialAsync_WithShareVersion_PersistsSubmittedShareVersion()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection(requiredApprovalCount: 1);
        var acceptedTrustee = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var profile = RegisterCeremonyProfile(store, "dkg-prod-1of1-submit-success", trusteeCount: 1, requiredApprovalCount: 1);
        var version = RegisterCeremonyVersion(
            store,
            election,
            profile,
            [acceptedTrustee],
            completedTrustees: [],
            ready: false);
        var readyToSubmitState = store.CeremonyTrusteeStates.Values.Single()
            .PublishTransportKey("transport-a", DateTime.UtcNow)
            .MarkJoined(DateTime.UtcNow)
            .RecordSelfTestSuccess(DateTime.UtcNow);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrustee.Id] = acceptedTrustee;
        store.CeremonyTrusteeStates[readyToSubmitState.Id] = readyToSubmitState;

        var result = await service.SubmitElectionCeremonyMaterialAsync(new SubmitElectionCeremonyMaterialRequest(
            election.ElectionId,
            version.Id,
            "trustee-a",
            RecipientTrusteeUserAddress: null,
            MessageType: "share-package",
            PayloadVersion: "v1",
            EncryptedPayload: [1, 2, 3],
            PayloadFingerprint: "payload-fingerprint",
            ShareVersion: "share-v1",
            CloseCountingPublicCommitment: CeremonyTestKeyFixtures.PublicKeyBytes));

        result.IsSuccess.Should().BeTrue();
        result.CeremonyTrusteeState.Should().NotBeNull();
        result.CeremonyTrusteeState!.State.Should().Be(ElectionTrusteeCeremonyState.CeremonyMaterialSubmitted);
        result.CeremonyTrusteeState.ShareVersion.Should().Be("share-v1");
        result.CeremonyTrusteeState.CloseCountingPublicCommitment.Should().Equal(CeremonyTestKeyFixtures.PublicKeyBytes);
        store.CeremonyTrusteeStates[readyToSubmitState.Id].ShareVersion.Should().Be("share-v1");
    }

    [Fact]
    public async Task CompleteElectionCeremonyTrusteeAsync_WhenEveryBoundTrusteeCompletes_MarksVersionReadyAndCreatesShareCustody()
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
            .RecordMaterialSubmitted(DateTime.UtcNow, "share-v1", CeremonyTestKeyFixtures.PublicKeyBytes);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrustee.Id] = acceptedTrustee;
        store.CeremonyTrusteeStates[submittedState.Id] = submittedState;

        var result = await service.CompleteElectionCeremonyTrusteeAsync(new CompleteElectionCeremonyTrusteeRequest(
            election.ElectionId,
            version.Id,
            "owner-address",
            "trustee-a",
            "share-v1"));

        result.IsSuccess.Should().BeTrue();
        result.CeremonyVersion.Should().NotBeNull();
        result.CeremonyVersion!.Status.Should().Be(ElectionCeremonyVersionStatus.Ready);
        result.CeremonyVersion.TallyPublicKeyFingerprint.Should().NotBeNullOrWhiteSpace();
        result.CeremonyVersion.TallyPublicKey.Should().NotBeNull();
        result.CeremonyVersion.TallyPublicKey.Should().NotEqual(CeremonyTestKeyFixtures.PublicKeyBytes);
        result.CeremonyShareCustody.Should().NotBeNull();
        result.CeremonyShareCustody!.ShareVersion.Should().Be("share-v1");
        store.CeremonyMessageEnvelopes.Should().ContainSingle();
        store.CeremonyMessageEnvelopes[0].MessageType.Should().Be(TrusteeThresholdShareDistribution.TrusteeVaultMessageType);
        store.CeremonyMessageEnvelopes[0].PayloadVersion.Should().Be(TrusteeThresholdShareDistribution.ServerIssuedPayloadVersion);
        store.CeremonyTranscriptEvents.Should().Contain(x =>
            x.EventType == ElectionCeremonyTranscriptEventType.VersionReady);
    }

    [Fact]
    public async Task CompleteElectionCeremonyTrusteeAsync_WithThreeOfFiveProfile_DoesNotMarkReadyUntilAllBoundTrusteesComplete()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection(requiredApprovalCount: 3);
        var acceptedTrusteeA = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var acceptedTrusteeB = CreateAcceptedTrusteeInvitation(election, "trustee-b", "Bob");
        var acceptedTrusteeC = CreateAcceptedTrusteeInvitation(election, "trustee-c", "Carol");
        var acceptedTrusteeD = CreateAcceptedTrusteeInvitation(election, "trustee-d", "Dan");
        var acceptedTrusteeE = CreateAcceptedTrusteeInvitation(election, "trustee-e", "Erin");
        var profile = RegisterCeremonyProfile(store, "dkg-prod-3of5-all-trustees", trusteeCount: 5, requiredApprovalCount: 3);
        var version = RegisterCeremonyVersion(
            store,
            election,
            profile,
            [acceptedTrusteeA, acceptedTrusteeB, acceptedTrusteeC, acceptedTrusteeD, acceptedTrusteeE],
            completedTrustees: ["trustee-a", "trustee-b"],
            ready: false);
        var submittedState = store.CeremonyTrusteeStates.Values.Single(x =>
                string.Equals(x.TrusteeUserAddress, "trustee-c", StringComparison.Ordinal))
            .PublishTransportKey("transport-c", DateTime.UtcNow)
            .MarkJoined(DateTime.UtcNow)
            .RecordSelfTestSuccess(DateTime.UtcNow)
            .RecordMaterialSubmitted(DateTime.UtcNow, "share-v3", CeremonyTestKeyFixtures.PublicKeyBytes);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedTrusteeA.Id] = acceptedTrusteeA;
        store.TrusteeInvitations[acceptedTrusteeB.Id] = acceptedTrusteeB;
        store.TrusteeInvitations[acceptedTrusteeC.Id] = acceptedTrusteeC;
        store.TrusteeInvitations[acceptedTrusteeD.Id] = acceptedTrusteeD;
        store.TrusteeInvitations[acceptedTrusteeE.Id] = acceptedTrusteeE;
        store.CeremonyTrusteeStates[submittedState.Id] = submittedState;

        var result = await service.CompleteElectionCeremonyTrusteeAsync(new CompleteElectionCeremonyTrusteeRequest(
            election.ElectionId,
            version.Id,
            "owner-address",
            "trustee-c",
            "share-v3"));

        result.IsSuccess.Should().BeTrue();
        result.CeremonyVersion.Should().NotBeNull();
        result.CeremonyVersion!.Status.Should().Be(ElectionCeremonyVersionStatus.InProgress);
        result.CeremonyVersion.TallyPublicKeyFingerprint.Should().BeNullOrEmpty();
        store.CeremonyTranscriptEvents.Should().NotContain(x =>
            x.EventType == ElectionCeremonyTranscriptEventType.VersionReady);
    }

    [Fact]
    public void TrusteeThresholdShareDistribution_Create_ForThreeOfFiveProfile_AllowsAnyThreeSharesToReconstructTheSamePublicKey()
    {
        var store = new ElectionStore();
        var election = CreateTrusteeElection(requiredApprovalCount: 3);
        var acceptedTrustees = new[]
        {
            CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice"),
            CreateAcceptedTrusteeInvitation(election, "trustee-b", "Bob"),
            CreateAcceptedTrusteeInvitation(election, "trustee-c", "Carol"),
            CreateAcceptedTrusteeInvitation(election, "trustee-d", "Dan"),
            CreateAcceptedTrusteeInvitation(election, "trustee-e", "Erin"),
        };
        var profile = RegisterCeremonyProfile(store, "dkg-prod-3of5-threshold-distribution", trusteeCount: 5, requiredApprovalCount: 3);
        var version = RegisterCeremonyVersion(
            store,
            election,
            profile,
            acceptedTrustees,
            completedTrustees: ["trustee-a", "trustee-b", "trustee-c", "trustee-d", "trustee-e"],
            ready: false);
        var candidateStates = store.CeremonyTrusteeStates.Values
            .Select(state => state with
            {
                State = ElectionTrusteeCeremonyState.CeremonyCompleted,
                CompletedAt = DateTime.UtcNow,
                ShareVersion = $"share-{state.TrusteeUserAddress}",
            })
            .OrderBy(state => state.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var curve = new BabyJubJubCurve();

        var distribution = TrusteeThresholdShareDistribution.Create(
            election,
            version,
            candidateStates,
            curve);

        distribution.Assignments.Should().HaveCount(5);

        foreach (var subset in BuildAssignmentSubsets(distribution.Assignments, 3))
        {
            var reconstructedSecret = ReconstructSecretScalarFromAssignments(subset, curve);
            var reconstructedPublicKey = curve.ScalarMul(curve.Generator, reconstructedSecret).ToBytes();
            reconstructedPublicKey.Should().Equal(distribution.TallyPublicKey);
        }
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
    public async Task EvaluateOpenReadinessAsync_WithReadyThreeOfFiveVersionButOnlyThresholdCompleted_ReturnsNotReady()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var election = CreateTrusteeElection(requiredApprovalCount: 3);
        var acceptedInvitationA = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var acceptedInvitationB = CreateAcceptedTrusteeInvitation(election, "trustee-b", "Bob");
        var acceptedInvitationC = CreateAcceptedTrusteeInvitation(election, "trustee-c", "Carol");
        var acceptedInvitationD = CreateAcceptedTrusteeInvitation(election, "trustee-d", "Dan");
        var acceptedInvitationE = CreateAcceptedTrusteeInvitation(election, "trustee-e", "Erin");
        var profile = RegisterCeremonyProfile(store, "dkg-prod-3of5", trusteeCount: 5, requiredApprovalCount: 3);
        RegisterCeremonyVersion(
            store,
            election,
            profile,
            [acceptedInvitationA, acceptedInvitationB, acceptedInvitationC, acceptedInvitationD, acceptedInvitationE],
            completedTrustees: ["trustee-a", "trustee-b", "trustee-c"],
            ready: true);

        store.Elections[election.ElectionId] = election;
        store.TrusteeInvitations[acceptedInvitationA.Id] = acceptedInvitationA;
        store.TrusteeInvitations[acceptedInvitationB.Id] = acceptedInvitationB;
        store.TrusteeInvitations[acceptedInvitationC.Id] = acceptedInvitationC;
        store.TrusteeInvitations[acceptedInvitationD.Id] = acceptedInvitationD;
        store.TrusteeInvitations[acceptedInvitationE.Id] = acceptedInvitationE;
        AddRosterEntries(store, CreateRosterEntry(election, "4001"));

        var result = await service.EvaluateOpenReadinessAsync(new EvaluateElectionOpenReadinessRequest(
            election.ElectionId,
            RequiredWarningCodes: []));

        result.IsReadyToOpen.Should().BeFalse();
        result.ValidationErrors.Should().Contain(x =>
            x.Contains("every bound trustee package", StringComparison.OrdinalIgnoreCase));
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
    public async Task EvaluateOpenReadinessAsync_WithAdminOnlyProtectedProfileAndUnavailableCustody_ReturnsNotReady()
    {
        var store = new ElectionStore();
        var service = CreateService(
            store,
            adminOnlyProtectedTallyEnvelopeCrypto: new UnavailableAdminOnlyProtectedTallyEnvelopeCrypto());
        var election = CreateAdminElection();

        store.Elections[election.ElectionId] = election;
        AddRosterEntries(store, CreateRosterEntry(election, "4001"));

        var result = await service.EvaluateOpenReadinessAsync(new EvaluateElectionOpenReadinessRequest(
            election.ElectionId,
            RequiredWarningCodes: []));

        result.IsReadyToOpen.Should().BeFalse();
        result.ValidationErrors.Should().Contain(x =>
            x.Contains("OS-protected envelope provider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StartGovernedProposalAsync_WithValidOpenRequest_PersistsPendingProposal()
    {
        var store = new ElectionStore();
        var service = CreateService(store);
        var proposalId = Guid.NewGuid();
        var sourceTransactionId = Guid.NewGuid();
        var profile = RegisterCeremonyProfile(store, "dkg-prod-1of2", trusteeCount: 2, requiredApprovalCount: 1);
        var election = CreateTrusteeElection(
            requiredApprovalCount: 1,
            selectedProfileId: profile.ProfileId,
            selectedProfileDevOnly: false);
        var acceptedTrusteeA = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var acceptedTrusteeB = CreateAcceptedTrusteeInvitation(election, "trustee-b", "Bob");
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
        result.BoundaryArtifact.CeremonySnapshot.Should().NotBeNull();
        result.BoundaryArtifact.CeremonySnapshot!.ProfileId.Should().Be(election.SelectedProfileId);
        result.BoundaryArtifact.CeremonySnapshot.RequiredApprovalCount.Should().Be(1);
        result.BoundaryArtifact.CeremonySnapshot.ActiveTrustees.Should().ContainSingle();
        result.BoundaryArtifact.CeremonySnapshot.ActiveTrustees[0].TrusteeUserAddress.Should().Be("owner-address");
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
        store.AdminOnlyProtectedTallyEnvelopes.Should().ContainSingle();
        var protectedEnvelope = store.AdminOnlyProtectedTallyEnvelopes[election.ElectionId];
        protectedEnvelope.SelectedProfileId.Should().Be(election.SelectedProfileId);
        protectedEnvelope.TallyPublicKeyFingerprint.Should().Be(result.BoundaryArtifact.CeremonySnapshot.TallyPublicKeyFingerprint);
        protectedEnvelope.TallyPublicKey.Should().Equal(result.BoundaryArtifact.CeremonySnapshot.TallyPublicKey);
        protectedEnvelope.SealedTallyPrivateScalar.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CloseElectionAsync_WithAdminOnlyProtectedTallyBinding_CarriesItIntoCloseBoundary()
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
        var rosterEntry = CreateRosterEntry(election, "4001");

        store.Elections[election.ElectionId] = election;
        store.WarningAcknowledgements.Add(warning);
        AddRosterEntries(store, rosterEntry);

        var openResult = await service.OpenElectionAsync(new OpenElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            RequiredWarningCodes: [ElectionWarningCode.LowAnonymitySet],
            TrusteePolicyExecutionReference: "n/a",
            ReportingPolicyExecutionReference: "reporting-v1",
            ReviewWindowExecutionReference: "no-review"));

        openResult.IsSuccess.Should().BeTrue();
        openResult.BoundaryArtifact.Should().NotBeNull();

        var closeResult = await service.CloseElectionAsync(new CloseElectionRequest(
            election.ElectionId,
            "owner-address"));

        closeResult.IsSuccess.Should().BeTrue();
        closeResult.BoundaryArtifact.Should().NotBeNull();
        closeResult.BoundaryArtifact!.ArtifactType.Should().Be(ElectionBoundaryArtifactType.Close);
        closeResult.BoundaryArtifact.CeremonySnapshot.Should().NotBeNull();
        closeResult.BoundaryArtifact.CeremonySnapshot!.TallyPublicKeyFingerprint
            .Should()
            .Be(openResult.BoundaryArtifact!.CeremonySnapshot!.TallyPublicKeyFingerprint);
        closeResult.BoundaryArtifact.CeremonySnapshot.CeremonyVersionId
            .Should()
            .Be(openResult.BoundaryArtifact.CeremonySnapshot.CeremonyVersionId);
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
        var approvalTransactionId = Guid.NewGuid();
        var profile = RegisterCeremonyProfile(store, "dkg-prod-1of2-open", trusteeCount: 2, requiredApprovalCount: 1);
        var election = CreateTrusteeElection(
            requiredApprovalCount: 1,
            selectedProfileId: profile.ProfileId,
            selectedProfileDevOnly: false);
        var acceptedTrusteeA = CreateAcceptedTrusteeInvitation(election, "trustee-a", "Alice");
        var acceptedTrusteeB = CreateAcceptedTrusteeInvitation(election, "trustee-b", "Bob");
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
    public async Task ApproveGovernedProposalAsync_ForCloseAtThreshold_PersistsOnlyCloseBoundary()
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
        result.Election.TallyReadyAt.Should().BeNull();
        result.GovernedProposal.Should().NotBeNull();
        result.GovernedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionSucceeded);
        result.BoundaryArtifact.Should().NotBeNull();
        result.BoundaryArtifact!.ArtifactType.Should().Be(ElectionBoundaryArtifactType.Close);
        result.BoundaryArtifact.AcceptedBallotSetHash.Should().BeNull();
        result.BoundaryArtifact.FinalEncryptedTallyHash.Should().BeNull();
        result.BoundaryArtifact.SourceTransactionId.Should().Be(approvalTransactionId);
        result.BoundaryArtifact.SourceBlockHeight.Should().Be(48);
        store.Elections[openElection.ElectionId].CloseArtifactId.Should().Be(result.BoundaryArtifact.Id);
        store.Elections[openElection.ElectionId].TallyReadyAt.Should().BeNull();
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
    public async Task ApproveGovernedProposalAsync_ForFinalizeAtThreshold_CopiesOfficialResultAndFinalizesElection()
    {
        var store = new ElectionStore();
        var scenario = SeedClosedTrusteeElectionForFinalization(store, requiredApprovalCount: 1);
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash));
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
        result.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Finalized);
        result.GovernedProposal.Should().NotBeNull();
        result.GovernedProposal!.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionSucceeded);
        result.GovernedProposal.LatestTransactionId.Should().Be(approvalTransactionId);
        result.GovernedProposal.LatestBlockHeight.Should().Be(61);
        result.FinalizationSession.Should().BeNull();
        result.BoundaryArtifact.Should().NotBeNull();
        result.BoundaryArtifact!.ArtifactType.Should().Be(ElectionBoundaryArtifactType.Finalize);
        result.FinalizationReleaseEvidence.Should().BeNull();
        store.FinalizationSessions.Should().BeEmpty();
        store.FinalizationReleaseEvidenceRecords.Should().BeEmpty();
        store.Elections[scenario.Election.ElectionId].FinalizeArtifactId.Should().Be(result.BoundaryArtifact.Id);
        store.Elections[scenario.Election.ElectionId].OfficialResultArtifactId.Should().NotBeNull();
        store.ResultArtifacts.Should().HaveCount(2);
        store.ResultArtifacts.Count(x => x.ArtifactKind == ElectionResultArtifactKind.Official).Should().Be(1);
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WhenThresholdReached_MarksCloseCountingJobAndDefersExecution()
    {
        var store = new ElectionStore();
        var closeCountingExecutorKeyRegistry = new InMemoryCloseCountingExecutorKeyRegistry();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 1,
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash),
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var shareTransactionId = Guid.NewGuid();

        var request = CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally,
            sourceTransactionId: shareTransactionId,
            sourceBlockHeight: 71,
            sourceBlockId: Guid.NewGuid());

        var result = await service.SubmitFinalizationShareAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Election.Should().NotBeNull();
        result.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        result.BoundaryArtifact.Should().BeNull();
        result.FinalizationShare.Should().NotBeNull();
        result.FinalizationShare!.Status.Should().Be(ElectionFinalizationShareStatus.Accepted);
        result.FinalizationShare.SourceTransactionId.Should().Be(shareTransactionId);
        result.FinalizationSession.Should().NotBeNull();
        result.FinalizationSession!.Status.Should().Be(ElectionFinalizationSessionStatus.AwaitingShares);
        result.FinalizationReleaseEvidence.Should().BeNull();
        store.FinalizationShares.Should().ContainSingle();
        store.FinalizationShares[0].CloseCountingJobId.Should().Be(scenario.CloseCountingJob.Id);
        store.FinalizationShares[0].ExecutorKeyAlgorithm.Should().Be(ExecutorKeyAlgorithm);
        store.FinalizationShares[0].ShareMaterial.Should().NotBe("ciphertext-share-material");
        store.FinalizationReleaseEvidenceRecords.Should().BeEmpty();
        store.CloseCountingJobs[scenario.CloseCountingJob.Id].Status.Should().Be(ElectionCloseCountingJobStatus.ThresholdReached);
        store.CloseCountingJobs[scenario.CloseCountingJob.Id].ThresholdReachedAt.Should().NotBeNull();
        store.CloseCountingJobs[scenario.CloseCountingJob.Id].LatestTransactionId.Should().Be(shareTransactionId);
        store.CloseCountingJobs[scenario.CloseCountingJob.Id].LatestBlockHeight.Should().Be(71);
        store.Elections[scenario.Election.ElectionId].TallyReadyArtifactId.Should().BeNull();
        store.Elections[scenario.Election.ElectionId].UnofficialResultArtifactId.Should().BeNull();
        store.Elections[scenario.Election.ElectionId].FinalizeArtifactId.Should().BeNull();
        store.Elections[scenario.Election.ElectionId].ClosedProgressStatus.Should().Be(ElectionClosedProgressStatus.TallyCalculationInProgress);
        store.ResultArtifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WithSealedExecutorEnvelope_AcceptsShareAfterCredentialsRotate()
    {
        var store = new ElectionStore();
        var activeRegistry = new InMemoryCloseCountingExecutorKeyRegistry();
        var envelopeCrypto = new TransparentTestCloseCountingExecutorEnvelopeCrypto();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 1,
            closeCountingExecutorKeyRegistry: activeRegistry,
            closeCountingExecutorEnvelopeCrypto: envelopeCrypto);
        activeRegistry.Destroy(scenario.CloseCountingJob.Id);
        var rotatedCredentials = new FakeCredentialsProvider
        {
            Credentials = new CredentialsProfile
            {
                ProfileName = "rotated-lifecycle-node",
                PublicSigningAddress = "rotated-lifecycle-signer",
                PrivateSigningKey = "rotated-lifecycle-private-signing-key",
                PublicEncryptAddress = "rotated-lifecycle-public-encrypt-address",
                PrivateEncryptKey = "rotated-lifecycle-private-encrypt-key",
            },
        };
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash),
            closeCountingExecutorKeyRegistry: new InMemoryCloseCountingExecutorKeyRegistry(),
            credentialsProvider: rotatedCredentials,
            closeCountingExecutorEnvelopeCrypto: envelopeCrypto);

        var result = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally,
            sourceTransactionId: Guid.NewGuid(),
            sourceBlockHeight: 71,
            sourceBlockId: Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.FinalizationShare.Should().NotBeNull();
        result.FinalizationShare!.Status.Should().Be(ElectionFinalizationShareStatus.Accepted);
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WithLegacyNodeWrappedExecutorEnvelope_RemainsCompatible()
    {
        var store = new ElectionStore();
        var activeRegistry = new InMemoryCloseCountingExecutorKeyRegistry();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 1,
            closeCountingExecutorKeyRegistry: activeRegistry,
            closeCountingExecutorEnvelopeCrypto: new TransparentTestCloseCountingExecutorEnvelopeCrypto());
        activeRegistry.TryGet(scenario.CloseCountingJob.Id, out var executorKeyMaterial).Should().BeTrue();
        executorKeyMaterial.Should().NotBeNull();
        store.ExecutorSessionKeyEnvelopes[scenario.CloseCountingJob.Id] = scenario.ExecutorSessionKeyEnvelope with
        {
            SealedExecutorSessionPrivateKey = LegacyNodeIdentityCloseCountingExecutorEnvelopeCrypto.SealPrivateKey(
                executorKeyMaterial!.PrivateKey,
                new FakeCredentialsProvider()),
            SealAlgorithm = CloseCountingExecutorEnvelopeCryptoConstants.LegacyNodeEncryptAddressSealAlgorithm,
            SealedByServiceIdentity = TestNodeCredentials.PublicSigningAddress,
        };
        activeRegistry.Destroy(scenario.CloseCountingJob.Id);
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash),
            closeCountingExecutorKeyRegistry: new InMemoryCloseCountingExecutorKeyRegistry(),
            credentialsProvider: new FakeCredentialsProvider(),
            closeCountingExecutorEnvelopeCrypto: new UnavailableCloseCountingExecutorEnvelopeCrypto());

        var result = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally,
            sourceTransactionId: Guid.NewGuid(),
            sourceBlockHeight: 71,
            sourceBlockId: Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.FinalizationShare.Should().NotBeNull();
        result.FinalizationShare!.Status.Should().Be(ElectionFinalizationShareStatus.Accepted);
    }

    [Fact]
    public async Task ExecuteCloseCountingJobAsync_WhenThresholdReached_PublishesUnofficialResultAndStoresReleaseEvidence()
    {
        var store = new ElectionStore();
        var closeCountingExecutorKeyRegistry = new InMemoryCloseCountingExecutorKeyRegistry();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 1,
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash),
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var shareTransactionId = Guid.NewGuid();

        var submitResult = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally,
            sourceTransactionId: shareTransactionId,
            sourceBlockHeight: 71,
            sourceBlockId: Guid.NewGuid()));

        submitResult.IsSuccess.Should().BeTrue();

        var executeResult = await service.ExecuteCloseCountingJobAsync(new ExecuteElectionCloseCountingJobRequest(
            scenario.CloseCountingJob.Id,
            LeaseHolderId: "tally-executor:test-node"));

        executeResult.IsSuccess.Should().BeTrue();
        executeResult.Election.Should().NotBeNull();
        executeResult.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        executeResult.BoundaryArtifact.Should().NotBeNull();
        executeResult.BoundaryArtifact!.ArtifactType.Should().Be(ElectionBoundaryArtifactType.TallyReady);
        executeResult.FinalizationSession.Should().NotBeNull();
        executeResult.FinalizationSession!.Status.Should().Be(ElectionFinalizationSessionStatus.Completed);
        executeResult.FinalizationReleaseEvidence.Should().NotBeNull();
        executeResult.FinalizationReleaseEvidence!.AcceptedShareCount.Should().Be(1);
        store.FinalizationReleaseEvidenceRecords.Should().ContainSingle();
        store.FinalizationReleaseEvidenceRecords[0].FinalizationSessionId.Should().Be(scenario.Session.Id);
        store.Elections[scenario.Election.ElectionId].TallyReadyArtifactId.Should().Be(executeResult.BoundaryArtifact.Id);
        store.Elections[scenario.Election.ElectionId].UnofficialResultArtifactId.Should().NotBeNull();
        store.CloseCountingJobs[scenario.CloseCountingJob.Id].Status.Should().Be(ElectionCloseCountingJobStatus.Completed);
        store.TallyExecutorLeases[scenario.CloseCountingJob.Id].CompletionReason.Should().Be("close-counting-completed");
        store.ExecutorSessionKeyEnvelopes[scenario.CloseCountingJob.Id].SealedExecutorSessionPrivateKey
            .Should().Be(CloseCountingExecutorKeyRegistryConstants.DestroyedEnvelopeMarker);
        store.ExecutorSessionKeyEnvelopes[scenario.CloseCountingJob.Id].DestroyedAt.Should().NotBeNull();
        closeCountingExecutorKeyRegistry.TryGet(scenario.CloseCountingJob.Id, out _).Should().BeFalse();
        store.ResultArtifacts.Should().ContainSingle(x => x.ArtifactKind == ElectionResultArtifactKind.Unofficial);
        store.FinalizationShares.Should().ContainSingle();
        store.FinalizationShares[0].ShareMaterial.Should().Be(ElectionFinalizationShareStorageConstants.RedactedStoredShareMaterial);
        store.FinalizationShares[0].ShareMaterialHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteCloseCountingJobAsync_WhenThresholdReachedWithZeroBallots_PublishesZeroVoteUnofficialResult()
    {
        var store = new ElectionStore();
        var closeCountingExecutorKeyRegistry = new InMemoryCloseCountingExecutorKeyRegistry();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 1,
            finalEncryptedTallyHash: SHA256.HashData(Array.Empty<byte>()),
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);

        var emptyAcceptedBallotSetHash = ComputeAcceptedBallotInventoryHashForTests(
            Array.Empty<ElectionAcceptedBallotRecord>());
        var updatedCloseSnapshot = scenario.CloseSnapshot with
        {
            CountedParticipationCount = 0,
            BlankCount = 0,
            DidNotVoteCount = scenario.CloseSnapshot.ActiveDenominatorCount,
            CountedParticipationSetHash = Array.Empty<byte>(),
        };
        var updatedSession = scenario.Session with
        {
            AcceptedBallotSetHash = emptyAcceptedBallotSetHash,
            FinalEncryptedTallyHash = SHA256.HashData(Array.Empty<byte>()),
        };
        var updatedJob = scenario.CloseCountingJob with
        {
            FinalEncryptedTallyHash = SHA256.HashData(Array.Empty<byte>()),
        };

        store.AcceptedBallots.Clear();
        store.PublishedBallots.Clear();
        store.EligibilitySnapshots[0] = updatedCloseSnapshot;
        store.FinalizationSessions[updatedSession.Id] = updatedSession;
        store.CloseCountingJobs[updatedJob.Id] = updatedJob;
        scenario = scenario with
        {
            Session = updatedSession,
            CloseCountingJob = updatedJob,
            CloseSnapshot = updatedCloseSnapshot,
            AcceptedBallots = Array.Empty<ElectionAcceptedBallotRecord>(),
            PublishedBallots = Array.Empty<ElectionPublishedBallotRecord>(),
            AcceptedBallotSetHash = emptyAcceptedBallotSetHash,
            FinalEncryptedTallyHash = SHA256.HashData(Array.Empty<byte>()),
        };

        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([0, 0, 0], scenario.FinalEncryptedTallyHash),
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);

        var submitResult = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally,
            sourceTransactionId: Guid.NewGuid(),
            sourceBlockHeight: 71,
            sourceBlockId: Guid.NewGuid()));

        submitResult.IsSuccess.Should().BeTrue();

        var executeResult = await service.ExecuteCloseCountingJobAsync(new ExecuteElectionCloseCountingJobRequest(
            scenario.CloseCountingJob.Id,
            LeaseHolderId: "tally-executor:test-node"));

        executeResult.IsSuccess.Should().BeTrue();
        executeResult.BoundaryArtifact.Should().NotBeNull();
        executeResult.BoundaryArtifact!.ArtifactType.Should().Be(ElectionBoundaryArtifactType.TallyReady);
        executeResult.FinalizationSession.Should().NotBeNull();
        executeResult.FinalizationSession!.Status.Should().Be(ElectionFinalizationSessionStatus.Completed);
        store.CloseCountingJobs[scenario.CloseCountingJob.Id].Status.Should().Be(ElectionCloseCountingJobStatus.Completed);
        store.ResultArtifacts.Should().ContainSingle(x => x.ArtifactKind == ElectionResultArtifactKind.Unofficial);
        store.ResultArtifacts[0].TotalVotedCount.Should().Be(0);
        store.ResultArtifacts[0].BlankCount.Should().Be(0);
        store.ResultArtifacts[0].DidNotVoteCount.Should().Be(updatedCloseSnapshot.DidNotVoteCount);
        store.ResultArtifacts[0].NamedOptionResults.Select(x => x.VoteCount).Should().OnlyContain(x => x == 0);
    }

    [Fact]
    public async Task ExecuteCloseCountingJobAsync_AfterFailedThresholdSubset_RearmsAndUsesRecoverableSubset()
    {
        var store = new ElectionStore();
        var closeCountingExecutorKeyRegistry = new InMemoryCloseCountingExecutorKeyRegistry();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 1,
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var service = CreateService(
            store,
            electionResultCryptoService: new SelectiveElectionResultCryptoService(
                [2, 1, 0],
                scenario.FinalEncryptedTallyHash,
                ["trustee-a"]),
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);

        var wrongSubsetSubmitResult = await service.SubmitFinalizationShareAsync(
            CreateExecutorBoundFinalizationShareRequest(
                scenario,
                actorPublicAddress: "trustee-b",
                shareIndex: 2,
                shareVersion: "share-v2",
                shareMaterial: "ciphertext-share-material-b",
                targetType: ElectionFinalizationTargetType.AggregateTally,
                sourceTransactionId: Guid.NewGuid(),
                sourceBlockHeight: 71,
                sourceBlockId: Guid.NewGuid()));
        wrongSubsetSubmitResult.IsSuccess.Should().BeTrue();

        var failedExecuteResult = await service.ExecuteCloseCountingJobAsync(
            new ExecuteElectionCloseCountingJobRequest(
                scenario.CloseCountingJob.Id,
                LeaseHolderId: "tally-executor:test-node"));

        failedExecuteResult.IsSuccess.Should().BeFalse();
        failedExecuteResult.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        store.CloseCountingJobs[scenario.CloseCountingJob.Id].Status.Should().Be(ElectionCloseCountingJobStatus.Failed);
        store.FinalizationSessions[scenario.Session.Id].Status.Should().Be(ElectionFinalizationSessionStatus.AwaitingShares);

        var recoverySubmitResult = await service.SubmitFinalizationShareAsync(
            CreateExecutorBoundFinalizationShareRequest(
                scenario,
                actorPublicAddress: "trustee-a",
                shareIndex: 1,
                shareVersion: "share-v1",
                shareMaterial: "ciphertext-share-material-a",
                targetType: ElectionFinalizationTargetType.AggregateTally,
                sourceTransactionId: Guid.NewGuid(),
                sourceBlockHeight: 72,
                sourceBlockId: Guid.NewGuid()));

        recoverySubmitResult.IsSuccess.Should().BeTrue();
        store.CloseCountingJobs[scenario.CloseCountingJob.Id].Status.Should().Be(ElectionCloseCountingJobStatus.ThresholdReached);

        var recoveredExecuteResult = await service.ExecuteCloseCountingJobAsync(
            new ExecuteElectionCloseCountingJobRequest(
                scenario.CloseCountingJob.Id,
                LeaseHolderId: "tally-executor:test-node"));

        recoveredExecuteResult.IsSuccess.Should().BeTrue();
        recoveredExecuteResult.FinalizationReleaseEvidence.Should().NotBeNull();
        recoveredExecuteResult.FinalizationReleaseEvidence!.AcceptedShareCount.Should().Be(1);
        recoveredExecuteResult.FinalizationReleaseEvidence.AcceptedTrustees
            .Select(x => x.TrusteeUserAddress)
            .Should()
            .Equal("trustee-a");
        store.FinalizationShares.Count(x => x.Status == ElectionFinalizationShareStatus.Accepted).Should().Be(2);
        store.CloseCountingJobs[scenario.CloseCountingJob.Id].Status.Should().Be(ElectionCloseCountingJobStatus.Completed);
        store.Elections[scenario.Election.ElectionId].TallyReadyArtifactId.Should().NotBeNull();
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WhenExecutorRegistryIsLost_RecoversFromSealedEnvelope()
    {
        var store = new ElectionStore();
        var seededRegistry = new InMemoryCloseCountingExecutorKeyRegistry();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 1,
            closeCountingExecutorKeyRegistry: seededRegistry);
        seededRegistry.Destroy(scenario.CloseCountingJob.Id);

        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash),
            closeCountingExecutorKeyRegistry: new InMemoryCloseCountingExecutorKeyRegistry());

        var result = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally));

        result.IsSuccess.Should().BeTrue();
        result.FinalizationShare.Should().NotBeNull();
        result.FinalizationShare!.Status.Should().Be(ElectionFinalizationShareStatus.Accepted);
    }

    [Fact]
    public async Task ExecuteCloseCountingJobAsync_WhenThresholdReachedWithDevModePublishedBallots_UsesDevModeFallback()
    {
        var store = new ElectionStore();
        var closeCountingExecutorKeyRegistry = new InMemoryCloseCountingExecutorKeyRegistry();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 1,
            bindingStatus: ElectionBindingStatus.NonBinding,
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var expectedTallyHash = ComputeDevModePublishedTallyHashForTests(
            scenario.Election,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["yes"] = 2,
                ["no"] = 1,
            });
        var updatedSession = scenario.Session with
        {
            FinalEncryptedTallyHash = expectedTallyHash,
        };
        store.FinalizationSessions[updatedSession.Id] = updatedSession;
        scenario = scenario with
        {
            Session = updatedSession,
            FinalEncryptedTallyHash = expectedTallyHash,
        };

        store.PublishedBallots.Clear();
        store.PublishedBallots.Add(ElectionModelFactory.CreatePublishedBallotRecord(
            scenario.Election.ElectionId,
            publicationSequence: 1,
            encryptedBallotPackage: CreateDevModePublishedBallotPackageForTests(
                scenario.Election,
                "yes",
                ballotOrder: 1,
                actorPublicAddress: "voter-a"),
            proofBundle: "proof-1",
            publishedAt: DateTime.UtcNow.AddMinutes(-6)));
        store.PublishedBallots.Add(ElectionModelFactory.CreatePublishedBallotRecord(
            scenario.Election.ElectionId,
            publicationSequence: 2,
            encryptedBallotPackage: CreateDevModePublishedBallotPackageForTests(
                scenario.Election,
                "no",
                ballotOrder: 2,
                actorPublicAddress: "voter-b"),
            proofBundle: "proof-2",
            publishedAt: DateTime.UtcNow.AddMinutes(-6)));
        store.PublishedBallots.Add(ElectionModelFactory.CreatePublishedBallotRecord(
            scenario.Election.ElectionId,
            publicationSequence: 3,
            encryptedBallotPackage: CreateDevModePublishedBallotPackageForTests(
                scenario.Election,
                "yes",
                ballotOrder: 1,
                actorPublicAddress: "voter-c"),
            proofBundle: "proof-3",
            publishedAt: DateTime.UtcNow.AddMinutes(-6)));

        var service = CreateService(
            store,
            electionResultCryptoService: new DevModeFallbackElectionResultCryptoService(),
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var submitResult = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally));

        submitResult.IsSuccess.Should().BeTrue();

        var result = await service.ExecuteCloseCountingJobAsync(new ExecuteElectionCloseCountingJobRequest(
            scenario.CloseCountingJob.Id,
            LeaseHolderId: "tally-executor:test-node"));

        result.IsSuccess.Should().BeTrue();
        result.BoundaryArtifact.Should().NotBeNull();
        result.BoundaryArtifact!.ArtifactType.Should().Be(ElectionBoundaryArtifactType.TallyReady);
        result.BoundaryArtifact.FinalEncryptedTallyHash.Should().Equal(expectedTallyHash);
        result.FinalizationSession.Should().NotBeNull();
        result.FinalizationSession!.Status.Should().Be(ElectionFinalizationSessionStatus.Completed);
        result.FinalizationReleaseEvidence.Should().NotBeNull();
        store.ExecutorSessionKeyEnvelopes[scenario.CloseCountingJob.Id].DestroyedAt.Should().NotBeNull();
        closeCountingExecutorKeyRegistry.TryGet(scenario.CloseCountingJob.Id, out _).Should().BeFalse();
        store.ResultArtifacts.Should().ContainSingle(x => x.ArtifactKind == ElectionResultArtifactKind.Unofficial);
        store.ResultArtifacts[0].NamedOptionResults.Select(x => (x.OptionId, x.VoteCount))
            .Should().Equal(("yes", 2), ("no", 1));
        store.ResultArtifacts[0].BlankCount.Should().Be(0);
        store.ResultArtifacts[0].TotalVotedCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteCloseCountingJobAsync_WhenBindingElectionHasDevModePublishedBallots_FailsWithoutDevModeFallback()
    {
        var store = new ElectionStore();
        var closeCountingExecutorKeyRegistry = new InMemoryCloseCountingExecutorKeyRegistry();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 1,
            bindingStatus: ElectionBindingStatus.Binding,
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);

        store.PublishedBallots.Clear();
        store.PublishedBallots.Add(ElectionModelFactory.CreatePublishedBallotRecord(
            scenario.Election.ElectionId,
            publicationSequence: 1,
            encryptedBallotPackage: CreateDevModePublishedBallotPackageForTests(
                scenario.Election,
                "yes",
                ballotOrder: 1,
                actorPublicAddress: "voter-a"),
            proofBundle: "proof-1",
            publishedAt: DateTime.UtcNow.AddMinutes(-6)));
        store.PublishedBallots.Add(ElectionModelFactory.CreatePublishedBallotRecord(
            scenario.Election.ElectionId,
            publicationSequence: 2,
            encryptedBallotPackage: CreateDevModePublishedBallotPackageForTests(
                scenario.Election,
                "no",
                ballotOrder: 2,
                actorPublicAddress: "voter-b"),
            proofBundle: "proof-2",
            publishedAt: DateTime.UtcNow.AddMinutes(-6)));
        store.PublishedBallots.Add(ElectionModelFactory.CreatePublishedBallotRecord(
            scenario.Election.ElectionId,
            publicationSequence: 3,
            encryptedBallotPackage: CreateDevModePublishedBallotPackageForTests(
                scenario.Election,
                "yes",
                ballotOrder: 1,
                actorPublicAddress: "voter-c"),
            proofBundle: "proof-3",
            publishedAt: DateTime.UtcNow.AddMinutes(-6)));

        var service = CreateService(
            store,
            electionResultCryptoService: new DevModeFallbackElectionResultCryptoService(),
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var submitResult = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally));

        submitResult.IsSuccess.Should().BeTrue();

        var result = await service.ExecuteCloseCountingJobAsync(new ExecuteElectionCloseCountingJobRequest(
            scenario.CloseCountingJob.Id,
            LeaseHolderId: "tally-executor:test-node"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        result.ErrorMessage.Should().Contain("Close-counting aggregate release failed");
        store.CloseCountingJobs[scenario.CloseCountingJob.Id].Status.Should().Be(ElectionCloseCountingJobStatus.Failed);
        store.CloseCountingJobs[scenario.CloseCountingJob.Id].FailureReason.Should().Contain("Close-counting aggregate release failed");
        store.FinalizationSessions[scenario.Session.Id].Status.Should().Be(ElectionFinalizationSessionStatus.AwaitingShares);
        store.BoundaryArtifacts.Should().NotContain(x => x.ArtifactType == ElectionBoundaryArtifactType.TallyReady);
        store.ResultArtifacts.Should().BeEmpty();
        store.ExecutorSessionKeyEnvelopes[scenario.CloseCountingJob.Id].DestroyedAt.Should().BeNull();
        closeCountingExecutorKeyRegistry.TryGet(scenario.CloseCountingJob.Id, out _).Should().BeTrue();
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WithFinalizeSessionPurpose_ReturnsInvalidStateAndDoesNotPersistShares()
    {
        var store = new ElectionStore();
        var scenario = SeedClosedTrusteeElectionForFinalization(store, requiredApprovalCount: 1);
        var service = CreateService(store);
        var session = ElectionModelFactory.CreateFinalizationSession(
            scenario.Election,
            scenario.CloseArtifact.Id,
            scenario.AcceptedBallotSetHash,
            scenario.FinalEncryptedTallyHash,
            ElectionFinalizationSessionPurpose.Finalization,
            scenario.OpenArtifact.CeremonySnapshot!,
            requiredShareCount: 1,
            eligibleTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
            ],
            createdByPublicAddress: "owner-address");
        store.FinalizationSessions[session.Id] = session;

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
            ShareMaterial: "ciphertext-share-material"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.InvalidState);
        result.ErrorMessage.Should().Contain("close-counting");
        store.FinalizationShares.Should().BeEmpty();
        store.FinalizationReleaseEvidenceRecords.Should().BeEmpty();
        store.Elections[scenario.Election.ElectionId].LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        store.FinalizationSessions[session.Id].Status.Should().Be(ElectionFinalizationSessionStatus.AwaitingShares);
    }

    [Fact]
    public async Task SubmitFinalizationShareAsync_WithSingleBallotTarget_PersistsRejectedShareAndKeepsSessionWaiting()
    {
        var store = new ElectionStore();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(store, requiredApprovalCount: 1);
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash));
        var session = scenario.Session;

        var result = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.SingleBallot));

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
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(store, requiredApprovalCount: 1);
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash));
        var session = scenario.Session;

        var result = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally,
            claimedFinalEncryptedTallyHash: [99, 98, 97]));

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
        var closeCountingExecutorKeyRegistry = new InMemoryCloseCountingExecutorKeyRegistry();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 2,
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash),
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var session = scenario.Session;

        var firstResult = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally));

        firstResult.IsSuccess.Should().BeTrue();
        firstResult.FinalizationShare.Should().NotBeNull();
        firstResult.FinalizationShare!.Status.Should().Be(ElectionFinalizationShareStatus.Accepted);
        store.Elections[scenario.Election.ElectionId].LifecycleState.Should().Be(ElectionLifecycleState.Closed);

        var duplicateResult = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally));

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
        var closeCountingExecutorKeyRegistry = new InMemoryCloseCountingExecutorKeyRegistry();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 1,
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash),
            closeCountingExecutorKeyRegistry: closeCountingExecutorKeyRegistry);
        var session = scenario.Session;

        var result = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: string.Empty,
            targetType: ElectionFinalizationTargetType.AggregateTally));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        result.ErrorMessage.Should().Contain("did not contain any share material");
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
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(store, requiredApprovalCount: 1);
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash));
        var session = scenario.Session;

        var result = await service.SubmitFinalizationShareAsync(CreateExecutorBoundFinalizationShareRequest(
            scenario,
            actorPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            shareMaterial: "ciphertext-share-material",
            targetType: ElectionFinalizationTargetType.AggregateTally,
            claimedCeremonyVersionId: Guid.NewGuid()));

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

    [Theory]
    [InlineData(ElectionBindingStatus.Binding)]
    [InlineData(ElectionBindingStatus.NonBinding)]
    public async Task SubmitFinalizationShareAsync_WithPlaintextShareMaterial_RejectsAndSanitizesStoredRecord(
        ElectionBindingStatus bindingStatus)
    {
        var store = new ElectionStore();
        var scenario = SeedClosedTrusteeElectionForCloseCountingSession(
            store,
            requiredApprovalCount: 1,
            bindingStatus: bindingStatus);
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1, 0], scenario.FinalEncryptedTallyHash));
        var session = scenario.Session;

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
            ShareMaterial: "ciphertext-share-material"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        result.ErrorMessage.Should().Contain("Plaintext trustee share material");
        store.FinalizationShares.Should().ContainSingle();
        store.FinalizationShares[0].Status.Should().Be(ElectionFinalizationShareStatus.Rejected);
        store.FinalizationShares[0].FailureCode.Should().Be("PLAINTEXT_SHARE_FORBIDDEN");
        store.FinalizationShares[0].ShareMaterial.Should().Be(
            ElectionFinalizationShareStorageConstants.RejectedPlaintextStoredShareMaterial);
        store.FinalizationShares[0].ShareMaterial.Should().NotBe("ciphertext-share-material");
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
        var sensitiveStorageMaintenance = new FakeElectionSensitiveStorageMaintenance();
        var service = CreateService(
            store,
            electionResultCryptoService: new FakeElectionResultCryptoService([2, 1], finalEncryptedTallyHash: new byte[] { 9, 10 }),
            sensitiveStorageMaintenance: sensitiveStorageMaintenance);
        var election = CreateOpenElection() with
        {
            OfficialResultVisibilityPolicy = OfficialResultVisibilityPolicy.PublicPlaintext,
        };
        var closeTransactionId = Guid.NewGuid();
        var tallyReadyTransactionId = Guid.NewGuid();
        var finalizeTransactionId = Guid.NewGuid();
        var acceptedBallotHash = new byte[] { 7, 8 };
        var publishedBallotHash = new byte[] { 8, 9 };
        var finalTallyHash = new byte[] { 9, 10 };

        store.Elections[election.ElectionId] = election;

        var closeResult = await service.CloseElectionAsync(new CloseElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            SourceTransactionId: closeTransactionId,
            SourceBlockHeight: 52,
            SourceBlockId: Guid.NewGuid()));

        var tallyReadyAt = DateTime.UtcNow;
        var tallyReadyElection = store.Elections[election.ElectionId] with
        {
            TallyReadyAt = tallyReadyAt,
            LastUpdatedAt = tallyReadyAt,
        };
        var tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.TallyReady,
            tallyReadyElection,
            recordedByPublicAddress: "owner-address",
            recordedAt: tallyReadyAt,
            acceptedBallotCount: 2,
            acceptedBallotSetHash: acceptedBallotHash,
            publishedBallotCount: 2,
            publishedBallotStreamHash: publishedBallotHash,
            finalEncryptedTallyHash: finalTallyHash,
            sourceTransactionId: tallyReadyTransactionId,
            sourceBlockHeight: 52,
            sourceBlockId: Guid.NewGuid());
        tallyReadyElection = tallyReadyElection with
        {
            TallyReadyArtifactId = tallyReadyArtifact.Id,
        };
        var denominatorEvidence = new ElectionResultDenominatorEvidence(
            closeResult.EligibilitySnapshot!.SnapshotType,
            closeResult.EligibilitySnapshot.Id,
            closeResult.EligibilitySnapshot.BoundaryArtifactId,
            closeResult.EligibilitySnapshot.ActiveDenominatorSetHash);
        var unofficialResult = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.PublicPlaintext,
            election.Title,
            [
                new ElectionResultOptionCount("alice", "Alice", null, 1, 1, 2),
                new ElectionResultOptionCount("bob", "Bob", null, 2, 2, 1),
            ],
            blankCount: 0,
            totalVotedCount: 3,
            eligibleToVoteCount: closeResult.EligibilitySnapshot.ActiveDenominatorCount,
            didNotVoteCount: closeResult.EligibilitySnapshot.DidNotVoteCount,
            denominatorEvidence,
            "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            publicPayload: "{\"kind\":\"unofficial\"}",
            recordedAt: tallyReadyAt,
            sourceTransactionId: tallyReadyTransactionId,
            sourceBlockHeight: 52,
            sourceBlockId: Guid.NewGuid());
        tallyReadyElection = tallyReadyElection with
        {
            UnofficialResultArtifactId = unofficialResult.Id,
        };
        store.Elections[election.ElectionId] = tallyReadyElection;
        store.BoundaryArtifacts.Add(tallyReadyArtifact);
        store.ResultArtifacts.Add(unofficialResult);
        var adminOnlyEnvelopeCrypto = new TransparentTestAdminOnlyProtectedTallyEnvelopeCrypto();
        store.AdminOnlyProtectedTallyEnvelopes[election.ElectionId] =
            ElectionModelFactory.CreateAdminOnlyProtectedTallyEnvelope(
                election.ElectionId,
                election.SelectedProfileId,
                tallyPublicKey: [0x01, 0x02, 0x03],
                tallyPublicKeyFingerprint: "test-fingerprint",
                sealedTallyPrivateScalar: adminOnlyEnvelopeCrypto.SealPrivateScalar(
                    "12345",
                    election.ElectionId,
                    election.SelectedProfileId),
                AdminOnlyProtectedTallyEnvelopeCryptoConstants.ScalarEncoding,
                adminOnlyEnvelopeCrypto.SealAlgorithm,
                adminOnlyEnvelopeCrypto.SealedByServiceIdentity);

        var finalizeResult = await service.FinalizeElectionAsync(new FinalizeElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            AcceptedBallotSetHash: acceptedBallotHash,
            FinalEncryptedTallyHash: finalTallyHash,
            SourceTransactionId: finalizeTransactionId,
            SourceBlockHeight: 53,
            SourceBlockId: Guid.NewGuid()));

        closeResult.IsSuccess.Should().BeTrue();
        finalizeResult.IsSuccess.Should().BeTrue();
        closeResult.Election!.VoteAcceptanceLockedAt.Should().NotBeNull();
        finalizeResult.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Finalized);
        store.BoundaryArtifacts.Select(x => x.ArtifactType).Should().Equal(
            ElectionBoundaryArtifactType.Close,
            ElectionBoundaryArtifactType.TallyReady,
            ElectionBoundaryArtifactType.Finalize);
        store.BoundaryArtifacts[0].AcceptedBallotSetHash.Should().BeNull();
        store.BoundaryArtifacts[0].SourceTransactionId.Should().Be(closeTransactionId);
        store.BoundaryArtifacts[0].SourceBlockHeight.Should().Be(52);
        store.BoundaryArtifacts[1].AcceptedBallotSetHash.Should().Equal(acceptedBallotHash);
        store.BoundaryArtifacts[1].PublishedBallotStreamHash.Should().Equal(publishedBallotHash);
        store.BoundaryArtifacts[1].FinalEncryptedTallyHash.Should().Equal(finalTallyHash);
        store.BoundaryArtifacts[1].SourceTransactionId.Should().Be(tallyReadyTransactionId);
        store.BoundaryArtifacts[1].SourceBlockHeight.Should().Be(52);
        store.BoundaryArtifacts[2].FinalEncryptedTallyHash.Should().Equal(finalTallyHash);
        store.BoundaryArtifacts[2].SourceTransactionId.Should().Be(finalizeTransactionId);
        store.BoundaryArtifacts[2].SourceBlockHeight.Should().Be(53);
        store.Elections[election.ElectionId].VoteAcceptanceLockedAt.Should().NotBeNull();
        store.Elections[election.ElectionId].CloseArtifactId.Should().Be(closeResult.BoundaryArtifact!.Id);
        store.Elections[election.ElectionId].TallyReadyArtifactId.Should().Be(tallyReadyArtifact.Id);
        store.Elections[election.ElectionId].FinalizeArtifactId.Should().Be(finalizeResult.BoundaryArtifact!.Id);
        store.Elections[election.ElectionId].OfficialResultArtifactId.Should().NotBeNull();
        store.ReportPackages.Should().ContainSingle();
        store.ReportArtifacts.Should().HaveCount(13);
        store.AdminOnlyProtectedTallyEnvelopes[election.ElectionId].SealedTallyPrivateScalar
            .Should().Be(AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker);
        store.AdminOnlyProtectedTallyEnvelopes[election.ElectionId].DestroyedAt.Should().NotBeNull();
        sensitiveStorageMaintenance.AdminOnlyProtectedTallyEnvelopeCompactionCount.Should().Be(1);
        store.ReportArtifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanAuditProvenanceReport)
            .Content.Should().Contain("LowAnonymitySet");
        store.ReportArtifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanAuditProvenanceReport)
            .Content.Should().Contain("Official result hash");
        store.ReportArtifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineAuditProvenanceReportProjection)
            .Content.Should().Contain("\"warningEvidence\"");
    }

    [Fact]
    public async Task FinalizeElectionAsync_WhenReportPackageGenerationFails_PersistsFailedAttemptAndKeepsElectionClosed()
    {
        var store = new ElectionStore();
        var setup = await SeedClosedAdminElectionReadyForFinalizeAsync(store);
        var reportPackageService = new FakeElectionReportPackageService(failBuildAttempts: 1);
        var service = CreateService(
            store,
            electionReportPackageService: reportPackageService);

        var result = await service.FinalizeElectionAsync(new FinalizeElectionRequest(
            ElectionId: setup.Election.ElectionId,
            ActorPublicAddress: "owner-address",
            AcceptedBallotSetHash: setup.AcceptedBallotSetHash,
            FinalEncryptedTallyHash: setup.FinalEncryptedTallyHash,
            SourceTransactionId: Guid.NewGuid(),
            SourceBlockHeight: 70,
            SourceBlockId: Guid.NewGuid()));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.ValidationFailed);
        store.Elections[setup.Election.ElectionId].LifecycleState.Should().Be(ElectionLifecycleState.Closed);
        store.Elections[setup.Election.ElectionId].FinalizeArtifactId.Should().BeNull();
        store.Elections[setup.Election.ElectionId].OfficialResultArtifactId.Should().BeNull();
        store.ResultArtifacts.Should().ContainSingle(x => x.ArtifactKind == ElectionResultArtifactKind.Unofficial);
        store.BoundaryArtifacts.Should().NotContain(x => x.ArtifactType == ElectionBoundaryArtifactType.Finalize);
        store.ReportPackages.Should().ContainSingle();
        store.ReportPackages.Values.Single().Status.Should().Be(ElectionReportPackageStatus.GenerationFailed);
        store.ReportPackages.Values.Single().AttemptNumber.Should().Be(1);
        store.ReportArtifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeElectionAsync_AfterFailedPackageAttempt_RetriesWithNewAttemptIdOnSameFrozenEvidence()
    {
        var store = new ElectionStore();
        var setup = await SeedClosedAdminElectionReadyForFinalizeAsync(store);
        var reportPackageService = new FakeElectionReportPackageService(failBuildAttempts: 1);
        var service = CreateService(
            store,
            electionReportPackageService: reportPackageService);

        var firstResult = await service.FinalizeElectionAsync(new FinalizeElectionRequest(
            ElectionId: setup.Election.ElectionId,
            ActorPublicAddress: "owner-address",
            AcceptedBallotSetHash: setup.AcceptedBallotSetHash,
            FinalEncryptedTallyHash: setup.FinalEncryptedTallyHash,
            SourceTransactionId: Guid.NewGuid(),
            SourceBlockHeight: 71,
            SourceBlockId: Guid.NewGuid()));
        var retryResult = await service.FinalizeElectionAsync(new FinalizeElectionRequest(
            ElectionId: setup.Election.ElectionId,
            ActorPublicAddress: "owner-address",
            AcceptedBallotSetHash: setup.AcceptedBallotSetHash,
            FinalEncryptedTallyHash: setup.FinalEncryptedTallyHash,
            SourceTransactionId: Guid.NewGuid(),
            SourceBlockHeight: 72,
            SourceBlockId: Guid.NewGuid()));

        firstResult.IsSuccess.Should().BeFalse();
        retryResult.IsSuccess.Should().BeTrue();
        store.Elections[setup.Election.ElectionId].LifecycleState.Should().Be(ElectionLifecycleState.Finalized);
        store.ReportPackages.Values.Should().HaveCount(2);

        var failedAttempt = store.ReportPackages.Values.Single(x => x.Status == ElectionReportPackageStatus.GenerationFailed);
        var sealedAttempt = store.ReportPackages.Values.Single(x => x.Status == ElectionReportPackageStatus.Sealed);
        sealedAttempt.AttemptNumber.Should().Be(2);
        sealedAttempt.PreviousAttemptId.Should().Be(failedAttempt.Id);
        sealedAttempt.FrozenEvidenceHash.Should().Equal(failedAttempt.FrozenEvidenceHash);
        store.ReportArtifacts.Should().HaveCount(13);
        store.BoundaryArtifacts.Should().Contain(x => x.ArtifactType == ElectionBoundaryArtifactType.Finalize);
        store.ResultArtifacts.Count(x => x.ArtifactKind == ElectionResultArtifactKind.Official).Should().Be(1);
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

    private static IReadOnlyList<IReadOnlyList<TrusteeThresholdShareAssignment>> BuildAssignmentSubsets(
        IReadOnlyList<TrusteeThresholdShareAssignment> assignments,
        int subsetSize)
    {
        var subsets = new List<IReadOnlyList<TrusteeThresholdShareAssignment>>();
        var current = new TrusteeThresholdShareAssignment[subsetSize];

        void Build(int startIndex, int depth)
        {
            if (depth == subsetSize)
            {
                subsets.Add(current.ToArray());
                return;
            }

            var remainingSlots = subsetSize - depth;
            for (var index = startIndex; index <= assignments.Count - remainingSlots; index++)
            {
                current[depth] = assignments[index];
                Build(index + 1, depth + 1);
            }
        }

        Build(0, 0);
        return subsets;
    }

    private static BigInteger ReconstructSecretScalarFromAssignments(
        IReadOnlyList<TrusteeThresholdShareAssignment> shares,
        IBabyJubJub curve)
    {
        var secret = BigInteger.Zero;

        foreach (var share in shares)
        {
            var xCoordinate = new BigInteger(share.ShareIndex);
            var yCoordinate = BigInteger.Parse(share.ShareMaterial);
            var numerator = BigInteger.One;
            var denominator = BigInteger.One;

            foreach (var otherShare in shares)
            {
                if (ReferenceEquals(share, otherShare))
                {
                    continue;
                }

                var otherX = new BigInteger(otherShare.ShareIndex);
                numerator = ModScalar(numerator * (-otherX), curve.Order);
                denominator = ModScalar(denominator * (xCoordinate - otherX), curve.Order);
            }

            var lagrangeCoefficient = ModScalar(
                numerator * ModInverseScalar(denominator, curve.Order),
                curve.Order);
            secret = ModScalar(secret + (yCoordinate * lagrangeCoefficient), curve.Order);
        }

        return secret;
    }

    private static BigInteger ModScalar(BigInteger value, BigInteger modulus)
    {
        var normalized = value % modulus;
        return normalized < 0 ? normalized + modulus : normalized;
    }

    private static BigInteger ModInverseScalar(BigInteger value, BigInteger modulus)
    {
        var normalizedValue = ModScalar(value, modulus);
        if (normalizedValue == BigInteger.Zero)
        {
            throw new InvalidOperationException("Cannot invert zero while reconstructing threshold shares.");
        }

        var t = BigInteger.Zero;
        var newT = BigInteger.One;
        var r = modulus;
        var newR = normalizedValue;

        while (newR != BigInteger.Zero)
        {
            var quotient = r / newR;
            (t, newT) = (newT, t - (quotient * newT));
            (r, newR) = (newR, r - (quotient * newR));
        }

        if (r > BigInteger.One)
        {
            throw new InvalidOperationException("Threshold share denominator is not invertible.");
        }

        return t < 0 ? t + modulus : t;
    }

    private static ElectionLifecycleService CreateService(
        ElectionStore store,
        ElectionCeremonyOptions? ceremonyOptions = null,
        IElectionCastIdempotencyCacheService? castIdempotencyCacheService = null,
        IElectionResultCryptoService? electionResultCryptoService = null,
        IElectionReportPackageService? electionReportPackageService = null,
        ICloseCountingExecutorKeyRegistry? closeCountingExecutorKeyRegistry = null,
        ICredentialsProvider? credentialsProvider = null,
        IIdentityService? identityService = null,
        ICloseCountingExecutorEnvelopeCrypto? closeCountingExecutorEnvelopeCrypto = null,
        IAdminOnlyProtectedTallyEnvelopeCrypto? adminOnlyProtectedTallyEnvelopeCrypto = null,
        IElectionSensitiveStorageMaintenance? sensitiveStorageMaintenance = null)
    {
        SeedStandardCeremonyProfiles(store);

        return new ElectionLifecycleService(
            new FakeUnitOfWorkProvider(store),
            NullLogger<ElectionLifecycleService>.Instance,
            ceremonyOptions ?? new ElectionCeremonyOptions(),
            castIdempotencyCacheService,
            electionResultCryptoService,
            electionReportPackageService,
            credentialsProvider ?? new FakeCredentialsProvider(),
            identityService ?? new FakeIdentityService(),
            closeCountingExecutorKeyRegistry,
            closeCountingExecutorEnvelopeCrypto ?? new TransparentTestCloseCountingExecutorEnvelopeCrypto(),
            adminOnlyProtectedTallyEnvelopeCrypto ?? new TransparentTestAdminOnlyProtectedTallyEnvelopeCrypto(),
            sensitiveStorageMaintenance: sensitiveStorageMaintenance);
    }

    private sealed class FakeElectionSensitiveStorageMaintenance : IElectionSensitiveStorageMaintenance
    {
        public int AdminOnlyProtectedTallyEnvelopeCompactionCount { get; private set; }

        public Task CompactAdminOnlyProtectedTallyEnvelopeStorageAsync(CancellationToken cancellationToken = default)
        {
            AdminOnlyProtectedTallyEnvelopeCompactionCount++;
            return Task.CompletedTask;
        }
    }

    private static async Task<AdminFinalizeReadyContext> SeedClosedAdminElectionReadyForFinalizeAsync(ElectionStore store)
    {
        var service = CreateService(store);
        var election = CreateOpenElection() with
        {
            OfficialResultVisibilityPolicy = OfficialResultVisibilityPolicy.PublicPlaintext,
        };
        var closeTransactionId = Guid.NewGuid();
        var tallyReadyTransactionId = Guid.NewGuid();
        var acceptedBallotHash = new byte[] { 7, 8 };
        var publishedBallotHash = new byte[] { 8, 9 };
        var finalEncryptedTallyHash = new byte[] { 9, 10 };

        store.Elections[election.ElectionId] = election;

        var closeResult = await service.CloseElectionAsync(new CloseElectionRequest(
            ElectionId: election.ElectionId,
            ActorPublicAddress: "owner-address",
            SourceTransactionId: closeTransactionId,
            SourceBlockHeight: 52,
            SourceBlockId: Guid.NewGuid()));
        var closedElection = store.Elections[election.ElectionId];
        var tallyReadyAt = DateTime.UtcNow;
        var tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.TallyReady,
            closedElection with
            {
                TallyReadyAt = tallyReadyAt,
                LastUpdatedAt = tallyReadyAt,
            },
            recordedByPublicAddress: "owner-address",
            recordedAt: tallyReadyAt,
            acceptedBallotCount: 3,
            acceptedBallotSetHash: acceptedBallotHash,
            publishedBallotCount: 3,
            publishedBallotStreamHash: publishedBallotHash,
            finalEncryptedTallyHash: finalEncryptedTallyHash,
            sourceTransactionId: tallyReadyTransactionId,
            sourceBlockHeight: 53,
            sourceBlockId: Guid.NewGuid());
        var denominatorEvidence = new ElectionResultDenominatorEvidence(
            closeResult.EligibilitySnapshot!.SnapshotType,
            closeResult.EligibilitySnapshot.Id,
            closeResult.EligibilitySnapshot.BoundaryArtifactId,
            closeResult.EligibilitySnapshot.ActiveDenominatorSetHash);
        var unofficialResult = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.PublicPlaintext,
            election.Title,
            [
                new ElectionResultOptionCount("alice", "Alice", null, 1, 1, 2),
                new ElectionResultOptionCount("bob", "Bob", null, 2, 2, 1),
            ],
            blankCount: 0,
            totalVotedCount: 3,
            eligibleToVoteCount: closeResult.EligibilitySnapshot.ActiveDenominatorCount,
            didNotVoteCount: closeResult.EligibilitySnapshot.DidNotVoteCount,
            denominatorEvidence,
            "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            publicPayload: "{\"kind\":\"unofficial\"}",
            recordedAt: tallyReadyAt,
            sourceTransactionId: tallyReadyTransactionId,
            sourceBlockHeight: 53,
            sourceBlockId: Guid.NewGuid());

        var readyElection = closedElection with
        {
            LastUpdatedAt = tallyReadyAt,
            TallyReadyAt = tallyReadyAt,
            TallyReadyArtifactId = tallyReadyArtifact.Id,
            UnofficialResultArtifactId = unofficialResult.Id,
        };

        store.Elections[election.ElectionId] = readyElection;
        store.BoundaryArtifacts.Add(tallyReadyArtifact);
        store.ResultArtifacts.Add(unofficialResult);

        return new AdminFinalizeReadyContext(
            readyElection,
            acceptedBallotHash,
            finalEncryptedTallyHash,
            closeResult.EligibilitySnapshot);
    }

    private static ElectionRecord CreateAdminElection(
        string title = "Board Election",
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null,
        string selectedProfileId = "admin-prod-1of1",
        bool selectedProfileDevOnly = false) =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: title,
            shortDescription: "Annual board vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "ORG-2026-01",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            selectedProfileId: selectedProfileId,
            selectedProfileDevOnly: selectedProfileDevOnly,
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
        string ballotNullifier = "nullifier-1",
        string encryptedBallotPackage = "ciphertext-payload",
        string proofBundle = "proof-bundle") =>
        new(
            scenario.Election.ElectionId,
            "voter-address",
            idempotencyKey,
            EncryptedBallotPackage: encryptedBallotPackage,
            ProofBundle: proofBundle,
            BallotNullifier: ballotNullifier,
            OpenArtifactId: scenario.OpenArtifact.Id,
            EligibleSetHash: scenario.EligibleSetHash.ToArray(),
            CeremonyVersionId: scenario.CeremonySnapshot.CeremonyVersionId,
            DkgProfileId: scenario.CeremonySnapshot.ProfileId,
            TallyPublicKeyFingerprint: scenario.CeremonySnapshot.TallyPublicKeyFingerprint);

    private static string CreateDevModeAcceptedBallotPackageForTests() =>
        JsonSerializer.Serialize(new
        {
            mode = "election-dev-mode-v1",
            packageType = "dev-protected-ballot",
            optionId = "option-a",
            optionLabel = "Alice",
            optionDescription = "First option",
            ballotOrder = 1,
            isBlankOption = false,
            selectionFingerprint = "selection-a",
        });

    private static string CreateDevModeAcceptedProofBundleForTests() =>
        JsonSerializer.Serialize(new
        {
            mode = "election-dev-mode-v1",
            proofType = "dev-plaintext-proof",
            openArtifactId = "open-artifact",
            statement = "plaintext-choice-projection",
        });

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

    private static DateTime GetProtectedAcceptedBallotTimestamp(ElectionRecord election)
    {
        var anchor = (election.OpenedAt ?? election.CreatedAt).ToUniversalTime();
        return new DateTime(anchor.Year, anchor.Month, anchor.Day, anchor.Hour, anchor.Minute, 0, DateTimeKind.Utc);
    }

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
        var acceptedBallotHash = new byte[] { 41, 42, 43 };
        var publishedBallotHash = new byte[] { 44, 45, 46 };
        var finalTallyHash = new byte[] { 51, 52, 53 };
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
            recordedAt: closedElection.ClosedAt!.Value);
        var tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.TallyReady,
            closedElection,
            recordedByPublicAddress: "owner-address",
            recordedAt: closedElection.TallyReadyAt!.Value,
            acceptedBallotCount: 3,
            acceptedBallotSetHash: acceptedBallotHash,
            publishedBallotCount: 3,
            publishedBallotStreamHash: publishedBallotHash,
            finalEncryptedTallyHash: finalTallyHash);
        var closeSnapshot = ElectionModelFactory.CreateEligibilitySnapshot(
            closedElection.ElectionId,
            ElectionEligibilitySnapshotType.Close,
            closedElection.EligibilityMutationPolicy,
            rosteredCount: 5,
            linkedCount: 5,
            activeDenominatorCount: 5,
            countedParticipationCount: 3,
            blankCount: 0,
            didNotVoteCount: 2,
            rosteredVoterSetHash: [1, 2, 3],
            activeDenominatorSetHash: [4, 5, 6],
            countedParticipationSetHash: [7, 8, 9],
            recordedByPublicAddress: "owner-address",
            boundaryArtifactId: closeArtifact.Id,
            recordedAt: closedElection.ClosedAt!.Value);
        var unofficialResult = ElectionModelFactory.CreateResultArtifact(
            closedElection.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.ParticipantEncrypted,
            closedElection.Title,
            [
                new ElectionResultOptionCount("yes", "Yes", null, 1, 1, 2),
                new ElectionResultOptionCount("no", "No", null, 2, 2, 1),
            ],
            blankCount: 0,
            totalVotedCount: 3,
            eligibleToVoteCount: 5,
            didNotVoteCount: 2,
            denominatorEvidence: new ElectionResultDenominatorEvidence(
                closeSnapshot.SnapshotType,
                closeSnapshot.Id,
                closeSnapshot.BoundaryArtifactId,
                closeSnapshot.ActiveDenominatorSetHash),
            recordedByPublicAddress: "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            encryptedPayload: "enc::unofficial",
            recordedAt: closedElection.TallyReadyAt!.Value);
        var storedElection = closedElection with
        {
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = tallyReadyArtifact.Id,
            UnofficialResultArtifactId = unofficialResult.Id,
        };
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            storedElection,
            ElectionGovernedActionType.Finalize,
            proposedByPublicAddress: "owner-address");

        store.Elections[storedElection.ElectionId] = storedElection;
        store.TrusteeInvitations[trusteeA.Id] = trusteeA;
        store.TrusteeInvitations[trusteeB.Id] = trusteeB;
        store.ElectionEnvelopeAccessRecords[(storedElection.ElectionId, storedElection.OwnerPublicAddress)] =
            new ElectionEnvelopeAccessRecord(
                storedElection.ElectionId,
                storedElection.OwnerPublicAddress,
                NodeEncryptedElectionPrivateKey: "node-encrypted-private-key",
                ActorEncryptedElectionPrivateKey: "actor-encrypted-private-key",
                GrantedAt: DateTime.UtcNow,
                SourceTransactionId: null,
                SourceBlockHeight: null,
                SourceBlockId: null);
        store.EligibilitySnapshots.Add(closeSnapshot);
        store.BoundaryArtifacts.Add(openArtifact);
        store.BoundaryArtifacts.Add(closeArtifact);
        store.BoundaryArtifacts.Add(tallyReadyArtifact);
        store.ResultArtifacts.Add(unofficialResult);
        store.GovernedProposals[proposal.Id] = proposal;

        return new TrusteeFinalizationScenario(
            storedElection,
            proposal,
            ceremonyVersion,
            openArtifact,
            closeArtifact,
            tallyReadyArtifact,
            unofficialResult,
            acceptedBallotHash,
            finalTallyHash);
    }

    private static TrusteeCloseCountingScenario SeedClosedTrusteeElectionForCloseCountingSession(
        ElectionStore store,
        int requiredApprovalCount,
        byte[]? finalEncryptedTallyHash = null,
        ElectionBindingStatus bindingStatus = ElectionBindingStatus.Binding,
        ICloseCountingExecutorKeyRegistry? closeCountingExecutorKeyRegistry = null,
        ICloseCountingExecutorEnvelopeCrypto? closeCountingExecutorEnvelopeCrypto = null)
    {
        closeCountingExecutorKeyRegistry ??= new InMemoryCloseCountingExecutorKeyRegistry();
        closeCountingExecutorEnvelopeCrypto ??= new TransparentTestCloseCountingExecutorEnvelopeCrypto();
        var draftElection = CreateTrusteeElection(
            requiredApprovalCount: requiredApprovalCount,
            bindingStatus: bindingStatus);
        var trusteeA = CreateAcceptedTrusteeInvitation(draftElection, "trustee-a", "Alice");
        var trusteeB = CreateAcceptedTrusteeInvitation(draftElection, "trustee-b", "Bob");
        var profile = RegisterCeremonyProfile(
            store,
            $"dkg-prod-2of2-close-counting-{requiredApprovalCount}",
            trusteeCount: 2,
            requiredApprovalCount: requiredApprovalCount);
        var ceremonyVersion = RegisterCeremonyVersion(
            store,
            draftElection,
            profile,
            [trusteeA, trusteeB],
            completedTrustees: ["trustee-a", "trustee-b"],
            ready: true,
            tallyFingerprint: $"close-counting-tally-{requiredApprovalCount}");
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
        var closedElection = openElection with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            OpenArtifactId = openArtifact.Id,
            ClosedAt = DateTime.UtcNow.AddMinutes(-5),
            VoteAcceptanceLockedAt = DateTime.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTime.UtcNow.AddMinutes(-5),
            ClosedProgressStatus = ElectionClosedProgressStatus.WaitingForTrusteeShares,
        };
        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            closedElection,
            recordedByPublicAddress: "owner-address",
            recordedAt: closedElection.ClosedAt!.Value);
        var storedElection = closedElection with
        {
            CloseArtifactId = closeArtifact.Id,
        };

        var acceptedBallots = new[]
        {
            new ElectionAcceptedBallotRecord(Guid.NewGuid(), storedElection.ElectionId, "ballot-a", "proof-a", "nullifier-a", DateTime.UtcNow.AddMinutes(-9)),
            new ElectionAcceptedBallotRecord(Guid.NewGuid(), storedElection.ElectionId, "ballot-b", "proof-b", "nullifier-b", DateTime.UtcNow.AddMinutes(-8)),
            new ElectionAcceptedBallotRecord(Guid.NewGuid(), storedElection.ElectionId, "ballot-c", "proof-c", "nullifier-c", DateTime.UtcNow.AddMinutes(-7)),
        };
        var publishedBallots = new[]
        {
            ElectionModelFactory.CreatePublishedBallotRecord(storedElection.ElectionId, 1, "ballot-a", "proof-a", DateTime.UtcNow.AddMinutes(-6)),
            ElectionModelFactory.CreatePublishedBallotRecord(storedElection.ElectionId, 2, "ballot-b", "proof-b", DateTime.UtcNow.AddMinutes(-6)),
            ElectionModelFactory.CreatePublishedBallotRecord(storedElection.ElectionId, 3, "ballot-c", "proof-c", DateTime.UtcNow.AddMinutes(-6)),
        };
        var acceptedBallotHash = ComputeAcceptedBallotInventoryHashForTests(acceptedBallots);
        var resolvedFinalEncryptedTallyHash = finalEncryptedTallyHash ?? [51, 52, 53];
        var closeSnapshot = ElectionModelFactory.CreateEligibilitySnapshot(
            storedElection.ElectionId,
            ElectionEligibilitySnapshotType.Close,
            storedElection.EligibilityMutationPolicy,
            rosteredCount: 5,
            linkedCount: 5,
            activeDenominatorCount: 5,
            countedParticipationCount: 3,
            blankCount: 0,
            didNotVoteCount: 2,
            rosteredVoterSetHash: [1, 2, 3],
            activeDenominatorSetHash: [4, 5, 6],
            countedParticipationSetHash: [7, 8, 9],
            recordedByPublicAddress: "owner-address",
            boundaryArtifactId: closeArtifact.Id,
            recordedAt: storedElection.ClosedAt!.Value);
        var session = ElectionModelFactory.CreateFinalizationSession(
            storedElection,
            closeArtifact.Id,
            acceptedBallotHash,
            resolvedFinalEncryptedTallyHash,
            ElectionFinalizationSessionPurpose.CloseCounting,
            ceremonySnapshot,
            requiredApprovalCount,
            [
                new ElectionTrusteeReference(trusteeA.TrusteeUserAddress, trusteeA.TrusteeDisplayName),
                new ElectionTrusteeReference(trusteeB.TrusteeUserAddress, trusteeB.TrusteeDisplayName),
            ],
            "owner-address",
            createdAt: DateTime.UtcNow.AddMinutes(-4));
        var closeCountingJob = ElectionModelFactory.CreateCloseCountingJob(
            session,
            createdAt: DateTime.UtcNow.AddMinutes(-4));
        var executorSessionKeys = closeCountingExecutorKeyRegistry.Create(closeCountingJob.Id, ExecutorKeyAlgorithm);
        var executorSessionKeyEnvelope = ElectionModelFactory.CreateExecutorSessionKeyEnvelope(
            closeCountingJob.Id,
            executorSessionKeys.PublicKey,
            closeCountingExecutorEnvelopeCrypto.SealPrivateKey(
                executorSessionKeys.PrivateKey,
                closeCountingJob.Id,
                ExecutorKeyAlgorithm),
            ExecutorKeyAlgorithm,
            closeCountingExecutorEnvelopeCrypto.SealAlgorithm,
            createdAt: DateTime.UtcNow.AddMinutes(-4),
            sealedByServiceIdentity: closeCountingExecutorEnvelopeCrypto.SealedByServiceIdentity);

        store.Elections[storedElection.ElectionId] = storedElection;
        store.TrusteeInvitations[trusteeA.Id] = trusteeA;
        store.TrusteeInvitations[trusteeB.Id] = trusteeB;
        store.ElectionEnvelopeAccessRecords[(storedElection.ElectionId, storedElection.OwnerPublicAddress)] =
            new ElectionEnvelopeAccessRecord(
                storedElection.ElectionId,
                storedElection.OwnerPublicAddress,
                NodeEncryptedElectionPrivateKey: "node-encrypted-private-key",
                ActorEncryptedElectionPrivateKey: "actor-encrypted-private-key",
                GrantedAt: DateTime.UtcNow,
                SourceTransactionId: null,
                SourceBlockHeight: null,
                SourceBlockId: null);
        store.BoundaryArtifacts.Add(openArtifact);
        store.BoundaryArtifacts.Add(closeArtifact);
        store.EligibilitySnapshots.Add(closeSnapshot);
        store.AcceptedBallots.AddRange(acceptedBallots);
        store.PublishedBallots.AddRange(publishedBallots);
        store.FinalizationSessions[session.Id] = session;
        store.CloseCountingJobs[closeCountingJob.Id] = closeCountingJob;
        store.ExecutorSessionKeyEnvelopes[closeCountingJob.Id] = executorSessionKeyEnvelope;

        return new TrusteeCloseCountingScenario(
            storedElection,
            session,
            closeCountingJob,
            executorSessionKeyEnvelope,
            closeCountingExecutorKeyRegistry,
            ceremonyVersion,
            openArtifact,
            closeArtifact,
            closeSnapshot,
            acceptedBallots,
            publishedBallots,
            acceptedBallotHash,
            resolvedFinalEncryptedTallyHash);
    }

    private static SubmitElectionFinalizationShareRequest CreateExecutorBoundFinalizationShareRequest(
        TrusteeCloseCountingScenario scenario,
        string actorPublicAddress,
        int shareIndex,
        string shareVersion,
        string shareMaterial,
        ElectionFinalizationTargetType targetType,
        Guid? claimedCeremonyVersionId = null,
        string? claimedTallyPublicKeyFingerprint = null,
        byte[]? claimedFinalEncryptedTallyHash = null,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        var encryptedExecutorSubmission = EncryptKeys.Encrypt(
            JsonSerializer.Serialize(new CloseCountingExecutorSubmissionPayload(
                scenario.CloseCountingJob.Id,
                scenario.Election.ElectionId.ToString(),
                scenario.Session.Id,
                actorPublicAddress,
                shareIndex,
                shareVersion,
                targetType,
                scenario.Session.CloseArtifactId,
                scenario.AcceptedBallotSetHash,
                claimedFinalEncryptedTallyHash ?? scenario.FinalEncryptedTallyHash,
                scenario.Session.TargetTallyId,
                claimedCeremonyVersionId ?? scenario.CeremonyVersion.Id,
                claimedTallyPublicKeyFingerprint ?? scenario.CeremonyVersion.TallyPublicKeyFingerprint,
                shareMaterial)),
            scenario.ExecutorSessionKeyEnvelope.ExecutorSessionPublicKey);

        return new SubmitElectionFinalizationShareRequest(
            ElectionId: scenario.Election.ElectionId,
            FinalizationSessionId: scenario.Session.Id,
            ActorPublicAddress: actorPublicAddress,
            ShareIndex: shareIndex,
            ShareVersion: shareVersion,
            TargetType: targetType,
            ClaimedCloseArtifactId: scenario.Session.CloseArtifactId,
            ClaimedAcceptedBallotSetHash: scenario.AcceptedBallotSetHash,
            ClaimedFinalEncryptedTallyHash: claimedFinalEncryptedTallyHash ?? scenario.FinalEncryptedTallyHash,
            ClaimedTargetTallyId: scenario.Session.TargetTallyId,
            ClaimedCeremonyVersionId: claimedCeremonyVersionId ?? scenario.CeremonyVersion.Id,
            ClaimedTallyPublicKeyFingerprint: claimedTallyPublicKeyFingerprint ?? scenario.CeremonyVersion.TallyPublicKeyFingerprint,
            ShareMaterial: null,
            CloseCountingJobId: scenario.CloseCountingJob.Id,
            ExecutorKeyAlgorithm: scenario.ExecutorSessionKeyEnvelope.KeyAlgorithm,
            EncryptedExecutorSubmission: encryptedExecutorSubmission,
            SourceTransactionId: sourceTransactionId,
            SourceBlockHeight: sourceBlockHeight,
            SourceBlockId: sourceBlockId);
    }

    private static ElectionRecord CreateTrusteeElection(
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null,
        int requiredApprovalCount = 2,
        ElectionBindingStatus bindingStatus = ElectionBindingStatus.Binding,
        string? selectedProfileId = null,
        bool? selectedProfileDevOnly = null) =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Referendum",
            shortDescription: "Policy vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "REF-2026-01",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: bindingStatus,
            selectedProfileId: selectedProfileId ?? ResolveTestProfileId(bindingStatus, requiredApprovalCount),
            selectedProfileDevOnly: selectedProfileDevOnly ?? ResolveSelectedProfileDevOnly(selectedProfileId ?? ResolveTestProfileId(bindingStatus, requiredApprovalCount)),
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
        string? tallyFingerprint = null)
    {
        tallyFingerprint ??= CeremonyTestKeyFixtures.Fingerprint;
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
                    .RecordMaterialSubmitted(DateTime.UtcNow, "share-v1", CeremonyTestKeyFixtures.PublicKeyBytes)
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
            version = version.MarkReady(
                DateTime.UtcNow,
                tallyFingerprint,
                CeremonyTestKeyFixtures.PublicKeyBytes);
        }

        store.CeremonyVersions[version.Id] = version;
        return version;
    }

    private static ElectionDraftSpecification CreateAdminDraftSpecification(
        string title = "Board Election",
        ElectionDisclosureMode disclosureMode = ElectionDisclosureMode.FinalResultsOnly,
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null,
        EligibilityMutationPolicy eligibilityMutationPolicy = EligibilityMutationPolicy.FrozenAtOpen,
        ElectionBindingStatus bindingStatus = ElectionBindingStatus.Binding,
        string? selectedProfileId = null) =>
        new(
            Title: title,
            ShortDescription: "Annual board vote",
            ExternalReferenceCode: "ORG-2026-01",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: bindingStatus,
            SelectedProfileId: selectedProfileId ?? (bindingStatus == ElectionBindingStatus.NonBinding ? "admin-dev-1of1" : "admin-prod-1of1"),
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
        int requiredApprovalCount = 2,
        ElectionBindingStatus bindingStatus = ElectionBindingStatus.Binding,
        string? selectedProfileId = null) =>
        new(
            Title: title,
            ShortDescription: "Policy vote",
            ExternalReferenceCode: "REF-2026-01",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: bindingStatus,
            SelectedProfileId: selectedProfileId ?? ResolveTestProfileId(bindingStatus, requiredApprovalCount),
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

    private static string ResolveTestProfileId(
        ElectionBindingStatus bindingStatus,
        int requiredApprovalCount) =>
        (bindingStatus, requiredApprovalCount) switch
        {
            (ElectionBindingStatus.NonBinding, 1) => "dkg-dev-1of1",
            (ElectionBindingStatus.NonBinding, 2) => "dkg-dev-2of2",
            (ElectionBindingStatus.NonBinding, _) => "dkg-dev-3of5",
            (ElectionBindingStatus.Binding, 1) => "dkg-prod-1of1",
            (ElectionBindingStatus.Binding, 2) => "dkg-prod-2of2",
            _ => "dkg-prod-3of5",
        };

    private static bool ResolveSelectedProfileDevOnly(string profileId) =>
        profileId.Contains("-dev-", StringComparison.OrdinalIgnoreCase);

    private static void SeedStandardCeremonyProfiles(ElectionStore store)
    {
        RegisterDefaultProfile(store, "dkg-prod-1of1", trusteeCount: 1, requiredApprovalCount: 1, devOnly: false);
        RegisterDefaultProfile(store, "dkg-prod-2of2", trusteeCount: 2, requiredApprovalCount: 2, devOnly: false);
        RegisterDefaultProfile(store, "dkg-prod-3of5", trusteeCount: 5, requiredApprovalCount: 3, devOnly: false);
        RegisterDefaultProfile(store, "dkg-dev-1of1", trusteeCount: 1, requiredApprovalCount: 1, devOnly: true);
        RegisterDefaultProfile(store, "dkg-dev-2of2", trusteeCount: 2, requiredApprovalCount: 2, devOnly: true);
        RegisterDefaultProfile(store, "dkg-dev-3of5", trusteeCount: 5, requiredApprovalCount: 3, devOnly: true);
    }

    private static void RegisterDefaultProfile(
        ElectionStore store,
        string profileId,
        int trusteeCount,
        int requiredApprovalCount,
        bool devOnly)
    {
        if (store.CeremonyProfiles.ContainsKey(profileId))
        {
            return;
        }

        store.CeremonyProfiles[profileId] = ElectionModelFactory.CreateCeremonyProfile(
            profileId,
            displayName: profileId,
            description: $"Seeded test profile {profileId}",
            providerKey: devOnly ? "hush-dev" : "hush-prod",
            profileVersion: "v1",
            trusteeCount: trusteeCount,
            requiredApprovalCount: requiredApprovalCount,
            devOnly: devOnly);
    }

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

    private static byte[] ComputeAcceptedBallotInventoryHashForTests(
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots)
    {
        var payload = string.Join(
            '\n',
            acceptedBallots
                .OrderBy(x => x.BallotNullifier, StringComparer.Ordinal)
                .Select(x => $"{x.BallotNullifier}|{ComputeHexSha256ForTests(x.EncryptedBallotPackage)}|{ComputeHexSha256ForTests(x.ProofBundle)}"));

        return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    }

    private static string ComputeHexSha256ForTests(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private static string CreateDevModePublishedBallotPackageForTests(
        ElectionRecord election,
        string optionId,
        int ballotOrder,
        string actorPublicAddress,
        bool isBlankOption = false,
        string? optionLabel = null) =>
        JsonSerializer.Serialize(new
        {
            mode = "election-dev-mode-v1",
            packageType = "dev-published-ballot",
            electionId = election.ElectionId.ToString(),
            optionId,
            optionLabel = optionLabel ?? optionId,
            optionDescription = string.Empty,
            ballotOrder,
            isBlankOption,
            publicationNonce = ComputeHexSha256ForTests($"{actorPublicAddress}:{optionId}:{ballotOrder}"),
        });

    private static byte[] ComputeDevModePublishedTallyHashForTests(
        ElectionRecord election,
        IReadOnlyDictionary<string, int> countsByOptionId)
    {
        var tallyPayload = string.Join(
            '\n',
            election.Options
                .OrderBy(x => x.BallotOrder)
                .Select(x => $"{x.OptionId}|{countsByOptionId.GetValueOrDefault(x.OptionId, 0)}"));

        return SHA256.HashData(
            Encoding.UTF8.GetBytes($"admin-only-dev-tally:v1|{election.ElectionId}|{tallyPayload}"));
    }

    private sealed record TrusteeFinalizationScenario(
        ElectionRecord Election,
        ElectionGovernedProposalRecord Proposal,
        ElectionCeremonyVersionRecord CeremonyVersion,
        ElectionBoundaryArtifactRecord OpenArtifact,
        ElectionBoundaryArtifactRecord CloseArtifact,
        ElectionBoundaryArtifactRecord TallyReadyArtifact,
        ElectionResultArtifactRecord UnofficialResult,
        byte[] AcceptedBallotSetHash,
        byte[] FinalEncryptedTallyHash);

    private sealed record TrusteeCloseCountingScenario(
        ElectionRecord Election,
        ElectionFinalizationSessionRecord Session,
        ElectionCloseCountingJobRecord CloseCountingJob,
        ElectionExecutorSessionKeyEnvelopeRecord ExecutorSessionKeyEnvelope,
        ICloseCountingExecutorKeyRegistry CloseCountingExecutorKeyRegistry,
        ElectionCeremonyVersionRecord CeremonyVersion,
        ElectionBoundaryArtifactRecord OpenArtifact,
        ElectionBoundaryArtifactRecord CloseArtifact,
        ElectionEligibilitySnapshotRecord CloseSnapshot,
        IReadOnlyList<ElectionAcceptedBallotRecord> AcceptedBallots,
        IReadOnlyList<ElectionPublishedBallotRecord> PublishedBallots,
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
        public List<ElectionResultArtifactRecord> ResultArtifacts { get; } = [];
        public List<ElectionDraftSnapshotRecord> DraftSnapshots { get; } = [];
        public List<ElectionRosterEntryRecord> RosterEntries { get; } = [];
        public List<ElectionEligibilityActivationEventRecord> EligibilityActivationEvents { get; } = [];
        public List<ElectionParticipationRecord> ParticipationRecords { get; } = [];
        public List<ElectionCommitmentRegistrationRecord> CommitmentRegistrations { get; } = [];
        public List<ElectionCheckoffConsumptionRecord> CheckoffConsumptions { get; } = [];
        public List<ElectionEligibilitySnapshotRecord> EligibilitySnapshots { get; } = [];
        public List<ElectionBoundaryArtifactRecord> BoundaryArtifacts { get; } = [];
        public List<ElectionAcceptedBallotRecord> AcceptedBallots { get; } = [];
        public List<ElectionBallotMemPoolRecord> BallotMemPoolEntries { get; } = [];
        public List<ElectionPublishedBallotRecord> PublishedBallots { get; } = [];
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
        public Dictionary<Guid, ElectionCloseCountingJobRecord> CloseCountingJobs { get; } = [];
        public Dictionary<Guid, ElectionExecutorSessionKeyEnvelopeRecord> ExecutorSessionKeyEnvelopes { get; } = [];
        public Dictionary<ElectionId, ElectionAdminOnlyProtectedTallyEnvelopeRecord> AdminOnlyProtectedTallyEnvelopes { get; } = [];
        public Dictionary<Guid, ElectionTallyExecutorLeaseRecord> TallyExecutorLeases { get; } = [];
        public Dictionary<Guid, ElectionFinalizationSessionRecord> FinalizationSessions { get; } = [];
        public List<ElectionFinalizationShareRecord> FinalizationShares { get; } = [];
        public List<ElectionFinalizationReleaseEvidenceRecord> FinalizationReleaseEvidenceRecords { get; } = [];
        public Dictionary<Guid, ElectionReportPackageRecord> ReportPackages { get; } = [];
        public List<ElectionReportArtifactRecord> ReportArtifacts { get; } = [];
        public List<ElectionReportAccessGrantRecord> ReportAccessGrants { get; } = [];
        public TaskCompletionSource<bool>? GetElectionForUpdateEntered { get; set; }
        public TaskCompletionSource<bool>? ReleaseGetElectionForUpdate { get; set; }
    }

    private static CredentialsProfile CreateTestNodeCredentials()
    {
        var encryptKeys = new EncryptKeys();
        return new CredentialsProfile
        {
            ProfileName = "lifecycle-test-node",
            PublicSigningAddress = "lifecycle-test-signer",
            PrivateSigningKey = "lifecycle-test-private-signing-key",
            PublicEncryptAddress = encryptKeys.PublicKey,
            PrivateEncryptKey = encryptKeys.PrivateKey,
        };
    }

    private sealed class FakeCredentialsProvider : ICredentialsProvider
    {
        public CredentialsProfile Credentials { get; set; } = TestNodeCredentials;

        public CredentialsProfile GetCredentials() => Credentials;
    }

    private sealed class FakeIdentityService : IIdentityService
    {
        private readonly Dictionary<string, ProfileBase> _profiles = new(StringComparer.OrdinalIgnoreCase);

        public Task<ProfileBase> RetrieveIdentityAsync(string publicSigningAddress)
        {
            if (string.IsNullOrWhiteSpace(publicSigningAddress))
            {
                return Task.FromResult<ProfileBase>(new NonExistingProfile());
            }

            if (_profiles.TryGetValue(publicSigningAddress, out var existing))
            {
                return Task.FromResult(existing);
            }

            var encryptKeys = new EncryptKeys();
            var profile = new Profile(
                Alias: publicSigningAddress,
                ShortAlias: publicSigningAddress[..Math.Min(12, publicSigningAddress.Length)],
                PublicSigningAddress: publicSigningAddress,
                PublicEncryptAddress: encryptKeys.PublicKey,
                IsPublic: true,
                BlockIndex: BlockIndexHandler.CreateNew(1));
            _profiles[publicSigningAddress] = profile;
            return Task.FromResult<ProfileBase>(profile);
        }
    }

    private sealed record AdminFinalizeReadyContext(
        ElectionRecord Election,
        byte[] AcceptedBallotSetHash,
        byte[] FinalEncryptedTallyHash,
        ElectionEligibilitySnapshotRecord CloseEligibilitySnapshot);

    private sealed class FakeElectionResultCryptoService(
        IReadOnlyList<int> decodedCounts,
        byte[] finalEncryptedTallyHash) : IElectionResultCryptoService
    {
        public ElectionAggregateReleaseResult TryReleaseAggregateTally(
            IReadOnlyList<string> encryptedBallotPackages,
            IReadOnlyList<ElectionFinalizationShareRecord> acceptedShares,
            int maxSupportedCount) =>
            ElectionAggregateReleaseResult.Success(finalEncryptedTallyHash, decodedCounts);

        public string EncryptForElectionParticipants(string plaintextPayload, string nodeEncryptedElectionPrivateKey) =>
            $"enc::{plaintextPayload}";
    }

    private sealed class DevModeFallbackElectionResultCryptoService : IElectionResultCryptoService
    {
        public ElectionAggregateReleaseResult TryReleaseAggregateTally(
            IReadOnlyList<string> encryptedBallotPackages,
            IReadOnlyList<ElectionFinalizationShareRecord> acceptedShares,
            int maxSupportedCount) =>
            ElectionAggregateReleaseResult.Failure(
                "UNSUPPORTED_BALLOT_PAYLOAD",
                "dev ballots require fallback");

        public string EncryptForElectionParticipants(string plaintextPayload, string nodeEncryptedElectionPrivateKey) =>
            $"enc::{plaintextPayload}";
    }

    private sealed class SelectiveElectionResultCryptoService(
        IReadOnlyList<int> decodedCounts,
        byte[] finalEncryptedTallyHash,
        IReadOnlyList<string> successfulTrustees) : IElectionResultCryptoService
    {
        public ElectionAggregateReleaseResult TryReleaseAggregateTally(
            IReadOnlyList<string> encryptedBallotPackages,
            IReadOnlyList<ElectionFinalizationShareRecord> acceptedShares,
            int maxSupportedCount)
        {
            var normalizedTrustees = acceptedShares
                .Select(x => x.TrusteeUserAddress)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var expectedTrustees = successfulTrustees
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return normalizedTrustees.SequenceEqual(expectedTrustees, StringComparer.OrdinalIgnoreCase)
                ? ElectionAggregateReleaseResult.Success(finalEncryptedTallyHash, decodedCounts)
                : ElectionAggregateReleaseResult.Failure(
                    "MALFORMED_SHARE",
                    "Accepted shares reconstruct a different public key than the published ballots.");
        }

        public string EncryptForElectionParticipants(string plaintextPayload, string nodeEncryptedElectionPrivateKey) =>
            $"enc::{plaintextPayload}";
    }

    private sealed class FakeElectionReportPackageService(int failBuildAttempts = 0) : IElectionReportPackageService
    {
        private readonly ElectionReportPackageService _inner = new();
        private int _remainingFailures = failBuildAttempts;

        public ElectionReportPackageBuildResult Build(ElectionReportPackageBuildRequest request)
        {
            var successResult = _inner.Build(request);
            if (_remainingFailures <= 0 || !successResult.IsSuccess)
            {
                return successResult;
            }

            _remainingFailures--;
            return ElectionReportPackageBuildResult.Failure(
                ElectionModelFactory.CreateFailedReportPackageAttempt(
                    request.Election.ElectionId,
                    request.AttemptNumber,
                    request.TallyReadyArtifact.Id,
                    request.UnofficialResult.Id,
                    successResult.Package.FrozenEvidenceHash,
                    successResult.Package.FrozenEvidenceFingerprint,
                    request.AttemptedByPublicAddress,
                    "FORCED_PACKAGE_FAILURE",
                    "Forced report package failure for test coverage.",
                    previousAttemptId: request.PreviousAttemptId,
                    finalizationSessionId: request.FinalizationSession?.Id,
                    closeBoundaryArtifactId: request.CloseArtifact.Id,
                    closeEligibilitySnapshotId: request.CloseEligibilitySnapshot?.Id,
                    finalizationReleaseEvidenceId: request.FinalizationReleaseEvidence?.Id,
                    attemptedAt: request.AttemptedAt));
        }
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

        public Task UpdateBoundaryArtifactAsync(ElectionBoundaryArtifactRecord artifact)
        {
            store.BoundaryArtifacts.RemoveAll(x => x.Id == artifact.Id);
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

        public Task DeleteElectionEnvelopeAccessAsync(ElectionId electionId, string actorPublicAddress)
        {
            store.ElectionEnvelopeAccessRecords.Remove((electionId, actorPublicAddress));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionResultArtifactRecord>> GetResultArtifactsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionResultArtifactRecord>>(
                store.ResultArtifacts
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.RecordedAt)
                    .ThenBy(x => x.ArtifactKind)
                    .ToArray());

        public Task<ElectionResultArtifactRecord?> GetResultArtifactAsync(Guid resultArtifactId) =>
            Task.FromResult(store.ResultArtifacts.FirstOrDefault(x => x.Id == resultArtifactId));

        public Task<ElectionResultArtifactRecord?> GetResultArtifactAsync(
            ElectionId electionId,
            ElectionResultArtifactKind artifactKind) =>
            Task.FromResult(
                store.ResultArtifacts.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    x.ArtifactKind == artifactKind));

        public Task SaveResultArtifactAsync(ElectionResultArtifactRecord resultArtifact)
        {
            store.ResultArtifacts.RemoveAll(x => x.Id == resultArtifact.Id);
            store.ResultArtifacts.Add(resultArtifact);
            return Task.CompletedTask;
        }

        public Task UpdateResultArtifactAsync(ElectionResultArtifactRecord resultArtifact) =>
            SaveResultArtifactAsync(resultArtifact);

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
                    .ThenBy(x => x.Id)
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

        public Task<IReadOnlyList<ElectionBallotMemPoolRecord>> GetBallotMemPoolEntriesAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionBallotMemPoolRecord>>(
                store.BallotMemPoolEntries
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.QueuedAt)
                    .ThenBy(x => x.Id)
                    .ToArray());

        public Task<IReadOnlyList<ElectionId>> GetElectionIdsWithBallotMemPoolEntriesAsync() =>
            Task.FromResult<IReadOnlyList<ElectionId>>(
                store.BallotMemPoolEntries
                    .Select(x => x.ElectionId)
                    .Distinct()
                    .ToArray());

        public Task<ElectionBallotMemPoolRecord?> GetBallotMemPoolEntryByAcceptedBallotAsync(
            ElectionId electionId,
            Guid acceptedBallotId) =>
            Task.FromResult(
                store.BallotMemPoolEntries.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    x.AcceptedBallotId == acceptedBallotId));

        public Task SaveBallotMemPoolEntryAsync(ElectionBallotMemPoolRecord ballotMemPoolEntry)
        {
            store.BallotMemPoolEntries.RemoveAll(x => x.Id == ballotMemPoolEntry.Id);
            store.BallotMemPoolEntries.Add(ballotMemPoolEntry);
            return Task.CompletedTask;
        }

        public Task DeleteBallotMemPoolEntryAsync(Guid ballotMemPoolEntryId)
        {
            store.BallotMemPoolEntries.RemoveAll(x => x.Id == ballotMemPoolEntryId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionPublishedBallotRecord>> GetPublishedBallotsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionPublishedBallotRecord>>(
                store.PublishedBallots
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.PublicationSequence)
                    .ToArray());

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

        public Task<IReadOnlyList<ElectionCloseCountingJobRecord>> GetCloseCountingJobsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionCloseCountingJobRecord>>(
                store.CloseCountingJobs.Values
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.CreatedAt)
                    .ToArray());

        public Task<ElectionCloseCountingJobRecord?> GetCloseCountingJobAsync(Guid closeCountingJobId) =>
            Task.FromResult(store.CloseCountingJobs.GetValueOrDefault(closeCountingJobId));

        public Task SaveCloseCountingJobAsync(ElectionCloseCountingJobRecord closeCountingJob)
        {
            store.CloseCountingJobs[closeCountingJob.Id] = closeCountingJob;
            return Task.CompletedTask;
        }

        public Task UpdateCloseCountingJobAsync(ElectionCloseCountingJobRecord closeCountingJob)
        {
            store.CloseCountingJobs[closeCountingJob.Id] = closeCountingJob;
            return Task.CompletedTask;
        }

        public Task<ElectionExecutorSessionKeyEnvelopeRecord?> GetExecutorSessionKeyEnvelopeAsync(Guid closeCountingJobId) =>
            Task.FromResult(store.ExecutorSessionKeyEnvelopes.GetValueOrDefault(closeCountingJobId));

        public Task SaveExecutorSessionKeyEnvelopeAsync(ElectionExecutorSessionKeyEnvelopeRecord envelope)
        {
            store.ExecutorSessionKeyEnvelopes[envelope.CloseCountingJobId] = envelope;
            return Task.CompletedTask;
        }

        public Task UpdateExecutorSessionKeyEnvelopeAsync(ElectionExecutorSessionKeyEnvelopeRecord envelope)
        {
            store.ExecutorSessionKeyEnvelopes[envelope.CloseCountingJobId] = envelope;
            return Task.CompletedTask;
        }

        public Task<ElectionAdminOnlyProtectedTallyEnvelopeRecord?> GetAdminOnlyProtectedTallyEnvelopeAsync(ElectionId electionId) =>
            Task.FromResult(store.AdminOnlyProtectedTallyEnvelopes.GetValueOrDefault(electionId));

        public Task SaveAdminOnlyProtectedTallyEnvelopeAsync(ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope)
        {
            store.AdminOnlyProtectedTallyEnvelopes[envelope.ElectionId] = envelope;
            return Task.CompletedTask;
        }

        public Task UpdateAdminOnlyProtectedTallyEnvelopeAsync(ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope)
        {
            store.AdminOnlyProtectedTallyEnvelopes[envelope.ElectionId] = envelope;
            return Task.CompletedTask;
        }

        public Task<ElectionTallyExecutorLeaseRecord?> GetTallyExecutorLeaseAsync(Guid closeCountingJobId) =>
            Task.FromResult(store.TallyExecutorLeases.GetValueOrDefault(closeCountingJobId));

        public Task SaveTallyExecutorLeaseAsync(ElectionTallyExecutorLeaseRecord lease)
        {
            store.TallyExecutorLeases[lease.CloseCountingJobId] = lease;
            return Task.CompletedTask;
        }

        public Task UpdateTallyExecutorLeaseAsync(ElectionTallyExecutorLeaseRecord lease)
        {
            store.TallyExecutorLeases[lease.CloseCountingJobId] = lease;
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

        public Task UpdateFinalizationShareAsync(ElectionFinalizationShareRecord shareRecord)
        {
            var index = store.FinalizationShares.FindIndex(x => x.Id == shareRecord.Id);
            if (index >= 0)
            {
                store.FinalizationShares[index] = shareRecord;
            }

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

        public Task<IReadOnlyList<ElectionReportPackageRecord>> GetReportPackagesAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionReportPackageRecord>>(
                store.ReportPackages.Values
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.AttemptNumber)
                    .ThenBy(x => x.AttemptedAt)
                    .ToArray());

        public Task<ElectionReportPackageRecord?> GetLatestReportPackageAsync(ElectionId electionId) =>
            Task.FromResult(
                store.ReportPackages.Values
                    .Where(x => x.ElectionId == electionId)
                    .OrderByDescending(x => x.AttemptNumber)
                    .ThenByDescending(x => x.AttemptedAt)
                    .FirstOrDefault());

        public Task<ElectionReportPackageRecord?> GetReportPackageAsync(Guid reportPackageId) =>
            Task.FromResult(store.ReportPackages.GetValueOrDefault(reportPackageId));

        public Task SaveReportPackageAsync(ElectionReportPackageRecord reportPackage)
        {
            store.ReportPackages[reportPackage.Id] = reportPackage;
            return Task.CompletedTask;
        }

        public Task UpdateReportPackageAsync(ElectionReportPackageRecord reportPackage)
        {
            store.ReportPackages[reportPackage.Id] = reportPackage;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionReportArtifactRecord>> GetReportArtifactsAsync(Guid reportPackageId) =>
            Task.FromResult<IReadOnlyList<ElectionReportArtifactRecord>>(
                store.ReportArtifacts
                    .Where(x => x.ReportPackageId == reportPackageId)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.ArtifactKind)
                    .ToArray());

        public Task SaveReportArtifactAsync(ElectionReportArtifactRecord reportArtifact)
        {
            store.ReportArtifacts.RemoveAll(x => x.Id == reportArtifact.Id);
            store.ReportArtifacts.Add(reportArtifact);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ElectionReportAccessGrantRecord>> GetReportAccessGrantsAsync(ElectionId electionId) =>
            Task.FromResult<IReadOnlyList<ElectionReportAccessGrantRecord>>(
                store.ReportAccessGrants
                    .Where(x => x.ElectionId == electionId)
                    .OrderBy(x => x.GrantedAt)
                    .ToArray());

        public Task<ElectionReportAccessGrantRecord?> GetReportAccessGrantAsync(ElectionId electionId, string actorPublicAddress) =>
            Task.FromResult(
                store.ReportAccessGrants.FirstOrDefault(x =>
                    x.ElectionId == electionId &&
                    string.Equals(x.ActorPublicAddress, actorPublicAddress, StringComparison.Ordinal)));

        public Task SaveReportAccessGrantAsync(ElectionReportAccessGrantRecord accessGrant)
        {
            store.ReportAccessGrants.RemoveAll(x => x.Id == accessGrant.Id);
            store.ReportAccessGrants.Add(accessGrant);
            return Task.CompletedTask;
        }
    }
}
