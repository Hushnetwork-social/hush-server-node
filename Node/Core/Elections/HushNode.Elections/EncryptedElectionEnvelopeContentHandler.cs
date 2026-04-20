using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using HushNode.Credentials;
using HushNode.Reactions.Crypto;
using HushNode.Elections.Storage;
using HushNode.MemPool;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using HushShared.Reactions.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class EncryptedElectionEnvelopeContentHandler(
    IElectionEnvelopeCryptoService envelopeCryptoService,
    ICreateElectionDraftValidationService createElectionDraftValidationService,
    UpdateElectionDraftContentHandler updateElectionDraftContentHandler,
    InviteElectionTrusteeContentHandler inviteElectionTrusteeContentHandler,
    RevokeElectionTrusteeInvitationContentHandler revokeElectionTrusteeInvitationContentHandler,
    StartElectionGovernedProposalContentHandler startElectionGovernedProposalContentHandler,
    ApproveElectionGovernedProposalContentHandler approveElectionGovernedProposalContentHandler,
    RetryElectionGovernedProposalExecutionContentHandler retryElectionGovernedProposalExecutionContentHandler,
    OpenElectionContentHandler openElectionContentHandler,
    CloseElectionContentHandler closeElectionContentHandler,
    FinalizeElectionContentHandler finalizeElectionContentHandler,
    ICredentialsProvider credentialsProvider,
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
    IMemPoolService memPoolService,
    ElectionCeremonyOptions ceremonyOptions) : ITransactionContentHandler, ITransactionValidationFailureReporter
{
    private readonly IElectionEnvelopeCryptoService _envelopeCryptoService = envelopeCryptoService;
    private readonly ICreateElectionDraftValidationService _createElectionDraftValidationService =
        createElectionDraftValidationService;
    private readonly UpdateElectionDraftContentHandler _updateElectionDraftContentHandler =
        updateElectionDraftContentHandler;
    private readonly InviteElectionTrusteeContentHandler _inviteElectionTrusteeContentHandler =
        inviteElectionTrusteeContentHandler;
    private readonly RevokeElectionTrusteeInvitationContentHandler _revokeElectionTrusteeInvitationContentHandler =
        revokeElectionTrusteeInvitationContentHandler;
    private readonly StartElectionGovernedProposalContentHandler _startElectionGovernedProposalContentHandler =
        startElectionGovernedProposalContentHandler;
    private readonly ApproveElectionGovernedProposalContentHandler _approveElectionGovernedProposalContentHandler =
        approveElectionGovernedProposalContentHandler;
    private readonly RetryElectionGovernedProposalExecutionContentHandler
        _retryElectionGovernedProposalExecutionContentHandler = retryElectionGovernedProposalExecutionContentHandler;
    private readonly OpenElectionContentHandler _openElectionContentHandler = openElectionContentHandler;
    private readonly CloseElectionContentHandler _closeElectionContentHandler = closeElectionContentHandler;
    private readonly FinalizeElectionContentHandler _finalizeElectionContentHandler = finalizeElectionContentHandler;
    private readonly ICredentialsProvider _credentialsProvider = credentialsProvider;
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IMemPoolService _memPoolService = memPoolService;
    private readonly ElectionCeremonyOptions _ceremonyOptions = ceremonyOptions;
    private readonly ConcurrentDictionary<Guid, TransactionValidationFailure> _validationFailures = new();

    public bool CanValidate(Guid transactionKind) =>
        EncryptedElectionEnvelopePayloadHandler.EncryptedElectionEnvelopePayloadKind == transactionKind;

    public bool TryTakeValidationFailure(Guid transactionId, out TransactionValidationFailure failure) =>
        _validationFailures.TryRemove(transactionId, out failure!);

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        _validationFailures.TryRemove(transaction.TransactionId.Value, out _);

        var decryptedEnvelope = _envelopeCryptoService.TryDecryptSigned(transaction);
        if (decryptedEnvelope is null)
        {
            RecordValidationFailure(
                transaction.TransactionId.Value,
                "election_envelope_decrypt_failed",
                "Election envelope decryption failed during validation.");
            return null;
        }

        var signatory = decryptedEnvelope.Transaction.UserSignature?.Signatory;
        if (string.IsNullOrWhiteSpace(signatory))
        {
            RecordValidationFailure(
                decryptedEnvelope.Transaction.TransactionId.Value,
                "election_envelope_missing_signatory",
                "Election envelope validation requires a non-empty signatory.");
            return null;
        }

        if (!IsValidInnerAction(decryptedEnvelope, signatory))
        {
            return null;
        }

        var blockProducerCredentials = _credentialsProvider.GetCredentials();
        return decryptedEnvelope.Transaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }

    private void RecordValidationFailure(
        Guid transactionId,
        string code,
        string message) =>
        _validationFailures[transactionId] = new TransactionValidationFailure(code, message);

    private bool RejectWithValidationFailure(
        Guid transactionId,
        string code,
        string message)
    {
        RecordValidationFailure(transactionId, code, message);
        return false;
    }

    private bool TryValidateCeremonyTransition(
        Guid transactionId,
        string failureCode,
        Action transition)
    {
        try
        {
            transition();
            return true;
        }
        catch (ArgumentException ex)
        {
            return RejectWithValidationFailure(transactionId, failureCode, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return RejectWithValidationFailure(transactionId, failureCode, ex.Message);
        }
    }

    private bool IsValidInnerAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        return decryptedEnvelope.ActionType switch
        {
            EncryptedElectionEnvelopeActionTypes.CreateDraft =>
                IsValidCreateDraftAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.UpdateDraft =>
                IsValidUpdateDraftAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.ImportRoster =>
                IsValidImportRosterAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.ClaimRosterEntry =>
                IsValidClaimRosterEntryAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.ActivateRosterEntry =>
                IsValidActivateRosterEntryAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.RegisterVotingCommitment =>
                IsValidRegisterVotingCommitmentAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.AcceptBallotCast =>
                IsValidAcceptBallotCastAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.InviteTrustee =>
                IsValidInviteTrusteeAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.CreateReportAccessGrant =>
                IsValidCreateReportAccessGrantAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.AcceptTrusteeInvitation =>
                IsValidResolveTrusteeInvitationAction(decryptedEnvelope, signatory, ownerOnly: false),
            EncryptedElectionEnvelopeActionTypes.RejectTrusteeInvitation =>
                IsValidResolveTrusteeInvitationAction(decryptedEnvelope, signatory, ownerOnly: false),
            EncryptedElectionEnvelopeActionTypes.RevokeTrusteeInvitation =>
                IsValidRevokeTrusteeInvitationAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.StartGovernedProposal =>
                IsValidStartGovernedProposalAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.ApproveGovernedProposal =>
                IsValidApproveGovernedProposalAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.RetryGovernedProposalExecution =>
                IsValidRetryGovernedProposalExecutionAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.OpenElection =>
                IsValidOpenElectionAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.CloseElection =>
                IsValidCloseElectionAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.FinalizeElection =>
                IsValidFinalizeElectionAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.SubmitFinalizationShare =>
                IsValidSubmitFinalizationShareAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.StartCeremony =>
                IsValidStartCeremonyAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.RestartCeremony =>
                IsValidRestartCeremonyAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.PublishCeremonyTransportKey =>
                IsValidPublishCeremonyTransportKeyAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.JoinCeremony =>
                IsValidJoinCeremonyAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.RecordCeremonySelfTestSuccess =>
                IsValidRecordCeremonySelfTestAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.SubmitCeremonyMaterial =>
                IsValidSubmitCeremonyMaterialAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.RecordCeremonyValidationFailure =>
                IsValidRecordCeremonyValidationFailureAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.CompleteCeremonyTrustee =>
                IsValidCompleteCeremonyTrusteeAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.RecordCeremonyShareExport =>
                IsValidRecordCeremonyShareExportAction(decryptedEnvelope, signatory),
            EncryptedElectionEnvelopeActionTypes.RecordCeremonyShareImport =>
                IsValidRecordCeremonyShareImportAction(decryptedEnvelope, signatory),
            _ => false,
        };
    }

    private bool IsValidCreateDraftAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var createDraftAction = decryptedEnvelope.DeserializeAction<CreateElectionDraftActionPayload>();
        if (createDraftAction is null)
        {
            return false;
        }

        var typedPayload = new CreateElectionDraftPayload(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            createDraftAction.OwnerPublicAddress,
            createDraftAction.SnapshotReason,
            createDraftAction.Draft);

        return _createElectionDraftValidationService.IsValid(typedPayload, signatory);
    }

    private bool IsValidUpdateDraftAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var updateDraftAction = decryptedEnvelope.DeserializeAction<UpdateElectionDraftActionPayload>();
        if (updateDraftAction is null)
        {
            return false;
        }

        var unsignedTransaction = UpdateElectionDraftPayloadHandler.CreateNew(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            updateDraftAction.ActorPublicAddress,
            updateDraftAction.SnapshotReason,
            updateDraftAction.Draft);
        var signedTransaction = new SignedTransaction<UpdateElectionDraftPayload>(
            unsignedTransaction,
            new SignatureInfo(signatory, decryptedEnvelope.Transaction.UserSignature!.Signature));

        return _updateElectionDraftContentHandler.ValidateAndSign(signedTransaction) is not null;
    }

    private bool IsValidImportRosterAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var importAction = decryptedEnvelope.DeserializeAction<ImportElectionRosterActionPayload>();
        if (importAction is null
            || !HasMatchingActor(signatory, importAction.ActorPublicAddress)
            || ElectionEligibilityContracts.ValidateRosterImportEntries(importAction.RosterEntries).Count > 0)
        {
            return false;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = repository.GetElectionAsync(decryptedEnvelope.Transaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (election is null
            || election.LifecycleState != ElectionLifecycleState.Draft
            || !string.Equals(election.OwnerPublicAddress, importAction.ActorPublicAddress, StringComparison.Ordinal))
        {
            return false;
        }

        var pendingProposal = repository
            .GetPendingGovernedProposalAsync(decryptedEnvelope.Transaction.Payload.ElectionId)
            .GetAwaiter()
            .GetResult();
        return !IsDraftBlockedByPendingOpenProposal(pendingProposal);
    }

    private bool IsValidClaimRosterEntryAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var claimAction = decryptedEnvelope.DeserializeAction<ClaimElectionRosterEntryActionPayload>();
        if (claimAction is null || !HasMatchingActor(signatory, claimAction.ActorPublicAddress))
        {
            return false;
        }

        if (EncryptedElectionEnvelopePayloadHandler.IsPrivacyHardenedEnvelopeVersion(
                decryptedEnvelope.Transaction.Payload.EnvelopeVersion) &&
            !string.IsNullOrWhiteSpace(claimAction.VerificationCode))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_claim_verification_code_public",
                "Claim roster entry verification code must not be exposed in the public v2.1 envelope payload.");
        }

        var verificationCode = ResolveClaimVerificationCode(
            decryptedEnvelope.Transaction.Payload,
            claimAction.VerificationCode);
        if (!string.Equals(
                verificationCode,
                ElectionEligibilityContracts.TemporaryVerificationCode,
                StringComparison.Ordinal))
        {
            return false;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = repository.GetElectionAsync(decryptedEnvelope.Transaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (election is null
            || (election.LifecycleState != ElectionLifecycleState.Draft &&
                election.LifecycleState != ElectionLifecycleState.Open &&
                election.LifecycleState != ElectionLifecycleState.Closed &&
                election.LifecycleState != ElectionLifecycleState.Finalized))
        {
            return false;
        }

        var rosterEntry = repository
            .GetRosterEntryAsync(decryptedEnvelope.Transaction.Payload.ElectionId, claimAction.OrganizationVoterId)
            .GetAwaiter()
            .GetResult();
        if (rosterEntry is null
            || (election.LifecycleState == ElectionLifecycleState.Open && !rosterEntry.WasPresentAtOpen))
        {
            return false;
        }

        var actorExistingEntry = repository
            .GetRosterEntryByLinkedActorAsync(decryptedEnvelope.Transaction.Payload.ElectionId, claimAction.ActorPublicAddress)
            .GetAwaiter()
            .GetResult();
        return actorExistingEntry is null
            || string.Equals(
                actorExistingEntry.OrganizationVoterId,
                claimAction.OrganizationVoterId,
                StringComparison.OrdinalIgnoreCase);
    }

    private bool IsValidActivateRosterEntryAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var activateAction = decryptedEnvelope.DeserializeAction<ActivateElectionRosterEntryActionPayload>();
        if (activateAction is null || !HasMatchingActor(signatory, activateAction.ActorPublicAddress))
        {
            return false;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = repository.GetElectionAsync(decryptedEnvelope.Transaction.Payload.ElectionId).GetAwaiter().GetResult();
        return election is not null
            && election.LifecycleState == ElectionLifecycleState.Open
            && string.Equals(election.OwnerPublicAddress, activateAction.ActorPublicAddress, StringComparison.Ordinal);
    }

    private bool IsValidRegisterVotingCommitmentAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var registerAction = decryptedEnvelope.DeserializeAction<RegisterElectionVotingCommitmentActionPayload>();
        if (registerAction is null)
        {
            RecordValidationFailure(
                transactionId,
                "election_commitment_validation_failed",
                "Voting commitment payload could not be read.");
            return false;
        }

        if (!HasMatchingActor(signatory, registerAction.ActorPublicAddress))
        {
            RecordValidationFailure(
                transactionId,
                "election_commitment_actor_mismatch",
                "Voting commitment validation requires the authenticated voter to match the signed actor.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(registerAction.CommitmentHash))
        {
            RecordValidationFailure(
                transactionId,
                "election_commitment_validation_failed",
                "A non-empty commitment hash is required.");
            return false;
        }

        if (!ElectionDevModePrivacyGuard.TryValidateCommitmentRegistration(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                registerAction.ActorPublicAddress,
                registerAction.CommitmentHash,
                out var commitmentValidationError))
        {
            RecordValidationFailure(
                transactionId,
                "election_commitment_validation_failed",
                commitmentValidationError);
            return false;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = repository.GetElectionAsync(decryptedEnvelope.Transaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (election is null)
        {
            RecordValidationFailure(
                transactionId,
                "election_commitment_not_found",
                $"Election {decryptedEnvelope.Transaction.Payload.ElectionId} was not found.");
            return false;
        }

        if (election.VoteAcceptanceLockedAt.HasValue ||
            election.LifecycleState == ElectionLifecycleState.Closed ||
            election.LifecycleState == ElectionLifecycleState.Finalized)
        {
            RecordValidationFailure(
                transactionId,
                "election_commitment_close_persisted",
                "Voting commitment registration is closed because the persisted close boundary has already been reached.");
            return false;
        }

        if (election.LifecycleState != ElectionLifecycleState.Open)
        {
            RecordValidationFailure(
                transactionId,
                "election_commitment_not_openable_for_registration",
                "Voting commitment registration is only available while the election is open.");
            return false;
        }

        var rosterEntry = repository
            .GetRosterEntryByLinkedActorAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                registerAction.ActorPublicAddress)
            .GetAwaiter()
            .GetResult();
        if (rosterEntry is null)
        {
            RecordValidationFailure(
                transactionId,
                "election_commitment_not_linked",
                "The authenticated Hush account is not linked to a roster entry for this election.");
            return false;
        }

        if (!rosterEntry.WasPresentAtOpen)
        {
            RecordValidationFailure(
                transactionId,
                "election_commitment_not_openable_for_registration",
                "Only voters who were already rostered at open can register a voting commitment.");
            return false;
        }

        if (!IsRosterEntryEligibleForCast(election, rosterEntry))
        {
            RecordValidationFailure(
                transactionId,
                "election_commitment_not_active",
                "This voter does not currently hold an active voting right for commitment registration.");
            return false;
        }

        var existingRegistration = repository
            .GetCommitmentRegistrationAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                rosterEntry!.OrganizationVoterId)
            .GetAwaiter()
            .GetResult();
        if (existingRegistration is not null)
        {
            RecordValidationFailure(
                transactionId,
                "election_commitment_already_registered",
                "A voting commitment is already registered for this voter in this election.");
            return false;
        }

        return true;
    }

    private bool IsValidAcceptBallotCastAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var acceptAction = decryptedEnvelope.DeserializeAction<AcceptElectionBallotCastActionPayload>();
        if (acceptAction is null)
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_validation_failed",
                "Ballot-cast payload could not be read.");
            return false;
        }

        if (!HasMatchingActor(signatory, acceptAction.ActorPublicAddress))
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_actor_mismatch",
                "Ballot-cast validation requires the authenticated voter to match the signed actor.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(acceptAction.IdempotencyKey) ||
            string.IsNullOrWhiteSpace(acceptAction.EncryptedBallotPackage) ||
            string.IsNullOrWhiteSpace(acceptAction.ProofBundle) ||
            string.IsNullOrWhiteSpace(acceptAction.BallotNullifier) ||
            acceptAction.EligibleSetHash is not { Length: > 0 } ||
            string.IsNullOrWhiteSpace(acceptAction.DkgProfileId) ||
            string.IsNullOrWhiteSpace(acceptAction.TallyPublicKeyFingerprint))
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_validation_failed",
                "The final cast request is missing one or more required FEAT-099 acceptance fields.");
            return false;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = repository.GetElectionAsync(decryptedEnvelope.Transaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (election is null)
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_not_found",
                $"Election {decryptedEnvelope.Transaction.Payload.ElectionId} was not found.");
            return false;
        }

        if (!ElectionDevModePrivacyGuard.TryValidateAcceptedBallotArtifacts(
                election.SelectedProfileDevOnly,
                decryptedEnvelope.Transaction.Payload.ElectionId,
                acceptAction.ActorPublicAddress,
                acceptAction.EncryptedBallotPackage,
                acceptAction.ProofBundle,
                acceptAction.BallotNullifier,
                out var castPrivacyValidationError))
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_validation_failed",
                castPrivacyValidationError);
            return false;
        }

        if (election.VoteAcceptanceLockedAt.HasValue ||
            election.LifecycleState == ElectionLifecycleState.Closed ||
            election.LifecycleState == ElectionLifecycleState.Finalized)
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_close_persisted",
                "Vote acceptance is closed because the persisted close boundary has already been reached.");
            return false;
        }

        if (election.LifecycleState != ElectionLifecycleState.Open)
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_wrong_election_context",
                "Votes can only be accepted while the election is open.");
            return false;
        }

        var scopedIdempotencyKey = ComputeScopedHash(acceptAction.IdempotencyKey);
        var existingIdempotency = repository
            .GetCastIdempotencyRecordAsync(decryptedEnvelope.Transaction.Payload.ElectionId, scopedIdempotencyKey)
            .GetAwaiter()
            .GetResult();
        if (existingIdempotency is not null)
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_already_used",
                "This election-scoped submission key has already been used.");
            return false;
        }

        if (HasPendingAcceptBallotCastSubmission(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                acceptAction.IdempotencyKey))
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_still_processing",
                "This election-scoped submission key is already pending in the mempool.");
            return false;
        }

        var rosterEntry = repository
            .GetRosterEntryByLinkedActorAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                acceptAction.ActorPublicAddress)
            .GetAwaiter()
            .GetResult();
        if (rosterEntry is null)
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_not_linked",
                "The authenticated Hush account is not linked to a roster entry for this election.");
            return false;
        }

        if (!IsRosterEntryEligibleForCast(election, rosterEntry))
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_not_active",
                "This voter does not currently hold an active voting right for cast acceptance.");
            return false;
        }

        var commitmentRegistration = repository
            .GetCommitmentRegistrationAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                rosterEntry!.OrganizationVoterId)
            .GetAwaiter()
            .GetResult();
        if (commitmentRegistration is null
            || !string.Equals(
                commitmentRegistration.LinkedActorPublicAddress,
                acceptAction.ActorPublicAddress,
                StringComparison.Ordinal))
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_commitment_missing",
                "A voting commitment must be registered before the final cast can be accepted.");
            return false;
        }

        var checkoffConsumption = repository
            .GetCheckoffConsumptionAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                rosterEntry.OrganizationVoterId)
            .GetAwaiter()
            .GetResult();
        if (checkoffConsumption is not null)
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_already_voted",
                "This voter has already consumed the voting right for this election.");
            return false;
        }

        var participationRecord = repository
            .GetParticipationRecordAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                rosterEntry.OrganizationVoterId)
            .GetAwaiter()
            .GetResult();
        if (participationRecord?.CountsAsParticipation == true)
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_already_voted",
                "This voter is already counted as voted for this election.");
            return false;
        }

        var acceptedBallot = repository
            .GetAcceptedBallotByNullifierAsync(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                acceptAction.BallotNullifier)
            .GetAwaiter()
            .GetResult();
        if (acceptedBallot is not null)
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_duplicate_nullifier",
                "This ballot nullifier has already been accepted for the election.");
            return false;
        }

        if (HasPendingAcceptBallotNullifier(
                decryptedEnvelope.Transaction.Payload.ElectionId,
                acceptAction.BallotNullifier))
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_duplicate_nullifier",
                "This ballot nullifier is already pending in the mempool for the election.");
            return false;
        }

        var boundaryArtifacts = repository
            .GetBoundaryArtifactsAsync(decryptedEnvelope.Transaction.Payload.ElectionId)
            .GetAwaiter()
            .GetResult();
        var openArtifact = boundaryArtifacts.FirstOrDefault(x =>
            x.Id == acceptAction.OpenArtifactId &&
            x.ArtifactType == ElectionBoundaryArtifactType.Open);
        var ceremonySnapshot = ElectionProtectedTallyBinding.ResolveOpenBoundaryBinding(election, openArtifact);
        var matchesBoundary =
            openArtifact is not null &&
            election.OpenArtifactId == openArtifact.Id &&
            ByteArrayEquals(openArtifact.FrozenEligibleVoterSetHash, acceptAction.EligibleSetHash) &&
            ceremonySnapshot is not null &&
            ceremonySnapshot.CeremonyVersionId == acceptAction.CeremonyVersionId &&
            string.Equals(ceremonySnapshot.ProfileId, acceptAction.DkgProfileId, StringComparison.Ordinal) &&
            string.Equals(
                ceremonySnapshot.TallyPublicKeyFingerprint,
                acceptAction.TallyPublicKeyFingerprint,
                StringComparison.Ordinal);

        if (!matchesBoundary)
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_wrong_election_context",
                "The ballot package is bound to a different election boundary than the one currently open.");
            return false;
        }

        if (!TryValidateBindingProofBundle(
                acceptAction,
                out var proofValidationError))
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_proof_invalid",
                proofValidationError);
            return false;
        }

        if (ceremonySnapshot?.TallyPublicKey is { Length: > 0 } &&
            !TryValidateBindingBallotTargetKey(
                acceptAction.EncryptedBallotPackage,
                ceremonySnapshot.TallyPublicKey,
                out var ballotKeyValidationError))
        {
            RecordValidationFailure(
                transactionId,
                "election_cast_wrong_tally_key",
                ballotKeyValidationError);
            return false;
        }

        return true;
    }

    private static bool TryValidateBindingProofBundle(
        AcceptElectionBallotCastActionPayload acceptAction,
        out string error)
    {
        error = string.Empty;

        try
        {
            using var ballotDocument = JsonDocument.Parse(acceptAction.EncryptedBallotPackage);
            if (!TryGetJsonStringProperty(ballotDocument.RootElement, "version", out var ballotVersion) ||
                !string.Equals(ballotVersion, "omega-binding-ballot-v1", StringComparison.Ordinal))
            {
                return true;
            }

            using var proofDocument = JsonDocument.Parse(acceptAction.ProofBundle);
            if (!TryGetJsonStringProperty(proofDocument.RootElement, "version", out var version) ||
                !string.Equals(version, "omega-binding-proof-v1", StringComparison.Ordinal))
            {
                error = "Binding ballot packages require the omega-binding-proof-v1 envelope.";
                return false;
            }

            if (!TryGetJsonStringProperty(proofDocument.RootElement, "proofType", out var proofType) ||
                !string.Equals(proofType, "binding-circuit-envelope", StringComparison.Ordinal))
            {
                error = "Binding proof bundles must declare the binding-circuit-envelope proof type.";
                return false;
            }

            if (!TryGetJsonStringProperty(proofDocument.RootElement, "proofProfile", out var proofProfile) ||
                !string.Equals(proofProfile, "PRODUCTION_LIKE_PROFILE", StringComparison.Ordinal))
            {
                error = "Binding proof bundles must use the production-like proof profile.";
                return false;
            }

            if (!TryGetJsonStringProperty(proofDocument.RootElement, "circuitVersion", out _))
            {
                error = "Binding proof bundles must declare a circuit version.";
                return false;
            }

            if (!TryGetJsonStringProperty(proofDocument.RootElement, "artifactShape", out var artifactShape) ||
                !string.Equals(artifactShape, "opaque-one-hot-elgamal", StringComparison.Ordinal))
            {
                error = "Binding proof bundles must declare the opaque-one-hot-elgamal artifact shape.";
                return false;
            }

            var expectedBallotPackageHash = ComputeLowerHexSha256(acceptAction.EncryptedBallotPackage);
            if (!TryGetJsonStringProperty(proofDocument.RootElement, "ballotPackageHash", out var ballotPackageHash) ||
                !string.Equals(ballotPackageHash, expectedBallotPackageHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "Binding proof bundles must match the submitted ballot package hash.";
                return false;
            }

            if (!TryGetJsonStringProperty(proofDocument.RootElement, "openArtifactId", out var openArtifactId) ||
                !Guid.TryParse(openArtifactId, out var parsedOpenArtifactId) ||
                parsedOpenArtifactId != acceptAction.OpenArtifactId)
            {
                error = "Binding proof bundles must bind the active open artifact.";
                return false;
            }

            var expectedEligibleSetHash = Convert.ToBase64String(acceptAction.EligibleSetHash);
            if (!TryGetJsonStringProperty(proofDocument.RootElement, "eligibleSetHash", out var eligibleSetHash) ||
                !string.Equals(eligibleSetHash, expectedEligibleSetHash, StringComparison.Ordinal))
            {
                error = "Binding proof bundles must bind the active eligible-set hash.";
                return false;
            }

            if (!TryGetJsonStringProperty(proofDocument.RootElement, "ceremonyVersionId", out var ceremonyVersionId) ||
                !Guid.TryParse(ceremonyVersionId, out var parsedCeremonyVersionId) ||
                parsedCeremonyVersionId != acceptAction.CeremonyVersionId)
            {
                error = "Binding proof bundles must bind the active ceremony version.";
                return false;
            }

            if (!TryGetJsonStringProperty(proofDocument.RootElement, "dkgProfileId", out var dkgProfileId) ||
                !string.Equals(dkgProfileId, acceptAction.DkgProfileId, StringComparison.Ordinal))
            {
                error = "Binding proof bundles must bind the active ceremony profile.";
                return false;
            }

            if (!TryGetJsonStringProperty(proofDocument.RootElement, "tallyPublicKeyFingerprint", out var tallyPublicKeyFingerprint) ||
                !string.Equals(
                    tallyPublicKeyFingerprint,
                    acceptAction.TallyPublicKeyFingerprint,
                    StringComparison.Ordinal))
            {
                error = "Binding proof bundles must bind the active tally public key fingerprint.";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            error = "Binding proof bundles must be readable JSON.";
            return false;
        }
    }

    private static bool TryValidateBindingBallotTargetKey(
        string encryptedBallotPackage,
        byte[] expectedTallyPublicKey,
        out string error)
    {
        error = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(encryptedBallotPackage);
            if (!document.RootElement.TryGetProperty("version", out var versionElement) ||
                !string.Equals(versionElement.GetString(), "omega-binding-ballot-v1", StringComparison.Ordinal))
            {
                return true;
            }

            if (!document.RootElement.TryGetProperty("publicKey", out var publicKeyElement) ||
                !publicKeyElement.TryGetProperty("x", out var xElement) ||
                !publicKeyElement.TryGetProperty("y", out var yElement))
            {
                error = "Binding ballot packages must carry the ceremony tally public key.";
                return false;
            }

            var providedPoint = new ECPoint(
                BigInteger.Parse(xElement.GetString() ?? string.Empty, CultureInfo.InvariantCulture),
                BigInteger.Parse(yElement.GetString() ?? string.Empty, CultureInfo.InvariantCulture));
            var expectedPoint = ECPoint.FromBytes(expectedTallyPublicKey);
            if (providedPoint.X != expectedPoint.X || providedPoint.Y != expectedPoint.Y)
            {
                error = "Binding ballot packages must target the active ceremony tally public key.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or ArgumentException)
        {
            error = "Binding ballot packages must carry a readable tally public key.";
            return false;
        }
    }

    private static bool TryGetJsonStringProperty(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!TryGetJsonPropertyCaseInsensitive(element, propertyName, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var rawValue = propertyElement.GetString();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        value = rawValue.Trim();
        return true;
    }

    private static bool TryGetJsonPropertyCaseInsensitive(
        JsonElement element,
        string propertyName,
        out JsonElement propertyElement)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    propertyElement = property.Value;
                    return true;
                }
            }
        }

        propertyElement = default;
        return false;
    }

    private bool IsValidInviteTrusteeAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var inviteTrusteeAction = decryptedEnvelope.DeserializeAction<InviteElectionTrusteeActionPayload>();
        if (inviteTrusteeAction is null)
        {
            return false;
        }

        if (EncryptedElectionEnvelopePayloadHandler.IsPrivacyHardenedEnvelopeVersion(
                decryptedEnvelope.Transaction.Payload.EnvelopeVersion))
        {
            if (!string.IsNullOrWhiteSpace(inviteTrusteeAction.TrusteeEncryptedElectionPrivateKey))
            {
                return RejectWithValidationFailure(
                    transactionId,
                    "election_invite_trustee_wrap_public",
                    "Trustee envelope access material must not be exposed in the public v2.1 action payload.");
            }

            var inviteArtifacts = decryptedEnvelope.DeserializeActionArtifacts<InviteElectionTrusteeActionArtifacts>();
            if (inviteArtifacts is null || string.IsNullOrWhiteSpace(inviteArtifacts.TrusteeEncryptedElectionPrivateKey))
            {
                return RejectWithValidationFailure(
                    transactionId,
                    "election_invite_trustee_wrap_missing",
                    "Trustee envelope access material is missing from the v2.1 action artifacts.");
            }
        }

        var unsignedTransaction = InviteElectionTrusteePayloadHandler.CreateNew(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            inviteTrusteeAction.InvitationId,
            inviteTrusteeAction.ActorPublicAddress,
            inviteTrusteeAction.TrusteeUserAddress,
            inviteTrusteeAction.TrusteeDisplayName);
        var signedTransaction = new SignedTransaction<InviteElectionTrusteePayload>(
            unsignedTransaction,
            new SignatureInfo(signatory, decryptedEnvelope.Transaction.UserSignature!.Signature));

        return _inviteElectionTrusteeContentHandler.ValidateAndSign(signedTransaction) is not null;
    }

    private bool IsValidCreateReportAccessGrantAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var createGrantAction = decryptedEnvelope.DeserializeAction<CreateElectionReportAccessGrantActionPayload>();
        var designatedAuditorPublicAddress = createGrantAction?.DesignatedAuditorPublicAddress?.Trim() ?? string.Empty;
        if (createGrantAction is null
            || !HasMatchingActor(signatory, createGrantAction.ActorPublicAddress)
            || string.IsNullOrWhiteSpace(designatedAuditorPublicAddress))
        {
            return false;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = repository.GetElectionAsync(decryptedEnvelope.Transaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (election is null
            || !string.Equals(election.OwnerPublicAddress, createGrantAction.ActorPublicAddress, StringComparison.Ordinal)
            || string.Equals(election.OwnerPublicAddress, designatedAuditorPublicAddress, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var invitations = repository.GetTrusteeInvitationsAsync(decryptedEnvelope.Transaction.Payload.ElectionId)
            .GetAwaiter()
            .GetResult();
        if (invitations.Any(x =>
                x.Status == ElectionTrusteeInvitationStatus.Accepted &&
                string.Equals(x.TrusteeUserAddress, designatedAuditorPublicAddress, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var existingGrant = repository
            .GetReportAccessGrantAsync(decryptedEnvelope.Transaction.Payload.ElectionId, designatedAuditorPublicAddress)
            .GetAwaiter()
            .GetResult();
        return existingGrant is null;
    }

    private bool IsValidResolveTrusteeInvitationAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory,
        bool ownerOnly)
    {
        var resolveAction = decryptedEnvelope.DeserializeAction<ResolveElectionTrusteeInvitationActionPayload>();
        if (resolveAction is null || !HasMatchingActor(signatory, resolveAction.ActorPublicAddress))
        {
            return false;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = repository.GetElectionAsync(decryptedEnvelope.Transaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (election is null || election.LifecycleState != ElectionLifecycleState.Draft)
        {
            return false;
        }

        var invitation = repository.GetTrusteeInvitationAsync(resolveAction.InvitationId).GetAwaiter().GetResult();
        if (invitation is null
            || invitation.ElectionId != decryptedEnvelope.Transaction.Payload.ElectionId
            || invitation.Status != ElectionTrusteeInvitationStatus.Pending)
        {
            return false;
        }

        var authorizedActor = ownerOnly ? election.OwnerPublicAddress : invitation.TrusteeUserAddress;
        if (!string.Equals(authorizedActor, signatory, StringComparison.Ordinal))
        {
            return false;
        }

        var pendingProposal = repository
            .GetPendingGovernedProposalAsync(decryptedEnvelope.Transaction.Payload.ElectionId)
            .GetAwaiter()
            .GetResult();
        return !IsDraftBlockedByPendingOpenProposal(pendingProposal);
    }

    private bool IsValidRevokeTrusteeInvitationAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var revokeAction = decryptedEnvelope.DeserializeAction<RevokeElectionTrusteeInvitationActionPayload>();
        if (revokeAction is null)
        {
            return false;
        }

        var unsignedTransaction = RevokeElectionTrusteeInvitationPayloadHandler.CreateNew(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            revokeAction.InvitationId,
            revokeAction.ActorPublicAddress);
        var signedTransaction = new SignedTransaction<RevokeElectionTrusteeInvitationPayload>(
            unsignedTransaction,
            new SignatureInfo(signatory, decryptedEnvelope.Transaction.UserSignature!.Signature));

        return _revokeElectionTrusteeInvitationContentHandler.ValidateAndSign(signedTransaction) is not null;
    }

    private bool IsValidStartGovernedProposalAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var startAction = decryptedEnvelope.DeserializeAction<StartElectionGovernedProposalActionPayload>();
        if (startAction is null)
        {
            return false;
        }

        var unsignedTransaction = StartElectionGovernedProposalPayloadHandler.CreateNew(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            startAction.ProposalId,
            startAction.ActionType,
            startAction.ActorPublicAddress);
        var signedTransaction = new SignedTransaction<StartElectionGovernedProposalPayload>(
            unsignedTransaction,
            new SignatureInfo(signatory, decryptedEnvelope.Transaction.UserSignature!.Signature));

        return _startElectionGovernedProposalContentHandler.ValidateAndSign(signedTransaction) is not null;
    }

    private bool IsValidApproveGovernedProposalAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var approveAction = decryptedEnvelope.DeserializeAction<ApproveElectionGovernedProposalActionPayload>();
        if (approveAction is null)
        {
            return false;
        }

        var unsignedTransaction = ApproveElectionGovernedProposalPayloadHandler.CreateNew(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            approveAction.ProposalId,
            approveAction.ActorPublicAddress,
            approveAction.ApprovalNote);
        var signedTransaction = new SignedTransaction<ApproveElectionGovernedProposalPayload>(
            unsignedTransaction,
            new SignatureInfo(signatory, decryptedEnvelope.Transaction.UserSignature!.Signature));

        return _approveElectionGovernedProposalContentHandler.ValidateAndSign(signedTransaction) is not null;
    }

    private bool IsValidRetryGovernedProposalExecutionAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var retryAction = decryptedEnvelope.DeserializeAction<RetryElectionGovernedProposalExecutionActionPayload>();
        if (retryAction is null)
        {
            return false;
        }

        var unsignedTransaction = RetryElectionGovernedProposalExecutionPayloadHandler.CreateNew(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            retryAction.ProposalId,
            retryAction.ActorPublicAddress);
        var signedTransaction = new SignedTransaction<RetryElectionGovernedProposalExecutionPayload>(
            unsignedTransaction,
            new SignatureInfo(signatory, decryptedEnvelope.Transaction.UserSignature!.Signature));

        return _retryElectionGovernedProposalExecutionContentHandler.ValidateAndSign(signedTransaction) is not null;
    }

    private bool IsValidOpenElectionAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var openAction = decryptedEnvelope.DeserializeAction<OpenElectionActionPayload>();
        if (openAction is null)
        {
            return false;
        }

        var unsignedTransaction = OpenElectionPayloadHandler.CreateNew(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            openAction.ActorPublicAddress,
            openAction.RequiredWarningCodes,
            openAction.FrozenEligibleVoterSetHash,
            openAction.TrusteePolicyExecutionReference,
            openAction.ReportingPolicyExecutionReference,
            openAction.ReviewWindowExecutionReference);
        var signedTransaction = new SignedTransaction<OpenElectionPayload>(
            unsignedTransaction,
            new SignatureInfo(signatory, decryptedEnvelope.Transaction.UserSignature!.Signature));

        return _openElectionContentHandler.ValidateAndSign(signedTransaction) is not null;
    }

    private bool IsValidCloseElectionAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var closeAction = decryptedEnvelope.DeserializeAction<CloseElectionActionPayload>();
        if (closeAction is null)
        {
            return false;
        }

        var unsignedTransaction = CloseElectionPayloadHandler.CreateNew(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            closeAction.ActorPublicAddress,
            closeAction.AcceptedBallotSetHash,
            closeAction.FinalEncryptedTallyHash);
        var signedTransaction = new SignedTransaction<CloseElectionPayload>(
            unsignedTransaction,
            new SignatureInfo(signatory, decryptedEnvelope.Transaction.UserSignature!.Signature));

        return _closeElectionContentHandler.ValidateAndSign(signedTransaction) is not null;
    }

    private bool IsValidFinalizeElectionAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var finalizeAction = decryptedEnvelope.DeserializeAction<FinalizeElectionActionPayload>();
        if (finalizeAction is null)
        {
            return false;
        }

        var unsignedTransaction = FinalizeElectionPayloadHandler.CreateNew(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            finalizeAction.ActorPublicAddress,
            finalizeAction.AcceptedBallotSetHash,
            finalizeAction.FinalEncryptedTallyHash);
        var signedTransaction = new SignedTransaction<FinalizeElectionPayload>(
            unsignedTransaction,
            new SignatureInfo(signatory, decryptedEnvelope.Transaction.UserSignature!.Signature));

        return _finalizeElectionContentHandler.ValidateAndSign(signedTransaction) is not null;
    }

    private bool IsValidSubmitFinalizationShareAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var shareAction = decryptedEnvelope.DeserializeAction<SubmitElectionFinalizationShareActionPayload>();
        if (shareAction is null || !HasMatchingActor(signatory, shareAction.ActorPublicAddress))
        {
            return false;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = repository.GetElectionAsync(decryptedEnvelope.Transaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (election is null ||
            election.LifecycleState != ElectionLifecycleState.Closed)
        {
            return false;
        }

        var session = repository.GetFinalizationSessionAsync(shareAction.FinalizationSessionId).GetAwaiter().GetResult();
        if (session is null ||
            session.ElectionId != decryptedEnvelope.Transaction.Payload.ElectionId ||
            session.Status == ElectionFinalizationSessionStatus.Completed)
        {
            return false;
        }

        if (session.SessionPurpose != ElectionFinalizationSessionPurpose.CloseCounting)
        {
            RecordValidationFailure(
                transactionId,
                "election_finalization_share_wrong_session_purpose",
                "Trustee shares are only accepted for close-counting release sessions.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(shareAction.ShareMaterial))
        {
            RecordValidationFailure(
                transactionId,
                "election_finalization_share_plaintext_forbidden",
                "Plaintext trustee share material is no longer accepted in election envelopes.");
            return false;
        }

        if (!shareAction.CloseCountingJobId.HasValue ||
            string.IsNullOrWhiteSpace(shareAction.ExecutorKeyAlgorithm) ||
            string.IsNullOrWhiteSpace(shareAction.EncryptedExecutorSubmission))
        {
            RecordValidationFailure(
                transactionId,
                "election_finalization_share_missing_executor_submission",
                "Executor-encrypted trustee share submission and job binding are required.");
            return false;
        }

        if (shareAction.CloseCountingJobId.HasValue)
        {
            var closeCountingJob = repository.GetCloseCountingJobAsync(shareAction.CloseCountingJobId.Value).GetAwaiter().GetResult();
            if (closeCountingJob is null ||
                closeCountingJob.FinalizationSessionId != session.Id)
            {
                RecordValidationFailure(
                    transactionId,
                    "election_finalization_share_unknown_close_counting_job",
                    "Trustee share submission must bind to the active close-counting job.");
                return false;
            }

            var executorEnvelope = repository.GetExecutorSessionKeyEnvelopeAsync(closeCountingJob.Id).GetAwaiter().GetResult();
            if (executorEnvelope is null ||
                !string.Equals(
                    shareAction.ExecutorKeyAlgorithm?.Trim(),
                    executorEnvelope.KeyAlgorithm,
                    StringComparison.Ordinal))
            {
                RecordValidationFailure(
                    transactionId,
                    "election_finalization_share_unknown_executor_key",
                    "Trustee share submission must use the active executor session key.");
                return false;
            }
        }

        return session.EligibleTrustees.Any(x =>
            string.Equals(x.TrusteeUserAddress, signatory, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsValidStartCeremonyAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var startAction = decryptedEnvelope.DeserializeAction<StartElectionCeremonyActionPayload>();
        if (startAction is null
            || !HasMatchingActor(signatory, startAction.ActorPublicAddress))
        {
            return false;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        if (!IsValidCeremonyBootstrap(
                repository,
                decryptedEnvelope.Transaction.Payload.ElectionId,
                startAction.ActorPublicAddress,
                startAction.ProfileId,
                requireMissingActiveVersion: true))
        {
            return false;
        }

        return true;
    }

    private bool IsValidRestartCeremonyAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var restartAction = decryptedEnvelope.DeserializeAction<RestartElectionCeremonyActionPayload>();
        if (restartAction is null
            || !HasMatchingActor(signatory, restartAction.ActorPublicAddress)
            || string.IsNullOrWhiteSpace(restartAction.RestartReason))
        {
            return false;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        return IsValidCeremonyBootstrap(
            repository,
            decryptedEnvelope.Transaction.Payload.ElectionId,
            restartAction.ActorPublicAddress,
            restartAction.ProfileId,
            requireMissingActiveVersion: false);
    }

    private bool IsValidPublishCeremonyTransportKeyAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var publishAction = decryptedEnvelope.DeserializeAction<PublishElectionCeremonyTransportKeyActionPayload>();
        if (publishAction is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_publish_invalid_payload",
                "Publish ceremony transport key action payload could not be read.");
        }

        if (!HasMatchingActor(signatory, publishAction.ActorPublicAddress))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_publish_actor_mismatch",
                "The authenticated trustee must match the signed publish transport key actor.");
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var context = LoadActiveCeremonyTrusteeContext(
            repository,
            decryptedEnvelope.Transaction.Payload.ElectionId,
            publishAction.CeremonyVersionId,
            publishAction.ActorPublicAddress,
            requireOwnerActor: false);
        if (!context.IsSuccess || context.TrusteeState is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                context.FailureCode ?? "election_ceremony_publish_invalid_context",
                context.FailureMessage ?? "Publish ceremony transport key action is not available in the current ceremony context.");
        }

        return TryValidateCeremonyTransition(
            transactionId,
            "election_ceremony_publish_invalid_state",
            () => context.TrusteeState.PublishTransportKey(
                publishAction.TransportPublicKeyFingerprint,
                DateTime.UtcNow));
    }

    private bool IsValidJoinCeremonyAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var joinAction = decryptedEnvelope.DeserializeAction<JoinElectionCeremonyActionPayload>();
        if (joinAction is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_join_invalid_payload",
                "Join ceremony action payload could not be read.");
        }

        if (!HasMatchingActor(signatory, joinAction.ActorPublicAddress))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_join_actor_mismatch",
                "The authenticated trustee must match the signed join ceremony actor.");
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var context = LoadActiveCeremonyTrusteeContext(
            repository,
            decryptedEnvelope.Transaction.Payload.ElectionId,
            joinAction.CeremonyVersionId,
            joinAction.ActorPublicAddress,
            requireOwnerActor: false);
        if (!context.IsSuccess || context.TrusteeState is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                context.FailureCode ?? "election_ceremony_join_invalid_context",
                context.FailureMessage ?? "Join ceremony action is not available in the current ceremony context.");
        }

        return TryValidateCeremonyTransition(
            transactionId,
            "election_ceremony_join_invalid_state",
            () => context.TrusteeState.MarkJoined(DateTime.UtcNow));
    }

    private bool IsValidRecordCeremonySelfTestAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var selfTestAction = decryptedEnvelope.DeserializeAction<RecordElectionCeremonySelfTestActionPayload>();
        if (selfTestAction is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_self_test_invalid_payload",
                "Record ceremony self-test action payload could not be read.");
        }

        if (!HasMatchingActor(signatory, selfTestAction.ActorPublicAddress))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_self_test_actor_mismatch",
                "The authenticated trustee must match the signed self-test actor.");
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var context = LoadActiveCeremonyTrusteeContext(
            repository,
            decryptedEnvelope.Transaction.Payload.ElectionId,
            selfTestAction.CeremonyVersionId,
            selfTestAction.ActorPublicAddress,
            requireOwnerActor: false);
        if (!context.IsSuccess || context.TrusteeState is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                context.FailureCode ?? "election_ceremony_self_test_invalid_context",
                context.FailureMessage ?? "Record ceremony self-test action is not available in the current ceremony context.");
        }

        return TryValidateCeremonyTransition(
            transactionId,
            "election_ceremony_self_test_invalid_state",
            () => context.TrusteeState.RecordSelfTestSuccess(DateTime.UtcNow));
    }

    private bool IsValidSubmitCeremonyMaterialAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var submitAction = decryptedEnvelope.DeserializeAction<SubmitElectionCeremonyMaterialActionPayload>();
        if (submitAction is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_submit_invalid_payload",
                "Submit ceremony material action payload could not be read.");
        }

        if (!HasMatchingActor(signatory, submitAction.ActorPublicAddress))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_submit_actor_mismatch",
                "The authenticated trustee must match the signed submit ceremony material actor.");
        }

        if (string.IsNullOrWhiteSpace(submitAction.EncryptedPayload))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_submit_invalid_payload",
                "Encrypted ceremony payload is required.");
        }

        if (string.IsNullOrWhiteSpace(submitAction.ShareVersion))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_submit_invalid_payload",
                "Share version is required.");
        }

        var curve = new BabyJubJubCurve();
        if (!ElectionTallyPublicKeyDerivation.TryParsePointPayload(
                submitAction.CloseCountingPublicCommitment,
                curve,
                out var parsedCommitment,
                out var commitmentValidationError))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_submit_invalid_commitment",
                commitmentValidationError);
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var context = LoadActiveCeremonyTrusteeContext(
            repository,
            decryptedEnvelope.Transaction.Payload.ElectionId,
            submitAction.CeremonyVersionId,
            submitAction.ActorPublicAddress,
            requireOwnerActor: false);
        if (!context.IsSuccess || context.Version is null || context.TrusteeState is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                context.FailureCode ?? "election_ceremony_submit_invalid_context",
                context.FailureMessage ?? "Submit ceremony material action is not available in the current ceremony context.");
        }

        if (submitAction.RecipientTrusteeUserAddress is not null
            && !context.Version.BoundTrustees.Any(x =>
                string.Equals(x.TrusteeUserAddress, submitAction.RecipientTrusteeUserAddress, StringComparison.OrdinalIgnoreCase)))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_submit_invalid_recipient",
                "Recipient trustee is not bound to the active ceremony version.");
        }

        return TryValidateCeremonyTransition(
            transactionId,
            "election_ceremony_submit_invalid_state",
            () => context.TrusteeState.RecordMaterialSubmitted(
                DateTime.UtcNow,
                submitAction.ShareVersion,
                parsedCommitment!));
    }

    private bool IsValidRecordCeremonyValidationFailureAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var validationFailureAction =
            decryptedEnvelope.DeserializeAction<RecordElectionCeremonyValidationFailureActionPayload>();
        if (validationFailureAction is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_validation_failure_invalid_payload",
                "Record ceremony validation failure action payload could not be read.");
        }

        if (!HasMatchingActor(signatory, validationFailureAction.ActorPublicAddress))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_validation_failure_actor_mismatch",
                "The authenticated owner must match the signed validation failure actor.");
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var ownerContext = LoadActiveCeremonyTrusteeContext(
            repository,
            decryptedEnvelope.Transaction.Payload.ElectionId,
            validationFailureAction.CeremonyVersionId,
            validationFailureAction.ActorPublicAddress,
            requireOwnerActor: true);
        if (!ownerContext.IsSuccess)
        {
            return RejectWithValidationFailure(
                transactionId,
                ownerContext.FailureCode ?? "election_ceremony_validation_failure_invalid_context",
                ownerContext.FailureMessage ?? "Record ceremony validation failure action is not available in the current ceremony context.");
        }

        var trusteeState = repository
            .GetCeremonyTrusteeStateAsync(validationFailureAction.CeremonyVersionId, validationFailureAction.TrusteeUserAddress)
            .GetAwaiter()
            .GetResult();
        if (trusteeState is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_validation_failure_unknown_trustee",
                $"Trustee {validationFailureAction.TrusteeUserAddress} is not bound to ceremony version {validationFailureAction.CeremonyVersionId}.");
        }

        return TryValidateCeremonyTransition(
            transactionId,
            "election_ceremony_validation_failure_invalid_state",
            () => trusteeState.RecordValidationFailure(
                validationFailureAction.ValidationFailureReason,
                DateTime.UtcNow));
    }

    private bool IsValidCompleteCeremonyTrusteeAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var completeAction = decryptedEnvelope.DeserializeAction<CompleteElectionCeremonyTrusteeActionPayload>();
        if (completeAction is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_complete_invalid_payload",
                "Complete ceremony trustee action payload could not be read.");
        }

        if (!HasMatchingActor(signatory, completeAction.ActorPublicAddress))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_complete_actor_mismatch",
                "The authenticated owner must match the signed complete ceremony actor.");
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var ownerContext = LoadActiveCeremonyTrusteeContext(
            repository,
            decryptedEnvelope.Transaction.Payload.ElectionId,
            completeAction.CeremonyVersionId,
            completeAction.ActorPublicAddress,
            requireOwnerActor: true);
        if (!ownerContext.IsSuccess || ownerContext.Version is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                ownerContext.FailureCode ?? "election_ceremony_complete_invalid_context",
                ownerContext.FailureMessage ?? "Complete ceremony trustee action is not available in the current ceremony context.");
        }

        var trusteeState = repository
            .GetCeremonyTrusteeStateAsync(completeAction.CeremonyVersionId, completeAction.TrusteeUserAddress)
            .GetAwaiter()
            .GetResult();
        if (trusteeState is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_complete_unknown_trustee",
                $"Trustee {completeAction.TrusteeUserAddress} is not bound to ceremony version {completeAction.CeremonyVersionId}.");
        }

        if (!TryValidateCeremonyTransition(
                transactionId,
                "election_ceremony_complete_invalid_state",
                () => trusteeState.MarkCompleted(DateTime.UtcNow, completeAction.ShareVersion)))
        {
            return false;
        }

        if (ownerContext.Version.Status != ElectionCeremonyVersionStatus.InProgress &&
            ownerContext.Version.Status != ElectionCeremonyVersionStatus.Ready)
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_complete_invalid_version_status",
                "Only active in-progress or ready ceremony versions can accept trustee completions.");
        }

        return true;
    }

    private bool IsValidRecordCeremonyShareExportAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var exportAction = decryptedEnvelope.DeserializeAction<RecordElectionCeremonyShareExportActionPayload>();
        if (exportAction is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_export_invalid_payload",
                "Record ceremony share export action payload could not be read.");
        }

        if (!HasMatchingActor(signatory, exportAction.ActorPublicAddress))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_export_actor_mismatch",
                "The authenticated trustee must match the signed share export actor.");
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var custodyContext = LoadShareCustodyContext(
            repository,
            decryptedEnvelope.Transaction.Payload.ElectionId,
            exportAction.CeremonyVersionId,
            exportAction.ActorPublicAddress,
            exportAction.ActorPublicAddress,
            exportAction.ShareVersion,
            requireOwnerActor: false);
        if (!custodyContext.IsSuccess || custodyContext.CustodyRecord is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                custodyContext.FailureCode ?? "election_ceremony_export_invalid_context",
                custodyContext.FailureMessage ?? "Record ceremony share export action is not available in the current ceremony context.");
        }

        return TryValidateCeremonyTransition(
            transactionId,
            "election_ceremony_export_invalid_state",
            () => custodyContext.CustodyRecord.RecordExport(DateTime.UtcNow));
    }

    private bool IsValidRecordCeremonyShareImportAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var transactionId = decryptedEnvelope.Transaction.TransactionId.Value;
        var importAction = decryptedEnvelope.DeserializeAction<RecordElectionCeremonyShareImportActionPayload>();
        if (importAction is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_import_invalid_payload",
                "Record ceremony share import action payload could not be read.");
        }

        if (!HasMatchingActor(signatory, importAction.ActorPublicAddress))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_import_actor_mismatch",
                "The authenticated trustee must match the signed share import actor.");
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var custodyContext = LoadShareCustodyContext(
            repository,
            decryptedEnvelope.Transaction.Payload.ElectionId,
            importAction.CeremonyVersionId,
            importAction.ActorPublicAddress,
            importAction.ActorPublicAddress,
            shareVersion: null,
            requireOwnerActor: false);
        if (!custodyContext.IsSuccess || custodyContext.CustodyRecord is null)
        {
            return RejectWithValidationFailure(
                transactionId,
                custodyContext.FailureCode ?? "election_ceremony_import_invalid_context",
                custodyContext.FailureMessage ?? "Record ceremony share import action is not available in the current ceremony context.");
        }

        if (!custodyContext.CustodyRecord.MatchesImportBinding(
                importAction.ImportedElectionId,
                importAction.ImportedCeremonyVersionId,
                importAction.ImportedTrusteeUserAddress,
                importAction.ImportedShareVersion))
        {
            return RejectWithValidationFailure(
                transactionId,
                "election_ceremony_import_binding_mismatch",
                "Imported share package does not match the exact ceremony binding.");
        }

        return true;
    }

    private bool IsValidCeremonyBootstrap(
        IElectionsRepository repository,
        ElectionId electionId,
        string actorPublicAddress,
        string profileId,
        bool requireMissingActiveVersion)
    {
        var election = repository.GetElectionAsync(electionId).GetAwaiter().GetResult();
        if (!IsDraftTrusteeSetupOwnerActionValid(election, actorPublicAddress))
        {
            return false;
        }

        var pendingProposal = repository.GetPendingGovernedProposalAsync(electionId).GetAwaiter().GetResult();
        if (IsDraftBlockedByPendingOpenProposal(pendingProposal))
        {
            return false;
        }

        var profile = repository.GetCeremonyProfileAsync(profileId).GetAwaiter().GetResult();
        if (profile is null || (profile.DevOnly && !_ceremonyOptions.EnableDevCeremonyProfiles))
        {
            return false;
        }

        if (!election!.RequiredApprovalCount.HasValue
            || profile.RequiredApprovalCount != election.RequiredApprovalCount.Value)
        {
            return false;
        }

        var invitations = repository.GetTrusteeInvitationsAsync(electionId).GetAwaiter().GetResult();
        var acceptedTrustees = invitations.Count(x => x.Status == ElectionTrusteeInvitationStatus.Accepted);
        if (acceptedTrustees != profile.TrusteeCount)
        {
            return false;
        }

        var existingActiveVersion = repository.GetActiveCeremonyVersionAsync(electionId).GetAwaiter().GetResult();
        return !requireMissingActiveVersion || existingActiveVersion is null;
    }

    private static bool HasMatchingActor(string signatory, string actorPublicAddress) =>
        !string.IsNullOrWhiteSpace(signatory)
        && string.Equals(signatory, actorPublicAddress, StringComparison.Ordinal);

    private static bool IsRosterEntryEligibleForCast(
        ElectionRecord election,
        ElectionRosterEntryRecord? rosterEntry)
    {
        if (rosterEntry is null || !rosterEntry.WasPresentAtOpen)
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

    private static string ComputeScopedHash(string value) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(value.Trim())));

    private static string ComputeLowerHexSha256(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))
            .ToLowerInvariant();

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

    private bool HasPendingAcceptBallotCastSubmission(
        ElectionId electionId,
        string idempotencyKey)
    {
        var normalizedIdempotencyKey = idempotencyKey.Trim();
        if (string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
        {
            return false;
        }

        foreach (var transaction in _memPoolService.PeekPendingValidatedTransactions())
        {
            var decryptedEnvelope = _envelopeCryptoService.TryDecryptValidated(transaction);
            if (decryptedEnvelope is null ||
                decryptedEnvelope.ActionType != EncryptedElectionEnvelopeActionTypes.AcceptBallotCast ||
                decryptedEnvelope.Transaction.Payload.ElectionId != electionId)
            {
                continue;
            }

            var acceptAction = decryptedEnvelope.DeserializeAction<AcceptElectionBallotCastActionPayload>();
            if (acceptAction is null)
            {
                continue;
            }

            if (string.Equals(
                    acceptAction.IdempotencyKey?.Trim(),
                    normalizedIdempotencyKey,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasPendingAcceptBallotNullifier(
        ElectionId electionId,
        string ballotNullifier)
    {
        var normalizedBallotNullifier = ballotNullifier.Trim();
        if (string.IsNullOrWhiteSpace(normalizedBallotNullifier))
        {
            return false;
        }

        foreach (var transaction in _memPoolService.PeekPendingValidatedTransactions())
        {
            var decryptedEnvelope = _envelopeCryptoService.TryDecryptValidated(transaction);
            if (decryptedEnvelope is null ||
                decryptedEnvelope.ActionType != EncryptedElectionEnvelopeActionTypes.AcceptBallotCast ||
                decryptedEnvelope.Transaction.Payload.ElectionId != electionId)
            {
                continue;
            }

            var acceptAction = decryptedEnvelope.DeserializeAction<AcceptElectionBallotCastActionPayload>();
            if (acceptAction is null)
            {
                continue;
            }

            if (string.Equals(
                    acceptAction.BallotNullifier?.Trim(),
                    normalizedBallotNullifier,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ByteArrayEquals(byte[]? left, byte[]? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index += 1)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDraftTrusteeSetupOwnerActionValid(ElectionRecord? election, string actorPublicAddress) =>
        election is not null
        && election.LifecycleState == ElectionLifecycleState.Draft
        && election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold
        && string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal);

    private static bool IsDraftBlockedByPendingOpenProposal(ElectionGovernedProposalRecord? pendingProposal) =>
        pendingProposal is not null
        && pendingProposal.ActionType == ElectionGovernedActionType.Open
        && pendingProposal.ExecutionStatus == ElectionGovernedProposalExecutionStatus.WaitingForApprovals;

    private static CeremonyTrusteeContextValidationResult LoadActiveCeremonyTrusteeContext(
        IElectionsRepository repository,
        ElectionId electionId,
        Guid ceremonyVersionId,
        string actorPublicAddress,
        bool requireOwnerActor)
    {
        var election = repository.GetElectionAsync(electionId).GetAwaiter().GetResult();
        if (election is null)
        {
            return CeremonyTrusteeContextValidationResult.Failure(
                "election_ceremony_not_found",
                $"Election {electionId} was not found.");
        }

        if (election.LifecycleState != ElectionLifecycleState.Draft)
        {
            return CeremonyTrusteeContextValidationResult.Failure(
                "election_ceremony_not_draft",
                "Election ceremony actions are only allowed while the election remains in draft.");
        }

        var version = repository.GetCeremonyVersionAsync(ceremonyVersionId).GetAwaiter().GetResult();
        if (version is null || version.ElectionId != electionId || !version.IsActive)
        {
            return CeremonyTrusteeContextValidationResult.Failure(
                "election_ceremony_version_not_found",
                $"Ceremony version {ceremonyVersionId} was not found for election {electionId}.");
        }

        var activeVersion = repository.GetActiveCeremonyVersionAsync(electionId).GetAwaiter().GetResult();
        if (activeVersion is null || activeVersion.Id != ceremonyVersionId)
        {
            return CeremonyTrusteeContextValidationResult.Failure(
                "election_ceremony_inactive_version",
                "Only the active ceremony version may receive new trustee actions.");
        }

        if (requireOwnerActor)
        {
            return string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal)
                ? CeremonyTrusteeContextValidationResult.Success(election, version, null)
                : CeremonyTrusteeContextValidationResult.Failure(
                    "election_ceremony_owner_required",
                    "Only the election owner can perform this ceremony action.");
        }

        var trusteeState = repository.GetCeremonyTrusteeStateAsync(ceremonyVersionId, actorPublicAddress).GetAwaiter().GetResult();
        return trusteeState is null
            ? CeremonyTrusteeContextValidationResult.Failure(
                "election_ceremony_trustee_not_bound",
                "Only trustees bound to the active ceremony version can perform this action.")
            : CeremonyTrusteeContextValidationResult.Success(election, version, trusteeState);
    }

    private static ShareCustodyContextValidationResult LoadShareCustodyContext(
        IElectionsRepository repository,
        ElectionId electionId,
        Guid ceremonyVersionId,
        string actorPublicAddress,
        string trusteeUserAddress,
        string? shareVersion,
        bool requireOwnerActor)
    {
        var election = repository.GetElectionAsync(electionId).GetAwaiter().GetResult();
        if (election is null)
        {
            return ShareCustodyContextValidationResult.Failure(
                "election_ceremony_not_found",
                $"Election {electionId} was not found.");
        }

        if (!requireOwnerActor
            && !string.Equals(actorPublicAddress, trusteeUserAddress, StringComparison.Ordinal))
        {
            return ShareCustodyContextValidationResult.Failure(
                "election_ceremony_share_forbidden",
                "Trustees can only manage their own share-custody records.");
        }

        var version = repository.GetCeremonyVersionAsync(ceremonyVersionId).GetAwaiter().GetResult();
        if (version is null || version.ElectionId != electionId || !version.IsActive)
        {
            return ShareCustodyContextValidationResult.Failure(
                "election_ceremony_version_not_found",
                $"Ceremony version {ceremonyVersionId} was not found for election {electionId}.");
        }

        var trusteeState = repository.GetCeremonyTrusteeStateAsync(ceremonyVersionId, trusteeUserAddress).GetAwaiter().GetResult();
        if (trusteeState is null || trusteeState.State != ElectionTrusteeCeremonyState.CeremonyCompleted)
        {
            return ShareCustodyContextValidationResult.Failure(
                "election_ceremony_share_not_ready",
                "Share-custody actions require a ceremony-complete trustee state.");
        }

        var custodyRecord = repository.GetCeremonyShareCustodyRecordAsync(ceremonyVersionId, trusteeUserAddress).GetAwaiter().GetResult();
        if (custodyRecord is null)
        {
            return ShareCustodyContextValidationResult.Failure(
                "election_ceremony_share_not_found",
                $"Share-custody record for trustee {trusteeUserAddress} was not found.");
        }

        if (shareVersion is not null
            && !string.Equals(custodyRecord.ShareVersion, shareVersion, StringComparison.Ordinal))
        {
            return ShareCustodyContextValidationResult.Failure(
                "election_ceremony_share_version_mismatch",
                "Share version does not match the active ceremony binding.");
        }

        return ShareCustodyContextValidationResult.Success(election, version, custodyRecord);
    }

    private static int CountCompletedTrustees(IReadOnlyList<ElectionCeremonyTrusteeStateRecord> trusteeStates) =>
        trusteeStates.Count(x => x.State == ElectionTrusteeCeremonyState.CeremonyCompleted);

    private sealed record CeremonyTrusteeContextValidationResult(
        bool IsSuccess,
        ElectionRecord? Election,
        ElectionCeremonyVersionRecord? Version,
        ElectionCeremonyTrusteeStateRecord? TrusteeState,
        string? FailureCode,
        string? FailureMessage)
    {
        public static CeremonyTrusteeContextValidationResult Success(
            ElectionRecord election,
            ElectionCeremonyVersionRecord version,
            ElectionCeremonyTrusteeStateRecord? trusteeState) =>
            new(true, election, version, trusteeState, null, null);

        public static CeremonyTrusteeContextValidationResult Failure(
            string failureCode,
            string failureMessage) =>
            new(false, null, null, null, failureCode, failureMessage);
    }

    private sealed record ShareCustodyContextValidationResult(
        bool IsSuccess,
        ElectionRecord? Election,
        ElectionCeremonyVersionRecord? Version,
        ElectionCeremonyShareCustodyRecord? CustodyRecord,
        string? FailureCode,
        string? FailureMessage)
    {
        public static ShareCustodyContextValidationResult Success(
            ElectionRecord election,
            ElectionCeremonyVersionRecord version,
            ElectionCeremonyShareCustodyRecord custodyRecord) =>
            new(true, election, version, custodyRecord, null, null);

        public static ShareCustodyContextValidationResult Failure(
            string failureCode,
            string failureMessage) =>
            new(false, null, null, null, failureCode, failureMessage);
    }
}
