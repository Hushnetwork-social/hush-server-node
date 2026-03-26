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
        result.ErrorCode.Should().Be(ElectionCommandErrorCode.DependencyBlocked);
        result.ValidationErrors.Should().Contain(x => x.Contains("FEAT-096", StringComparison.Ordinal));
        store.BoundaryArtifacts.Should().BeEmpty();
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
        finalizeResult.Election!.LifecycleState.Should().Be(ElectionLifecycleState.Finalized);
        store.BoundaryArtifacts.Select(x => x.ArtifactType).Should().Equal(
            ElectionBoundaryArtifactType.Close,
            ElectionBoundaryArtifactType.Finalize);
        store.BoundaryArtifacts[0].AcceptedBallotSetHash.Should().Equal(closeBallotHash);
        store.BoundaryArtifacts[1].FinalEncryptedTallyHash.Should().Equal(finalizeTallyHash);
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
    }
}
