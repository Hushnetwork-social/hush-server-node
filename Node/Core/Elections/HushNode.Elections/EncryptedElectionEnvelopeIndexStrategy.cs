using System.Text;
using HushNode.Caching;
using HushNode.Elections.Storage;
using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class EncryptedElectionEnvelopeIndexStrategy(
    IElectionEnvelopeCryptoService envelopeCryptoService,
    IElectionLifecycleService electionLifecycleService,
    IBlockchainCache blockchainCache,
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
    ILogger<EncryptedElectionEnvelopeIndexStrategy> logger) : IIndexStrategy
{
    private readonly IElectionEnvelopeCryptoService _envelopeCryptoService = envelopeCryptoService;
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly ILogger<EncryptedElectionEnvelopeIndexStrategy> _logger = logger;

    public bool CanHandle(AbstractTransaction transaction) =>
        EncryptedElectionEnvelopePayloadHandler.EncryptedElectionEnvelopePayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction)
    {
        var decryptedEnvelope = _envelopeCryptoService.TryDecryptValidated(transaction);
        if (decryptedEnvelope is null)
        {
            return;
        }

        ElectionCommandResult result = decryptedEnvelope.ActionType switch
        {
            EncryptedElectionEnvelopeActionTypes.CreateDraft =>
                await HandleCreateDraftAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.UpdateDraft =>
                await HandleUpdateDraftAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.ImportRoster =>
                await HandleImportRosterAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.ClaimRosterEntry =>
                await HandleClaimRosterEntryAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.ActivateRosterEntry =>
                await HandleActivateRosterEntryAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RegisterVotingCommitment =>
                await HandleRegisterVotingCommitmentAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.AcceptBallotCast =>
                await HandleAcceptBallotCastAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.InviteTrustee =>
                await HandleInviteTrusteeAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.CreateReportAccessGrant =>
                await HandleCreateReportAccessGrantAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.AcceptTrusteeInvitation =>
                await HandleAcceptTrusteeInvitationAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RejectTrusteeInvitation =>
                await HandleRejectTrusteeInvitationAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RevokeTrusteeInvitation =>
                await HandleRevokeTrusteeInvitationAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.StartGovernedProposal =>
                await HandleStartGovernedProposalAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.ApproveGovernedProposal =>
                await HandleApproveGovernedProposalAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RetryGovernedProposalExecution =>
                await HandleRetryGovernedProposalExecutionAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.OpenElection =>
                await HandleOpenElectionAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.CloseElection =>
                await HandleCloseElectionAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.FinalizeElection =>
                await HandleFinalizeElectionAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.SubmitFinalizationShare =>
                await HandleSubmitFinalizationShareAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.StartCeremony =>
                await HandleStartCeremonyAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RestartCeremony =>
                await HandleRestartCeremonyAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.PublishCeremonyTransportKey =>
                await HandlePublishCeremonyTransportKeyAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.JoinCeremony =>
                await HandleJoinCeremonyAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RecordCeremonySelfTestSuccess =>
                await HandleRecordCeremonySelfTestAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.SubmitCeremonyMaterial =>
                await HandleSubmitCeremonyMaterialAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RecordCeremonyValidationFailure =>
                await HandleRecordCeremonyValidationFailureAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.CompleteCeremonyTrustee =>
                await HandleCompleteCeremonyTrusteeAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RecordCeremonyShareExport =>
                await HandleRecordCeremonyShareExportAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RecordCeremonyShareImport =>
                await HandleRecordCeremonyShareImportAsync(decryptedEnvelope),
            _ => ElectionCommandResult.Failure(
                ElectionCommandErrorCode.NotSupported,
                $"Unsupported encrypted election action type {decryptedEnvelope.ActionType}."),
        };

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "[EncryptedElectionEnvelopeIndexStrategy] Failed to index encrypted election envelope {TransactionId} ({ActionType}): {ErrorCode} {ErrorMessage}",
                decryptedEnvelope.Transaction.TransactionId,
                decryptedEnvelope.ActionType,
                result.ErrorCode,
                result.ErrorMessage);
        }
    }

    private async Task<ElectionCommandResult> HandleCreateDraftAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var createDraftAction = decryptedEnvelope.DeserializeAction<CreateElectionDraftActionPayload>();
        if (createDraftAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Create draft action payload could not be deserialized.");
        }

        var result = await _electionLifecycleService.CreateDraftAsync(new CreateElectionDraftRequest(
            OwnerPublicAddress: createDraftAction.OwnerPublicAddress,
            ActorPublicAddress: createDraftAction.OwnerPublicAddress,
            SnapshotReason: createDraftAction.SnapshotReason,
            Draft: createDraftAction.Draft,
            PreassignedElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (result.IsSuccess)
        {
            await SaveElectionEnvelopeAccessAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                createDraftAction.OwnerPublicAddress,
                ResolveAccessEncryptionMaterial(decryptedEnvelope.Transaction.Payload),
                decryptedEnvelope.Transaction.Payload.ActorEncryptedElectionPrivateKey,
                decryptedEnvelope.Transaction.TransactionTimeStamp.Value,
                decryptedEnvelope.Transaction.TransactionId.Value);
        }

        return result;
    }

    private async Task<ElectionCommandResult> HandleUpdateDraftAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var updateDraftAction = decryptedEnvelope.DeserializeAction<UpdateElectionDraftActionPayload>();
        if (updateDraftAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Update draft action payload could not be deserialized.");
        }

        return await _electionLifecycleService.UpdateDraftAsync(new UpdateElectionDraftRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ActorPublicAddress: updateDraftAction.ActorPublicAddress,
            SnapshotReason: updateDraftAction.SnapshotReason,
            Draft: updateDraftAction.Draft,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleImportRosterAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var importAction = decryptedEnvelope.DeserializeAction<ImportElectionRosterActionPayload>();
        if (importAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Import roster action payload could not be deserialized.");
        }

        return await _electionLifecycleService.ImportRosterAsync(new ImportElectionRosterRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ActorPublicAddress: importAction.ActorPublicAddress,
            RosterEntries: importAction.RosterEntries,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleClaimRosterEntryAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var claimAction = decryptedEnvelope.DeserializeAction<ClaimElectionRosterEntryActionPayload>();
        if (claimAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Claim roster entry action payload could not be deserialized.");
        }

        var result = await _electionLifecycleService.ClaimRosterEntryAsync(new ClaimElectionRosterEntryRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ActorPublicAddress: claimAction.ActorPublicAddress,
            OrganizationVoterId: claimAction.OrganizationVoterId,
            VerificationCode: ResolveClaimVerificationCode(
                decryptedEnvelope.Transaction.Payload,
                claimAction.VerificationCode),
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (result.IsSuccess &&
            !await ShouldRetainElectionEnvelopeAccessAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                claimAction.ActorPublicAddress))
        {
            await DeleteElectionEnvelopeAccessAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                claimAction.ActorPublicAddress,
                decryptedEnvelope.Transaction.TransactionId.Value);
        }

        return result;
    }

    private async Task<ElectionCommandResult> HandleActivateRosterEntryAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var activateAction = decryptedEnvelope.DeserializeAction<ActivateElectionRosterEntryActionPayload>();
        if (activateAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Activate roster entry action payload could not be deserialized.");
        }

        return await _electionLifecycleService.ActivateRosterEntryAsync(new ActivateElectionRosterEntryRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ActorPublicAddress: activateAction.ActorPublicAddress,
            OrganizationVoterId: activateAction.OrganizationVoterId,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleRegisterVotingCommitmentAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var registerAction = decryptedEnvelope.DeserializeAction<RegisterElectionVotingCommitmentActionPayload>();
        if (registerAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Register voting commitment action payload could not be deserialized.");
        }

        var result = await _electionLifecycleService.RegisterVotingCommitmentAsync(
            new RegisterElectionVotingCommitmentRequest(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                registerAction.ActorPublicAddress,
                registerAction.CommitmentHash));

        return result.IsSuccess && result.Election is not null
            ? ElectionCommandResult.Success(result.Election, rosterEntry: result.RosterEntry)
            : ElectionCommandResult.Failure(
                result.FailureReason switch
                {
                    ElectionCommitmentRegistrationFailureReason.NotFound => ElectionCommandErrorCode.NotFound,
                    ElectionCommitmentRegistrationFailureReason.NotLinked => ElectionCommandErrorCode.Forbidden,
                    ElectionCommitmentRegistrationFailureReason.NotActive => ElectionCommandErrorCode.DependencyBlocked,
                    ElectionCommitmentRegistrationFailureReason.AlreadyRegistered => ElectionCommandErrorCode.Conflict,
                    ElectionCommitmentRegistrationFailureReason.ElectionNotOpenableForRegistration => ElectionCommandErrorCode.InvalidState,
                    ElectionCommitmentRegistrationFailureReason.ClosePersisted => ElectionCommandErrorCode.InvalidState,
                    _ => ElectionCommandErrorCode.ValidationFailed,
                },
                result.ErrorMessage ?? "Voting commitment registration failed.");
    }

    private async Task<ElectionCommandResult> HandleAcceptBallotCastAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var acceptAction = decryptedEnvelope.DeserializeAction<AcceptElectionBallotCastActionPayload>();
        if (acceptAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Accept ballot cast action payload could not be deserialized.");
        }

        var result = await _electionLifecycleService.AcceptBallotCastAsync(
            new AcceptElectionBallotCastRequest(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                acceptAction.ActorPublicAddress,
                acceptAction.IdempotencyKey,
                acceptAction.EncryptedBallotPackage,
                acceptAction.ProofBundle,
                acceptAction.BallotNullifier,
                acceptAction.OpenArtifactId,
                acceptAction.EligibleSetHash,
                acceptAction.CeremonyVersionId,
                acceptAction.DkgProfileId,
                acceptAction.TallyPublicKeyFingerprint));

        return result.IsSuccess && result.Election is not null
            ? ElectionCommandResult.Success(
                result.Election,
                rosterEntry: result.RosterEntry,
                participationRecord: result.ParticipationRecord)
            : ElectionCommandResult.Failure(
                result.FailureReason switch
                {
                    ElectionCastAcceptanceFailureReason.NotFound => ElectionCommandErrorCode.NotFound,
                    ElectionCastAcceptanceFailureReason.NotLinked => ElectionCommandErrorCode.Forbidden,
                    ElectionCastAcceptanceFailureReason.NotActive => ElectionCommandErrorCode.DependencyBlocked,
                    ElectionCastAcceptanceFailureReason.CommitmentMissing => ElectionCommandErrorCode.DependencyBlocked,
                    ElectionCastAcceptanceFailureReason.StillProcessing => ElectionCommandErrorCode.Conflict,
                    ElectionCastAcceptanceFailureReason.AlreadyUsed => ElectionCommandErrorCode.Conflict,
                    ElectionCastAcceptanceFailureReason.DuplicateNullifier => ElectionCommandErrorCode.Conflict,
                    ElectionCastAcceptanceFailureReason.WrongElectionContext => ElectionCommandErrorCode.ValidationFailed,
                    ElectionCastAcceptanceFailureReason.ClosePersisted => ElectionCommandErrorCode.InvalidState,
                    ElectionCastAcceptanceFailureReason.AlreadyVoted => ElectionCommandErrorCode.Conflict,
                    _ => ElectionCommandErrorCode.ValidationFailed,
                },
                result.ErrorMessage ?? "Ballot acceptance failed.");
    }

    private async Task<ElectionCommandResult> HandleInviteTrusteeAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var inviteTrusteeAction = decryptedEnvelope.DeserializeAction<InviteElectionTrusteeActionPayload>();
        if (inviteTrusteeAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Invite trustee action payload could not be deserialized.");
        }

        var result = await _electionLifecycleService.InviteTrusteeAsync(new InviteElectionTrusteeRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ActorPublicAddress: inviteTrusteeAction.ActorPublicAddress,
            TrusteeUserAddress: inviteTrusteeAction.TrusteeUserAddress,
            TrusteeDisplayName: inviteTrusteeAction.TrusteeDisplayName,
            PreassignedInvitationId: inviteTrusteeAction.InvitationId,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (result.IsSuccess)
        {
            var trusteeEncryptedElectionPrivateKey = ResolveTrusteeAccessEnvelope(
                decryptedEnvelope,
                inviteTrusteeAction);
            if (string.IsNullOrWhiteSpace(trusteeEncryptedElectionPrivateKey))
            {
                return ElectionCommandResult.Failure(
                    ElectionCommandErrorCode.ValidationFailed,
                    "Invite trustee action is missing trustee envelope access material.");
            }

            await SaveElectionEnvelopeAccessAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                inviteTrusteeAction.TrusteeUserAddress,
                ResolveAccessEncryptionMaterial(decryptedEnvelope.Transaction.Payload),
                trusteeEncryptedElectionPrivateKey,
                decryptedEnvelope.Transaction.TransactionTimeStamp.Value,
                decryptedEnvelope.Transaction.TransactionId.Value);
        }

        return result;
    }

    private async Task<ElectionCommandResult> HandleCreateReportAccessGrantAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var createGrantAction = decryptedEnvelope.DeserializeAction<CreateElectionReportAccessGrantActionPayload>();
        if (createGrantAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Create report access grant action payload could not be deserialized.");
        }

        return await _electionLifecycleService.CreateReportAccessGrantAsync(new CreateElectionReportAccessGrantRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ActorPublicAddress: createGrantAction.ActorPublicAddress,
            DesignatedAuditorPublicAddress: createGrantAction.DesignatedAuditorPublicAddress,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleAcceptTrusteeInvitationAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var resolveAction = decryptedEnvelope.DeserializeAction<ResolveElectionTrusteeInvitationActionPayload>();
        if (resolveAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Accept trustee invitation action payload could not be deserialized.");
        }

        return await _electionLifecycleService.AcceptTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            InvitationId: resolveAction.InvitationId,
            ActorPublicAddress: resolveAction.ActorPublicAddress,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleRejectTrusteeInvitationAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var resolveAction = decryptedEnvelope.DeserializeAction<ResolveElectionTrusteeInvitationActionPayload>();
        if (resolveAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Reject trustee invitation action payload could not be deserialized.");
        }

        return await _electionLifecycleService.RejectTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            InvitationId: resolveAction.InvitationId,
            ActorPublicAddress: resolveAction.ActorPublicAddress,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleRevokeTrusteeInvitationAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var revokeAction = decryptedEnvelope.DeserializeAction<RevokeElectionTrusteeInvitationActionPayload>();
        if (revokeAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Revoke trustee invitation action payload could not be deserialized.");
        }

        return await _electionLifecycleService.RevokeTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            InvitationId: revokeAction.InvitationId,
            ActorPublicAddress: revokeAction.ActorPublicAddress,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleStartGovernedProposalAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var startAction = decryptedEnvelope.DeserializeAction<StartElectionGovernedProposalActionPayload>();
        if (startAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Start governed proposal action payload could not be deserialized.");
        }

        return await _electionLifecycleService.StartGovernedProposalAsync(new StartElectionGovernedProposalRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ActionType: startAction.ActionType,
            ActorPublicAddress: startAction.ActorPublicAddress,
            PreassignedProposalId: startAction.ProposalId,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleApproveGovernedProposalAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var approveAction = decryptedEnvelope.DeserializeAction<ApproveElectionGovernedProposalActionPayload>();
        if (approveAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Approve governed proposal action payload could not be deserialized.");
        }

        return await _electionLifecycleService.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ProposalId: approveAction.ProposalId,
            ActorPublicAddress: approveAction.ActorPublicAddress,
            ApprovalNote: approveAction.ApprovalNote,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleRetryGovernedProposalExecutionAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var retryAction = decryptedEnvelope.DeserializeAction<RetryElectionGovernedProposalExecutionActionPayload>();
        if (retryAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Retry governed proposal execution action payload could not be deserialized.");
        }

        return await _electionLifecycleService.RetryGovernedProposalExecutionAsync(
            new RetryElectionGovernedProposalExecutionRequest(
                ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
                ProposalId: retryAction.ProposalId,
                ActorPublicAddress: retryAction.ActorPublicAddress,
                SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
                SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
                SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleOpenElectionAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var openAction = decryptedEnvelope.DeserializeAction<OpenElectionActionPayload>();
        if (openAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Open election action payload could not be deserialized.");
        }

        return await _electionLifecycleService.OpenElectionAsync(new OpenElectionRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ActorPublicAddress: openAction.ActorPublicAddress,
            RequiredWarningCodes: openAction.RequiredWarningCodes,
            FrozenEligibleVoterSetHash: openAction.FrozenEligibleVoterSetHash,
            TrusteePolicyExecutionReference: openAction.TrusteePolicyExecutionReference,
            ReportingPolicyExecutionReference: openAction.ReportingPolicyExecutionReference,
            ReviewWindowExecutionReference: openAction.ReviewWindowExecutionReference,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleCloseElectionAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var closeAction = decryptedEnvelope.DeserializeAction<CloseElectionActionPayload>();
        if (closeAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Close election action payload could not be deserialized.");
        }

        return await _electionLifecycleService.CloseElectionAsync(new CloseElectionRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ActorPublicAddress: closeAction.ActorPublicAddress,
            AcceptedBallotSetHash: closeAction.AcceptedBallotSetHash,
            FinalEncryptedTallyHash: closeAction.FinalEncryptedTallyHash,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleFinalizeElectionAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var finalizeAction = decryptedEnvelope.DeserializeAction<FinalizeElectionActionPayload>();
        if (finalizeAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Finalize election action payload could not be deserialized.");
        }

        return await _electionLifecycleService.FinalizeElectionAsync(new FinalizeElectionRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ActorPublicAddress: finalizeAction.ActorPublicAddress,
            AcceptedBallotSetHash: finalizeAction.AcceptedBallotSetHash,
            FinalEncryptedTallyHash: finalizeAction.FinalEncryptedTallyHash,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleSubmitFinalizationShareAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var shareAction = decryptedEnvelope.DeserializeAction<SubmitElectionFinalizationShareActionPayload>();
        if (shareAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Submit finalization share action payload could not be deserialized.");
        }

        return await _electionLifecycleService.SubmitFinalizationShareAsync(new SubmitElectionFinalizationShareRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            FinalizationSessionId: shareAction.FinalizationSessionId,
            ActorPublicAddress: shareAction.ActorPublicAddress,
            ShareIndex: shareAction.ShareIndex,
            ShareVersion: shareAction.ShareVersion,
            TargetType: shareAction.TargetType,
            ClaimedCloseArtifactId: shareAction.ClaimedCloseArtifactId,
            ClaimedAcceptedBallotSetHash: shareAction.ClaimedAcceptedBallotSetHash,
            ClaimedFinalEncryptedTallyHash: shareAction.ClaimedFinalEncryptedTallyHash,
            ClaimedTargetTallyId: shareAction.ClaimedTargetTallyId,
            ClaimedCeremonyVersionId: shareAction.ClaimedCeremonyVersionId,
            ClaimedTallyPublicKeyFingerprint: shareAction.ClaimedTallyPublicKeyFingerprint,
            ShareMaterial: shareAction.ShareMaterial,
            CloseCountingJobId: shareAction.CloseCountingJobId,
            ExecutorKeyAlgorithm: shareAction.ExecutorKeyAlgorithm,
            EncryptedExecutorSubmission: shareAction.EncryptedExecutorSubmission,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));
    }

    private async Task<ElectionCommandResult> HandleStartCeremonyAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var startAction = decryptedEnvelope.DeserializeAction<StartElectionCeremonyActionPayload>();
        if (startAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Start ceremony action payload could not be deserialized.");
        }

        return await _electionLifecycleService.StartElectionCeremonyAsync(new StartElectionCeremonyRequest(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            startAction.ActorPublicAddress,
            startAction.ProfileId));
    }

    private async Task<ElectionCommandResult> HandleRestartCeremonyAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var restartAction = decryptedEnvelope.DeserializeAction<RestartElectionCeremonyActionPayload>();
        if (restartAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Restart ceremony action payload could not be deserialized.");
        }

        return await _electionLifecycleService.RestartElectionCeremonyAsync(new RestartElectionCeremonyRequest(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            restartAction.ActorPublicAddress,
            restartAction.ProfileId,
            restartAction.RestartReason));
    }

    private async Task<ElectionCommandResult> HandlePublishCeremonyTransportKeyAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var publishAction = decryptedEnvelope.DeserializeAction<PublishElectionCeremonyTransportKeyActionPayload>();
        if (publishAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Publish ceremony transport key action payload could not be deserialized.");
        }

        return await _electionLifecycleService.PublishElectionCeremonyTransportKeyAsync(
            new PublishElectionCeremonyTransportKeyRequest(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                publishAction.CeremonyVersionId,
                publishAction.ActorPublicAddress,
                publishAction.TransportPublicKeyFingerprint));
    }

    private async Task<ElectionCommandResult> HandleJoinCeremonyAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var joinAction = decryptedEnvelope.DeserializeAction<JoinElectionCeremonyActionPayload>();
        if (joinAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Join ceremony action payload could not be deserialized.");
        }

        return await _electionLifecycleService.JoinElectionCeremonyAsync(new JoinElectionCeremonyRequest(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            joinAction.CeremonyVersionId,
            joinAction.ActorPublicAddress));
    }

    private async Task<ElectionCommandResult> HandleRecordCeremonySelfTestAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var selfTestAction = decryptedEnvelope.DeserializeAction<RecordElectionCeremonySelfTestActionPayload>();
        if (selfTestAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Record ceremony self-test action payload could not be deserialized.");
        }

        return await _electionLifecycleService.RecordElectionCeremonySelfTestSuccessAsync(
            new RecordElectionCeremonySelfTestRequest(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                selfTestAction.CeremonyVersionId,
                selfTestAction.ActorPublicAddress));
    }

    private async Task<ElectionCommandResult> HandleSubmitCeremonyMaterialAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var submitAction = decryptedEnvelope.DeserializeAction<SubmitElectionCeremonyMaterialActionPayload>();
        if (submitAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Submit ceremony material action payload could not be deserialized.");
        }

        if (!ElectionTallyPublicKeyDerivation.TryParsePointPayload(
                submitAction.CloseCountingPublicCommitment,
                new HushNode.Reactions.Crypto.BabyJubJubCurve(),
                out var closeCountingPublicCommitment,
                out var commitmentValidationError))
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                commitmentValidationError);
        }

        return await _electionLifecycleService.SubmitElectionCeremonyMaterialAsync(
            new SubmitElectionCeremonyMaterialRequest(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                submitAction.CeremonyVersionId,
                submitAction.ActorPublicAddress,
                submitAction.RecipientTrusteeUserAddress,
                submitAction.MessageType,
                submitAction.PayloadVersion,
                Encoding.UTF8.GetBytes(submitAction.EncryptedPayload),
                submitAction.PayloadFingerprint,
                submitAction.ShareVersion,
                closeCountingPublicCommitment!));
    }

    private async Task<ElectionCommandResult> HandleRecordCeremonyValidationFailureAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var validationFailureAction =
            decryptedEnvelope.DeserializeAction<RecordElectionCeremonyValidationFailureActionPayload>();
        if (validationFailureAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Record ceremony validation failure action payload could not be deserialized.");
        }

        return await _electionLifecycleService.RecordElectionCeremonyValidationFailureAsync(
            new RecordElectionCeremonyValidationFailureRequest(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                validationFailureAction.CeremonyVersionId,
                validationFailureAction.ActorPublicAddress,
                validationFailureAction.TrusteeUserAddress,
                validationFailureAction.ValidationFailureReason,
                validationFailureAction.EvidenceReference));
    }

    private async Task<ElectionCommandResult> HandleCompleteCeremonyTrusteeAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var completeAction = decryptedEnvelope.DeserializeAction<CompleteElectionCeremonyTrusteeActionPayload>();
        if (completeAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Complete ceremony trustee action payload could not be deserialized.");
        }

        return await _electionLifecycleService.CompleteElectionCeremonyTrusteeAsync(
            new CompleteElectionCeremonyTrusteeRequest(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                completeAction.CeremonyVersionId,
                completeAction.ActorPublicAddress,
                completeAction.TrusteeUserAddress,
                completeAction.ShareVersion,
                completeAction.TallyPublicKeyFingerprint));
    }

    private async Task<ElectionCommandResult> HandleRecordCeremonyShareExportAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var exportAction = decryptedEnvelope.DeserializeAction<RecordElectionCeremonyShareExportActionPayload>();
        if (exportAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Record ceremony share export action payload could not be deserialized.");
        }

        return await _electionLifecycleService.RecordElectionCeremonyShareExportAsync(
            new RecordElectionCeremonyShareExportRequest(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                exportAction.CeremonyVersionId,
                exportAction.ActorPublicAddress,
                exportAction.ShareVersion));
    }

    private async Task<ElectionCommandResult> HandleRecordCeremonyShareImportAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var importAction = decryptedEnvelope.DeserializeAction<RecordElectionCeremonyShareImportActionPayload>();
        if (importAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Record ceremony share import action payload could not be deserialized.");
        }

        return await _electionLifecycleService.RecordElectionCeremonyShareImportAsync(
            new RecordElectionCeremonyShareImportRequest(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                importAction.CeremonyVersionId,
                importAction.ActorPublicAddress,
                importAction.ImportedElectionId,
                importAction.ImportedCeremonyVersionId,
                importAction.ImportedTrusteeUserAddress,
                importAction.ImportedShareVersion));
    }

    private async Task SaveElectionEnvelopeAccessAsync(
        ElectionId electionId,
        string actorPublicAddress,
        string encryptionMaterial,
        string actorEncryptedElectionPrivateKey,
        DateTime grantedAt,
        Guid sourceTransactionId)
    {
        var normalizedGrantedAt = grantedAt.Kind switch
        {
            DateTimeKind.Utc => grantedAt,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(grantedAt, DateTimeKind.Utc),
            _ => grantedAt.ToUniversalTime(),
        };

        using var unitOfWork = _unitOfWorkProvider.CreateWritable();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var existingRecord = await repository.GetElectionEnvelopeAccessAsync(electionId, actorPublicAddress);
        var accessRecord = new ElectionEnvelopeAccessRecord(
            electionId,
            actorPublicAddress,
            encryptionMaterial,
            actorEncryptedElectionPrivateKey,
            normalizedGrantedAt,
            sourceTransactionId,
            null,
            null);

        if (existingRecord is null)
        {
            await repository.SaveElectionEnvelopeAccessAsync(accessRecord);
        }
        else
        {
            await repository.UpdateElectionEnvelopeAccessAsync(accessRecord);
        }

        await unitOfWork.CommitAsync();
    }

    private static string ResolveAccessEncryptionMaterial(EncryptedElectionEnvelopePayload payload) =>
        EncryptedElectionEnvelopePayloadHandler.IsDirectPublicEnvelopeVersion(payload.EnvelopeVersion)
            ? payload.ElectionPublicEncryptKey
            : payload.NodeEncryptedElectionPrivateKey;

    private static string ResolveClaimVerificationCode(
        EncryptedElectionEnvelopePayload payload,
        string? verificationCode)
    {
        if (EncryptedElectionEnvelopePayloadHandler.IsPrivacyHardenedEnvelopeVersion(payload.EnvelopeVersion) &&
            string.IsNullOrWhiteSpace(verificationCode))
        {
            return ElectionEligibilityContracts.TemporaryVerificationCode;
        }

        return verificationCode?.Trim() ?? string.Empty;
    }

    private static string? ResolveTrusteeAccessEnvelope(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        InviteElectionTrusteeActionPayload inviteTrusteeAction)
    {
        if (!EncryptedElectionEnvelopePayloadHandler.IsPrivacyHardenedEnvelopeVersion(
                decryptedEnvelope.Transaction.Payload.EnvelopeVersion))
        {
            return inviteTrusteeAction.TrusteeEncryptedElectionPrivateKey;
        }

        var inviteArtifacts = decryptedEnvelope.DeserializeActionArtifacts<InviteElectionTrusteeActionArtifacts>();
        return inviteArtifacts?.TrusteeEncryptedElectionPrivateKey;
    }

    private async Task DeleteElectionEnvelopeAccessAsync(
        ElectionId electionId,
        string actorPublicAddress,
        Guid sourceTransactionId)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var existingRecord = await repository.GetElectionEnvelopeAccessAsync(electionId, actorPublicAddress);
        if (existingRecord is null)
        {
            return;
        }

        await repository.DeleteElectionEnvelopeAccessAsync(electionId, actorPublicAddress);
        await unitOfWork.CommitAsync();

        _logger.LogInformation(
            "[EncryptedElectionEnvelopeIndexStrategy] Removed envelope access for actor {ActorPublicAddress} on election {ElectionId} after transaction {TransactionId}",
            actorPublicAddress,
            electionId,
            sourceTransactionId);
    }

    private async Task<bool> ShouldRetainElectionEnvelopeAccessAsync(
        ElectionId electionId,
        string actorPublicAddress)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionAsync(electionId);
        if (election is not null &&
            string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var invitations = await repository.GetTrusteeInvitationsAsync(electionId);
        if (invitations.Any(x =>
                string.Equals(x.TrusteeUserAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase) &&
                x.Status == ElectionTrusteeInvitationStatus.Accepted))
        {
            return true;
        }

        var grants = await repository.GetReportAccessGrantsAsync(electionId);
        return grants.Any(x =>
            string.Equals(x.ActorPublicAddress, actorPublicAddress, StringComparison.OrdinalIgnoreCase));
    }
}
