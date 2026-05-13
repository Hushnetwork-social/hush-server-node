using System.Text;
using System.Text.Json;
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
            EncryptedElectionEnvelopeActionTypes.RefreshProtocolPackageBinding =>
                await HandleRefreshProtocolPackageBindingAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.ImportRoster =>
                await HandleImportRosterAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.ClaimRosterEntry =>
                await HandleClaimRosterEntryAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.ActivateRosterEntry =>
                await HandleActivateRosterEntryAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RegisterVotingCommitment =>
                await HandleRegisterVotingCommitmentAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RegisterPreparedBallotCommitment =>
                await HandleRegisterPreparedBallotCommitmentAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.SpoilPreparedBallot =>
                await HandleSpoilPreparedBallotAsync(decryptedEnvelope),
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
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread =>
                await HandleSubmitAnomalyThreadAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RequestAnomalyInformation =>
                await HandleRequestAnomalyInformationAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyInformation =>
                await HandleSubmitAnomalyInformationAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAuthorityResponse =>
                await HandleRecordAnomalyAuthorityResponseAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.ClassifyAnomalyThread =>
                await HandleClassifyAnomalyThreadAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RegisterExternalAnomalyClaimant =>
                await HandleRegisterExternalAnomalyClaimantAsync(decryptedEnvelope),
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAttachmentManifest =>
                await HandleRecordAnomalyAttachmentManifestAsync(decryptedEnvelope),
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

    private async Task<ElectionCommandResult> HandleRefreshProtocolPackageBindingAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var refreshAction = decryptedEnvelope.DeserializeAction<RefreshProtocolPackageBindingActionPayload>();
        if (refreshAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Refresh protocol package binding action payload could not be deserialized.");
        }

        return await _electionLifecycleService.RefreshProtocolPackageBindingAsync(
            new RefreshElectionProtocolPackageBindingRequest(
                ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
                ActorPublicAddress: refreshAction.ActorPublicAddress,
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
                registerAction.CommitmentHash,
                registerAction.OrganizationVoterId));

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
                acceptAction.TallyPublicKeyFingerprint,
                acceptAction.PreparedBallotId,
                acceptAction.PreparedBallotHash,
                acceptAction.ReceiptCommitment,
                acceptAction.ReceiptCommitmentScheme,
                acceptAction.BallotDefinitionVersion,
                acceptAction.BallotDefinitionHash,
                acceptAction.OrganizationVoterId));

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

    private async Task<ElectionCommandResult> HandleRegisterPreparedBallotCommitmentAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var registerAction = decryptedEnvelope.DeserializeAction<RegisterPreparedBallotCommitmentActionPayload>();
        if (registerAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Register prepared ballot commitment action payload could not be deserialized.");
        }

        var result = await _electionLifecycleService.RegisterPreparedBallotCommitmentAsync(
            new RegisterPreparedBallotCommitmentRequest(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                registerAction.ActorPublicAddress,
                registerAction.PreparedBallotId,
                registerAction.PreparedBallotHash,
                registerAction.BallotDefinitionVersion,
                registerAction.BallotDefinitionHash,
                registerAction.CeremonyProfileId,
                registerAction.ProofStatementId,
                registerAction.PrecommittedAt,
                decryptedEnvelope.Transaction.TransactionId.Value,
                _blockchainCache.LastBlockIndex.Value,
                _blockchainCache.CurrentBlockId.Value,
                registerAction.OrganizationVoterId));

        return result.IsSuccess && result.Election is not null
            ? ElectionCommandResult.Success(result.Election, rosterEntry: result.RosterEntry)
            : ElectionCommandResult.Failure(
                result.FailureReason switch
                {
                    ElectionPreparedBallotCommitmentFailureReason.NotFound => ElectionCommandErrorCode.NotFound,
                    ElectionPreparedBallotCommitmentFailureReason.NotLinked => ElectionCommandErrorCode.Forbidden,
                    ElectionPreparedBallotCommitmentFailureReason.NotActive => ElectionCommandErrorCode.DependencyBlocked,
                    ElectionPreparedBallotCommitmentFailureReason.CommitmentMissing => ElectionCommandErrorCode.DependencyBlocked,
                    ElectionPreparedBallotCommitmentFailureReason.ElectionNotOpen => ElectionCommandErrorCode.InvalidState,
                    ElectionPreparedBallotCommitmentFailureReason.ClosePersisted => ElectionCommandErrorCode.InvalidState,
                    ElectionPreparedBallotCommitmentFailureReason.DuplicatePreparedBallot => ElectionCommandErrorCode.Conflict,
                    ElectionPreparedBallotCommitmentFailureReason.UnsupportedCeremonyProfile => ElectionCommandErrorCode.NotSupported,
                    _ => ElectionCommandErrorCode.ValidationFailed,
                },
                result.ErrorMessage ?? "Prepared ballot commitment registration failed.");
    }

    private async Task<ElectionCommandResult> HandleSpoilPreparedBallotAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var spoilAction = decryptedEnvelope.DeserializeAction<SpoilPreparedBallotActionPayload>();
        if (spoilAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Spoil prepared ballot action payload could not be deserialized.");
        }

        var result = await _electionLifecycleService.SpoilPreparedBallotAsync(
            new SpoilPreparedBallotRequest(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                spoilAction.ActorPublicAddress,
                spoilAction.PreparedBallotId,
                spoilAction.PreparedBallotHash,
                spoilAction.SpoiledTranscriptHash,
                spoilAction.SpoilRecordHash,
                spoilAction.LocalVerifierVersion,
                spoilAction.SpoiledAt,
                decryptedEnvelope.Transaction.TransactionId.Value,
                _blockchainCache.LastBlockIndex.Value,
                _blockchainCache.CurrentBlockId.Value,
                spoilAction.OrganizationVoterId));

        return result.IsSuccess && result.Election is not null
            ? ElectionCommandResult.Success(result.Election, rosterEntry: result.RosterEntry)
            : ElectionCommandResult.Failure(
                result.FailureReason switch
                {
                    ElectionSpoilPreparedBallotFailureReason.NotFound => ElectionCommandErrorCode.NotFound,
                    ElectionSpoilPreparedBallotFailureReason.NotLinked => ElectionCommandErrorCode.Forbidden,
                    ElectionSpoilPreparedBallotFailureReason.NotActive => ElectionCommandErrorCode.DependencyBlocked,
                    ElectionSpoilPreparedBallotFailureReason.ElectionNotOpen => ElectionCommandErrorCode.InvalidState,
                    ElectionSpoilPreparedBallotFailureReason.ClosePersisted => ElectionCommandErrorCode.InvalidState,
                    ElectionSpoilPreparedBallotFailureReason.PreparedBallotAlreadySpoiled => ElectionCommandErrorCode.Conflict,
                    ElectionSpoilPreparedBallotFailureReason.PreparedBallotAlreadyCast => ElectionCommandErrorCode.Conflict,
                    _ => ElectionCommandErrorCode.ValidationFailed,
                },
                result.ErrorMessage ?? "Prepared ballot spoil failed.");
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

        var result = await _electionLifecycleService.CreateReportAccessGrantAsync(new CreateElectionReportAccessGrantRequest(
            ElectionId: decryptedEnvelope.Transaction.Payload.ElectionId,
            ActorPublicAddress: createGrantAction.ActorPublicAddress,
            DesignatedAuditorPublicAddress: createGrantAction.DesignatedAuditorPublicAddress,
            SourceTransactionId: decryptedEnvelope.Transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (result.IsSuccess)
        {
            await BackfillAuditorAnomalyRecipientWrapsAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                createGrantAction.DesignatedAuditorPublicAddress,
                NormalizeUtc(decryptedEnvelope.Transaction.TransactionTimeStamp.Value));
        }

        return result;
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

    private async Task<ElectionCommandResult> HandleSubmitAnomalyThreadAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var submitAction = decryptedEnvelope.DeserializeAction<SubmitElectionAnomalyThreadActionPayload>();
        if (submitAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Submit anomaly thread action payload could not be deserialized.");
        }

        using var unitOfWork = _unitOfWorkProvider.CreateWritable();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionAsync(decryptedEnvelope.Transaction.Payload.ElectionId);
        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Election was not found for anomaly submission indexing.");
        }

        var roleResolution = await ResolveActorAnomalyRoleAsync(
            repository,
            election,
            submitAction.ActorPublicAddress,
            submitAction.ActorRoleContextId,
            NormalizeUtc(decryptedEnvelope.Transaction.TransactionTimeStamp.Value));
        if (!roleResolution.IsResolved)
        {
            await SaveAnomalyActionRecordAsync(
                repository,
                decryptedEnvelope,
                submitAction.ActorPublicAddress,
                submitAction.ActionNonce,
                null,
                ElectionAnomalyActionOutcomeIds.IgnoredDuplicate,
                roleResolution.ValidationCode ?? ElectionAnomalyValidationCodes.PersonScopeUnresolved);
            await unitOfWork.CommitAsync();
            return ElectionCommandResult.Success(election);
        }

        var existingThread = await repository.GetAnomalyThreadByPersonScopeAsync(
            election.ElectionId,
            roleResolution.SubmitterPersonScopeId!);
        if (existingThread is not null)
        {
            await SaveAnomalyActionRecordAsync(
                repository,
                decryptedEnvelope,
                submitAction.ActorPublicAddress,
                submitAction.ActionNonce,
                existingThread.Id,
                ElectionAnomalyActionOutcomeIds.IgnoredDuplicate,
                ElectionAnomalyValidationCodes.DuplicateThread);
            await unitOfWork.CommitAsync();
            return ElectionCommandResult.Success(election);
        }

        var recordedAt = NormalizeUtc(decryptedEnvelope.Transaction.TransactionTimeStamp.Value);
        var threadId = submitAction.AnomalyThreadId;
        var threadEvent = CreateAnomalyEvent(
            threadId,
            election.ElectionId,
            sequence: 1,
            ElectionAnomalyEventTypeIds.ThreadSubmitted,
            CreateMessageEventPayload(submitAction.CategoryId, submitAction.InitialMessage),
            previousEventHash: null,
            submitAction.ActionNonce,
            decryptedEnvelope.Transaction.TransactionId.Value,
            submitAction.ActorPublicAddress,
            recordedAt);
        var currentThreadHash = ElectionAnomalyEventHasher.ComputeThreadHash([threadEvent]);
        var thread = new ElectionAnomalyThreadRecord(
            threadId,
            election.ElectionId,
            roleResolution.SubmitterPersonScopeId!,
            roleResolution.PersonScopeDerivationVersion,
            roleResolution.ActorPublicAddress!,
            roleResolution.RoleContextId,
            roleResolution.RoleEvidenceTypeId!,
            roleResolution.RoleEvidenceReference!,
            roleResolution.LifecycleStateAtSubmission!.Value,
            roleResolution.SubmissionWindowClosesAt,
            submitAction.CategoryId,
            ElectionAnomalyCaseStateIds.Submitted,
            SeverityCandidateId: null,
            GovernedDecisionRef: null,
            HasOpenClarificationRequest: false,
            OpenClarificationRequestId: null,
            recordedAt,
            recordedAt,
            decryptedEnvelope.Transaction.TransactionId.Value,
            _blockchainCache.LastBlockIndex.Value,
            _blockchainCache.CurrentBlockId.Value,
            currentThreadHash);
        var message = CreateAnomalyMessageRecord(
            submitAction.InitialMessage,
            threadId,
            threadEvent.Id,
            election.ElectionId,
            recordedAt);
        var wraps = CreateRecipientWrapRecords(
            submitAction.InitialMessage.RecipientWraps,
            message.Id,
            threadId,
            election.ElectionId,
            recordedAt);

        await repository.SaveAnomalyThreadWithInitialEventAsync(thread, threadEvent, message, wraps);
        await SaveAnomalyActionRecordAsync(
            repository,
            decryptedEnvelope,
            submitAction.ActorPublicAddress,
            submitAction.ActionNonce,
            threadId,
            ElectionAnomalyActionOutcomeIds.Accepted,
            validationCode: null);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(election);
    }

    private async Task<ElectionCommandResult> HandleRegisterExternalAnomalyClaimantAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var registerAction = decryptedEnvelope.DeserializeAction<RegisterExternalElectionAnomalyClaimantActionPayload>();
        if (registerAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Register external anomaly claimant action payload could not be deserialized.");
        }

        using var unitOfWork = _unitOfWorkProvider.CreateWritable();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionAsync(decryptedEnvelope.Transaction.Payload.ElectionId);
        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Election was not found for external anomaly claimant indexing.");
        }

        var roleResolution = ElectionAnomalyAuthorization.ResolveExternalClaimantSubmitter(
            election,
            registerAction.ActorPublicAddress,
            registerAction.ExternalClaimantReferenceHash,
            NormalizeUtc(decryptedEnvelope.Transaction.TransactionTimeStamp.Value));
        if (!roleResolution.IsResolved)
        {
            await SaveAnomalyActionRecordAsync(
                repository,
                decryptedEnvelope,
                registerAction.ActorPublicAddress,
                registerAction.ActionNonce,
                null,
                ElectionAnomalyActionOutcomeIds.IgnoredDuplicate,
                roleResolution.ValidationCode ?? ElectionAnomalyValidationCodes.PersonScopeUnresolved);
            await unitOfWork.CommitAsync();
            return ElectionCommandResult.Success(election);
        }

        var existingThread = await repository.GetAnomalyThreadByPersonScopeAsync(
            election.ElectionId,
            roleResolution.SubmitterPersonScopeId!);
        if (existingThread is not null)
        {
            await SaveAnomalyActionRecordAsync(
                repository,
                decryptedEnvelope,
                registerAction.ActorPublicAddress,
                registerAction.ActionNonce,
                existingThread.Id,
                ElectionAnomalyActionOutcomeIds.IgnoredDuplicate,
                ElectionAnomalyValidationCodes.DuplicateThread);
            await unitOfWork.CommitAsync();
            return ElectionCommandResult.Success(election);
        }

        var recordedAt = NormalizeUtc(decryptedEnvelope.Transaction.TransactionTimeStamp.Value);
        var threadId = registerAction.AnomalyThreadId;
        var threadEvent = CreateAnomalyEvent(
            threadId,
            election.ElectionId,
            sequence: 1,
            ElectionAnomalyEventTypeIds.ExternalClaimantRegistered,
            JsonSerializer.Serialize(new
            {
                registerAction.CategoryId,
                registerAction.ExternalClaimantReferenceHash,
                Message = CreateMessageEventPayload(registerAction.CategoryId, registerAction.InitialMessage),
            }),
            previousEventHash: null,
            registerAction.ActionNonce,
            decryptedEnvelope.Transaction.TransactionId.Value,
            registerAction.ActorPublicAddress,
            recordedAt);
        var currentThreadHash = ElectionAnomalyEventHasher.ComputeThreadHash([threadEvent]);
        var thread = new ElectionAnomalyThreadRecord(
            threadId,
            election.ElectionId,
            roleResolution.SubmitterPersonScopeId!,
            roleResolution.PersonScopeDerivationVersion,
            roleResolution.ActorPublicAddress!,
            roleResolution.RoleContextId,
            roleResolution.RoleEvidenceTypeId!,
            roleResolution.RoleEvidenceReference!,
            roleResolution.LifecycleStateAtSubmission!.Value,
            roleResolution.SubmissionWindowClosesAt,
            registerAction.CategoryId,
            ElectionAnomalyCaseStateIds.Submitted,
            SeverityCandidateId: null,
            GovernedDecisionRef: null,
            HasOpenClarificationRequest: false,
            OpenClarificationRequestId: null,
            recordedAt,
            recordedAt,
            decryptedEnvelope.Transaction.TransactionId.Value,
            _blockchainCache.LastBlockIndex.Value,
            _blockchainCache.CurrentBlockId.Value,
            currentThreadHash);
        var message = CreateAnomalyMessageRecord(
            registerAction.InitialMessage,
            threadId,
            threadEvent.Id,
            election.ElectionId,
            recordedAt);
        var wraps = CreateRecipientWrapRecords(
            registerAction.InitialMessage.RecipientWraps,
            message.Id,
            threadId,
            election.ElectionId,
            recordedAt);

        await repository.SaveAnomalyThreadWithInitialEventAsync(thread, threadEvent, message, wraps);
        await SaveAnomalyActionRecordAsync(
            repository,
            decryptedEnvelope,
            registerAction.ActorPublicAddress,
            registerAction.ActionNonce,
            threadId,
            ElectionAnomalyActionOutcomeIds.Accepted,
            validationCode: null);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(election);
    }

    private async Task<ElectionCommandResult> HandleRequestAnomalyInformationAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var requestAction = decryptedEnvelope.DeserializeAction<RequestElectionAnomalyInformationActionPayload>();
        if (requestAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Request anomaly information action payload could not be deserialized.");
        }

        return await AppendAnomalyEventAsync(
            decryptedEnvelope,
            requestAction.AnomalyThreadId,
            requestAction.ActionNonce,
            requestAction.ActorPublicAddress,
            ElectionAnomalyEventTypeIds.AuthorityInformationRequested,
            JsonSerializer.Serialize(new
            {
                requestAction.ClarificationRequestId,
                requestAction.MaxResponseCharacters,
                Message = CreateMessageEventPayload(null, requestAction.RequestMessage),
            }),
            requestAction.RequestMessage,
            thread => thread with
            {
                CurrentCaseStateId = ElectionAnomalyCaseStateIds.AuthorityRequestedInformation,
                HasOpenClarificationRequest = true,
                OpenClarificationRequestId = requestAction.ClarificationRequestId,
            });
    }

    private async Task<ElectionCommandResult> HandleSubmitAnomalyInformationAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var responseAction = decryptedEnvelope.DeserializeAction<SubmitElectionAnomalyInformationActionPayload>();
        if (responseAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Submit anomaly information action payload could not be deserialized.");
        }

        return await AppendAnomalyEventAsync(
            decryptedEnvelope,
            responseAction.AnomalyThreadId,
            responseAction.ActionNonce,
            responseAction.ActorPublicAddress,
            ElectionAnomalyEventTypeIds.SubmitterInformationProvided,
            JsonSerializer.Serialize(new
            {
                responseAction.ClarificationRequestId,
                Message = CreateMessageEventPayload(null, responseAction.ResponseMessage),
            }),
            responseAction.ResponseMessage,
            thread => thread with
            {
                CurrentCaseStateId = ElectionAnomalyCaseStateIds.SubmitterInformationProvided,
                HasOpenClarificationRequest = false,
                OpenClarificationRequestId = null,
            });
    }

    private async Task<ElectionCommandResult> HandleRecordAnomalyAuthorityResponseAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var responseAction = decryptedEnvelope.DeserializeAction<RecordElectionAnomalyAuthorityResponseActionPayload>();
        if (responseAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Record anomaly authority response action payload could not be deserialized.");
        }

        return await AppendAnomalyEventAsync(
            decryptedEnvelope,
            responseAction.AnomalyThreadId,
            responseAction.ActionNonce,
            responseAction.ActorPublicAddress,
            ElectionAnomalyEventTypeIds.AuthorityResponded,
            JsonSerializer.Serialize(new
            {
                Message = CreateMessageEventPayload(null, responseAction.AuthorityResponseMessage),
            }),
            responseAction.AuthorityResponseMessage,
            thread => thread with
            {
                CurrentCaseStateId = ElectionAnomalyCaseStateIds.OwnerResponded,
            });
    }

    private async Task<ElectionCommandResult> HandleClassifyAnomalyThreadAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var classifyAction = decryptedEnvelope.DeserializeAction<ClassifyElectionAnomalyThreadActionPayload>();
        if (classifyAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Classify anomaly thread action payload could not be deserialized.");
        }

        return await AppendAnomalyEventAsync(
            decryptedEnvelope,
            classifyAction.AnomalyThreadId,
            classifyAction.ActionNonce,
            classifyAction.ActorPublicAddress,
            ElectionAnomalyEventTypeIds.ThreadClassified,
            JsonSerializer.Serialize(new
            {
                classifyAction.CategoryId,
                classifyAction.CaseStateId,
                classifyAction.SeverityCandidateId,
                classifyAction.GovernedDecisionRef,
            }),
            message: null,
            thread => thread with
            {
                CurrentCategoryId = string.IsNullOrWhiteSpace(classifyAction.CategoryId)
                    ? thread.CurrentCategoryId
                    : classifyAction.CategoryId,
                CurrentCaseStateId = string.IsNullOrWhiteSpace(classifyAction.CaseStateId)
                    ? thread.CurrentCaseStateId
                    : classifyAction.CaseStateId,
                SeverityCandidateId = string.IsNullOrWhiteSpace(classifyAction.SeverityCandidateId)
                    ? thread.SeverityCandidateId
                    : classifyAction.SeverityCandidateId,
                GovernedDecisionRef = string.IsNullOrWhiteSpace(classifyAction.GovernedDecisionRef)
                    ? thread.GovernedDecisionRef
                    : classifyAction.GovernedDecisionRef,
            });
    }

    private async Task<ElectionCommandResult> HandleRecordAnomalyAttachmentManifestAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var manifestAction = decryptedEnvelope.DeserializeAction<RecordElectionAnomalyAttachmentManifestActionPayload>();
        if (manifestAction is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Record anomaly attachment manifest action payload could not be deserialized.");
        }

        return await AppendAnomalyEventAsync(
            decryptedEnvelope,
            manifestAction.AnomalyThreadId,
            manifestAction.ActionNonce,
            manifestAction.ActorPublicAddress,
            ElectionAnomalyEventTypeIds.AttachmentManifestRecorded,
            JsonSerializer.Serialize(new
            {
                manifestAction.AttachmentManifestId,
                manifestAction.AttachmentKindId,
                manifestAction.EncryptedPayloadReference,
                manifestAction.EncryptedPayloadHash,
                manifestAction.ContentHash,
                manifestAction.SizeBytes,
                manifestAction.MimeType,
                manifestAction.ValidationStatusId,
                manifestAction.ClarificationRequestId,
            }),
            message: null,
            thread => thread);
    }

    private async Task<ElectionCommandResult> AppendAnomalyEventAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        Guid anomalyThreadId,
        Guid actionNonce,
        string actorPublicAddress,
        string eventTypeId,
        string eventPayloadJson,
        ElectionAnomalyMessageEnvelopePayload? message,
        Func<ElectionAnomalyThreadRecord, ElectionAnomalyThreadRecord> updateThread)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = await repository.GetElectionAsync(decryptedEnvelope.Transaction.Payload.ElectionId);
        if (election is null)
        {
            return ElectionCommandResult.Failure(
                ElectionCommandErrorCode.ValidationFailed,
                "Election was not found for anomaly event indexing.");
        }

        var thread = await repository.GetAnomalyThreadAsync(anomalyThreadId);
        if (thread is null || thread.ElectionId != election.ElectionId)
        {
            await SaveAnomalyActionRecordAsync(
                repository,
                decryptedEnvelope,
                actorPublicAddress,
                actionNonce,
                anomalyThreadId,
                ElectionAnomalyActionOutcomeIds.IgnoredDuplicate,
                ElectionAnomalyValidationCodes.ClarificationRequestNotOpen);
            await unitOfWork.CommitAsync();
            return ElectionCommandResult.Success(election);
        }

        var latestEvent = await repository.GetLatestAnomalyThreadEventAsync(thread.Id);
        var sequence = (latestEvent?.Sequence ?? 0) + 1;
        var recordedAt = NormalizeUtc(decryptedEnvelope.Transaction.TransactionTimeStamp.Value);
        var threadEvent = CreateAnomalyEvent(
            thread.Id,
            election.ElectionId,
            sequence,
            eventTypeId,
            eventPayloadJson,
            latestEvent?.EventHash,
            actionNonce,
            decryptedEnvelope.Transaction.TransactionId.Value,
            actorPublicAddress,
            recordedAt);
        var existingEvents = (await repository.GetAnomalyThreadEventsAsync(thread.Id)).ToList();
        if (existingEvents.Count == 0 && latestEvent is not null)
        {
            existingEvents.Add(latestEvent);
        }

        var updatedThreadHash = ElectionAnomalyEventHasher.ComputeThreadHash(existingEvents.Append(threadEvent));
        var updatedThread = updateThread(thread) with
        {
            LastUpdatedAt = recordedAt,
            CurrentThreadHash = updatedThreadHash,
        };

        await repository.SaveAnomalyThreadEventAsync(threadEvent);
        if (message is not null)
        {
            var messageRecord = CreateAnomalyMessageRecord(
                message,
                thread.Id,
                threadEvent.Id,
                election.ElectionId,
                recordedAt);
            var wraps = CreateRecipientWrapRecords(
                message.RecipientWraps,
                messageRecord.Id,
                thread.Id,
                election.ElectionId,
                recordedAt);
            await repository.SaveAnomalyMessageEnvelopeAsync(messageRecord);
            await repository.SaveAnomalyRecipientWrapsAsync(wraps);
        }

        await repository.UpdateAnomalyThreadAsync(updatedThread);
        await SaveAnomalyActionRecordAsync(
            repository,
            decryptedEnvelope,
            actorPublicAddress,
            actionNonce,
            thread.Id,
            ElectionAnomalyActionOutcomeIds.Accepted,
            validationCode: null);
        await unitOfWork.CommitAsync();

        return ElectionCommandResult.Success(election);
    }

    private static async Task<ElectionAnomalySubmitterResolution> ResolveActorAnomalyRoleAsync(
        IElectionsRepository repository,
        ElectionRecord election,
        string actorPublicAddress,
        string? requestedRoleContextId,
        DateTime submittedAt)
    {
        var rosterEntry = await repository.GetRosterEntryByLinkedActorAsync(election.ElectionId, actorPublicAddress);
        var trusteeInvitation = (await repository.GetAcceptedTrusteeInvitationsByActorAsync(actorPublicAddress))
            .FirstOrDefault(x => x.ElectionId == election.ElectionId);
        var reportAccessGrant = await repository.GetReportAccessGrantAsync(election.ElectionId, actorPublicAddress);

        return ElectionAnomalyAuthorization.ResolveActorSubmitter(
            election,
            actorPublicAddress,
            submittedAt,
            rosterEntry,
            trusteeInvitation,
            reportAccessGrant,
            requestedRoleContextId);
    }

    private ElectionAnomalyThreadEventRecord CreateAnomalyEvent(
        Guid anomalyThreadId,
        ElectionId electionId,
        int sequence,
        string eventTypeId,
        string eventPayloadJson,
        string? previousEventHash,
        Guid actionNonce,
        Guid sourceTransactionId,
        string actorPublicAddress,
        DateTime occurredAt)
    {
        var eventRecord = new ElectionAnomalyThreadEventRecord(
            Guid.NewGuid(),
            anomalyThreadId,
            electionId,
            sequence,
            eventTypeId,
            eventPayloadJson,
            EventHash: "pending",
            previousEventHash,
            actionNonce,
            sourceTransactionId,
            _blockchainCache.LastBlockIndex.Value,
            _blockchainCache.CurrentBlockId.Value,
            actorPublicAddress,
            occurredAt);

        return eventRecord with
        {
            EventHash = ElectionAnomalyEventHasher.ComputeEventHash(eventRecord),
        };
    }

    private static ElectionAnomalyMessageEnvelopeRecord CreateAnomalyMessageRecord(
        ElectionAnomalyMessageEnvelopePayload message,
        Guid anomalyThreadId,
        Guid eventId,
        ElectionId electionId,
        DateTime recordedAt) =>
        new(
            message.MessageId,
            anomalyThreadId,
            eventId,
            electionId,
            message.MessageKindId,
            message.EncryptedBody,
            message.EncryptedBodyHash,
            message.PlaintextBodyHash,
            message.PlaintextCharacterCount,
            message.EncryptionAlgorithm,
            recordedAt);

    private static IReadOnlyCollection<ElectionAnomalyRecipientWrapRecord> CreateRecipientWrapRecords(
        IReadOnlyList<ElectionAnomalyRecipientWrapPayload> recipientWraps,
        Guid messageEnvelopeId,
        Guid anomalyThreadId,
        ElectionId electionId,
        DateTime recordedAt) =>
        recipientWraps
            .Select(wrap => new ElectionAnomalyRecipientWrapRecord(
                Guid.NewGuid(),
                messageEnvelopeId,
                anomalyThreadId,
                electionId,
                wrap.RecipientRoleId,
                wrap.RecipientPublicAddress,
                wrap.RecipientKeyFingerprint,
                wrap.EncryptedContentKey,
                wrap.WrapAlgorithm,
                wrap.WrapStatusId,
                recordedAt))
            .ToArray();

    private static string CreateMessageEventPayload(
        string? categoryId,
        ElectionAnomalyMessageEnvelopePayload message) =>
        JsonSerializer.Serialize(new
        {
            CategoryId = categoryId,
            message.MessageId,
            message.MessageKindId,
            message.EncryptedBodyHash,
            message.PlaintextBodyHash,
            message.PlaintextCharacterCount,
            RecipientWraps = message.RecipientWraps.Select(wrap => new
            {
                wrap.RecipientRoleId,
                wrap.RecipientPublicAddress,
                wrap.RecipientKeyFingerprint,
                wrap.WrapStatusId,
            }),
        });

    private async Task SaveAnomalyActionRecordAsync(
        IElectionsRepository repository,
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string actorPublicAddress,
        Guid? actionNonce,
        Guid? anomalyThreadId,
        string actionOutcomeId,
        string? validationCode)
    {
        await repository.SaveAnomalyActionRecordAsync(new ElectionAnomalyActionRecord(
            Guid.NewGuid(),
            decryptedEnvelope.Transaction.Payload.ElectionId,
            anomalyThreadId,
            actionNonce,
            decryptedEnvelope.ActionType,
            actionOutcomeId,
            actorPublicAddress,
            validationCode,
            DiagnosticReference: null,
            decryptedEnvelope.Transaction.TransactionId.Value,
            _blockchainCache.LastBlockIndex.Value,
            _blockchainCache.CurrentBlockId.Value,
            NormalizeUtc(decryptedEnvelope.Transaction.TransactionTimeStamp.Value)));
    }

    private async Task BackfillAuditorAnomalyRecipientWrapsAsync(
        ElectionId electionId,
        string auditorPublicAddress,
        DateTime recordedAt)
    {
        using var unitOfWork = _unitOfWorkProvider.CreateWritable();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var threads = await repository.GetAnomalyThreadsAsync(electionId);
        var missingWraps = new List<ElectionAnomalyRecipientWrapRecord>();

        foreach (var thread in threads)
        {
            var messages = await repository.GetAnomalyMessageEnvelopesAsync(thread.Id);
            var existingWraps = await repository.GetAnomalyRecipientWrapsAsync(thread.Id);
            foreach (var message in messages)
            {
                var hasAuditorWrap = existingWraps.Any(wrap =>
                    wrap.MessageEnvelopeId == message.Id &&
                    wrap.RecipientRoleId == ElectionAnomalyRecipientRoleIds.DesignatedAuditor &&
                    string.Equals(wrap.RecipientPublicAddress, auditorPublicAddress, StringComparison.Ordinal));
                if (hasAuditorWrap)
                {
                    continue;
                }

                missingWraps.Add(new ElectionAnomalyRecipientWrapRecord(
                    Guid.NewGuid(),
                    message.Id,
                    thread.Id,
                    electionId,
                    ElectionAnomalyRecipientRoleIds.DesignatedAuditor,
                    auditorPublicAddress,
                    RecipientKeyFingerprint: string.Empty,
                    EncryptedContentKey: string.Empty,
                    WrapAlgorithm: string.Empty,
                    ElectionAnomalyRecipientWrapStatusIds.PendingBackfill,
                    recordedAt));
            }
        }

        if (missingWraps.Count == 0)
        {
            return;
        }

        await repository.SaveAnomalyRecipientWrapsAsync(missingWraps);
        await unitOfWork.CommitAsync();
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value.ToUniversalTime(),
        };

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
