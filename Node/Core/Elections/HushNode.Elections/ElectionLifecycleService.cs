using System.Data;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class ElectionLifecycleService : IElectionLifecycleService
{
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider;
    private readonly ILogger<ElectionLifecycleService> _logger;
    private readonly ElectionCeremonyOptions _ceremonyOptions;

    public ElectionLifecycleService(
        IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
        ILogger<ElectionLifecycleService> logger)
        : this(unitOfWorkProvider, logger, new ElectionCeremonyOptions())
    {
    }

    public ElectionLifecycleService(
        IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
        ILogger<ElectionLifecycleService> logger,
        ElectionCeremonyOptions ceremonyOptions)
    {
        _unitOfWorkProvider = unitOfWorkProvider;
        _logger = logger;
        _ceremonyOptions = ceremonyOptions;
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
            updatedState = ceremonyContext.TrusteeState!.RecordMaterialSubmitted(submittedAt);
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
        var warningAcknowledgements = await repository.GetWarningAcknowledgementsAsync(request.ElectionId);
        var activeCeremonyVersion = await repository.GetActiveCeremonyVersionAsync(request.ElectionId);
        var activeCeremonyTrusteeStates = activeCeremonyVersion is null
            ? Array.Empty<ElectionCeremonyTrusteeStateRecord>()
            : await repository.GetCeremonyTrusteeStatesAsync(activeCeremonyVersion.Id);

        return EvaluateOpenReadiness(
            election,
            invitations,
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
                governedProposal: executionOutcome.Proposal,
                governedProposalApproval: approval);
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
            governedProposal: executionOutcome.Proposal);
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

    public Task<ElectionCommandResult> FinalizeElectionAsync(FinalizeElectionRequest request) =>
        TransitionElectionAsync(
            request.ElectionId,
            request.ActorPublicAddress,
            ElectionLifecycleState.Closed,
            ElectionBoundaryArtifactType.Finalize,
            request.AcceptedBallotSetHash,
            request.FinalEncryptedTallyHash,
            allowTrusteeThresholdExecution: false,
            request.SourceTransactionId,
            request.SourceBlockHeight,
            request.SourceBlockId);

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
                TallyReadyAt = artifact.FinalEncryptedTallyHash is { Length: > 0 } ? transitionTime : null,
                CloseArtifactId = artifact.Id,
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
                var warningAcknowledgements = await repository.GetWarningAcknowledgementsAsync(election.ElectionId);
                var activeCeremonyVersion = await repository.GetActiveCeremonyVersionAsync(election.ElectionId);
                var activeCeremonyTrusteeStates = activeCeremonyVersion is null
                    ? Array.Empty<ElectionCeremonyTrusteeStateRecord>()
                    : await repository.GetCeremonyTrusteeStatesAsync(activeCeremonyVersion.Id);
                var readiness = EvaluateOpenReadiness(
                    election,
                    invitations,
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
                    ? null
                    : ElectionCommandResult.Failure(
                        ElectionCommandErrorCode.InvalidState,
                        "Governed finalize proposals are only allowed when the election is tally ready.");

            default:
                throw new ArgumentOutOfRangeException(nameof(actionType), actionType, "Unsupported governed action type.");
        }
    }

    private async Task<(ElectionRecord Election, ElectionGovernedProposalRecord Proposal, ElectionBoundaryArtifactRecord? BoundaryArtifact)> ExecuteGovernedProposalAndPersistOutcomeAsync(
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
                return (election, failedProposal, null);
            }

            var succeededProposal = proposal.RecordExecutionSuccess(
                DateTime.UtcNow,
                executionTriggeredByPublicAddress,
                latestTransactionId: sourceTransactionId,
                latestBlockHeight: sourceBlockHeight,
                latestBlockId: sourceBlockId);
            await repository.UpdateGovernedProposalAsync(succeededProposal);
            return (executionResult.Election, succeededProposal, executionResult.BoundaryArtifact);
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
            return (election, failedProposal, null);
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
            ElectionGovernedActionType.Finalize => ExecuteTransitionInternalAsync(
                repository,
                election,
                proposal.ProposedByPublicAddress,
                ElectionLifecycleState.Closed,
                ElectionBoundaryArtifactType.Finalize,
                acceptedBallotSetHash: null,
                finalEncryptedTallyHash: null,
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
        var warningAcknowledgements = await repository.GetWarningAcknowledgementsAsync(election.ElectionId);
        var activeCeremonyVersion = await repository.GetActiveCeremonyVersionAsync(election.ElectionId);
        var activeCeremonyTrusteeStates = activeCeremonyVersion is null
            ? Array.Empty<ElectionCeremonyTrusteeStateRecord>()
            : await repository.GetCeremonyTrusteeStatesAsync(activeCeremonyVersion.Id);
        var readiness = EvaluateOpenReadiness(
            election,
            invitations,
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
        var artifact = ElectionModelFactory.CreateBoundaryArtifact(
            artifactType: ElectionBoundaryArtifactType.Open,
            election: election,
            recordedByPublicAddress: actorPublicAddress,
            recordedAt: openedAt,
            trusteeSnapshot: CreateTrusteeSnapshot(election, invitations, readiness.CeremonySnapshot),
            ceremonySnapshot: readiness.CeremonySnapshot,
            frozenEligibleVoterSetHash: frozenEligibleVoterSetHash,
            trusteePolicyExecutionReference: trusteePolicyExecutionReference,
            reportingPolicyExecutionReference: reportingPolicyExecutionReference,
            reviewWindowExecutionReference: reviewWindowExecutionReference,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);

        var openedElection = election with
        {
            LifecycleState = ElectionLifecycleState.Open,
            LastUpdatedAt = openedAt,
            OpenedAt = openedAt,
            VoteAcceptanceLockedAt = null,
            OpenArtifactId = artifact.Id,
        };

        await repository.SaveBoundaryArtifactAsync(artifact);
        await repository.SaveElectionAsync(openedElection);

        return ElectionCommandResult.Success(openedElection, boundaryArtifact: artifact);
    }

    private static ElectionCommandResult? ValidateDraftNotBlockedByGovernedOpenProposal(ElectionGovernedProposalRecord? pendingProposal) =>
        pendingProposal?.ActionType == ElectionGovernedActionType.Open
            ? ElectionCommandResult.Failure(
                ElectionCommandErrorCode.InvalidState,
                "Draft election changes are blocked while a governed open proposal is pending.")
            : null;

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
        ElectionCeremonyVersionRecord? activeCeremonyVersion,
        IReadOnlyList<ElectionCeremonyTrusteeStateRecord> activeCeremonyTrusteeStates,
        IReadOnlyList<ElectionWarningAcknowledgementRecord> warningAcknowledgements,
        IReadOnlyList<ElectionWarningCode>? requestedWarnings,
        bool blockGovernedWorkflowMissing)
    {
        var errors = new List<string>();
        var requiredWarnings = NormalizeWarningCodes(requestedWarnings).ToList();
        var nonBlankOptions = election.Options.Where(x => !x.IsBlankOption).ToArray();
        ElectionCeremonyBindingSnapshot? ceremonySnapshot = null;

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

        var transitionTime = DateTime.UtcNow;
        var artifact = ElectionModelFactory.CreateBoundaryArtifact(
            artifactType,
            election,
            actorPublicAddress,
            recordedAt: transitionTime,
            acceptedBallotSetHash: acceptedBallotSetHash,
            finalEncryptedTallyHash: finalEncryptedTallyHash,
            sourceTransactionId: sourceTransactionId,
            sourceBlockHeight: sourceBlockHeight,
            sourceBlockId: sourceBlockId);

        var updatedElection = ApplyLifecycleTransition(election, artifact, transitionTime);

        await repository.SaveBoundaryArtifactAsync(artifact);
        await repository.SaveElectionAsync(updatedElection);

        return ElectionCommandResult.Success(updatedElection, boundaryArtifact: artifact);
    }

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
