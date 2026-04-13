using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushNode.Caching;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class ElectionLifecycleService : IElectionLifecycleService
{
    private static readonly JsonSerializerOptions ResultPayloadJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider;
    private readonly ILogger<ElectionLifecycleService> _logger;
    private readonly ElectionCeremonyOptions _ceremonyOptions;
    private readonly IElectionCastIdempotencyCacheService? _castIdempotencyCacheService;
    private readonly IElectionResultCryptoService? _electionResultCryptoService;
    private readonly IElectionReportPackageService _electionReportPackageService;
    private readonly ConcurrentDictionary<string, bool> _pendingCastTracking = new();

    public ElectionLifecycleService(
        IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
        ILogger<ElectionLifecycleService> logger)
        : this(unitOfWorkProvider, logger, new ElectionCeremonyOptions(), null, null, null)
    {
    }

    public ElectionLifecycleService(
        IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
        ILogger<ElectionLifecycleService> logger,
        ElectionCeremonyOptions ceremonyOptions)
        : this(unitOfWorkProvider, logger, ceremonyOptions, null, null, null)
    {
    }

    public ElectionLifecycleService(
        IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
        ILogger<ElectionLifecycleService> logger,
        ElectionCeremonyOptions ceremonyOptions,
        IElectionCastIdempotencyCacheService? castIdempotencyCacheService,
        IElectionResultCryptoService? electionResultCryptoService = null,
        IElectionReportPackageService? electionReportPackageService = null)
    {
        _unitOfWorkProvider = unitOfWorkProvider;
        _logger = logger;
        _ceremonyOptions = ceremonyOptions;
        _castIdempotencyCacheService = castIdempotencyCacheService;
        _electionResultCryptoService = electionResultCryptoService;
        _electionReportPackageService = electionReportPackageService ?? new ElectionReportPackageService();
    }

    public async Task<ElectionCommandResult> CreateDraftAsync(CreateElectionDraftRequest request)
    {
        if (!string.Equals(request.OwnerPublicAddress, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can create the draft election.");
        }

        var validationErrors = ElectionDraftValidator.ValidateDraftRequest(
            request.ActorPublicAddress,
            request.SnapshotReason,
            request.Draft);
        if (validationErrors.Count > 0)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Draft election validation failed.",
                validationErrors);
        }

        var election = ElectionModelFactory.CreateDraftRecord(
            electionId: request.PreassignedElectionId ?? ElectionId.NewElectionId,
            title: request.Draft.Title,
            shortDescription: request.Draft.ShortDescription,
            ownerPublicAddress: request.OwnerPublicAddress,
            externalReferenceCode: request.Draft.ExternalReferenceCode,
            electionClass: request.Draft.ElectionClass,
            bindingStatus: request.Draft.BindingStatus,
            governanceMode: request.Draft.GovernanceMode,
            disclosureMode: request.Draft.DisclosureMode,
            participationPrivacyMode: request.Draft.ParticipationPrivacyMode,
            voteUpdatePolicy: request.Draft.VoteUpdatePolicy,
            eligibilitySourceType: request.Draft.EligibilitySourceType,
            eligibilityMutationPolicy: request.Draft.EligibilityMutationPolicy,
            outcomeRule: request.Draft.OutcomeRule,
            approvedClientApplications: NormalizeApprovedApplications(request.Draft.ApprovedClientApplications),
            protocolOmegaVersion: request.Draft.ProtocolOmegaVersion,
            reportingPolicy: request.Draft.ReportingPolicy,
            reviewWindowPolicy: request.Draft.ReviewWindowPolicy,
            ownerOptions: NormalizeOwnerOptions(request.Draft.OwnerOptions),
            acknowledgedWarningCodes: NormalizeWarningCodes(request.Draft.AcknowledgedWarningCodes),
            requiredApprovalCount: request.Draft.RequiredApprovalCount);

        var snapshot = ElectionModelFactory.CreateDraftSnapshot(
            election,
            request.SnapshotReason,
            request.ActorPublicAddress,
            sourceTransactionId: request.SourceTransactionId,
            sourceBlockHeight: request.SourceBlockHeight,
            sourceBlockId: request.SourceBlockId);
        var warningAcknowledgements = CreateWarningAcknowledgementsForRevision(
            election.ElectionId,
            election.CurrentDraftRevision,
            request.ActorPublicAddress,
            election.AcknowledgedWarningCodes,
            sourceTransactionId: request.SourceTransactionId,
            sourceBlockHeight: request.SourceBlockHeight,
            sourceBlockId: request.SourceBlockId);

        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();

        await repository.SaveElectionAsync(election);
        await repository.SaveDraftSnapshotAsync(snapshot);
        foreach (var acknowledgement in warningAcknowledgements)
        {
            await repository.SaveWarningAcknowledgementAsync(acknowledgement);
        }

        await unitOfWork.CommitAsync();

        _logger.LogInformation(
            "[ElectionLifecycleService] Created draft election {ElectionId} at revision {Revision}",
            election.ElectionId,
            election.CurrentDraftRevision);

        return ElectionCommandResult.Success(election, draftSnapshot: snapshot);
    }

    public async Task<ElectionCommandResult> UpdateDraftAsync(UpdateElectionDraftRequest request)
    {
        var validationErrors = ElectionDraftValidator.ValidateDraftRequest(
            request.ActorPublicAddress,
            request.SnapshotReason,
            request.Draft);
        if (validationErrors.Count > 0)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Draft election validation failed.",
                validationErrors);
        }

        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var existing = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (existing is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (existing.LifecycleState != ElectionLifecycleState.Draft)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Immutable FEAT-094 policy cannot be edited after the election opens.");
        }

        var pendingProposal = await repository.GetPendingGovernedProposalAsync(request.ElectionId);
        var governedDraftLock = ValidateDraftNotBlockedByGovernedOpenProposal(pendingProposal);
        if (governedDraftLock is not null)
        {
            return governedDraftLock;
        }

        if (!string.Equals(existing.OwnerPublicAddress, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can edit a draft election.");
        }

        var updated = ElectionModelFactory.CreateDraftRecord(
            electionId: existing.ElectionId,
            title: request.Draft.Title,
            shortDescription: request.Draft.ShortDescription,
            ownerPublicAddress: existing.OwnerPublicAddress,
            externalReferenceCode: request.Draft.ExternalReferenceCode,
            electionClass: request.Draft.ElectionClass,
            bindingStatus: request.Draft.BindingStatus,
            governanceMode: request.Draft.GovernanceMode,
            disclosureMode: request.Draft.DisclosureMode,
            participationPrivacyMode: request.Draft.ParticipationPrivacyMode,
            voteUpdatePolicy: request.Draft.VoteUpdatePolicy,
            eligibilitySourceType: request.Draft.EligibilitySourceType,
            eligibilityMutationPolicy: request.Draft.EligibilityMutationPolicy,
            outcomeRule: request.Draft.OutcomeRule,
            approvedClientApplications: NormalizeApprovedApplications(request.Draft.ApprovedClientApplications),
            protocolOmegaVersion: request.Draft.ProtocolOmegaVersion,
            reportingPolicy: request.Draft.ReportingPolicy,
            reviewWindowPolicy: request.Draft.ReviewWindowPolicy,
            ownerOptions: NormalizeOwnerOptions(request.Draft.OwnerOptions),
            acknowledgedWarningCodes: NormalizeWarningCodes(request.Draft.AcknowledgedWarningCodes),
            currentDraftRevision: existing.CurrentDraftRevision + 1,
            requiredApprovalCount: request.Draft.RequiredApprovalCount,
            createdAt: existing.CreatedAt) with
        {
            LastUpdatedAt = DateTime.UtcNow,
        };

        var snapshot = ElectionModelFactory.CreateDraftSnapshot(
            updated,
            request.SnapshotReason,
            request.ActorPublicAddress,
            sourceTransactionId: request.SourceTransactionId,
            sourceBlockHeight: request.SourceBlockHeight,
            sourceBlockId: request.SourceBlockId);
        var warningAcknowledgements = CreateWarningAcknowledgementsForRevision(
            updated.ElectionId,
            updated.CurrentDraftRevision,
            request.ActorPublicAddress,
            updated.AcknowledgedWarningCodes,
            sourceTransactionId: request.SourceTransactionId,
            sourceBlockHeight: request.SourceBlockHeight,
            sourceBlockId: request.SourceBlockId);

        await repository.SaveElectionAsync(updated);
        await repository.SaveDraftSnapshotAsync(snapshot);
        foreach (var acknowledgement in warningAcknowledgements)
        {
            await repository.SaveWarningAcknowledgementAsync(acknowledgement);
        }

        await unitOfWork.CommitAsync();

        _logger.LogInformation(
            "[ElectionLifecycleService] Updated draft election {ElectionId} to revision {Revision}",
            updated.ElectionId,
            updated.CurrentDraftRevision);

        return ElectionCommandResult.Success(updated, draftSnapshot: snapshot);
    }

    public async Task<ElectionCommandResult> ImportRosterAsync(ImportElectionRosterRequest request)
    {
        var validationErrors = ElectionEligibilityContracts.ValidateRosterImportEntries(request.RosterEntries);
        if (validationErrors.Count > 0)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Election roster import validation failed.",
                validationErrors);
        }

        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (election.LifecycleState != ElectionLifecycleState.Draft)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Roster import is only allowed while the election remains in draft.");
        }

        if (!string.Equals(election.OwnerPublicAddress, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can import the election roster.");
        }

        var pendingProposal = await repository.GetPendingGovernedProposalAsync(request.ElectionId);
        var governedDraftLock = ValidateDraftNotBlockedByGovernedOpenProposal(pendingProposal);
        if (governedDraftLock is not null)
        {
            return governedDraftLock;
        }

        ElectionRosterEntryRecord[] rosterEntries;
        try
        {
            var existingRosterEntries = await repository.GetRosterEntriesAsync(request.ElectionId);
            var existingOrganizationVoterIds = new HashSet<string>(
                existingRosterEntries.Select(x => x.OrganizationVoterId.Trim()),
                StringComparer.OrdinalIgnoreCase);
            var importedAt = DateTime.UtcNow;
            rosterEntries = request.RosterEntries
                .Where(entry => existingOrganizationVoterIds.Add(entry.OrganizationVoterId.Trim()))
                .Select(entry => ElectionModelFactory.CreateRosterEntry(
                    request.ElectionId,
                    entry.OrganizationVoterId,
                    entry.ContactType,
                    entry.ContactValue,
                    entry.IsInitiallyActive
                        ? ElectionVotingRightStatus.Active
                        : ElectionVotingRightStatus.Inactive,
                    importedAt,
                    request.SourceTransactionId,
                    request.SourceBlockHeight,
                    request.SourceBlockId))
                .ToArray();

            foreach (var rosterEntry in rosterEntries)
            {
                await repository.SaveRosterEntryAsync(rosterEntry);
            }

            var updatedElection = rosterEntries.Length > 0
                ? election with
                {
                    LastUpdatedAt = importedAt,
                }
                : election;

            await repository.SaveElectionAsync(updatedElection);

            election = updatedElection;
        }
        catch (ArgumentException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Election roster import validation failed.",
                [ex.Message]);
        }

        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(
            election,
            rosterEntries: rosterEntries);
    }

    public async Task<ElectionCommandResult> ClaimRosterEntryAsync(ClaimElectionRosterEntryRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (election.LifecycleState != ElectionLifecycleState.Draft &&
            election.LifecycleState != ElectionLifecycleState.Open &&
            election.LifecycleState != ElectionLifecycleState.Closed &&
            election.LifecycleState != ElectionLifecycleState.Finalized)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Identity claim-linking is only allowed while the election is draft, open, closed, or finalized.");
        }

        var rosterEntry = await repository.GetRosterEntryAsync(request.ElectionId, request.OrganizationVoterId);
        if (rosterEntry is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Roster entry {request.OrganizationVoterId} was not found for election {request.ElectionId}.");
        }

        if (!string.Equals(
                request.VerificationCode?.Trim(),
                ElectionEligibilityContracts.TemporaryVerificationCode,
                StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                $"Temporary verification code must currently be {ElectionEligibilityContracts.TemporaryVerificationCode}.");
        }

        if (election.LifecycleState == ElectionLifecycleState.Open && !rosterEntry.WasPresentAtOpen)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "Only voters who were rostered before open can claim this election identity.");
        }

        var actorExistingEntry = await repository.GetRosterEntryByLinkedActorAsync(
            request.ElectionId,
            request.ActorPublicAddress);
        if (actorExistingEntry is not null &&
            !string.Equals(actorExistingEntry.OrganizationVoterId, request.OrganizationVoterId, StringComparison.OrdinalIgnoreCase))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "This Hush account is already linked to a different roster entry for the election.");
        }

        ElectionRosterEntryRecord updatedEntry;
        try
        {
            var linkedAt = DateTime.UtcNow;
            updatedEntry = rosterEntry.LinkToActor(
                request.ActorPublicAddress,
                linkedAt,
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId);

            await repository.UpdateRosterEntryAsync(updatedEntry);
            await repository.SaveElectionAsync(election with
            {
                LastUpdatedAt = linkedAt,
            });
        }
        catch (ArgumentException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                ex.Message);
        }

        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(
            election with
            {
                LastUpdatedAt = updatedEntry.LastUpdatedAt,
            },
            rosterEntry: updatedEntry);
    }

    public async Task<ElectionCommandResult> ActivateRosterEntryAsync(ActivateElectionRosterEntryRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (!string.Equals(election.OwnerPublicAddress, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can activate a rostered voter.");
        }

        if (election.LifecycleState != ElectionLifecycleState.Open)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Late activation is only allowed while the election remains open.");
        }

        var rosterEntry = await repository.GetRosterEntryAsync(request.ElectionId, request.OrganizationVoterId);
        if (rosterEntry is null)
        {
            var blockedResult = await RecordBlockedActivationAsync(
                repository,
                election,
                request.OrganizationVoterId,
                request.ActorPublicAddress,
                ElectionEligibilityActivationBlockReason.RosterEntryNotFound,
                ElectionCommandErrorCode.NotFound,
                $"Roster entry {request.OrganizationVoterId} was not found in the frozen election roster.",
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId);
            await unitOfWork.CommitAsync();
            return blockedResult;
        }

        if (election.EligibilityMutationPolicy != EligibilityMutationPolicy.LateActivationForRosteredVotersOnly)
        {
            var blockedResult = await RecordBlockedActivationAsync(
                repository,
                election,
                rosterEntry.OrganizationVoterId,
                request.ActorPublicAddress,
                ElectionEligibilityActivationBlockReason.PolicyDisallowsLateActivation,
                ElectionCommandErrorCode.InvalidState,
                "This election freezes the active voter set at open and does not allow late activation.",
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId);
            await unitOfWork.CommitAsync();
            return blockedResult;
        }

        if (!rosterEntry.WasPresentAtOpen)
        {
            var blockedResult = await RecordBlockedActivationAsync(
                repository,
                election,
                rosterEntry.OrganizationVoterId,
                request.ActorPublicAddress,
                ElectionEligibilityActivationBlockReason.NotRosteredAtOpen,
                ElectionCommandErrorCode.ValidationFailed,
                "Late activation is only allowed for voters who were already rostered at open.",
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId);
            await unitOfWork.CommitAsync();
            return blockedResult;
        }

        if (!rosterEntry.IsLinked)
        {
            var blockedResult = await RecordBlockedActivationAsync(
                repository,
                election,
                rosterEntry.OrganizationVoterId,
                request.ActorPublicAddress,
                ElectionEligibilityActivationBlockReason.NotLinkedToHushAccount,
                ElectionCommandErrorCode.ValidationFailed,
                "Late activation requires the voter to claim-link a Hush account first.",
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId);
            await unitOfWork.CommitAsync();
            return blockedResult;
        }

        if (rosterEntry.IsActive)
        {
            var blockedResult = await RecordBlockedActivationAsync(
                repository,
                election,
                rosterEntry.OrganizationVoterId,
                request.ActorPublicAddress,
                ElectionEligibilityActivationBlockReason.AlreadyActive,
                ElectionCommandErrorCode.Conflict,
                "Voting rights are already active for this roster entry.",
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId);
            await unitOfWork.CommitAsync();
            return blockedResult;
        }

        var activatedAt = DateTime.UtcNow;
        var updatedEntry = rosterEntry.MarkVotingRightActive(
            request.ActorPublicAddress,
            activatedAt,
            request.SourceTransactionId,
            request.SourceBlockHeight,
            request.SourceBlockId);
        var activationEvent = ElectionModelFactory.CreateEligibilityActivationEvent(
            request.ElectionId,
            rosterEntry.OrganizationVoterId,
            request.ActorPublicAddress,
            ElectionEligibilityActivationOutcome.Activated,
            occurredAt: activatedAt,
            sourceTransactionId: request.SourceTransactionId,
            sourceBlockHeight: request.SourceBlockHeight,
            sourceBlockId: request.SourceBlockId);
        var updatedElection = election with
        {
            LastUpdatedAt = activatedAt,
        };

        await repository.UpdateRosterEntryAsync(updatedEntry);
        await repository.SaveEligibilityActivationEventAsync(activationEvent);
        await repository.SaveElectionAsync(updatedElection);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(
            updatedElection,
            rosterEntry: updatedEntry,
            eligibilityActivationEvent: activationEvent);
    }

    public async Task<ElectionCommitmentRegistrationResult> RegisterVotingCommitmentAsync(
        RegisterElectionVotingCommitmentRequest request)
    {
        if (!ElectionDevModePrivacyGuard.TryValidateCommitmentRegistration(
                request.ElectionId,
                request.ActorPublicAddress,
                request.CommitmentHash,
                out var commitmentValidationError))
        {
            return ElectionCommitmentRegistrationResult.Failure(
                ElectionCommitmentRegistrationFailureReason.ValidationFailed,
                commitmentValidationError);
        }

        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommitmentRegistrationResult.Failure(
                ElectionCommitmentRegistrationFailureReason.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (election.VoteAcceptanceLockedAt.HasValue || election.LifecycleState == ElectionLifecycleState.Closed || election.LifecycleState == ElectionLifecycleState.Finalized)
        {
            return ElectionCommitmentRegistrationResult.Failure(
                ElectionCommitmentRegistrationFailureReason.ClosePersisted,
                "Voting commitment registration is closed because the election close boundary is already persisted.");
        }

        if (election.LifecycleState != ElectionLifecycleState.Open)
        {
            return ElectionCommitmentRegistrationResult.Failure(
                ElectionCommitmentRegistrationFailureReason.ElectionNotOpenableForRegistration,
                "Voting commitment registration is only available while the election is open.");
        }

        var rosterEntry = await repository.GetRosterEntryByLinkedActorAsync(request.ElectionId, request.ActorPublicAddress);
        if (rosterEntry is null)
        {
            return ElectionCommitmentRegistrationResult.Failure(
                ElectionCommitmentRegistrationFailureReason.NotLinked,
                "The authenticated Hush account is not linked to a roster entry for this election.");
        }

        if (!rosterEntry.WasPresentAtOpen)
        {
            return ElectionCommitmentRegistrationResult.Failure(
                ElectionCommitmentRegistrationFailureReason.ElectionNotOpenableForRegistration,
                "Only voters who were already rostered at open can register a voting commitment.");
        }

        if (!IsRosterEntryEligibleForCommitmentRegistration(election, rosterEntry))
        {
            return ElectionCommitmentRegistrationResult.Failure(
                ElectionCommitmentRegistrationFailureReason.NotActive,
                "This voter does not currently hold an active voting right for commitment registration.");
        }

        var existingRegistration = await repository.GetCommitmentRegistrationAsync(
            request.ElectionId,
            rosterEntry.OrganizationVoterId);
        if (existingRegistration is not null)
        {
            return ElectionCommitmentRegistrationResult.Failure(
                ElectionCommitmentRegistrationFailureReason.AlreadyRegistered,
                "A voting commitment is already registered for this voter in this election.");
        }

        var registeredAt = DateTime.UtcNow;

        try
        {
            var commitmentRegistration = ElectionModelFactory.CreateCommitmentRegistrationRecord(
                request.ElectionId,
                rosterEntry.OrganizationVoterId,
                request.ActorPublicAddress,
                request.CommitmentHash,
                registeredAt);
            var updatedElection = election with
            {
                LastUpdatedAt = registeredAt,
            };

            await repository.SaveCommitmentRegistrationAsync(commitmentRegistration);
            await repository.SaveElectionAsync(updatedElection);
            await unitOfWork.CommitAsync();

            return ElectionCommitmentRegistrationResult.Success(
                updatedElection,
                rosterEntry,
                commitmentRegistration);
        }
        catch (ArgumentException ex)
        {
            return ElectionCommitmentRegistrationResult.Failure(
                ElectionCommitmentRegistrationFailureReason.ValidationFailed,
                ex.Message);
        }
    }

    public async Task<ElectionCastAcceptanceResult> AcceptBallotCastAsync(AcceptElectionBallotCastRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return ElectionCastAcceptanceResult.Failure(
                ElectionCastAcceptanceFailureReason.ValidationFailed,
                "A non-empty idempotency key is required.");
        }

        if (request.EligibleSetHash is null || request.EligibleSetHash.Length == 0)
        {
            return ElectionCastAcceptanceResult.Failure(
                ElectionCastAcceptanceFailureReason.ValidationFailed,
                "A non-empty eligible-set hash is required.");
        }

        if (!ElectionDevModePrivacyGuard.TryValidateAcceptedBallotArtifacts(
                request.ElectionId,
                request.ActorPublicAddress,
                request.EncryptedBallotPackage,
                request.ProofBundle,
                request.BallotNullifier,
                out var castPrivacyValidationError))
        {
            return ElectionCastAcceptanceResult.Failure(
                ElectionCastAcceptanceFailureReason.ValidationFailed,
                castPrivacyValidationError);
        }

        var idempotencyKeyHash = ComputeScopedHash(request.IdempotencyKey);
        var pendingKey = BuildPendingCastTrackingKey(request.ElectionId, idempotencyKeyHash);
        if (!_pendingCastTracking.TryAdd(pendingKey, true))
        {
            return ElectionCastAcceptanceResult.Failure(
                ElectionCastAcceptanceFailureReason.StillProcessing,
                "This election-scoped submission key is already pending in the mempool.");
        }

        try
        {
            using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
            var repository = unitOfWork.GetRepository<IElectionsRepository>();
            var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

            if (election is null)
            {
                return ElectionCastAcceptanceResult.Failure(
                    ElectionCastAcceptanceFailureReason.NotFound,
                    $"Election {request.ElectionId} was not found.");
            }

            if (election.VoteAcceptanceLockedAt.HasValue ||
                election.LifecycleState == ElectionLifecycleState.Closed ||
                election.LifecycleState == ElectionLifecycleState.Finalized)
            {
                return ElectionCastAcceptanceResult.Failure(
                    ElectionCastAcceptanceFailureReason.ClosePersisted,
                    "Vote acceptance is closed because the persisted close boundary has already been reached.");
            }

            if (election.LifecycleState != ElectionLifecycleState.Open)
            {
                return ElectionCastAcceptanceResult.Failure(
                    ElectionCastAcceptanceFailureReason.WrongElectionContext,
                    "Votes can only be accepted while the election is open.");
            }

            var existingIdempotency = await repository.GetCastIdempotencyRecordAsync(request.ElectionId, idempotencyKeyHash);
            if (existingIdempotency is not null)
            {
                return ElectionCastAcceptanceResult.Failure(
                    ElectionCastAcceptanceFailureReason.AlreadyUsed,
                    "This election-scoped submission key has already been used.");
            }

            var rosterEntry = await repository.GetRosterEntryByLinkedActorAsync(request.ElectionId, request.ActorPublicAddress);
            if (rosterEntry is null)
            {
                return ElectionCastAcceptanceResult.Failure(
                    ElectionCastAcceptanceFailureReason.NotLinked,
                    "The authenticated Hush account is not linked to a roster entry for this election.");
            }

            if (!IsRosterEntryEligibleForCommitmentRegistration(election, rosterEntry))
            {
                return ElectionCastAcceptanceResult.Failure(
                    ElectionCastAcceptanceFailureReason.NotActive,
                    "This voter does not currently hold an active voting right for cast acceptance.");
            }

            var commitmentRegistration = await repository.GetCommitmentRegistrationAsync(
                request.ElectionId,
                rosterEntry.OrganizationVoterId);
            if (commitmentRegistration is null ||
                !string.Equals(commitmentRegistration.LinkedActorPublicAddress, request.ActorPublicAddress, StringComparison.Ordinal))
            {
                return ElectionCastAcceptanceResult.Failure(
                    ElectionCastAcceptanceFailureReason.CommitmentMissing,
                    "A voting commitment must be registered before the final cast can be accepted.");
            }

            var checkoffConsumption = await repository.GetCheckoffConsumptionAsync(
                request.ElectionId,
                rosterEntry.OrganizationVoterId);
            if (checkoffConsumption is not null)
            {
                return ElectionCastAcceptanceResult.Failure(
                    ElectionCastAcceptanceFailureReason.AlreadyVoted,
                    "This voter has already consumed the voting right for this election.");
            }

            var existingParticipation = await repository.GetParticipationRecordAsync(
                request.ElectionId,
                rosterEntry.OrganizationVoterId);
            if (existingParticipation?.CountsAsParticipation == true)
            {
                return ElectionCastAcceptanceResult.Failure(
                    ElectionCastAcceptanceFailureReason.AlreadyVoted,
                    "This voter is already counted as voted for this election.");
            }

            var acceptedBallotWithSameNullifier = await repository.GetAcceptedBallotByNullifierAsync(
                request.ElectionId,
                request.BallotNullifier);
            if (acceptedBallotWithSameNullifier is not null)
            {
                return ElectionCastAcceptanceResult.Failure(
                    ElectionCastAcceptanceFailureReason.DuplicateNullifier,
                    "This ballot nullifier has already been accepted for the election.");
            }

            var boundaryValidation = await ValidateCastBoundaryContextAsync(repository, election, request);
            if (boundaryValidation is not null)
            {
                return boundaryValidation;
            }

            var acceptedAt = DateTime.UtcNow;
            var protectedAcceptedAt = CreateProtectedAcceptedBallotTimestamp(election);

            try
            {
                var updatedElection = election with
                {
                    LastUpdatedAt = acceptedAt,
                };
                var participationRecord = existingParticipation is null
                    ? ElectionModelFactory.CreateParticipationRecord(
                        request.ElectionId,
                        rosterEntry.OrganizationVoterId,
                        ElectionParticipationStatus.CountedAsVoted,
                        acceptedAt)
                    : existingParticipation.UpdateStatus(
                        ElectionParticipationStatus.CountedAsVoted,
                        acceptedAt);
                var newCheckoffConsumption = ElectionModelFactory.CreateCheckoffConsumptionRecord(
                    request.ElectionId,
                    rosterEntry.OrganizationVoterId,
                    acceptedAt);
                var acceptedBallot = ElectionModelFactory.CreateAcceptedBallotRecord(
                    request.ElectionId,
                    request.EncryptedBallotPackage,
                    request.ProofBundle,
                    request.BallotNullifier,
                    protectedAcceptedAt);
                var ballotMemPoolEntry = ElectionModelFactory.CreateBallotMemPoolEntry(
                    request.ElectionId,
                    acceptedBallot.Id,
                    protectedAcceptedAt);
                var idempotencyRecord = ElectionModelFactory.CreateCastIdempotencyRecord(
                    request.ElectionId,
                    idempotencyKeyHash,
                    acceptedAt);

                await repository.SaveParticipationRecordAsync(participationRecord);
                await repository.SaveCheckoffConsumptionAsync(newCheckoffConsumption);
                await repository.SaveAcceptedBallotAsync(acceptedBallot);
                await repository.SaveBallotMemPoolEntryAsync(ballotMemPoolEntry);
                await repository.SaveCastIdempotencyRecordAsync(idempotencyRecord);
                await repository.SaveElectionAsync(updatedElection);
                await unitOfWork.CommitAsync();
                if (_castIdempotencyCacheService is not null)
                {
                    await _castIdempotencyCacheService.SetAsync(
                        request.ElectionId.ToString(),
                        idempotencyKeyHash);
                }

                return ElectionCastAcceptanceResult.Success(
                    updatedElection,
                    rosterEntry,
                    commitmentRegistration,
                    participationRecord,
                    newCheckoffConsumption,
                    acceptedBallot,
                    idempotencyRecord);
            }
            catch (ArgumentException ex)
            {
                return ElectionCastAcceptanceResult.Failure(
                    ElectionCastAcceptanceFailureReason.ValidationFailed,
                    ex.Message);
            }
        }
        finally
        {
            _pendingCastTracking.TryRemove(pendingKey, out _);
        }
    }

    public async Task<ElectionCommandResult> InviteTrusteeAsync(InviteElectionTrusteeRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        var phaseResult = ValidateDraftOwnershipAndTrusteeMode(election, request.ActorPublicAddress);
        if (phaseResult is not null)
        {
            return phaseResult;
        }

        var pendingProposal = await repository.GetPendingGovernedProposalAsync(request.ElectionId);
        var governedDraftLock = ValidateDraftNotBlockedByGovernedOpenProposal(pendingProposal);
        if (governedDraftLock is not null)
        {
            return governedDraftLock;
        }

        var invitations = await repository.GetTrusteeInvitationsAsync(request.ElectionId);
        var existing = invitations.FirstOrDefault(x =>
            string.Equals(x.TrusteeUserAddress, request.TrusteeUserAddress, StringComparison.OrdinalIgnoreCase) &&
            (x.Status == ElectionTrusteeInvitationStatus.Pending || x.Status == ElectionTrusteeInvitationStatus.Accepted));

        if (existing is not null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                $"Trustee {request.TrusteeUserAddress} already has an active invitation state.");
        }

        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            request.ElectionId,
            request.TrusteeUserAddress,
            request.TrusteeDisplayName,
            request.ActorPublicAddress,
            election.CurrentDraftRevision,
            invitationId: request.PreassignedInvitationId,
            latestTransactionId: request.SourceTransactionId,
            latestBlockHeight: request.SourceBlockHeight,
            latestBlockId: request.SourceBlockId);

        await repository.SaveTrusteeInvitationAsync(invitation);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(election, trusteeInvitation: invitation);
    }

    public async Task<ElectionCommandResult> CreateReportAccessGrantAsync(CreateElectionReportAccessGrantRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (!string.Equals(election.OwnerPublicAddress, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the election owner can manage designated-auditor grants.");
        }

        var designatedAuditorPublicAddress = request.DesignatedAuditorPublicAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(designatedAuditorPublicAddress))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "A designated-auditor public address is required.");
        }

        if (string.Equals(
                election.OwnerPublicAddress,
                designatedAuditorPublicAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "The election owner cannot also be added as a designated auditor.");
        }

        var invitations = await repository.GetTrusteeInvitationsAsync(request.ElectionId);
        var acceptedTrustee = invitations.Any(x =>
            x.Status == ElectionTrusteeInvitationStatus.Accepted &&
            string.Equals(x.TrusteeUserAddress, designatedAuditorPublicAddress, StringComparison.OrdinalIgnoreCase));
        if (acceptedTrustee)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "Accepted trustees cannot also be designated auditors for the same election.");
        }

        var existingGrant = await repository.GetReportAccessGrantAsync(request.ElectionId, designatedAuditorPublicAddress);
        if (existingGrant is not null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "This Hush account is already a designated auditor for the election.");
        }

        try
        {
            var grantedAt = DateTime.UtcNow;
            var accessGrant = ElectionModelFactory.CreateReportAccessGrant(
                request.ElectionId,
                designatedAuditorPublicAddress,
                request.ActorPublicAddress,
                grantedAt: grantedAt);
            var updatedElection = election with
            {
                LastUpdatedAt = grantedAt,
            };

            await repository.SaveReportAccessGrantAsync(accessGrant);
            await repository.SaveElectionAsync(updatedElection);
            await unitOfWork.CommitAsync();

            return ElectionCommandResult.Success(updatedElection, reportAccessGrant: accessGrant);
        }
        catch (ArgumentException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                ex.Message);
        }
    }

    public Task<ElectionCommandResult> AcceptTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request) =>
        ResolveTrusteeInvitationAsync(
            request,
            ownerOnly: false,
            transition: (invitation, draftRevision, lifecycleState) =>
                invitation.Accept(
                    DateTime.UtcNow,
                    draftRevision,
                    lifecycleState,
                    latestTransactionId: request.SourceTransactionId,
                    latestBlockHeight: request.SourceBlockHeight,
                    latestBlockId: request.SourceBlockId));

    public Task<ElectionCommandResult> RejectTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request) =>
        ResolveTrusteeInvitationAsync(
            request,
            ownerOnly: false,
            transition: (invitation, draftRevision, lifecycleState) =>
                invitation.Reject(
                    DateTime.UtcNow,
                    draftRevision,
                    lifecycleState,
                    latestTransactionId: request.SourceTransactionId,
                    latestBlockHeight: request.SourceBlockHeight,
                    latestBlockId: request.SourceBlockId));

    public Task<ElectionCommandResult> RevokeTrusteeInvitationAsync(ResolveElectionTrusteeInvitationRequest request) =>
        ResolveTrusteeInvitationAsync(
            request,
            ownerOnly: true,
            transition: (invitation, draftRevision, lifecycleState) =>
                invitation.Revoke(
                    DateTime.UtcNow,
                    draftRevision,
                    lifecycleState,
                    latestTransactionId: request.SourceTransactionId,
                    latestBlockHeight: request.SourceBlockHeight,
                    latestBlockId: request.SourceBlockId));

    public async Task<ElectionCommandResult> StartElectionCeremonyAsync(StartElectionCeremonyRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var outcome = await StartOrRestartElectionCeremonyInternalAsync(
            repository,
            request.ElectionId,
            request.ActorPublicAddress,
            request.ProfileId,
            restartReason: null);

        if (outcome.IsSuccess)
        {
            await unitOfWork.CommitAsync();
        }

        return outcome;
    }

    public async Task<ElectionCommandResult> RestartElectionCeremonyAsync(RestartElectionCeremonyRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var outcome = await StartOrRestartElectionCeremonyInternalAsync(
            repository,
            request.ElectionId,
            request.ActorPublicAddress,
            request.ProfileId,
            request.RestartReason);

        if (outcome.IsSuccess)
        {
            await unitOfWork.CommitAsync();
        }

        return outcome;
    }

    public async Task<ElectionCommandResult> PublishElectionCeremonyTransportKeyAsync(PublishElectionCeremonyTransportKeyRequest request)
    {
        return await ExecuteCeremonyTrusteeUpdateAsync(
            request.ElectionId,
            request.CeremonyVersionId,
            request.ActorPublicAddress,
            activeVersion =>
            {
                var occurredAt = DateTime.UtcNow;
                var updatedState = activeVersion.TrusteeState!.PublishTransportKey(
                    request.TransportPublicKeyFingerprint,
                    occurredAt);
                var transcriptEvent = ElectionModelFactory.CreateCeremonyTranscriptEvent(
                    activeVersion.Election.ElectionId,
                    activeVersion.Version.Id,
                    activeVersion.Version.VersionNumber,
                    ElectionCeremonyTranscriptEventType.TrusteeTransportKeyPublished,
                    $"{updatedState.TrusteeUserAddress} published a ceremony transport key.",
                    occurredAt: occurredAt,
                    actorPublicAddress: request.ActorPublicAddress,
                    trusteeUserAddress: updatedState.TrusteeUserAddress,
                    trusteeDisplayName: updatedState.TrusteeDisplayName,
                    trusteeState: updatedState.State,
                    evidenceReference: updatedState.TransportPublicKeyFingerprint);

                return Task.FromResult(new CeremonyTrusteeUpdateOutcome(
                    updatedState,
                    transcriptEvent));
            },
            requireOwnerActor: false);
    }

    public async Task<ElectionCommandResult> JoinElectionCeremonyAsync(JoinElectionCeremonyRequest request)
    {
        return await ExecuteCeremonyTrusteeUpdateAsync(
            request.ElectionId,
            request.CeremonyVersionId,
            request.ActorPublicAddress,
            activeVersion =>
            {
                var occurredAt = DateTime.UtcNow;
                var updatedState = activeVersion.TrusteeState!.MarkJoined(occurredAt);
                var transcriptEvent = ElectionModelFactory.CreateCeremonyTranscriptEvent(
                    activeVersion.Election.ElectionId,
                    activeVersion.Version.Id,
                    activeVersion.Version.VersionNumber,
                    ElectionCeremonyTranscriptEventType.TrusteeJoined,
                    $"{updatedState.TrusteeUserAddress} joined ceremony version {activeVersion.Version.VersionNumber}.",
                    occurredAt: occurredAt,
                    actorPublicAddress: request.ActorPublicAddress,
                    trusteeUserAddress: updatedState.TrusteeUserAddress,
                    trusteeDisplayName: updatedState.TrusteeDisplayName,
                    trusteeState: updatedState.State);

                return Task.FromResult(new CeremonyTrusteeUpdateOutcome(
                    updatedState,
                    transcriptEvent));
            },
            requireOwnerActor: false);
    }

    public async Task<ElectionCommandResult> RecordElectionCeremonySelfTestSuccessAsync(RecordElectionCeremonySelfTestRequest request)
    {
        return await ExecuteCeremonyTrusteeUpdateAsync(
            request.ElectionId,
            request.CeremonyVersionId,
            request.ActorPublicAddress,
            activeVersion =>
            {
                var occurredAt = DateTime.UtcNow;
                var updatedState = activeVersion.TrusteeState!.RecordSelfTestSuccess(occurredAt);
                var transcriptEvent = ElectionModelFactory.CreateCeremonyTranscriptEvent(
                    activeVersion.Election.ElectionId,
                    activeVersion.Version.Id,
                    activeVersion.Version.VersionNumber,
                    ElectionCeremonyTranscriptEventType.TrusteeSelfTestSucceeded,
                    $"{updatedState.TrusteeUserAddress} completed the mandatory ceremony self-test.",
                    occurredAt: occurredAt,
                    actorPublicAddress: request.ActorPublicAddress,
                    trusteeUserAddress: updatedState.TrusteeUserAddress,
                    trusteeDisplayName: updatedState.TrusteeDisplayName,
                    trusteeState: updatedState.State);

                return Task.FromResult(new CeremonyTrusteeUpdateOutcome(
                    updatedState,
                    transcriptEvent));
            },
            requireOwnerActor: false);
    }

    public async Task<ElectionCommandResult> SubmitElectionCeremonyMaterialAsync(SubmitElectionCeremonyMaterialRequest request)
    {
        if (request.EncryptedPayload is null || request.EncryptedPayload.Length == 0)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Encrypted ceremony payload is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ShareVersion))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Share version is required.");
        }

        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var activeVersion = await LoadActiveCeremonyTrusteeContextAsync(
            repository,
            request.ElectionId,
            request.CeremonyVersionId,
            request.ActorPublicAddress);
        if (!activeVersion.IsSuccess)
        {
            return activeVersion.ErrorResult!;
        }

        var ceremonyContext = activeVersion.Context!;

        if (request.RecipientTrusteeUserAddress is not null &&
            !ceremonyContext.Version.BoundTrustees.Any(x =>
                string.Equals(x.TrusteeUserAddress, request.RecipientTrusteeUserAddress, StringComparison.OrdinalIgnoreCase)))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Recipient trustee is not bound to the active ceremony version.");
        }

        ElectionCeremonyTrusteeStateRecord updatedState;
        ElectionCeremonyMessageEnvelopeRecord messageEnvelope;
        ElectionCeremonyTranscriptEventRecord transcriptEvent;
        try
        {
            var submittedAt = DateTime.UtcNow;
            updatedState = ceremonyContext.TrusteeState!.RecordMaterialSubmitted(submittedAt, request.ShareVersion);
            messageEnvelope = ElectionModelFactory.CreateCeremonyMessageEnvelope(
                request.ElectionId,
                request.CeremonyVersionId,
                ceremonyContext.Version.VersionNumber,
                ceremonyContext.Version.ProfileId,
                request.ActorPublicAddress,
                request.RecipientTrusteeUserAddress,
                request.MessageType,
                request.PayloadVersion,
                request.EncryptedPayload,
                request.PayloadFingerprint,
                submittedAt);
            transcriptEvent = ElectionModelFactory.CreateCeremonyTranscriptEvent(
                request.ElectionId,
                request.CeremonyVersionId,
                ceremonyContext.Version.VersionNumber,
                ElectionCeremonyTranscriptEventType.TrusteeMaterialSubmitted,
                $"{updatedState.TrusteeUserAddress} submitted ceremony material of type {request.MessageType}.",
                occurredAt: submittedAt,
                actorPublicAddress: request.ActorPublicAddress,
                trusteeUserAddress: updatedState.TrusteeUserAddress,
                trusteeDisplayName: updatedState.TrusteeDisplayName,
                trusteeState: updatedState.State,
                evidenceReference: request.PayloadFingerprint);
        }
        catch (ArgumentException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                ex.Message);
        }

        await repository.UpdateCeremonyTrusteeStateAsync(updatedState);
        await repository.SaveCeremonyMessageEnvelopeAsync(messageEnvelope);
        await repository.SaveCeremonyTranscriptEventAsync(transcriptEvent);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(
            ceremonyContext.Election,
            ceremonyVersion: ceremonyContext.Version,
            ceremonyTranscriptEvents: [transcriptEvent],
            ceremonyTrusteeState: updatedState,
            ceremonyMessageEnvelope: messageEnvelope);
    }

    public async Task<ElectionCommandResult> RecordElectionCeremonyValidationFailureAsync(RecordElectionCeremonyValidationFailureRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        var phaseResult = ValidateDraftOwnershipAndTrusteeMode(election, request.ActorPublicAddress);
        if (phaseResult is not null)
        {
            return phaseResult;
        }

        var version = await repository.GetCeremonyVersionAsync(request.CeremonyVersionId);
        if (version is null || version.ElectionId != request.ElectionId || !version.IsActive)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Ceremony version {request.CeremonyVersionId} was not found for election {request.ElectionId}.");
        }

        var trusteeState = await repository.GetCeremonyTrusteeStateAsync(request.CeremonyVersionId, request.TrusteeUserAddress);
        if (trusteeState is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Trustee {request.TrusteeUserAddress} is not bound to ceremony version {request.CeremonyVersionId}.");
        }

        ElectionCeremonyTrusteeStateRecord updatedState;
        ElectionCeremonyTranscriptEventRecord transcriptEvent;
        try
        {
            var failedAt = DateTime.UtcNow;
            updatedState = trusteeState.RecordValidationFailure(request.ValidationFailureReason, failedAt);
            transcriptEvent = ElectionModelFactory.CreateCeremonyTranscriptEvent(
                request.ElectionId,
                request.CeremonyVersionId,
                version.VersionNumber,
                ElectionCeremonyTranscriptEventType.TrusteeValidationFailed,
                $"{updatedState.TrusteeUserAddress} failed deterministic ceremony validation.",
                occurredAt: failedAt,
                actorPublicAddress: request.ActorPublicAddress,
                trusteeUserAddress: updatedState.TrusteeUserAddress,
                trusteeDisplayName: updatedState.TrusteeDisplayName,
                trusteeState: updatedState.State,
                evidenceReference: request.EvidenceReference);
        }
        catch (ArgumentException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                ex.Message);
        }

        await repository.UpdateCeremonyTrusteeStateAsync(updatedState);
        await repository.SaveCeremonyTranscriptEventAsync(transcriptEvent);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(
            election,
            ceremonyVersion: version,
            ceremonyTranscriptEvents: [transcriptEvent],
            ceremonyTrusteeState: updatedState);
    }

    public async Task<ElectionCommandResult> CompleteElectionCeremonyTrusteeAsync(CompleteElectionCeremonyTrusteeRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        var phaseResult = ValidateDraftOwnershipAndTrusteeMode(election, request.ActorPublicAddress);
        if (phaseResult is not null)
        {
            return phaseResult;
        }

        var version = await repository.GetCeremonyVersionAsync(request.CeremonyVersionId);
        if (version is null || version.ElectionId != request.ElectionId || !version.IsActive)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Ceremony version {request.CeremonyVersionId} was not found for election {request.ElectionId}.");
        }

        var trusteeState = await repository.GetCeremonyTrusteeStateAsync(request.CeremonyVersionId, request.TrusteeUserAddress);
        if (trusteeState is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Trustee {request.TrusteeUserAddress} is not bound to ceremony version {request.CeremonyVersionId}.");
        }

        ElectionCeremonyTrusteeStateRecord updatedState;
        List<ElectionCeremonyTranscriptEventRecord> updatedEvents;
        DateTime completedAt;
        try
        {
            completedAt = DateTime.UtcNow;
            updatedState = trusteeState.MarkCompleted(completedAt, request.ShareVersion);
            updatedEvents =
            [
                ElectionModelFactory.CreateCeremonyTranscriptEvent(
                    request.ElectionId,
                    request.CeremonyVersionId,
                    version.VersionNumber,
                    ElectionCeremonyTranscriptEventType.TrusteeCompleted,
                    $"{updatedState.TrusteeUserAddress} completed ceremony version {version.VersionNumber}.",
                    occurredAt: completedAt,
                    actorPublicAddress: request.ActorPublicAddress,
                    trusteeUserAddress: updatedState.TrusteeUserAddress,
                    trusteeDisplayName: updatedState.TrusteeDisplayName,
                    trusteeState: updatedState.State),
            ];
        }
        catch (ArgumentException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                ex.Message);
        }

        await repository.UpdateCeremonyTrusteeStateAsync(updatedState);

        var shareCustody = await repository.GetCeremonyShareCustodyRecordAsync(request.CeremonyVersionId, request.TrusteeUserAddress);
        shareCustody ??= ElectionModelFactory.CreateCeremonyShareCustodyRecord(
            request.ElectionId,
            request.CeremonyVersionId,
            request.TrusteeUserAddress,
            request.ShareVersion);
        if (shareCustody.ShareVersion != request.ShareVersion)
        {
            shareCustody = shareCustody with
            {
                ShareVersion = request.ShareVersion.Trim(),
                LastUpdatedAt = completedAt,
            };
        }

        if (await repository.GetCeremonyShareCustodyRecordAsync(request.CeremonyVersionId, request.TrusteeUserAddress) is null)
        {
            await repository.SaveCeremonyShareCustodyRecordAsync(shareCustody);
        }
        else
        {
            await repository.UpdateCeremonyShareCustodyRecordAsync(shareCustody);
        }

        var trusteeStates = await repository.GetCeremonyTrusteeStatesAsync(request.CeremonyVersionId);
        var completedTrustees = CountCompletedTrustees(trusteeStates, updatedState.TrusteeUserAddress);
        var updatedVersion = version;
        if (updatedVersion.Status == ElectionCeremonyVersionStatus.InProgress &&
            completedTrustees >= updatedVersion.RequiredApprovalCount)
        {
            if (string.IsNullOrWhiteSpace(request.TallyPublicKeyFingerprint))
            {
                return ElectionCommandResult.Failure(
                    ElectionCommandErrorCode.ValidationFailed,
                    "Tally public key fingerprint is required when the ceremony reaches readiness.");
            }

            updatedVersion = updatedVersion.MarkReady(completedAt, request.TallyPublicKeyFingerprint);
            await repository.UpdateCeremonyVersionAsync(updatedVersion);
            updatedEvents.Add(ElectionModelFactory.CreateCeremonyTranscriptEvent(
                request.ElectionId,
                request.CeremonyVersionId,
                updatedVersion.VersionNumber,
                ElectionCeremonyTranscriptEventType.VersionReady,
                $"Ceremony version {updatedVersion.VersionNumber} reached the required trustee threshold.",
                occurredAt: completedAt,
                actorPublicAddress: request.ActorPublicAddress,
                tallyPublicKeyFingerprint: updatedVersion.TallyPublicKeyFingerprint));
        }

        foreach (var transcriptEvent in updatedEvents)
        {
            await repository.SaveCeremonyTranscriptEventAsync(transcriptEvent);
        }

        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(
            election,
            ceremonyVersion: updatedVersion,
            ceremonyTranscriptEvents: updatedEvents,
            ceremonyTrusteeState: updatedState,
            ceremonyShareCustody: shareCustody);
    }

    public async Task<ElectionCommandResult> RecordElectionCeremonyShareExportAsync(RecordElectionCeremonyShareExportRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var loadOutcome = await LoadShareCustodyContextAsync(
            repository,
            request.ElectionId,
            request.CeremonyVersionId,
            request.ActorPublicAddress,
            request.ActorPublicAddress,
            request.ShareVersion,
            requireOwnerActor: false);
        if (!loadOutcome.IsSuccess)
        {
            return loadOutcome.ErrorResult!;
        }

        var updatedCustody = loadOutcome.CustodyRecord!.RecordExport(DateTime.UtcNow);
        await repository.UpdateCeremonyShareCustodyRecordAsync(updatedCustody);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(
            loadOutcome.Election!,
            ceremonyVersion: loadOutcome.Version,
            ceremonyShareCustody: updatedCustody);
    }

    public async Task<ElectionCommandResult> RecordElectionCeremonyShareImportAsync(RecordElectionCeremonyShareImportRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var loadOutcome = await LoadShareCustodyContextAsync(
            repository,
            request.ElectionId,
            request.CeremonyVersionId,
            request.ActorPublicAddress,
            request.ActorPublicAddress,
            shareVersion: null,
            requireOwnerActor: false);
        if (!loadOutcome.IsSuccess)
        {
            return loadOutcome.ErrorResult!;
        }

        var importedAt = DateTime.UtcNow;
        var updatedCustody = loadOutcome.CustodyRecord!.MatchesImportBinding(
                request.ImportedElectionId,
                request.ImportedCeremonyVersionId,
                request.ImportedTrusteeUserAddress,
                request.ImportedShareVersion)
            ? loadOutcome.CustodyRecord.RecordImportSuccess(importedAt)
            : loadOutcome.CustodyRecord.RecordImportFailure(
                "Imported share package does not match the exact election/version/trustee/share binding.",
                importedAt);

        await repository.UpdateCeremonyShareCustodyRecordAsync(updatedCustody);
        await unitOfWork.CommitAsync();

        return updatedCustody.Status == ElectionCeremonyShareCustodyStatus.Imported
            ? ElectionCommandResult.Success(
                loadOutcome.Election!,
                ceremonyVersion: loadOutcome.Version,
                ceremonyShareCustody: updatedCustody)
            : ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Imported share package does not match the exact ceremony binding.");
    }

    public async Task<ElectionOpenValidationResult> EvaluateOpenReadinessAsync(EvaluateElectionOpenReadinessRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionOpenValidationResult.NotReady(
                [$"Election {request.ElectionId} was not found."],
                Array.Empty<ElectionWarningCode>(),
                Array.Empty<ElectionWarningCode>());
        }

        var invitations = await repository.GetTrusteeInvitationsAsync(request.ElectionId);
        var rosterEntries = await repository.GetRosterEntriesAsync(request.ElectionId);
        var warningAcknowledgements = await repository.GetWarningAcknowledgementsAsync(request.ElectionId);
        var activeCeremonyVersion = await repository.GetActiveCeremonyVersionAsync(request.ElectionId);
        var activeCeremonyTrusteeStates = activeCeremonyVersion is null
            ? Array.Empty<ElectionCeremonyTrusteeStateRecord>()
            : await repository.GetCeremonyTrusteeStatesAsync(activeCeremonyVersion.Id);

        return EvaluateOpenReadiness(
            election,
            invitations,
            rosterEntries,
            activeCeremonyVersion,
            activeCeremonyTrusteeStates,
            warningAcknowledgements,
            request.RequiredWarningCodes,
            blockGovernedWorkflowMissing: false);
    }

    public async Task<ElectionCommandResult> StartGovernedProposalAsync(StartElectionGovernedProposalRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (!string.Equals(election.OwnerPublicAddress, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can start a governed election proposal.");
        }

        if (election.GovernanceMode != ElectionGovernanceMode.TrusteeThreshold)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotSupported,
                "Governed proposals are only supported for trustee-threshold elections.");
        }

        var pendingProposal = await repository.GetPendingGovernedProposalAsync(request.ElectionId);
        if (pendingProposal is not null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "Only one pending governed proposal may exist per election.");
        }

        if (request.PreassignedProposalId.HasValue)
        {
            var existingProposal = await repository.GetGovernedProposalAsync(request.PreassignedProposalId.Value);
            if (existingProposal is not null)
            {
                return ElectionCommandResult.Failure(
                    ElectionCommandErrorCode.Conflict,
                    $"Governed proposal {request.PreassignedProposalId.Value} already exists.");
            }
        }

        var validationResult = await ValidateGovernedProposalStartAsync(repository, election, request.ActionType);
        if (validationResult is not null)
        {
            return validationResult;
        }

        var proposalCreatedAt = DateTime.UtcNow;
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            election,
            request.ActionType,
            request.ActorPublicAddress,
            preassignedProposalId: request.PreassignedProposalId,
            createdAt: proposalCreatedAt,
            latestTransactionId: request.SourceTransactionId,
            latestBlockHeight: request.SourceBlockHeight,
            latestBlockId: request.SourceBlockId);
        var updatedElection = ApplyGovernedProposalStartEffects(election, request.ActionType, proposalCreatedAt);

        await repository.SaveGovernedProposalAsync(proposal);
        await repository.SaveElectionAsync(updatedElection);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(updatedElection, governedProposal: proposal);
    }

    public async Task<ElectionCommandResult> ApproveGovernedProposalAsync(ApproveElectionGovernedProposalRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (election.GovernanceMode != ElectionGovernanceMode.TrusteeThreshold)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotSupported,
                "Governed approvals are only supported for trustee-threshold elections.");
        }

        var proposal = await repository.GetGovernedProposalAsync(request.ProposalId);
        if (proposal is null || proposal.ElectionId != request.ElectionId)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Governed proposal {request.ProposalId} was not found for election {request.ElectionId}.");
        }

        if (proposal.ExecutionStatus != ElectionGovernedProposalExecutionStatus.WaitingForApprovals)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "This governed proposal is no longer accepting approvals.");
        }

        var governanceTrustees = await ResolveGovernanceApproverRosterAsync(repository, election, proposal);
        if (!governanceTrustees.Any(x =>
                string.Equals(x.TrusteeUserAddress, request.ActorPublicAddress, StringComparison.Ordinal)))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only active trustees for this governed proposal can approve it.");
        }

        var existingApproval = await repository.GetGovernedProposalApprovalAsync(
            proposal.Id,
            request.ActorPublicAddress);
        if (existingApproval is not null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "Trustee approval is already recorded and immutable.");
        }

        var approval = ElectionModelFactory.CreateGovernedProposalApproval(
            proposal,
            request.ActorPublicAddress,
            governanceTrustees
                .First(x => string.Equals(x.TrusteeUserAddress, request.ActorPublicAddress, StringComparison.Ordinal))
                .TrusteeDisplayName,
            request.ApprovalNote,
            sourceTransactionId: request.SourceTransactionId,
            sourceBlockHeight: request.SourceBlockHeight,
            sourceBlockId: request.SourceBlockId);
        var currentApprovals = await repository.GetGovernedProposalApprovalsAsync(proposal.Id);
        var nextApprovalCount = currentApprovals.Count + 1;

        await repository.SaveGovernedProposalApprovalAsync(approval);

        if (election.RequiredApprovalCount.HasValue && nextApprovalCount >= election.RequiredApprovalCount.Value)
        {
            var executionOutcome = await ExecuteGovernedProposalAndPersistOutcomeAsync(
                repository,
                election,
                proposal,
                request.ActorPublicAddress,
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId);

            await unitOfWork.CommitAsync();

            return ElectionCommandResult.Success(
                executionOutcome.Election,
                boundaryArtifact: executionOutcome.BoundaryArtifact,
                eligibilitySnapshot: executionOutcome.EligibilitySnapshot,
                governedProposal: executionOutcome.Proposal,
                governedProposalApproval: approval,
                finalizationSession: executionOutcome.FinalizationSession,
                finalizationShare: executionOutcome.FinalizationShare,
                finalizationReleaseEvidence: executionOutcome.FinalizationReleaseEvidence);
        }

        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(
            election,
            governedProposal: proposal,
            governedProposalApproval: approval);
    }

    public async Task<ElectionCommandResult> RetryGovernedProposalExecutionAsync(RetryElectionGovernedProposalExecutionRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (!string.Equals(election.OwnerPublicAddress, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can retry a governed proposal execution.");
        }

        var proposal = await repository.GetGovernedProposalAsync(request.ProposalId);
        if (proposal is null || proposal.ElectionId != request.ElectionId)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Governed proposal {request.ProposalId} was not found for election {request.ElectionId}.");
        }

        if (!proposal.CanRetry)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "Only failed governed proposals can be retried.");
        }

        var approvals = await repository.GetGovernedProposalApprovalsAsync(proposal.Id);
        if (!election.RequiredApprovalCount.HasValue || approvals.Count < election.RequiredApprovalCount.Value)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "The governed proposal cannot be retried until the required approval threshold has been met.");
        }

        var executionOutcome = await ExecuteGovernedProposalAndPersistOutcomeAsync(
            repository,
            election,
            proposal,
            request.ActorPublicAddress,
            request.SourceTransactionId,
            request.SourceBlockHeight,
            request.SourceBlockId);

        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(
            executionOutcome.Election,
            boundaryArtifact: executionOutcome.BoundaryArtifact,
            eligibilitySnapshot: executionOutcome.EligibilitySnapshot,
            governedProposal: executionOutcome.Proposal,
            finalizationSession: executionOutcome.FinalizationSession,
            finalizationShare: executionOutcome.FinalizationShare,
            finalizationReleaseEvidence: executionOutcome.FinalizationReleaseEvidence);
    }

    public async Task<ElectionCommandResult> OpenElectionAsync(OpenElectionRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }
        var result = await OpenElectionInternalAsync(
            repository,
            election,
            request.ActorPublicAddress,
            request.RequiredWarningCodes,
            request.FrozenEligibleVoterSetHash,
            request.TrusteePolicyExecutionReference,
            request.ReportingPolicyExecutionReference,
            request.ReviewWindowExecutionReference,
            allowTrusteeThresholdExecution: false,
            request.SourceTransactionId,
            request.SourceBlockHeight,
            request.SourceBlockId);

        if (result.IsSuccess)
        {
            await unitOfWork.CommitAsync();
        }

        return result;
    }

    public Task<ElectionCommandResult> CloseElectionAsync(CloseElectionRequest request) =>
        TransitionElectionAsync(
            request.ElectionId,
            request.ActorPublicAddress,
            ElectionLifecycleState.Open,
            ElectionBoundaryArtifactType.Close,
            request.AcceptedBallotSetHash,
            request.FinalEncryptedTallyHash,
            allowTrusteeThresholdExecution: false,
            request.SourceTransactionId,
            request.SourceBlockHeight,
            request.SourceBlockId);

    public async Task<ElectionCommandResult> FinalizeElectionAsync(FinalizeElectionRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        var result = await FinalizeElectionInternalAsync(
            repository,
            election,
            request.ActorPublicAddress,
            allowTrusteeThresholdExecution: false,
            request.SourceTransactionId,
            request.SourceBlockHeight,
            request.SourceBlockId);

        if (result.IsSuccess)
        {
            await unitOfWork.CommitAsync();
        }

        return result;
    }

    public async Task<ElectionCommandResult> SubmitFinalizationShareAsync(SubmitElectionFinalizationShareRequest request)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        if (election.LifecycleState != ElectionLifecycleState.Closed)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Finalization shares can only be submitted while the election remains closed.");
        }

        var session = await repository.GetFinalizationSessionAsync(request.FinalizationSessionId);
        if (session is null || session.ElectionId != request.ElectionId)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Finalization session {request.FinalizationSessionId} was not found for election {request.ElectionId}.");
        }

        if (session.Status == ElectionFinalizationSessionStatus.Completed)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "Finalization session is already completed.");
        }

        var trusteeReference = session.EligibleTrustees.FirstOrDefault(x =>
            string.Equals(x.TrusteeUserAddress, request.ActorPublicAddress, StringComparison.OrdinalIgnoreCase));
        if (trusteeReference is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only an eligible trustee can submit a finalization share for this session.");
        }

        var submittedAt = DateTime.UtcNow;
        var existingAcceptedShare = await repository.GetAcceptedFinalizationShareAsync(
            session.Id,
            request.ActorPublicAddress);
        if (existingAcceptedShare is not null)
        {
            var duplicateShare = ElectionModelFactory.CreateRejectedFinalizationShare(
                session.Id,
                request.ElectionId,
                request.ActorPublicAddress,
                trusteeReference.TrusteeDisplayName,
                request.ActorPublicAddress,
                request.ShareIndex,
                request.ShareVersion,
                request.TargetType,
                request.ClaimedCloseArtifactId,
                request.ClaimedAcceptedBallotSetHash ?? Array.Empty<byte>(),
                request.ClaimedFinalEncryptedTallyHash ?? Array.Empty<byte>(),
                request.ClaimedTargetTallyId,
                request.ClaimedCeremonyVersionId,
                request.ClaimedTallyPublicKeyFingerprint,
                request.ShareMaterial,
                "DUPLICATE_SHARE",
                "An accepted finalization share is already recorded for this trustee and session.",
                submittedAt,
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId);

            await repository.SaveFinalizationShareAsync(duplicateShare);
            await unitOfWork.CommitAsync();

            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "An accepted finalization share is already recorded for this trustee and session.");
        }

        var expectedShareIndex = ResolveExpectedFinalizationShareIndex(session, request.ActorPublicAddress);
        var validationOutcome = ValidateFinalizationShareSubmission(session, request, expectedShareIndex);
        var shareRecord = validationOutcome.IsAccepted
            ? ElectionModelFactory.CreateAcceptedFinalizationShare(
                session.Id,
                request.ElectionId,
                request.ActorPublicAddress,
                trusteeReference.TrusteeDisplayName,
                request.ActorPublicAddress,
                request.ShareIndex,
                request.ShareVersion,
                request.TargetType,
                request.ClaimedCloseArtifactId,
                request.ClaimedAcceptedBallotSetHash ?? Array.Empty<byte>(),
                request.ClaimedFinalEncryptedTallyHash ?? Array.Empty<byte>(),
                request.ClaimedTargetTallyId,
                request.ClaimedCeremonyVersionId,
                request.ClaimedTallyPublicKeyFingerprint,
                request.ShareMaterial,
                submittedAt,
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId)
            : ElectionModelFactory.CreateRejectedFinalizationShare(
                session.Id,
                request.ElectionId,
                request.ActorPublicAddress,
                trusteeReference.TrusteeDisplayName,
                request.ActorPublicAddress,
                request.ShareIndex,
                request.ShareVersion,
                request.TargetType,
                request.ClaimedCloseArtifactId,
                request.ClaimedAcceptedBallotSetHash ?? Array.Empty<byte>(),
                request.ClaimedFinalEncryptedTallyHash ?? Array.Empty<byte>(),
                request.ClaimedTargetTallyId,
                request.ClaimedCeremonyVersionId,
                request.ClaimedTallyPublicKeyFingerprint,
                request.ShareMaterial,
                validationOutcome.FailureCode!,
                validationOutcome.FailureReason!,
                submittedAt,
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId);

        await repository.SaveFinalizationShareAsync(shareRecord);

        if (!shareRecord.IsAccepted)
        {
            await unitOfWork.CommitAsync();

            return ElectionCommandResult.Failure(
                validationOutcome.FailureCode == "DUPLICATE_SHARE"
                    ? ElectionCommandErrorCode.Conflict
                    : ElectionCommandErrorCode.ValidationFailed,
                validationOutcome.FailureReason!);
        }

        var progressElection = election with
        {
            LastUpdatedAt = submittedAt,
        };
        await repository.SaveElectionAsync(progressElection);

        var acceptedShares = (await repository.GetFinalizationSharesAsync(session.Id))
            .Where(x => x.Status == ElectionFinalizationShareStatus.Accepted)
            .ToList();
        if (acceptedShares.All(x => x.Id != shareRecord.Id))
        {
            acceptedShares.Add(shareRecord);
        }

        if (acceptedShares.Count < session.RequiredShareCount)
        {
            if (session.SessionPurpose == ElectionFinalizationSessionPurpose.CloseCounting &&
                progressElection.ClosedProgressStatus != ElectionClosedProgressStatus.WaitingForTrusteeShares)
            {
                progressElection = progressElection with
                {
                    ClosedProgressStatus = ElectionClosedProgressStatus.WaitingForTrusteeShares,
                };
                await repository.SaveElectionAsync(progressElection);
            }

            await unitOfWork.CommitAsync();

            return ElectionCommandResult.Success(
                progressElection,
                finalizationSession: session,
                finalizationShare: shareRecord);
        }

        if (session.SessionPurpose == ElectionFinalizationSessionPurpose.CloseCounting)
        {
            progressElection = progressElection with
            {
                ClosedProgressStatus = ElectionClosedProgressStatus.TallyCalculationInProgress,
            };
            await repository.SaveElectionAsync(progressElection);
        }

        var acceptedTrustees = session.EligibleTrustees
            .Where(x => acceptedShares.Any(share =>
                string.Equals(share.TrusteeUserAddress, x.TrusteeUserAddress, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var completionResult = session.SessionPurpose == ElectionFinalizationSessionPurpose.CloseCounting
            ? await CompleteCloseCountingSessionAsync(
                repository,
                progressElection,
                session,
                acceptedShares,
                acceptedTrustees,
                request.ActorPublicAddress,
                submittedAt,
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId,
                shareRecord)
            : await CompleteFinalizationSessionAsync(
                repository,
                progressElection,
                session,
                acceptedTrustees,
                request.ActorPublicAddress,
                submittedAt,
                request.SourceTransactionId,
                request.SourceBlockHeight,
                request.SourceBlockId,
                shareRecord);

        if (!completionResult.IsSuccess &&
            session.SessionPurpose == ElectionFinalizationSessionPurpose.CloseCounting)
        {
            var restoredElection = progressElection with
            {
                ClosedProgressStatus = ElectionClosedProgressStatus.WaitingForTrusteeShares,
                LastUpdatedAt = submittedAt,
            };
            await repository.SaveElectionAsync(restoredElection);
        }

        if (completionResult.IsSuccess)
        {
            await unitOfWork.CommitAsync();
        }

        return completionResult;
    }

    private Task<ElectionCommandResult> ResolveTrusteeInvitationAsync(
        ResolveElectionTrusteeInvitationRequest request,
        bool ownerOnly,
        Func<ElectionTrusteeInvitationRecord, int, ElectionLifecycleState, ElectionTrusteeInvitationRecord> transition)
    {
        return ResolveTrusteeInvitationInternalAsync(request, ownerOnly, transition);
    }

    private Task<ElectionCommandResult> TransitionElectionAsync(
        ElectionId electionId,
        string actorPublicAddress,
        ElectionLifecycleState expectedState,
        ElectionBoundaryArtifactType artifactType,
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash,
        bool allowTrusteeThresholdExecution,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        return TransitionElectionInternalAsync(
            electionId,
            actorPublicAddress,
            expectedState,
            artifactType,
            acceptedBallotSetHash,
            finalEncryptedTallyHash,
            allowTrusteeThresholdExecution,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
    }

    private static ElectionRecord ApplyLifecycleTransition(
        ElectionRecord election,
        ElectionBoundaryArtifactRecord artifact,
        DateTime transitionTime) =>
        artifact.ArtifactType switch
        {
            ElectionBoundaryArtifactType.Close => election with
            {
                LifecycleState = ElectionLifecycleState.Closed,
                LastUpdatedAt = transitionTime,
                VoteAcceptanceLockedAt = election.VoteAcceptanceLockedAt ?? transitionTime,
                ClosedAt = transitionTime,
                CloseArtifactId = artifact.Id,
            },
            ElectionBoundaryArtifactType.TallyReady => election with
            {
                LastUpdatedAt = transitionTime,
                TallyReadyAt = transitionTime,
                TallyReadyArtifactId = artifact.Id,
            },
            ElectionBoundaryArtifactType.Finalize => election with
            {
                LifecycleState = ElectionLifecycleState.Finalized,
                LastUpdatedAt = transitionTime,
                FinalizedAt = transitionTime,
                FinalizeArtifactId = artifact.Id,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(artifact), artifact.ArtifactType, "Unsupported lifecycle transition artifact."),
        };

    private static ElectionRecord ApplyGovernedProposalStartEffects(
        ElectionRecord election,
        ElectionGovernedActionType actionType,
        DateTime proposalCreatedAt) =>
        actionType switch
        {
            ElectionGovernedActionType.Close => election with
            {
                LastUpdatedAt = proposalCreatedAt,
                VoteAcceptanceLockedAt = election.VoteAcceptanceLockedAt ?? proposalCreatedAt,
            },
            _ => election with
            {
                LastUpdatedAt = proposalCreatedAt,
            },
        };

    private async Task<ElectionCommandResult?> ValidateGovernedProposalStartAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionGovernedActionType actionType)
    {
        switch (actionType)
        {
            case ElectionGovernedActionType.Open:
            {
                var invitations = await repository.GetTrusteeInvitationsAsync(election.ElectionId);
                var rosterEntries = await repository.GetRosterEntriesAsync(election.ElectionId);
                var warningAcknowledgements = await repository.GetWarningAcknowledgementsAsync(election.ElectionId);
                var activeCeremonyVersion = await repository.GetActiveCeremonyVersionAsync(election.ElectionId);
                var activeCeremonyTrusteeStates = activeCeremonyVersion is null
                    ? Array.Empty<ElectionCeremonyTrusteeStateRecord>()
                    : await repository.GetCeremonyTrusteeStatesAsync(activeCeremonyVersion.Id);
                var readiness = EvaluateOpenReadiness(
                    election,
                    invitations,
                    rosterEntries,
                    activeCeremonyVersion,
                    activeCeremonyTrusteeStates,
                    warningAcknowledgements,
                    election.AcknowledgedWarningCodes,
                    blockGovernedWorkflowMissing: false);

                return readiness.IsReadyToOpen
                    ? null
                    : ElectionCommandResult.Failure(
                        ElectionCommandErrorCode.ValidationFailed,
                        "Governed open proposal cannot be started.",
                        readiness.ValidationErrors);
            }

            case ElectionGovernedActionType.Close:
                return election.LifecycleState == ElectionLifecycleState.Open
                    ? null
                    : ElectionCommandResult.Failure(
                        ElectionCommandErrorCode.InvalidState,
                        "Governed close proposals are only allowed from the open state.");

            case ElectionGovernedActionType.Finalize:
                if (election.LifecycleState != ElectionLifecycleState.Closed)
                {
                    return ElectionCommandResult.Failure(
                        ElectionCommandErrorCode.InvalidState,
                        "Governed finalize proposals are only allowed from the closed state.");
                }

                return election.TallyReadyAt.HasValue
                    ? election.UnofficialResultArtifactId.HasValue
                        ? null
                        : ElectionCommandResult.Failure(
                            ElectionCommandErrorCode.InvalidState,
                            "Governed finalize proposals are only allowed when the unofficial result exists.")
                    : ElectionCommandResult.Failure(
                        ElectionCommandErrorCode.InvalidState,
                        "Governed finalize proposals are only allowed when the election is tally ready.");

            default:
                throw new ArgumentOutOfRangeException(nameof(actionType), actionType, "Unsupported governed action type.");
        }
    }

    private async Task<(
        ElectionRecord Election,
        ElectionGovernedProposalRecord Proposal,
        ElectionBoundaryArtifactRecord? BoundaryArtifact,
        ElectionEligibilitySnapshotRecord? EligibilitySnapshot,
        ElectionFinalizationSessionRecord? FinalizationSession,
        ElectionFinalizationShareRecord? FinalizationShare,
        ElectionFinalizationReleaseEvidenceRecord? FinalizationReleaseEvidence)> ExecuteGovernedProposalAndPersistOutcomeAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionGovernedProposalRecord proposal,
        string executionTriggeredByPublicAddress,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        try
        {
            var executionResult = await ExecuteGovernedProposalCoreAsync(
                repository,
                election,
                proposal,
                sourceTransactionId,
                sourceBlockHeight,
                sourceBlockId);
            if (!executionResult.IsSuccess || executionResult.Election is null)
            {
                var failedProposal = proposal.RecordExecutionFailure(
                    BuildFailureReason(executionResult, "Governed proposal execution failed."),
                    DateTime.UtcNow,
                executionTriggeredByPublicAddress,
                latestTransactionId: sourceTransactionId,
                latestBlockHeight: sourceBlockHeight,
                latestBlockId: sourceBlockId);
                await repository.UpdateGovernedProposalAsync(failedProposal);
                return (election, failedProposal, null, null, null, null, null);
            }

            var succeededProposal = proposal.RecordExecutionSuccess(
                DateTime.UtcNow,
                executionTriggeredByPublicAddress,
                latestTransactionId: sourceTransactionId,
                latestBlockHeight: sourceBlockHeight,
                latestBlockId: sourceBlockId);
            await repository.UpdateGovernedProposalAsync(succeededProposal);
            return (
                executionResult.Election,
                succeededProposal,
                executionResult.BoundaryArtifact,
                executionResult.EligibilitySnapshot,
                executionResult.FinalizationSession,
                executionResult.FinalizationShare,
                executionResult.FinalizationReleaseEvidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[ElectionLifecycleService] Governed proposal {ProposalId} execution failed unexpectedly.",
                proposal.Id);

            var failedProposal = proposal.RecordExecutionFailure(
                ex.Message,
                DateTime.UtcNow,
                executionTriggeredByPublicAddress,
                latestTransactionId: sourceTransactionId,
                latestBlockHeight: sourceBlockHeight,
                latestBlockId: sourceBlockId);
            await repository.UpdateGovernedProposalAsync(failedProposal);
            return (election, failedProposal, null, null, null, null, null);
        }
    }

    private Task<ElectionCommandResult> ExecuteGovernedProposalCoreAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionGovernedProposalRecord proposal,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null) =>
        proposal.ActionType switch
        {
            ElectionGovernedActionType.Open => OpenElectionInternalAsync(
                repository,
                election,
                proposal.ProposedByPublicAddress,
                election.AcknowledgedWarningCodes,
                frozenEligibleVoterSetHash: null,
                trusteePolicyExecutionReference: "feat-096-governed-proposal",
                reportingPolicyExecutionReference: null,
                reviewWindowExecutionReference: null,
                allowTrusteeThresholdExecution: true,
                sourceTransactionId: sourceTransactionId,
                sourceBlockHeight: sourceBlockHeight,
                sourceBlockId: sourceBlockId),
            ElectionGovernedActionType.Close => ExecuteTransitionInternalAsync(
                repository,
                election,
                proposal.ProposedByPublicAddress,
                ElectionLifecycleState.Open,
                ElectionBoundaryArtifactType.Close,
                acceptedBallotSetHash: null,
                finalEncryptedTallyHash: null,
                allowTrusteeThresholdExecution: true,
                sourceTransactionId: sourceTransactionId,
                sourceBlockHeight: sourceBlockHeight,
                sourceBlockId: sourceBlockId),
            ElectionGovernedActionType.Finalize => FinalizeElectionInternalAsync(
                repository,
                election,
                proposal.ProposedByPublicAddress,
                allowTrusteeThresholdExecution: true,
                sourceTransactionId: sourceTransactionId,
                sourceBlockHeight: sourceBlockHeight,
                sourceBlockId: sourceBlockId),
            _ => throw new ArgumentOutOfRangeException(nameof(proposal), proposal.ActionType, "Unsupported governed action type."),
        };

    private async Task<ElectionCommandResult> OpenElectionInternalAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        string actorPublicAddress,
        IReadOnlyList<ElectionWarningCode>? requiredWarningCodes,
        byte[]? frozenEligibleVoterSetHash,
        string? trusteePolicyExecutionReference,
        string? reportingPolicyExecutionReference,
        string? reviewWindowExecutionReference,
        bool allowTrusteeThresholdExecution,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (!string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can open the election.");
        }

        if (election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold && !allowTrusteeThresholdExecution)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotSupported,
                "Trustee-threshold elections must use the governed proposal workflow to open.");
        }

        var invitations = await repository.GetTrusteeInvitationsAsync(election.ElectionId);
        var rosterEntries = await repository.GetRosterEntriesAsync(election.ElectionId);
        var warningAcknowledgements = await repository.GetWarningAcknowledgementsAsync(election.ElectionId);
        var activeCeremonyVersion = await repository.GetActiveCeremonyVersionAsync(election.ElectionId);
        var activeCeremonyTrusteeStates = activeCeremonyVersion is null
            ? Array.Empty<ElectionCeremonyTrusteeStateRecord>()
            : await repository.GetCeremonyTrusteeStatesAsync(activeCeremonyVersion.Id);
        var readiness = EvaluateOpenReadiness(
            election,
            invitations,
            rosterEntries,
            activeCeremonyVersion,
            activeCeremonyTrusteeStates,
            warningAcknowledgements,
            requiredWarningCodes,
            blockGovernedWorkflowMissing: !allowTrusteeThresholdExecution);

        if (!readiness.IsReadyToOpen)
        {
            return ElectionCommandResult.Failure(
                readiness.ValidationErrors.Any(x => x.Contains("FEAT-096", StringComparison.Ordinal))
                    ? ElectionCommandErrorCode.DependencyBlocked
                    : ElectionCommandErrorCode.ValidationFailed,
                "Election cannot be opened.",
                readiness.ValidationErrors);
        }

        var openedAt = DateTime.UtcNow;
        var frozenRosterEntries = rosterEntries
            .Select(x => x.FreezeAtOpen(
                openedAt,
                sourceTransactionId,
                sourceBlockHeight,
                sourceBlockId))
            .ToArray();
        var resolvedFrozenEligibleVoterSetHash = ResolveOpenBoundaryEligibleHash(
            frozenRosterEntries,
            election.EligibilityMutationPolicy);
        if (frozenEligibleVoterSetHash is { Length: > 0 } &&
            !ByteArrayEquals(frozenEligibleVoterSetHash, resolvedFrozenEligibleVoterSetHash))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Provided frozen eligible voter set hash does not match the server-derived eligibility basis.");
        }

        var artifact = ElectionModelFactory.CreateBoundaryArtifact(
            artifactType: ElectionBoundaryArtifactType.Open,
            election: election,
            recordedByPublicAddress: actorPublicAddress,
            recordedAt: openedAt,
            trusteeSnapshot: CreateTrusteeSnapshot(election, invitations, readiness.CeremonySnapshot),
            ceremonySnapshot: readiness.CeremonySnapshot,
            frozenEligibleVoterSetHash: frozenEligibleVoterSetHash is { Length: > 0 }
                ? frozenEligibleVoterSetHash
                : resolvedFrozenEligibleVoterSetHash,
            trusteePolicyExecutionReference: trusteePolicyExecutionReference,
            reportingPolicyExecutionReference: reportingPolicyExecutionReference,
            reviewWindowExecutionReference: reviewWindowExecutionReference,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);
        var openSnapshot = BuildOpenEligibilitySnapshot(
            election,
            frozenRosterEntries,
            artifact.Id,
            actorPublicAddress,
            openedAt,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);

        var openedElection = election with
        {
            LifecycleState = ElectionLifecycleState.Open,
            LastUpdatedAt = openedAt,
            OpenedAt = openedAt,
            VoteAcceptanceLockedAt = null,
            OpenArtifactId = artifact.Id,
        };

        foreach (var rosterEntry in frozenRosterEntries)
        {
            await repository.UpdateRosterEntryAsync(rosterEntry);
        }

        await repository.SaveBoundaryArtifactAsync(artifact);
        await repository.SaveEligibilitySnapshotAsync(openSnapshot);
        await repository.SaveElectionAsync(openedElection);

        return ElectionCommandResult.Success(
            openedElection,
            boundaryArtifact: artifact,
            rosterEntries: frozenRosterEntries,
            eligibilitySnapshot: openSnapshot);
    }

    private static ElectionCommandResult? ValidateDraftNotBlockedByGovernedOpenProposal(ElectionGovernedProposalRecord? pendingProposal) =>
        pendingProposal?.ActionType == ElectionGovernedActionType.Open
            ? ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Draft election changes are blocked while a governed open proposal is pending.")
            : null;

    private async Task<ElectionCommandResult> RecordBlockedActivationAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        string organizationVoterId,
        string actorPublicAddress,
        ElectionEligibilityActivationBlockReason blockReason,
        ElectionCommandErrorCode errorCode,
        string errorMessage,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        var occurredAt = DateTime.UtcNow;
        var activationEvent = ElectionModelFactory.CreateEligibilityActivationEvent(
            election.ElectionId,
            organizationVoterId,
            actorPublicAddress,
            ElectionEligibilityActivationOutcome.Blocked,
            blockReason,
            occurredAt,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);

        await repository.SaveEligibilityActivationEventAsync(activationEvent);
        await repository.SaveElectionAsync(election with
        {
            LastUpdatedAt = occurredAt,
        });

        return ElectionCommandResult.Failure(errorCode, errorMessage);
    }

    private static bool IsRosterEntryEligibleForCommitmentRegistration(
        ElectionRecord election,
        ElectionRosterEntryRecord rosterEntry)
    {
        if (election.LifecycleState != ElectionLifecycleState.Open || !rosterEntry.WasPresentAtOpen)
        {
            return false;
        }

        return election.EligibilityMutationPolicy switch
        {
            EligibilityMutationPolicy.FrozenAtOpen => rosterEntry.WasActiveAtOpen,
            EligibilityMutationPolicy.LateActivationForRosteredVotersOnly => rosterEntry.IsActive,
            _ => false,
        };
    }

    private async Task<ElectionCastAcceptanceResult?> ValidateCastBoundaryContextAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        AcceptElectionBallotCastRequest request)
    {
        if (!election.OpenArtifactId.HasValue || election.OpenArtifactId.Value != request.OpenArtifactId)
        {
            return ElectionCastAcceptanceResult.Failure(
                ElectionCastAcceptanceFailureReason.WrongElectionContext,
                "The cast request is bound to a different open boundary than the active election.");
        }

        var openArtifact = (await repository.GetBoundaryArtifactsAsync(request.ElectionId))
            .FirstOrDefault(x =>
                x.Id == request.OpenArtifactId &&
                x.ArtifactType == ElectionBoundaryArtifactType.Open);
        if (openArtifact is null)
        {
            return ElectionCastAcceptanceResult.Failure(
                ElectionCastAcceptanceFailureReason.WrongElectionContext,
                "The referenced open boundary artifact was not found.");
        }

        if (!BytesEqual(openArtifact.FrozenEligibleVoterSetHash, request.EligibleSetHash))
        {
            return ElectionCastAcceptanceResult.Failure(
                ElectionCastAcceptanceFailureReason.WrongElectionContext,
                "The cast request is bound to a different eligible-set hash than the active election.");
        }

        var ceremonySnapshot = ElectionProtectedTallyBinding.ResolveOpenBoundaryBinding(election, openArtifact);
        if (ceremonySnapshot is null)
        {
            return ElectionCastAcceptanceResult.Failure(
                ElectionCastAcceptanceFailureReason.WrongElectionContext,
                "The active open boundary does not expose a ceremony snapshot for cast validation.");
        }

        if (ceremonySnapshot.CeremonyVersionId != request.CeremonyVersionId ||
            !string.Equals(ceremonySnapshot.ProfileId, request.DkgProfileId, StringComparison.Ordinal) ||
            !string.Equals(ceremonySnapshot.TallyPublicKeyFingerprint, request.TallyPublicKeyFingerprint, StringComparison.Ordinal))
        {
            return ElectionCastAcceptanceResult.Failure(
                ElectionCastAcceptanceFailureReason.WrongElectionContext,
                "The cast request is bound to a different ceremony or tally-key context than the active election.");
        }

        return null;
    }

    private static string BuildPendingCastTrackingKey(ElectionId electionId, string idempotencyKeyHash) =>
        $"{electionId}:{idempotencyKeyHash}";

    private static DateTime CreateProtectedAcceptedBallotTimestamp(ElectionRecord election)
    {
        var anchor = (election.OpenedAt ?? election.CreatedAt).ToUniversalTime();
        return new DateTime(anchor.Year, anchor.Month, anchor.Day, anchor.Hour, anchor.Minute, 0, DateTimeKind.Utc);
    }

    private static string ComputeScopedHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", nameof(value));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
    }

    private static bool BytesEqual(byte[]? left, byte[]? right)
    {
        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static byte[] ResolveOpenBoundaryEligibleHash(
        IReadOnlyList<ElectionRosterEntryRecord> rosterEntries,
        EligibilityMutationPolicy eligibilityMutationPolicy) =>
        eligibilityMutationPolicy switch
        {
            EligibilityMutationPolicy.FrozenAtOpen => HashOrganizationVoterIds(
                rosterEntries
                    .Where(x => x.WasPresentAtOpen && x.WasActiveAtOpen)
                    .Select(x => x.OrganizationVoterId)),
            EligibilityMutationPolicy.LateActivationForRosteredVotersOnly => HashOrganizationVoterIds(
                rosterEntries
                    .Where(x => x.WasPresentAtOpen)
                    .Select(x => x.OrganizationVoterId)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(eligibilityMutationPolicy),
                eligibilityMutationPolicy,
                "Unsupported eligibility mutation policy."),
        };

    private static ElectionEligibilitySnapshotRecord BuildOpenEligibilitySnapshot(
        ElectionRecord election,
        IReadOnlyList<ElectionRosterEntryRecord> rosterEntries,
        Guid boundaryArtifactId,
        string actorPublicAddress,
        DateTime recordedAt,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        var frozenRosterEntries = rosterEntries
            .Where(x => x.WasPresentAtOpen)
            .ToArray();
        var linkedEntries = frozenRosterEntries
            .Where(x => x.IsLinked)
            .ToArray();
        var activeAtOpenEntries = frozenRosterEntries
            .Where(x => x.WasActiveAtOpen)
            .ToArray();

        return ElectionModelFactory.CreateEligibilitySnapshot(
            election.ElectionId,
            ElectionEligibilitySnapshotType.Open,
            election.EligibilityMutationPolicy,
            rosteredCount: frozenRosterEntries.Length,
            linkedCount: linkedEntries.Length,
            activeDenominatorCount: activeAtOpenEntries.Length,
            countedParticipationCount: 0,
            blankCount: 0,
            didNotVoteCount: activeAtOpenEntries.Length,
            rosteredVoterSetHash: HashOrganizationVoterIds(frozenRosterEntries.Select(x => x.OrganizationVoterId)),
            activeDenominatorSetHash: HashOrganizationVoterIds(activeAtOpenEntries.Select(x => x.OrganizationVoterId)),
            countedParticipationSetHash: HashOrganizationVoterIds(Array.Empty<string>()),
            recordedByPublicAddress: actorPublicAddress,
            boundaryArtifactId: boundaryArtifactId,
            recordedAt: recordedAt,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);
    }

    private static ElectionEligibilitySnapshotRecord BuildCloseEligibilitySnapshot(
        ElectionRecord election,
        IReadOnlyList<ElectionRosterEntryRecord> rosterEntries,
        IReadOnlyList<ElectionParticipationRecord> participationRecords,
        Guid boundaryArtifactId,
        string actorPublicAddress,
        DateTime recordedAt,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        var frozenRosterEntries = rosterEntries
            .Where(x => x.WasPresentAtOpen)
            .ToArray();
        var linkedEntries = frozenRosterEntries
            .Where(x => x.IsLinked)
            .ToArray();
        var activeDenominatorEntries = ResolveActiveDenominatorRosterEntries(election, frozenRosterEntries);
        var activeDenominatorIds = new HashSet<string>(
            activeDenominatorEntries.Select(x => x.OrganizationVoterId),
            StringComparer.OrdinalIgnoreCase);
        var countedParticipationRecords = participationRecords
            .Where(x =>
                activeDenominatorIds.Contains(x.OrganizationVoterId) &&
                x.CountsAsParticipation)
            .ToArray();
        var blankParticipationRecords = countedParticipationRecords
            .Where(x => x.ParticipationStatus == ElectionParticipationStatus.Blank)
            .ToArray();

        return ElectionModelFactory.CreateEligibilitySnapshot(
            election.ElectionId,
            ElectionEligibilitySnapshotType.Close,
            election.EligibilityMutationPolicy,
            rosteredCount: frozenRosterEntries.Length,
            linkedCount: linkedEntries.Length,
            activeDenominatorCount: activeDenominatorEntries.Count,
            countedParticipationCount: countedParticipationRecords.Length,
            blankCount: blankParticipationRecords.Length,
            didNotVoteCount: activeDenominatorEntries.Count - countedParticipationRecords.Length,
            rosteredVoterSetHash: HashOrganizationVoterIds(frozenRosterEntries.Select(x => x.OrganizationVoterId)),
            activeDenominatorSetHash: HashOrganizationVoterIds(activeDenominatorEntries.Select(x => x.OrganizationVoterId)),
            countedParticipationSetHash: HashOrganizationVoterIds(countedParticipationRecords.Select(x => x.OrganizationVoterId)),
            recordedByPublicAddress: actorPublicAddress,
            boundaryArtifactId: boundaryArtifactId,
            recordedAt: recordedAt,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);
    }

    private static IReadOnlyList<ElectionRosterEntryRecord> ResolveActiveDenominatorRosterEntries(
        ElectionRecord election,
        IReadOnlyList<ElectionRosterEntryRecord> frozenRosterEntries) =>
        election.EligibilityMutationPolicy switch
        {
            EligibilityMutationPolicy.FrozenAtOpen => frozenRosterEntries
                .Where(x => x.WasActiveAtOpen)
                .ToArray(),
            EligibilityMutationPolicy.LateActivationForRosteredVotersOnly => frozenRosterEntries
                .Where(x => x.VotingRightStatus == ElectionVotingRightStatus.Active)
                .ToArray(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(election),
                election.EligibilityMutationPolicy,
                "Unsupported eligibility mutation policy."),
        };

    private static byte[] HashOrganizationVoterIds(IEnumerable<string> organizationVoterIds)
    {
        var normalizedIds = organizationVoterIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Length}:{x}");

        return SHA256.HashData(Encoding.UTF8.GetBytes(
            $"feat095:organization-voter-set:{string.Join("\n", normalizedIds)}"));
    }

    private static ElectionTrusteeBoundarySnapshot? CreateTrusteeSnapshot(
        ElectionRecord election,
        IReadOnlyList<ElectionTrusteeInvitationRecord> invitations,
        ElectionCeremonyBindingSnapshot? ceremonySnapshot)
    {
        if (election.GovernanceMode != ElectionGovernanceMode.TrusteeThreshold || !election.RequiredApprovalCount.HasValue)
        {
            return null;
        }

        var activeTrustees = ceremonySnapshot?.ActiveTrustees
            ?? invitations
                .Where(x => x.Status == ElectionTrusteeInvitationStatus.Accepted)
                .Select(x => new ElectionTrusteeReference(x.TrusteeUserAddress, x.TrusteeDisplayName))
                .ToArray();

        return activeTrustees.Count == 0
            ? null
            : ElectionModelFactory.CreateTrusteeBoundarySnapshot(
                election.RequiredApprovalCount.Value,
                activeTrustees);
    }

    private static string BuildFailureReason(ElectionCommandResult result, string fallbackMessage)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        if (result.ValidationErrors.Count > 0)
        {
            return string.Join(" | ", result.ValidationErrors);
        }

        return fallbackMessage;
    }

    private static ElectionCommandResult? ValidateDraftOwnershipAndTrusteeMode(ElectionRecord election, string actorPublicAddress)
    {
        if (election.LifecycleState != ElectionLifecycleState.Draft)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Trustee draft setup can only be changed while the election remains in draft.");
        }

        if (!string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can manage trustee draft setup.");
        }

        if (election.GovernanceMode != ElectionGovernanceMode.TrusteeThreshold)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotSupported,
                "Trustee invitations are only supported for trustee-threshold elections.");
        }

        return null;
    }

    private static ElectionOpenValidationResult EvaluateOpenReadiness(
        ElectionRecord election,
        IReadOnlyList<ElectionTrusteeInvitationRecord> invitations,
        IReadOnlyList<ElectionRosterEntryRecord> rosterEntries,
        ElectionCeremonyVersionRecord? activeCeremonyVersion,
        IReadOnlyList<ElectionCeremonyTrusteeStateRecord> activeCeremonyTrusteeStates,
        IReadOnlyList<ElectionWarningAcknowledgementRecord> warningAcknowledgements,
        IReadOnlyList<ElectionWarningCode>? requestedWarnings,
        bool blockGovernedWorkflowMissing)
    {
        var errors = new List<string>();
        var requiredWarnings = NormalizeWarningCodes(requestedWarnings).ToList();
        var nonBlankOptions = election.Options.Where(x => !x.IsBlankOption).ToArray();
        ElectionCeremonyBindingSnapshot? ceremonySnapshot =
            election.GovernanceMode == ElectionGovernanceMode.AdminOnly
                ? ElectionProtectedTallyBinding.BuildAdminOnlyProtectedTallyBindingSnapshot(election)
                : null;

        if (election.LifecycleState != ElectionLifecycleState.Draft)
        {
            errors.Add("Only draft elections can be opened.");
        }

        if (nonBlankOptions.Length < 2)
        {
            errors.Add("At least two non-blank options are required before opening the election.");
        }

        if (election.OutcomeRule.Kind == OutcomeRuleKind.PassFail && nonBlankOptions.Length != 2)
        {
            errors.Add("Pass/fail elections require exactly two non-blank options before opening.");
        }

        if (election.GovernanceMode == ElectionGovernanceMode.AdminOnly &&
            election.ReviewWindowPolicy != ReviewWindowPolicy.NoReviewWindow)
        {
            errors.Add("Admin-only elections must use the no-review-window policy in FEAT-094.");
        }

        if (rosterEntries.Count == 0)
        {
            errors.Add("An imported election roster is required before opening the election.");
        }

        if (election.EligibilityMutationPolicy == EligibilityMutationPolicy.FrozenAtOpen &&
            rosterEntries.All(x => x.VotingRightStatus != ElectionVotingRightStatus.Active))
        {
            errors.Add("Frozen-at-open elections require at least one active rostered voter before open.");
        }

        if (election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold)
        {
            if (!election.RequiredApprovalCount.HasValue)
            {
                errors.Add("Trustee-threshold elections require enough accepted trustees to satisfy the required approval count before open.");
            }
            else
            {
                ceremonySnapshot = TryBuildCeremonySnapshot(
                    election,
                    activeCeremonyVersion,
                    activeCeremonyTrusteeStates,
                    errors);

                if (ceremonySnapshot is not null && ceremonySnapshot.EveryActiveTrusteeMustApprove)
                {
                    requiredWarnings.Add(ElectionWarningCode.AllTrusteesRequiredFragility);
                }
            }

            if (blockGovernedWorkflowMissing)
            {
                errors.Add("Trustee-threshold elections cannot open until FEAT-096 provides the governed approval workflow.");
            }
        }

        requiredWarnings = NormalizeWarningCodes(requiredWarnings).ToList();
        var currentRevisionAcknowledgements = warningAcknowledgements
            .Where(x => x.DraftRevision == election.CurrentDraftRevision)
            .Select(x => x.WarningCode)
            .Distinct()
            .ToHashSet();
        var electionWarnings = election.AcknowledgedWarningCodes.ToHashSet();

        var missingWarnings = requiredWarnings
            .Where(x => !electionWarnings.Contains(x) || !currentRevisionAcknowledgements.Contains(x))
            .Distinct()
            .ToArray();

        foreach (var warning in missingWarnings)
        {
            errors.Add($"Required warning acknowledgement is missing for {warning}.");
        }

        return errors.Count == 0
            ? ElectionOpenValidationResult.Ready(requiredWarnings, ceremonySnapshot)
            : ElectionOpenValidationResult.NotReady(errors, requiredWarnings, missingWarnings, ceremonySnapshot);
    }

    private static ElectionCeremonyBindingSnapshot? TryBuildCeremonySnapshot(
        ElectionRecord election,
        ElectionCeremonyVersionRecord? activeCeremonyVersion,
        IReadOnlyList<ElectionCeremonyTrusteeStateRecord> activeCeremonyTrusteeStates,
        List<string> errors)
    {
        if (!election.RequiredApprovalCount.HasValue)
        {
            return null;
        }

        if (activeCeremonyVersion is null)
        {
            errors.Add("Trustee-threshold elections require an active key-ceremony version before open.");
            return null;
        }

        if (activeCeremonyVersion.Status != ElectionCeremonyVersionStatus.Ready)
        {
            errors.Add("Trustee-threshold elections require a ready key-ceremony version before open.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(activeCeremonyVersion.TallyPublicKeyFingerprint))
        {
            errors.Add("Ready ceremony versions must publish the tally public key fingerprint before open.");
            return null;
        }

        if (activeCeremonyVersion.RequiredApprovalCount != election.RequiredApprovalCount.Value)
        {
            errors.Add("The active ceremony version threshold does not match the election approval threshold.");
        }

        var activeTrustees = GetCompletedTrusteeReferences(activeCeremonyVersion, activeCeremonyTrusteeStates);
        if (activeTrustees.Length < election.RequiredApprovalCount.Value)
        {
            errors.Add("Trustee-threshold elections require enough ceremony-complete trustees to satisfy the required approval count before open.");
            return null;
        }

        return ElectionModelFactory.CreateCeremonyBindingSnapshot(
            activeCeremonyVersion.Id,
            activeCeremonyVersion.VersionNumber,
            activeCeremonyVersion.ProfileId,
            activeCeremonyVersion.TrusteeCount,
            election.RequiredApprovalCount.Value,
            activeTrustees,
            activeCeremonyVersion.TallyPublicKeyFingerprint);
    }

    private static IReadOnlyList<ElectionOptionDefinition> NormalizeOwnerOptions(IReadOnlyList<ElectionOptionDefinition> ownerOptions) =>
        ownerOptions
            .Select(x => new ElectionOptionDefinition(
                x.OptionId,
                x.DisplayLabel,
                x.ShortDescription,
                x.BallotOrder,
                x.IsBlankOption))
            .ToArray();

    private static IReadOnlyList<ApprovedClientApplicationRecord> NormalizeApprovedApplications(IReadOnlyList<ApprovedClientApplicationRecord> applications) =>
        applications
            .Select(x => new ApprovedClientApplicationRecord(x.ApplicationId, x.Version))
            .ToArray();

    private static IReadOnlyList<ElectionWarningCode> NormalizeWarningCodes(IReadOnlyList<ElectionWarningCode>? warningCodes) =>
        warningCodes is null
            ? Array.Empty<ElectionWarningCode>()
            : warningCodes
                .Distinct()
                .OrderBy(x => (int)x)
                .ToArray();

    private static IReadOnlyList<ElectionWarningAcknowledgementRecord> CreateWarningAcknowledgementsForRevision(
        ElectionId electionId,
        int draftRevision,
        string actorPublicAddress,
        IReadOnlyList<ElectionWarningCode> warningCodes,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null) =>
        warningCodes
            .Select(x => ElectionModelFactory.CreateWarningAcknowledgement(
                electionId,
                x,
                draftRevision,
                actorPublicAddress,
                sourceTransactionId: sourceTransactionId,
                sourceBlockHeight: sourceBlockHeight,
                sourceBlockId: sourceBlockId))
            .ToArray();

    private async Task<ElectionCommandResult> StartOrRestartElectionCeremonyInternalAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        string actorPublicAddress,
        string profileId,
        string? restartReason)
    {
        var election = await repository.GetElectionForUpdateAsync(electionId);
        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {electionId} was not found.");
        }

        var phaseResult = ValidateDraftOwnershipAndTrusteeMode(election, actorPublicAddress);
        if (phaseResult is not null)
        {
            return phaseResult;
        }

        var pendingProposal = await repository.GetPendingGovernedProposalAsync(electionId);
        var governedDraftLock = ValidateDraftNotBlockedByGovernedOpenProposal(pendingProposal);
        if (governedDraftLock is not null)
        {
            return governedDraftLock;
        }

        var profile = await repository.GetCeremonyProfileAsync(profileId);
        if (profile is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Ceremony profile {profileId} was not found.");
        }

        if (profile.DevOnly && !_ceremonyOptions.EnableDevCeremonyProfiles)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotSupported,
                $"Ceremony profile {profileId} is disabled in this deployment.");
        }

        if (!election.RequiredApprovalCount.HasValue)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Trustee-threshold elections require a configured approval threshold before a ceremony can start.");
        }

        if (profile.RequiredApprovalCount != election.RequiredApprovalCount.Value)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "The selected ceremony profile threshold does not match the election approval threshold.");
        }

        var invitations = await repository.GetTrusteeInvitationsAsync(electionId);
        var acceptedTrustees = invitations
            .Where(x => x.Status == ElectionTrusteeInvitationStatus.Accepted)
            .Select(x => new ElectionTrusteeReference(x.TrusteeUserAddress, x.TrusteeDisplayName))
            .ToArray();

        if (acceptedTrustees.Length != profile.TrusteeCount)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                $"Ceremony profile {profile.ProfileId} requires exactly {profile.TrusteeCount} accepted trustees before the ceremony can start.");
        }

        var existingActiveVersion = await repository.GetActiveCeremonyVersionAsync(electionId);
        var transcriptEvents = new List<ElectionCeremonyTranscriptEventRecord>();
        if (existingActiveVersion is not null)
        {
            if (string.IsNullOrWhiteSpace(restartReason))
            {
                return ElectionCommandResult.Failure(
                    ElectionCommandErrorCode.Conflict,
                    "An active ceremony version already exists. Restart requires an explicit reason.");
            }

            var supersededAt = DateTime.UtcNow;
            var supersededVersion = existingActiveVersion.Supersede(supersededAt, restartReason);
            await repository.UpdateCeremonyVersionAsync(supersededVersion);
            transcriptEvents.Add(ElectionModelFactory.CreateCeremonyTranscriptEvent(
                electionId,
                supersededVersion.Id,
                supersededVersion.VersionNumber,
                ElectionCeremonyTranscriptEventType.VersionSuperseded,
                $"Ceremony version {supersededVersion.VersionNumber} was superseded before a restart.",
                occurredAt: supersededAt,
                actorPublicAddress: actorPublicAddress,
                restartReason: restartReason));
        }

        var existingVersions = await repository.GetCeremonyVersionsAsync(electionId);
        var versionNumber = existingVersions.Count == 0
            ? 1
            : existingVersions.Max(x => x.VersionNumber) + 1;
        var startedAt = DateTime.UtcNow;
        var version = ElectionModelFactory.CreateCeremonyVersion(
            electionId,
            versionNumber,
            profile.ProfileId,
            profile.RequiredApprovalCount,
            acceptedTrustees,
            actorPublicAddress,
            startedAt);

        await repository.SaveCeremonyVersionAsync(version);

        foreach (var trustee in acceptedTrustees)
        {
            await repository.SaveCeremonyTrusteeStateAsync(ElectionModelFactory.CreateCeremonyTrusteeState(
                electionId,
                version.Id,
                trustee.TrusteeUserAddress,
                trustee.TrusteeDisplayName,
                state: ElectionTrusteeCeremonyState.AcceptedTrustee,
                recordedAt: startedAt));
        }

        transcriptEvents.Add(ElectionModelFactory.CreateCeremonyTranscriptEvent(
            electionId,
            version.Id,
            version.VersionNumber,
            ElectionCeremonyTranscriptEventType.VersionStarted,
            $"Ceremony version {version.VersionNumber} started with profile {profile.ProfileId}.",
            occurredAt: startedAt,
            actorPublicAddress: actorPublicAddress));

        foreach (var transcriptEvent in transcriptEvents)
        {
            await repository.SaveCeremonyTranscriptEventAsync(transcriptEvent);
        }

        return ElectionCommandResult.Success(
            election,
            ceremonyProfile: profile,
            ceremonyVersion: version,
            ceremonyTranscriptEvents: transcriptEvents);
    }

    private async Task<ElectionCommandResult> ExecuteCeremonyTrusteeUpdateAsync(
        ElectionId electionId,
        Guid ceremonyVersionId,
        string actorPublicAddress,
        Func<ActiveCeremonyTrusteeContext, Task<CeremonyTrusteeUpdateOutcome>> update,
        bool requireOwnerActor)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var loadResult = await LoadActiveCeremonyTrusteeContextAsync(
            repository,
            electionId,
            ceremonyVersionId,
            actorPublicAddress,
            requireOwnerActor);
        if (!loadResult.IsSuccess)
        {
            return loadResult.ErrorResult!;
        }

        var ceremonyContext = loadResult.Context!;
        CeremonyTrusteeUpdateOutcome outcome;
        try
        {
            outcome = await update(ceremonyContext);
        }
        catch (ArgumentException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                ex.Message);
        }
        await repository.UpdateCeremonyTrusteeStateAsync(outcome.TrusteeState);
        await repository.SaveCeremonyTranscriptEventAsync(outcome.TranscriptEvent);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(
            ceremonyContext.Election,
            ceremonyVersion: ceremonyContext.Version,
            ceremonyTranscriptEvents: [outcome.TranscriptEvent],
            ceremonyTrusteeState: outcome.TrusteeState);
    }

    private async Task<CeremonyTrusteeContextLoadResult> LoadActiveCeremonyTrusteeContextAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        Guid ceremonyVersionId,
        string actorPublicAddress,
        bool requireOwnerActor = false)
    {
        var election = await repository.GetElectionForUpdateAsync(electionId);
        if (election is null)
        {
            return CeremonyTrusteeContextLoadResult.NotFound(
                $"Election {electionId} was not found.");
        }

        if (election.LifecycleState != ElectionLifecycleState.Draft)
        {
            return CeremonyTrusteeContextLoadResult.InvalidState(
                "Election ceremony actions are only allowed while the election remains in draft.");
        }

        var version = await repository.GetCeremonyVersionAsync(ceremonyVersionId);
        if (version is null || version.ElectionId != electionId || !version.IsActive)
        {
            return CeremonyTrusteeContextLoadResult.NotFound(
                $"Ceremony version {ceremonyVersionId} was not found for election {electionId}.");
        }

        var activeVersion = await repository.GetActiveCeremonyVersionAsync(electionId);
        if (activeVersion is null || activeVersion.Id != ceremonyVersionId)
        {
            return CeremonyTrusteeContextLoadResult.Conflict(
                "Only the active ceremony version may receive new trustee actions.");
        }

        if (requireOwnerActor)
        {
            if (!string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal))
            {
                return CeremonyTrusteeContextLoadResult.Forbidden(
                    "Only the election owner can perform this ceremony action.");
            }
        }

        var trusteeState = await repository.GetCeremonyTrusteeStateAsync(ceremonyVersionId, actorPublicAddress);
        if (!requireOwnerActor && trusteeState is null)
        {
            return CeremonyTrusteeContextLoadResult.Forbidden(
                "Only trustees bound to the active ceremony version can perform this action.");
        }

        if (requireOwnerActor)
        {
            return CeremonyTrusteeContextLoadResult.Success(
                new ActiveCeremonyTrusteeContext(election, version, null!));
        }

        return CeremonyTrusteeContextLoadResult.Success(
            new ActiveCeremonyTrusteeContext(election, version, trusteeState!));
    }

    private async Task<ShareCustodyContextLoadResult> LoadShareCustodyContextAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        Guid ceremonyVersionId,
        string actorPublicAddress,
        string trusteeUserAddress,
        string? shareVersion,
        bool requireOwnerActor)
    {
        var election = await repository.GetElectionForUpdateAsync(electionId);
        if (election is null)
        {
            return ShareCustodyContextLoadResult.NotFound(
                $"Election {electionId} was not found.");
        }

        if (!string.Equals(actorPublicAddress, trusteeUserAddress, StringComparison.Ordinal) && !requireOwnerActor)
        {
            return ShareCustodyContextLoadResult.Forbidden(
                "Trustees can only manage their own share-custody records.");
        }

        var version = await repository.GetCeremonyVersionAsync(ceremonyVersionId);
        if (version is null || version.ElectionId != electionId || !version.IsActive)
        {
            return ShareCustodyContextLoadResult.NotFound(
                $"Ceremony version {ceremonyVersionId} was not found for election {electionId}.");
        }

        var trusteeState = await repository.GetCeremonyTrusteeStateAsync(ceremonyVersionId, trusteeUserAddress);
        if (trusteeState is null || trusteeState.State != ElectionTrusteeCeremonyState.CeremonyCompleted)
        {
            return ShareCustodyContextLoadResult.InvalidState(
                "Share-custody actions require a ceremony-complete trustee state.");
        }

        var custodyRecord = await repository.GetCeremonyShareCustodyRecordAsync(ceremonyVersionId, trusteeUserAddress);
        if (custodyRecord is null)
        {
            return ShareCustodyContextLoadResult.NotFound(
                $"Share-custody record for trustee {trusteeUserAddress} was not found.");
        }

        if (shareVersion is not null && !string.Equals(custodyRecord.ShareVersion, shareVersion, StringComparison.Ordinal))
        {
            return ShareCustodyContextLoadResult.ValidationFailed(
                "Share version does not match the active ceremony binding.");
        }

        return ShareCustodyContextLoadResult.Success(election, version, custodyRecord);
    }

    private async Task<IReadOnlyList<ElectionTrusteeReference>> ResolveGovernanceApproverRosterAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionGovernedProposalRecord proposal)
    {
        if (proposal.ActionType == ElectionGovernedActionType.Open)
        {
            var activeVersion = await repository.GetActiveCeremonyVersionAsync(election.ElectionId);
            if (activeVersion is not null)
            {
                var trusteeStates = await repository.GetCeremonyTrusteeStatesAsync(activeVersion.Id);
                return GetCompletedTrusteeReferences(activeVersion, trusteeStates);
            }
        }

        if (election.OpenArtifactId.HasValue)
        {
            var boundaryArtifacts = await repository.GetBoundaryArtifactsAsync(election.ElectionId);
            var openArtifact = boundaryArtifacts.FirstOrDefault(x =>
                x.Id == election.OpenArtifactId.Value ||
                x.ArtifactType == ElectionBoundaryArtifactType.Open);
            if (openArtifact?.TrusteeSnapshot is not null)
            {
                return openArtifact.TrusteeSnapshot.AcceptedTrustees;
            }
        }

        var invitations = await repository.GetTrusteeInvitationsAsync(election.ElectionId);
        return invitations
            .Where(x => x.Status == ElectionTrusteeInvitationStatus.Accepted)
            .Select(x => new ElectionTrusteeReference(x.TrusteeUserAddress, x.TrusteeDisplayName))
            .ToArray();
    }

    private static ElectionTrusteeReference[] GetCompletedTrusteeReferences(
        ElectionCeremonyVersionRecord version,
        IReadOnlyList<ElectionCeremonyTrusteeStateRecord> trusteeStates) =>
        version.BoundTrustees
            .Where(boundTrustee => trusteeStates.Any(x =>
                string.Equals(x.TrusteeUserAddress, boundTrustee.TrusteeUserAddress, StringComparison.OrdinalIgnoreCase) &&
                x.State == ElectionTrusteeCeremonyState.CeremonyCompleted))
            .ToArray();

    private static bool HasCeremonyProgress(IReadOnlyList<ElectionCeremonyTrusteeStateRecord> trusteeStates) =>
        trusteeStates.Any(x =>
            x.HasPublishedTransportKey ||
            x.JoinedAt.HasValue ||
            x.SelfTestSucceededAt.HasValue ||
            x.MaterialSubmittedAt.HasValue ||
            x.ValidationFailedAt.HasValue ||
            x.CompletedAt.HasValue ||
            x.RemovedAt.HasValue ||
            (x.State != ElectionTrusteeCeremonyState.AcceptedTrustee &&
             x.State != ElectionTrusteeCeremonyState.CeremonyNotStarted));

    private static int CountCompletedTrustees(
        IReadOnlyList<ElectionCeremonyTrusteeStateRecord> trusteeStates,
        string updatedTrusteeUserAddress)
    {
        var completedCount = trusteeStates.Count(x => x.State == ElectionTrusteeCeremonyState.CeremonyCompleted);
        return trusteeStates.Any(x =>
                string.Equals(x.TrusteeUserAddress, updatedTrusteeUserAddress, StringComparison.OrdinalIgnoreCase) &&
                x.State == ElectionTrusteeCeremonyState.CeremonyCompleted)
            ? completedCount
            : completedCount + 1;
    }

    private async Task<ElectionCommandResult> ResolveTrusteeInvitationInternalAsync(
        ResolveElectionTrusteeInvitationRequest request,
        bool ownerOnly,
        Func<ElectionTrusteeInvitationRecord, int, ElectionLifecycleState, ElectionTrusteeInvitationRecord> transition)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(request.ElectionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {request.ElectionId} was not found.");
        }

        var invitation = await repository.GetTrusteeInvitationAsync(request.InvitationId);
        if (invitation is null || invitation.ElectionId != request.ElectionId)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Trustee invitation {request.InvitationId} was not found for election {request.ElectionId}.");
        }

        var authorizedActor = ownerOnly ? election.OwnerPublicAddress : invitation.TrusteeUserAddress;
        if (!string.Equals(authorizedActor, request.ActorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                ownerOnly
                    ? "Only the owner can revoke a trustee invitation."
                    : "Only the invited trustee can resolve this invitation.");
        }

        if (election.LifecycleState != ElectionLifecycleState.Draft)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Trustee invitations can only be resolved while the election remains in draft.");
        }

        var pendingProposal = await repository.GetPendingGovernedProposalAsync(request.ElectionId);
        var governedDraftLock = ValidateDraftNotBlockedByGovernedOpenProposal(pendingProposal);
        if (governedDraftLock is not null)
        {
            return governedDraftLock;
        }

        if (invitation.Status != ElectionTrusteeInvitationStatus.Pending)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "Only pending trustee invitations can be resolved.");
        }

        var updated = transition(invitation, election.CurrentDraftRevision, election.LifecycleState);
        if (updated.Status == ElectionTrusteeInvitationStatus.Accepted)
        {
            var existingGrant = await repository.GetReportAccessGrantAsync(request.ElectionId, updated.TrusteeUserAddress);
            if (existingGrant?.GrantRole == ElectionReportAccessGrantRole.DesignatedAuditor)
            {
                return ElectionCommandResult.Failure(
                    ElectionCommandErrorCode.Conflict,
                    "This Hush account is already a designated auditor for the election and cannot also accept a trustee invitation.");
            }
        }

        await repository.UpdateTrusteeInvitationAsync(updated);
        var ceremonyEvents = new List<ElectionCeremonyTranscriptEventRecord>();

        if (updated.Status == ElectionTrusteeInvitationStatus.Accepted)
        {
            var activeVersion = await repository.GetActiveCeremonyVersionAsync(request.ElectionId);
            if (activeVersion is not null)
            {
                var trusteeStates = await repository.GetCeremonyTrusteeStatesAsync(activeVersion.Id);
                if (HasCeremonyProgress(trusteeStates))
                {
                    var supersededAt = DateTime.UtcNow;
                    var supersededVersion = activeVersion.Supersede(
                        supersededAt,
                        $"Accepted trustee {updated.TrusteeUserAddress} changed the active ceremony roster after progress.");
                    await repository.UpdateCeremonyVersionAsync(supersededVersion);
                    ceremonyEvents.Add(ElectionModelFactory.CreateCeremonyTranscriptEvent(
                        request.ElectionId,
                        supersededVersion.Id,
                        supersededVersion.VersionNumber,
                        ElectionCeremonyTranscriptEventType.VersionSuperseded,
                        $"Ceremony version {supersededVersion.VersionNumber} was superseded after the accepted trustee roster changed.",
                        occurredAt: supersededAt,
                        actorPublicAddress: request.ActorPublicAddress,
                        trusteeUserAddress: updated.TrusteeUserAddress,
                        trusteeDisplayName: updated.TrusteeDisplayName,
                        restartReason: supersededVersion.SupersededReason));
                }
            }
        }

        foreach (var ceremonyEvent in ceremonyEvents)
        {
            await repository.SaveCeremonyTranscriptEventAsync(ceremonyEvent);
        }

        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(
            election,
            trusteeInvitation: updated,
            ceremonyTranscriptEvents: ceremonyEvents);
    }

    private async Task<ElectionCommandResult> TransitionElectionInternalAsync(
        ElectionId electionId,
        string actorPublicAddress,
        ElectionLifecycleState expectedState,
        ElectionBoundaryArtifactType artifactType,
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash,
        bool allowTrusteeThresholdExecution,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable(IsolationLevel.Serializable);
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionForUpdateAsync(electionId);

        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotFound,
                $"Election {electionId} was not found.");
        }

        var result = await ExecuteTransitionInternalAsync(
            repository,
            election,
            actorPublicAddress,
            expectedState,
            artifactType,
            acceptedBallotSetHash,
            finalEncryptedTallyHash,
            allowTrusteeThresholdExecution,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);

        if (result.IsSuccess)
        {
            await unitOfWork.CommitAsync();
        }

        return result;
    }

    private async Task<ElectionCommandResult> ExecuteTransitionInternalAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        string actorPublicAddress,
        ElectionLifecycleState expectedState,
        ElectionBoundaryArtifactType artifactType,
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash,
        bool allowTrusteeThresholdExecution,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (!string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                $"Only the owner can {artifactType.ToString().ToLowerInvariant()} the election.");
        }

        if (election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold && !allowTrusteeThresholdExecution)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotSupported,
                $"Trustee-threshold elections must use the governed proposal workflow to {artifactType.ToString().ToLowerInvariant()}.");
        }

        if (election.LifecycleState != expectedState)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                artifactType switch
                {
                    ElectionBoundaryArtifactType.Close => "Election close is only allowed from the open state.",
                    ElectionBoundaryArtifactType.Finalize => "Election finalize is only allowed from the closed state.",
                    _ => "Unsupported election transition.",
                });
        }

        if (artifactType == ElectionBoundaryArtifactType.Finalize && !election.TallyReadyAt.HasValue)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Election finalize is only allowed when the election is tally ready.");
        }

        ElectionCeremonyBindingSnapshot? carriedForwardCeremonySnapshot = null;
        if (artifactType != ElectionBoundaryArtifactType.Open && election.OpenArtifactId.HasValue)
        {
            var boundaryArtifacts = await repository.GetBoundaryArtifactsAsync(election.ElectionId);
            var openArtifact = boundaryArtifacts.FirstOrDefault(x =>
                x.Id == election.OpenArtifactId.Value &&
                x.ArtifactType == ElectionBoundaryArtifactType.Open);
            carriedForwardCeremonySnapshot = ElectionProtectedTallyBinding.ResolveOpenBoundaryBinding(election, openArtifact);
        }

        var transitionTime = DateTime.UtcNow;
        var artifact = ElectionModelFactory.CreateBoundaryArtifact(
            artifactType,
            election,
            actorPublicAddress,
            ceremonySnapshot: carriedForwardCeremonySnapshot,
            recordedAt: transitionTime,
            acceptedBallotSetHash: artifactType == ElectionBoundaryArtifactType.Close
                ? null
                : acceptedBallotSetHash,
            finalEncryptedTallyHash: artifactType == ElectionBoundaryArtifactType.Close
                ? null
                : finalEncryptedTallyHash,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);
        ElectionEligibilitySnapshotRecord? eligibilitySnapshot = null;
        if (artifactType == ElectionBoundaryArtifactType.Close)
        {
            var rosterEntries = await repository.GetRosterEntriesAsync(election.ElectionId);
            var participationRecords = await repository.GetParticipationRecordsAsync(election.ElectionId);
            eligibilitySnapshot = BuildCloseEligibilitySnapshot(
                election,
                rosterEntries,
                participationRecords,
                artifact.Id,
                actorPublicAddress,
                transitionTime,
                sourceTransactionId,
                sourceBlockHeight,
                sourceBlockId);
        }

        var updatedElection = ApplyLifecycleTransition(election, artifact, transitionTime);
        if (artifactType == ElectionBoundaryArtifactType.Close)
        {
            updatedElection = updatedElection with
            {
                ClosedProgressStatus = ElectionClosedProgressStatus.TallyCalculationInProgress,
            };
        }

        await repository.SaveBoundaryArtifactAsync(artifact);
        if (eligibilitySnapshot is not null)
        {
            await repository.SaveEligibilitySnapshotAsync(eligibilitySnapshot);
        }

        await repository.SaveElectionAsync(updatedElection);

        return ElectionCommandResult.Success(
            updatedElection,
            boundaryArtifact: artifact,
            eligibilitySnapshot: eligibilitySnapshot);
    }

    private async Task<ElectionCommandResult> FinalizeElectionInternalAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        string actorPublicAddress,
        bool allowTrusteeThresholdExecution,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (!string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can finalize the election.");
        }

        if (election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold && !allowTrusteeThresholdExecution)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotSupported,
                "Trustee-threshold elections must use the governed proposal workflow to finalize.");
        }

        if (election.LifecycleState != ElectionLifecycleState.Closed)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Election finalize is only allowed from the closed state.");
        }

        if (!election.TallyReadyAt.HasValue)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Election finalize is only allowed when the election is tally ready.");
        }

        var boundaryArtifacts = await repository.GetBoundaryArtifactsAsync(election.ElectionId);
        var tallyReadyArtifact = ResolveTallyReadyArtifact(election, boundaryArtifacts);
        if (tallyReadyArtifact is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Finalization requires the exact tally-ready boundary artifact to exist.");
        }

        var unofficialResult = election.UnofficialResultArtifactId.HasValue
            ? await repository.GetResultArtifactAsync(election.UnofficialResultArtifactId.Value)
            : await repository.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Unofficial);
        if (unofficialResult is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Finalization requires the exact unofficial result artifact to exist.");
        }

        if (unofficialResult.ArtifactKind != ElectionResultArtifactKind.Unofficial)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Finalization requires an unofficial result artifact as the source result.");
        }

        if (election.OfficialResultArtifactId.HasValue ||
            await repository.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Official) is not null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "The official result artifact already exists for this election.");
        }

        var officialRecordedAt = DateTime.UtcNow;
        var officialVisibility = election.OfficialResultVisibilityPolicy == OfficialResultVisibilityPolicy.PublicPlaintext
            ? ElectionResultArtifactVisibility.PublicPlaintext
            : ElectionResultArtifactVisibility.ParticipantEncrypted;
        var officialPayload = SerializeResultArtifactPayload(unofficialResult);

        string? encryptedPayload = null;
        string? publicPayload = null;
        if (officialVisibility == ElectionResultArtifactVisibility.PublicPlaintext)
        {
            publicPayload = officialPayload;
        }
        else
        {
            if (_electionResultCryptoService is null)
            {
                return ElectionCommandResult.Failure(
                    ElectionCommandErrorCode.DependencyBlocked,
                    "Official participant-encrypted result publication requires the FEAT-101 result crypto service.");
            }

            var ownerAccess = await repository.GetElectionEnvelopeAccessAsync(election.ElectionId, election.OwnerPublicAddress);
            if (ownerAccess is null)
            {
                return ElectionCommandResult.Failure(
                    ElectionCommandErrorCode.DependencyBlocked,
                    "Official participant-encrypted result publication requires the owner election envelope access record.");
            }

            encryptedPayload = _electionResultCryptoService.EncryptForElectionParticipants(
                officialPayload,
                ownerAccess.NodeEncryptedElectionPrivateKey);
        }

        var closeArtifact = boundaryArtifacts.FirstOrDefault(x =>
            x.Id == election.CloseArtifactId &&
            x.ArtifactType == ElectionBoundaryArtifactType.Close);
        if (closeArtifact is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Finalization requires the exact close boundary artifact to exist.");
        }

        var closeEligibilitySnapshot = await repository.GetEligibilitySnapshotAsync(
            election.ElectionId,
            ElectionEligibilitySnapshotType.Close);
        if (closeEligibilitySnapshot is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Finalization requires the close eligibility snapshot.");
        }

        var priorReportAttempt = await repository.GetLatestReportPackageAsync(election.ElectionId);
        if (priorReportAttempt is not null &&
            priorReportAttempt.Status == ElectionReportPackageStatus.Sealed)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "A sealed FEAT-102 report package already exists for this election.");
        }

        var reportAttemptNumber = (priorReportAttempt?.AttemptNumber ?? 0) + 1;

        var officialResult = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Official,
            officialVisibility,
            unofficialResult.Title,
            unofficialResult.NamedOptionResults,
            unofficialResult.BlankCount,
            unofficialResult.TotalVotedCount,
            unofficialResult.EligibleToVoteCount,
            unofficialResult.DidNotVoteCount,
            unofficialResult.DenominatorEvidence,
            actorPublicAddress,
            tallyReadyArtifactId: null,
            sourceResultArtifactId: unofficialResult.Id,
            encryptedPayload: encryptedPayload,
            publicPayload: publicPayload,
            recordedAt: officialRecordedAt,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);

        var finalizeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Finalize,
            election,
            actorPublicAddress,
            ceremonySnapshot: ElectionProtectedTallyBinding.ResolveBoundaryBinding(election, closeArtifact),
            recordedAt: officialRecordedAt,
            acceptedBallotSetHash: tallyReadyArtifact.AcceptedBallotSetHash,
            finalEncryptedTallyHash: tallyReadyArtifact.FinalEncryptedTallyHash,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);
        var finalizedElection = ApplyLifecycleTransition(election, finalizeArtifact, officialRecordedAt) with
        {
            LastUpdatedAt = officialRecordedAt,
            ClosedProgressStatus = ElectionClosedProgressStatus.None,
            OfficialResultArtifactId = officialResult.Id,
        };
        var finalizationContext = await ResolveReportPackageFinalizationContextAsync(
            repository,
            election.ElectionId,
            tallyReadyArtifact);
        ElectionGovernedProposalRecord? finalizationGovernedProposal = null;
        IReadOnlyList<ElectionGovernedProposalApprovalRecord> finalizationGovernedApprovals = Array.Empty<ElectionGovernedProposalApprovalRecord>();
        IReadOnlyList<ElectionFinalizationShareRecord> finalizationShares = Array.Empty<ElectionFinalizationShareRecord>();
        if (finalizationContext.Session is not null)
        {
            finalizationShares = await repository.GetFinalizationSharesAsync(finalizationContext.Session.Id);
            if (finalizationContext.Session.GovernedProposalId.HasValue)
            {
                finalizationGovernedProposal = await repository.GetGovernedProposalAsync(finalizationContext.Session.GovernedProposalId.Value);
                finalizationGovernedApprovals = finalizationGovernedProposal is null
                    ? Array.Empty<ElectionGovernedProposalApprovalRecord>()
                    : await repository.GetGovernedProposalApprovalsAsync(finalizationGovernedProposal.Id);
            }
        }

        var warningAcknowledgements = await repository.GetWarningAcknowledgementsAsync(election.ElectionId);
        var trusteeInvitations = await repository.GetTrusteeInvitationsAsync(election.ElectionId);
        var rosterEntries = await repository.GetRosterEntriesAsync(election.ElectionId);
        var participationRecords = await repository.GetParticipationRecordsAsync(election.ElectionId);
        var reportBuildResult = _electionReportPackageService.Build(new ElectionReportPackageBuildRequest(
            finalizedElection,
            closeArtifact,
            tallyReadyArtifact,
            finalizeArtifact,
            unofficialResult,
            officialResult,
            closeEligibilitySnapshot,
            finalizationContext.Session,
            finalizationContext.ReleaseEvidence,
            finalizationGovernedProposal,
            finalizationGovernedApprovals,
            finalizationShares,
            warningAcknowledgements,
            trusteeInvitations,
            rosterEntries,
            participationRecords,
            reportAttemptNumber,
            priorReportAttempt?.Id,
            actorPublicAddress,
            officialRecordedAt));

        if (priorReportAttempt is not null &&
            priorReportAttempt.Status == ElectionReportPackageStatus.GenerationFailed &&
            !ByteArrayEquals(priorReportAttempt.FrozenEvidenceHash, reportBuildResult.Package.FrozenEvidenceHash))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Finalization retry evidence no longer matches the frozen evidence from the last failed report-package attempt.");
        }

        if (!reportBuildResult.IsSuccess)
        {
            await repository.SaveReportPackageAsync(reportBuildResult.Package);
            await repository.SaveElectionAsync(election with
            {
                LastUpdatedAt = officialRecordedAt,
            });

            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                $"Finalization package generation failed: {reportBuildResult.Package.FailureReason ?? reportBuildResult.Package.FailureCode ?? "unknown error"}");
        }

        await repository.SaveResultArtifactAsync(officialResult);
        await repository.SaveBoundaryArtifactAsync(finalizeArtifact);
        await repository.SaveReportPackageAsync(reportBuildResult.Package);
        foreach (var reportArtifact in reportBuildResult.Artifacts)
        {
            await repository.SaveReportArtifactAsync(reportArtifact);
        }
        foreach (var accessGrant in reportBuildResult.AccessGrants)
        {
            await repository.SaveReportAccessGrantAsync(accessGrant);
        }

        await repository.SaveElectionAsync(finalizedElection);

        return ElectionCommandResult.Success(
            finalizedElection,
            boundaryArtifact: finalizeArtifact);
    }

    private async Task<ElectionCommandResult> StartFinalizationSessionInternalAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        string actorPublicAddress,
        byte[]? acceptedBallotSetHash,
        byte[]? finalEncryptedTallyHash,
        Guid? governedProposalId,
        Guid? sourceTransactionId = null,
        long? sourceBlockHeight = null,
        Guid? sourceBlockId = null)
    {
        if (!string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Forbidden,
                "Only the owner can start election finalization.");
        }

        if (election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold && !governedProposalId.HasValue)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotSupported,
                "Trustee-threshold elections must use the governed proposal workflow to start finalization.");
        }

        if (election.LifecycleState != ElectionLifecycleState.Closed)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Election finalization is only allowed from the closed state.");
        }

        if (!election.TallyReadyAt.HasValue)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Election finalization is only allowed when the election is tally ready.");
        }

        var existingSession = await repository.GetActiveFinalizationSessionAsync(election.ElectionId);
        if (existingSession is not null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "An active finalization session already exists for this election.");
        }

        var boundaryArtifacts = await repository.GetBoundaryArtifactsAsync(election.ElectionId);
        var closeArtifact = boundaryArtifacts.FirstOrDefault(x =>
            x.Id == election.CloseArtifactId &&
            x.ArtifactType == ElectionBoundaryArtifactType.Close);
        if (closeArtifact is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Finalization requires the exact close boundary artifact to exist.");
        }

        var tallyReadyArtifact = ResolveTallyReadyArtifact(election, boundaryArtifacts);
        if (tallyReadyArtifact is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Finalization requires the exact tally-ready boundary artifact to exist.");
        }

        if (tallyReadyArtifact.AcceptedBallotSetHash is not { Length: > 0 })
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Finalization requires the tally-ready accepted ballot set hash.");
        }

        if (tallyReadyArtifact.FinalEncryptedTallyHash is not { Length: > 0 })
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Finalization requires the tally-ready final encrypted tally hash.");
        }

        if (acceptedBallotSetHash is { Length: > 0 } &&
            !ByteArrayEquals(acceptedBallotSetHash, tallyReadyArtifact.AcceptedBallotSetHash))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Finalization request accepted ballot set hash does not match the tally-ready target.");
        }

        if (finalEncryptedTallyHash is { Length: > 0 } &&
            !ByteArrayEquals(finalEncryptedTallyHash, tallyReadyArtifact.FinalEncryptedTallyHash))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Finalization request encrypted tally hash does not match the tally-ready target.");
        }

        ElectionCeremonyBindingSnapshot? ceremonySnapshot = null;
        var requiredShareCount = 0;
        var eligibleTrustees = Array.Empty<ElectionTrusteeReference>();
        if (election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold)
        {
            var openArtifact = boundaryArtifacts.FirstOrDefault(x =>
                x.Id == election.OpenArtifactId &&
                x.ArtifactType == ElectionBoundaryArtifactType.Open);
            ceremonySnapshot = openArtifact?.CeremonySnapshot;

            if (ceremonySnapshot is null)
            {
                return ElectionCommandResult.Failure(
                    ElectionCommandErrorCode.DependencyBlocked,
                    "Trustee-threshold finalization requires the FEAT-097 open-boundary ceremony binding.");
            }

            requiredShareCount = ceremonySnapshot.RequiredApprovalCount;
            eligibleTrustees = ceremonySnapshot.ActiveTrustees.ToArray();
        }

        var createdAt = DateTime.UtcNow;
        var session = ElectionModelFactory.CreateFinalizationSession(
            election,
            closeArtifact.Id,
            tallyReadyArtifact.AcceptedBallotSetHash,
            tallyReadyArtifact.FinalEncryptedTallyHash,
            ElectionFinalizationSessionPurpose.Finalization,
            ceremonySnapshot,
            requiredShareCount,
            eligibleTrustees,
            actorPublicAddress,
            governedProposalId,
            createdAt,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);

        var updatedElection = election with
        {
            LastUpdatedAt = createdAt,
        };

        await repository.SaveFinalizationSessionAsync(session);
        await repository.SaveElectionAsync(updatedElection);

        if (election.GovernanceMode == ElectionGovernanceMode.AdminOnly)
        {
            return await CompleteFinalizationSessionAsync(
                repository,
                updatedElection,
                session,
                Array.Empty<ElectionTrusteeReference>(),
                actorPublicAddress,
                createdAt,
                sourceTransactionId,
                sourceBlockHeight,
                sourceBlockId,
                finalizationShare: null);
        }

        return ElectionCommandResult.Success(
            updatedElection,
            finalizationSession: session);
    }

    private async Task<ElectionCommandResult> CompleteFinalizationSessionAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionFinalizationSessionRecord session,
        IReadOnlyList<ElectionTrusteeReference> acceptedTrustees,
        string completedByPublicAddress,
        DateTime completedAt,
        Guid? sourceTransactionId,
        long? sourceBlockHeight,
        Guid? sourceBlockId,
        ElectionFinalizationShareRecord? finalizationShare)
    {
        var releaseEvidence = ElectionModelFactory.CreateFinalizationReleaseEvidence(
            session,
            acceptedTrustees,
            completedByPublicAddress,
            completedAt,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
        var completedSession = session.MarkCompleted(
            releaseEvidence.Id,
            completedAt,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
        var artifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Finalize,
            election,
            completedByPublicAddress,
            ceremonySnapshot: session.CeremonySnapshot,
            recordedAt: completedAt,
            acceptedBallotSetHash: session.AcceptedBallotSetHash,
            finalEncryptedTallyHash: session.FinalEncryptedTallyHash,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);
        var finalizedElection = ApplyLifecycleTransition(election, artifact, completedAt);

        await repository.SaveFinalizationReleaseEvidenceRecordAsync(releaseEvidence);
        await repository.UpdateFinalizationSessionAsync(completedSession);
        await repository.SaveBoundaryArtifactAsync(artifact);
        await repository.SaveElectionAsync(finalizedElection);

        return ElectionCommandResult.Success(
            finalizedElection,
            boundaryArtifact: artifact,
            finalizationSession: completedSession,
            finalizationShare: finalizationShare,
            finalizationReleaseEvidence: releaseEvidence);
    }

    private async Task<ElectionCommandResult> CompleteCloseCountingSessionAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionFinalizationSessionRecord session,
        IReadOnlyList<ElectionFinalizationShareRecord> acceptedShares,
        IReadOnlyList<ElectionTrusteeReference> acceptedTrustees,
        string completedByPublicAddress,
        DateTime completedAt,
        Guid? sourceTransactionId,
        long? sourceBlockHeight,
        Guid? sourceBlockId,
        ElectionFinalizationShareRecord? finalizationShare)
    {
        if (_electionResultCryptoService is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Close-counting completion requires the FEAT-101 result crypto service.");
        }

        var boundaryArtifacts = await repository.GetBoundaryArtifactsAsync(election.ElectionId);
        var closeArtifact = boundaryArtifacts.FirstOrDefault(x =>
            x.Id == session.CloseArtifactId &&
            x.ArtifactType == ElectionBoundaryArtifactType.Close);
        if (closeArtifact is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Close-counting completion requires the exact close boundary artifact.");
        }

        if (ResolveTallyReadyArtifact(election, boundaryArtifacts) is not null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "Close-counting completion cannot run after tally_ready already exists.");
        }

        var existingUnofficial = election.UnofficialResultArtifactId.HasValue
            ? await repository.GetResultArtifactAsync(election.UnofficialResultArtifactId.Value)
            : await repository.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Unofficial);
        if (existingUnofficial is not null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.Conflict,
                "Close-counting completion cannot run after an unofficial result already exists.");
        }

        var acceptedBallots = await repository.GetAcceptedBallotsAsync(election.ElectionId);
        var publishedBallots = await repository.GetPublishedBallotsAsync(election.ElectionId);
        if (acceptedBallots.Count != publishedBallots.Count)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Close-counting completion requires accepted and published ballot inventories to match.");
        }

        var closeSnapshot = await repository.GetEligibilitySnapshotAsync(
            election.ElectionId,
            ElectionEligibilitySnapshotType.Close);
        if (closeSnapshot is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Close-counting completion requires the close eligibility snapshot.");
        }

        var ownerAccess = await repository.GetElectionEnvelopeAccessAsync(election.ElectionId, election.OwnerPublicAddress);
        if (ownerAccess is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.DependencyBlocked,
                "Close-counting completion requires the owner election envelope access record.");
        }

        var acceptedHash = ComputeAcceptedBallotInventoryHash(acceptedBallots);
        if (!ByteArrayEquals(acceptedHash, session.AcceptedBallotSetHash))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Close-counting completion inventory does not match the bound accepted-ballot set hash.");
        }

        var publishedHash = ComputePublishedBallotStreamHash(publishedBallots);
        var release = _electionResultCryptoService.TryReleaseAggregateTally(
            publishedBallots.Select(x => x.EncryptedBallotPackage).ToArray(),
            acceptedShares,
            acceptedBallots.Count);
        if (!release.IsSuccessful)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                $"Close-counting aggregate release failed: {release.FailureReason}");
        }

        if (!ByteArrayEquals(release.FinalEncryptedTallyHash, session.FinalEncryptedTallyHash))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Close-counting aggregate release hash does not match the bound tally target.");
        }

        var decodedCounts = release.DecodedCounts ?? Array.Empty<int>();
        if (decodedCounts.Count != election.Options.Count)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                $"Close-counting aggregate release did not return a count for every ballot option. Expected {election.Options.Count} slots but received {decodedCounts.Count}.");
        }

        var optionCounts = election.Options
            .Select((option, index) => new { Option = option, Count = decodedCounts[index] })
            .ToArray();
        var blankOption = optionCounts.FirstOrDefault(x => x.Option.IsBlankOption);
        var namedOptionResults = optionCounts
            .Where(x => !x.Option.IsBlankOption)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Option.BallotOrder)
            .Select((x, index) => new ElectionResultOptionCount(
                x.Option.OptionId,
                x.Option.DisplayLabel,
                x.Option.ShortDescription,
                x.Option.BallotOrder,
                index + 1,
                x.Count))
            .ToArray();

        var blankCount = blankOption?.Count ?? 0;
        var totalVotedCount = decodedCounts.Sum();
        var eligibleToVoteCount = closeSnapshot.ActiveDenominatorCount;
        var didNotVoteCount = closeSnapshot.DidNotVoteCount;
        if (totalVotedCount != namedOptionResults.Sum(x => x.VoteCount) + blankCount)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Close-counting totals do not match the decoded named-option and blank counts.");
        }

        if (didNotVoteCount != eligibleToVoteCount - totalVotedCount)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Close-counting denominator totals do not reconcile with did-not-vote count.");
        }

        if (totalVotedCount != closeSnapshot.CountedParticipationCount)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Close-counting totals do not reconcile with the close participation snapshot.");
        }

        var tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.TallyReady,
            election,
            completedByPublicAddress,
            ceremonySnapshot: session.CeremonySnapshot,
            recordedAt: completedAt,
            acceptedBallotCount: acceptedBallots.Count,
            acceptedBallotSetHash: acceptedHash,
            publishedBallotCount: publishedBallots.Count,
            publishedBallotStreamHash: publishedHash,
            finalEncryptedTallyHash: release.FinalEncryptedTallyHash,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);
        var denominatorEvidence = new ElectionResultDenominatorEvidence(
            closeSnapshot.SnapshotType,
            closeSnapshot.Id,
            closeSnapshot.BoundaryArtifactId,
            closeSnapshot.ActiveDenominatorSetHash);
        var unofficialPayload = SerializeResultArtifactPayload(
            election.Title,
            namedOptionResults,
            blankCount,
            totalVotedCount,
            eligibleToVoteCount,
            didNotVoteCount,
            denominatorEvidence);
        var encryptedUnofficialPayload = _electionResultCryptoService.EncryptForElectionParticipants(
            unofficialPayload,
            ownerAccess.NodeEncryptedElectionPrivateKey);
        var unofficialResult = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.ParticipantEncrypted,
            election.Title,
            namedOptionResults,
            blankCount,
            totalVotedCount,
            eligibleToVoteCount,
            didNotVoteCount,
            denominatorEvidence,
            completedByPublicAddress,
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            encryptedPayload: encryptedUnofficialPayload,
            publicPayload: null,
            recordedAt: completedAt,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);

        var releaseEvidence = ElectionModelFactory.CreateFinalizationReleaseEvidence(
            session,
            acceptedTrustees,
            completedByPublicAddress,
            completedAt,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
        var completedSession = session.MarkCompleted(
            releaseEvidence.Id,
            completedAt,
            sourceTransactionId,
            sourceBlockHeight,
            sourceBlockId);
        var updatedElection = election with
        {
            LastUpdatedAt = completedAt,
            TallyReadyAt = completedAt,
            TallyReadyArtifactId = tallyReadyArtifact.Id,
            UnofficialResultArtifactId = unofficialResult.Id,
            ClosedProgressStatus = ElectionClosedProgressStatus.None,
        };

        await repository.SaveFinalizationReleaseEvidenceRecordAsync(releaseEvidence);
        await repository.UpdateFinalizationSessionAsync(completedSession);
        await repository.SaveBoundaryArtifactAsync(tallyReadyArtifact);
        await repository.SaveResultArtifactAsync(unofficialResult);
        await repository.SaveElectionAsync(updatedElection);

        return ElectionCommandResult.Success(
            updatedElection,
            boundaryArtifact: tallyReadyArtifact,
            finalizationSession: completedSession,
            finalizationShare: finalizationShare,
            finalizationReleaseEvidence: releaseEvidence);
    }

    private async Task<ReportPackageFinalizationContext> ResolveReportPackageFinalizationContextAsync(
        IElectionsRepository repository,
        ElectionId electionId,
        ElectionBoundaryArtifactRecord tallyReadyArtifact)
    {
        var finalizationSessions = await repository.GetFinalizationSessionsAsync(electionId);
        var finalizationReleaseEvidenceRecords = await repository.GetFinalizationReleaseEvidenceRecordsAsync(electionId);

        var releaseEvidence = finalizationReleaseEvidenceRecords
            .Where(x =>
                x.CloseArtifactId == tallyReadyArtifact.Id ||
                ByteArrayEquals(x.AcceptedBallotSetHash, tallyReadyArtifact.AcceptedBallotSetHash) ||
                ByteArrayEquals(x.FinalEncryptedTallyHash, tallyReadyArtifact.FinalEncryptedTallyHash))
            .OrderByDescending(x => x.CompletedAt)
            .FirstOrDefault();

        if (releaseEvidence is null)
        {
            return new ReportPackageFinalizationContext(null, null);
        }

        var session = finalizationSessions.FirstOrDefault(x => x.Id == releaseEvidence.FinalizationSessionId);
        return new ReportPackageFinalizationContext(session, releaseEvidence);
    }

    private static int ResolveExpectedFinalizationShareIndex(
        ElectionFinalizationSessionRecord session,
        string trusteeUserAddress)
    {
        for (var index = 0; index < session.EligibleTrustees.Count; index++)
        {
            if (string.Equals(
                    session.EligibleTrustees[index].TrusteeUserAddress,
                    trusteeUserAddress,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index + 1;
            }
        }

        return -1;
    }

    private static ElectionBoundaryArtifactRecord? ResolveTallyReadyArtifact(
        ElectionRecord election,
        IReadOnlyList<ElectionBoundaryArtifactRecord> boundaryArtifacts)
    {
        if (election.TallyReadyArtifactId.HasValue)
        {
            return boundaryArtifacts.FirstOrDefault(x =>
                x.Id == election.TallyReadyArtifactId.Value &&
                x.ArtifactType == ElectionBoundaryArtifactType.TallyReady);
        }

        return boundaryArtifacts
            .Where(x => x.ArtifactType == ElectionBoundaryArtifactType.TallyReady)
            .OrderByDescending(x => x.RecordedAt)
            .FirstOrDefault();
    }

    private static FinalizationShareValidationOutcome ValidateFinalizationShareSubmission(
        ElectionFinalizationSessionRecord session,
        SubmitElectionFinalizationShareRequest request,
        int expectedShareIndex)
    {
        if (request.TargetType != ElectionFinalizationTargetType.AggregateTally)
        {
            return FinalizationShareValidationOutcome.Rejected(
                "SINGLE_BALLOT_RELEASE_FORBIDDEN",
                "Only aggregate-tally release targets are allowed.");
        }

        if (request.ShareIndex < 1 ||
            string.IsNullOrWhiteSpace(request.ShareVersion) ||
            string.IsNullOrWhiteSpace(request.ShareMaterial) ||
            request.ClaimedAcceptedBallotSetHash is not { Length: > 0 } ||
            request.ClaimedFinalEncryptedTallyHash is not { Length: > 0 } ||
            string.IsNullOrWhiteSpace(request.ClaimedTargetTallyId))
        {
            return FinalizationShareValidationOutcome.Rejected(
                "MALFORMED_SHARE",
                "Finalization share submission is missing required share or target fields.");
        }

        if (expectedShareIndex < 1 || request.ShareIndex != expectedShareIndex)
        {
            return FinalizationShareValidationOutcome.Rejected(
                "MALFORMED_SHARE",
                "Finalization share index does not match the bound trustee position for this session.");
        }

        if (request.ClaimedCloseArtifactId != session.CloseArtifactId ||
            !ByteArrayEquals(request.ClaimedAcceptedBallotSetHash, session.AcceptedBallotSetHash) ||
            !ByteArrayEquals(request.ClaimedFinalEncryptedTallyHash, session.FinalEncryptedTallyHash) ||
            !string.Equals(request.ClaimedTargetTallyId.Trim(), session.TargetTallyId, StringComparison.Ordinal))
        {
            return FinalizationShareValidationOutcome.Rejected(
                "WRONG_TARGET_SHARE",
                "Finalization share submission does not match the exact close-boundary target.");
        }

        if (session.CeremonySnapshot is not null &&
            (request.ClaimedCeremonyVersionId != session.CeremonySnapshot.CeremonyVersionId ||
             !string.Equals(
                 request.ClaimedTallyPublicKeyFingerprint?.Trim(),
                 session.CeremonySnapshot.TallyPublicKeyFingerprint,
                 StringComparison.Ordinal)))
        {
            return FinalizationShareValidationOutcome.Rejected(
                "WRONG_TARGET_SHARE",
                "Finalization share submission does not match the exact session ceremony binding.");
        }

        return FinalizationShareValidationOutcome.Accepted();
    }

    private static byte[] ComputeAcceptedBallotInventoryHash(
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots)
    {
        var payload = string.Join(
            '\n',
            acceptedBallots
                .OrderBy(x => x.BallotNullifier, StringComparer.Ordinal)
                .Select(x => $"{x.BallotNullifier}|{ComputeHexSha256(x.EncryptedBallotPackage)}|{ComputeHexSha256(x.ProofBundle)}"));

        return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    }

    private static byte[] ComputePublishedBallotStreamHash(
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots)
    {
        var payload = string.Join(
            '\n',
            publishedBallots
                .OrderBy(x => x.PublicationSequence)
                .Select(x => $"{x.PublicationSequence}|{ComputeHexSha256(x.EncryptedBallotPackage)}|{ComputeHexSha256(x.ProofBundle)}"));

        return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    }

    private static string ComputeHexSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private static string SerializeResultArtifactPayload(ElectionResultArtifactRecord resultArtifact) =>
        SerializeResultArtifactPayload(
            resultArtifact.Title,
            resultArtifact.NamedOptionResults,
            resultArtifact.BlankCount,
            resultArtifact.TotalVotedCount,
            resultArtifact.EligibleToVoteCount,
            resultArtifact.DidNotVoteCount,
            resultArtifact.DenominatorEvidence);

    private static string SerializeResultArtifactPayload(
        string title,
        IReadOnlyList<ElectionResultOptionCount> namedOptionResults,
        int blankCount,
        int totalVotedCount,
        int eligibleToVoteCount,
        int didNotVoteCount,
        ElectionResultDenominatorEvidence denominatorEvidence) =>
        JsonSerializer.Serialize(
            new ResultArtifactPayload(
                title,
                namedOptionResults
                    .Select(x => new ResultOptionPayload(
                        x.OptionId,
                        x.DisplayLabel,
                        x.ShortDescription,
                        x.BallotOrder,
                        x.Rank,
                        x.VoteCount))
                    .ToArray(),
                blankCount,
                totalVotedCount,
                eligibleToVoteCount,
                didNotVoteCount,
                new ResultDenominatorEvidencePayload(
                    denominatorEvidence.SnapshotType.ToString(),
                    denominatorEvidence.EligibilitySnapshotId,
                    denominatorEvidence.BoundaryArtifactId,
                    Convert.ToHexString(denominatorEvidence.ActiveDenominatorSetHash))),
            ResultPayloadJsonOptions);

    private static bool ByteArrayEquals(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private sealed record ReportPackageFinalizationContext(
        ElectionFinalizationSessionRecord? Session,
        ElectionFinalizationReleaseEvidenceRecord? ReleaseEvidence);

    private sealed record FinalizationShareValidationOutcome(
        bool IsAccepted,
        string? FailureCode,
        string? FailureReason)
    {
        public static FinalizationShareValidationOutcome Accepted() =>
            new(true, null, null);

        public static FinalizationShareValidationOutcome Rejected(string failureCode, string failureReason) =>
            new(false, failureCode, failureReason);
    }

    private sealed record ResultArtifactPayload(
        string Title,
        IReadOnlyList<ResultOptionPayload> NamedOptionResults,
        int BlankCount,
        int TotalVotedCount,
        int EligibleToVoteCount,
        int DidNotVoteCount,
        ResultDenominatorEvidencePayload DenominatorEvidence);

    private sealed record ResultOptionPayload(
        string OptionId,
        string DisplayLabel,
        string? ShortDescription,
        int BallotOrder,
        int Rank,
        int VoteCount);

    private sealed record ResultDenominatorEvidencePayload(
        string SnapshotType,
        Guid? EligibilitySnapshotId,
        Guid? BoundaryArtifactId,
        string ActiveDenominatorSetHashHex);

    private sealed record ActiveCeremonyTrusteeContext(
        ElectionRecord Election,
        ElectionCeremonyVersionRecord Version,
        ElectionCeremonyTrusteeStateRecord? TrusteeState);

    private sealed record CeremonyTrusteeUpdateOutcome(
        ElectionCeremonyTrusteeStateRecord TrusteeState,
        ElectionCeremonyTranscriptEventRecord TranscriptEvent);

    private sealed record CeremonyTrusteeContextLoadResult(
        bool IsSuccess,
        ActiveCeremonyTrusteeContext? Context,
        ElectionCommandResult? ErrorResult)
    {
        public static CeremonyTrusteeContextLoadResult Success(ActiveCeremonyTrusteeContext context) =>
            new(true, context, null);

        public static CeremonyTrusteeContextLoadResult NotFound(string message) =>
            Failure(ElectionCommandErrorCode.NotFound, message);

        public static CeremonyTrusteeContextLoadResult Forbidden(string message) =>
            Failure(ElectionCommandErrorCode.Forbidden, message);

        public static CeremonyTrusteeContextLoadResult InvalidState(string message) =>
            Failure(ElectionCommandErrorCode.InvalidState, message);

        public static CeremonyTrusteeContextLoadResult Conflict(string message) =>
            Failure(ElectionCommandErrorCode.Conflict, message);

        private static CeremonyTrusteeContextLoadResult Failure(ElectionCommandErrorCode code, string message) =>
            new(false, null, ElectionCommandResult.Failure(code, message));
    }

    private sealed record ShareCustodyContextLoadResult(
        bool IsSuccess,
        ElectionRecord? Election,
        ElectionCeremonyVersionRecord? Version,
        ElectionCeremonyShareCustodyRecord? CustodyRecord,
        ElectionCommandResult? ErrorResult)
    {
        public static ShareCustodyContextLoadResult Success(
            ElectionRecord election,
            ElectionCeremonyVersionRecord version,
            ElectionCeremonyShareCustodyRecord custodyRecord) =>
            new(true, election, version, custodyRecord, null);

        public static ShareCustodyContextLoadResult NotFound(string message) =>
            Failure(ElectionCommandErrorCode.NotFound, message);

        public static ShareCustodyContextLoadResult Forbidden(string message) =>
            Failure(ElectionCommandErrorCode.Forbidden, message);

        public static ShareCustodyContextLoadResult InvalidState(string message) =>
            Failure(ElectionCommandErrorCode.InvalidState, message);

        public static ShareCustodyContextLoadResult ValidationFailed(string message) =>
            Failure(ElectionCommandErrorCode.ValidationFailed, message);

        private static ShareCustodyContextLoadResult Failure(ElectionCommandErrorCode code, string message) =>
            new(false, null, null, null, ElectionCommandResult.Failure(code, message));
    }
}
