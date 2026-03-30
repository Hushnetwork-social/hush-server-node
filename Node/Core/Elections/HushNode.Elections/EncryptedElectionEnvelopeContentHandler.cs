using System.Collections.Concurrent;
using HushNode.Credentials;
using HushNode.Elections.Storage;
using HushNode.MemPool;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
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
        var claimAction = decryptedEnvelope.DeserializeAction<ClaimElectionRosterEntryActionPayload>();
        if (claimAction is null
            || !HasMatchingActor(signatory, claimAction.ActorPublicAddress)
            || !string.Equals(
                claimAction.VerificationCode?.Trim(),
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
                election.LifecycleState != ElectionLifecycleState.Open))
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
        var matchesBoundary =
            openArtifact is not null &&
            election.OpenArtifactId == openArtifact.Id &&
            ByteArrayEquals(openArtifact.FrozenEligibleVoterSetHash, acceptAction.EligibleSetHash) &&
            openArtifact.CeremonySnapshot is not null &&
            openArtifact.CeremonySnapshot.CeremonyVersionId == acceptAction.CeremonyVersionId &&
            string.Equals(openArtifact.CeremonySnapshot.ProfileId, acceptAction.DkgProfileId, StringComparison.Ordinal) &&
            string.Equals(
                openArtifact.CeremonySnapshot.TallyPublicKeyFingerprint,
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

        return true;
    }

    private bool IsValidInviteTrusteeAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var inviteTrusteeAction = decryptedEnvelope.DeserializeAction<InviteElectionTrusteeActionPayload>();
        if (inviteTrusteeAction is null)
        {
            return false;
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
        var publishAction = decryptedEnvelope.DeserializeAction<PublishElectionCeremonyTransportKeyActionPayload>();
        if (publishAction is null
            || !HasMatchingActor(signatory, publishAction.ActorPublicAddress))
        {
            return false;
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
            return false;
        }

        try
        {
            context.TrusteeState.PublishTransportKey(
                publishAction.TransportPublicKeyFingerprint,
                DateTime.UtcNow);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsValidJoinCeremonyAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var joinAction = decryptedEnvelope.DeserializeAction<JoinElectionCeremonyActionPayload>();
        if (joinAction is null
            || !HasMatchingActor(signatory, joinAction.ActorPublicAddress))
        {
            return false;
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
            return false;
        }

        try
        {
            context.TrusteeState.MarkJoined(DateTime.UtcNow);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsValidRecordCeremonySelfTestAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var selfTestAction = decryptedEnvelope.DeserializeAction<RecordElectionCeremonySelfTestActionPayload>();
        if (selfTestAction is null
            || !HasMatchingActor(signatory, selfTestAction.ActorPublicAddress))
        {
            return false;
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
            return false;
        }

        try
        {
            context.TrusteeState.RecordSelfTestSuccess(DateTime.UtcNow);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsValidSubmitCeremonyMaterialAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var submitAction = decryptedEnvelope.DeserializeAction<SubmitElectionCeremonyMaterialActionPayload>();
        if (submitAction is null
            || !HasMatchingActor(signatory, submitAction.ActorPublicAddress)
            || string.IsNullOrWhiteSpace(submitAction.EncryptedPayload))
        {
            return false;
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
            return false;
        }

        if (submitAction.RecipientTrusteeUserAddress is not null
            && !context.Version.BoundTrustees.Any(x =>
                string.Equals(x.TrusteeUserAddress, submitAction.RecipientTrusteeUserAddress, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        try
        {
            context.TrusteeState.RecordMaterialSubmitted(DateTime.UtcNow);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsValidRecordCeremonyValidationFailureAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var validationFailureAction =
            decryptedEnvelope.DeserializeAction<RecordElectionCeremonyValidationFailureActionPayload>();
        if (validationFailureAction is null
            || !HasMatchingActor(signatory, validationFailureAction.ActorPublicAddress))
        {
            return false;
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
            return false;
        }

        var trusteeState = repository
            .GetCeremonyTrusteeStateAsync(validationFailureAction.CeremonyVersionId, validationFailureAction.TrusteeUserAddress)
            .GetAwaiter()
            .GetResult();
        if (trusteeState is null)
        {
            return false;
        }

        try
        {
            trusteeState.RecordValidationFailure(
                validationFailureAction.ValidationFailureReason,
                DateTime.UtcNow);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsValidCompleteCeremonyTrusteeAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var completeAction = decryptedEnvelope.DeserializeAction<CompleteElectionCeremonyTrusteeActionPayload>();
        if (completeAction is null
            || !HasMatchingActor(signatory, completeAction.ActorPublicAddress))
        {
            return false;
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
            return false;
        }

        var trusteeState = repository
            .GetCeremonyTrusteeStateAsync(completeAction.CeremonyVersionId, completeAction.TrusteeUserAddress)
            .GetAwaiter()
            .GetResult();
        if (trusteeState is null)
        {
            return false;
        }

        try
        {
            trusteeState.MarkCompleted(DateTime.UtcNow, completeAction.ShareVersion);
        }
        catch
        {
            return false;
        }

        var trusteeStates = repository
            .GetCeremonyTrusteeStatesAsync(completeAction.CeremonyVersionId)
            .GetAwaiter()
            .GetResult();
        var completedTrustees = CountCompletedTrustees(trusteeStates, completeAction.TrusteeUserAddress);
        if (ownerContext.Version.Status == ElectionCeremonyVersionStatus.InProgress
            && completedTrustees >= ownerContext.Version.RequiredApprovalCount
            && string.IsNullOrWhiteSpace(completeAction.TallyPublicKeyFingerprint))
        {
            return false;
        }

        return true;
    }

    private bool IsValidRecordCeremonyShareExportAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var exportAction = decryptedEnvelope.DeserializeAction<RecordElectionCeremonyShareExportActionPayload>();
        if (exportAction is null
            || !HasMatchingActor(signatory, exportAction.ActorPublicAddress))
        {
            return false;
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
            return false;
        }

        try
        {
            custodyContext.CustodyRecord.RecordExport(DateTime.UtcNow);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsValidRecordCeremonyShareImportAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        var importAction = decryptedEnvelope.DeserializeAction<RecordElectionCeremonyShareImportActionPayload>();
        if (importAction is null
            || !HasMatchingActor(signatory, importAction.ActorPublicAddress))
        {
            return false;
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
        return custodyContext.IsSuccess
            && custodyContext.CustodyRecord is not null
            && custodyContext.CustodyRecord.MatchesImportBinding(
                importAction.ImportedElectionId,
                importAction.ImportedCeremonyVersionId,
                importAction.ImportedTrusteeUserAddress,
                importAction.ImportedShareVersion);
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
                System.Text.Encoding.UTF8.GetBytes(value.Trim())));

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
        if (election is null || election.LifecycleState != ElectionLifecycleState.Draft)
        {
            return CeremonyTrusteeContextValidationResult.Failure();
        }

        var version = repository.GetCeremonyVersionAsync(ceremonyVersionId).GetAwaiter().GetResult();
        if (version is null || version.ElectionId != electionId || !version.IsActive)
        {
            return CeremonyTrusteeContextValidationResult.Failure();
        }

        var activeVersion = repository.GetActiveCeremonyVersionAsync(electionId).GetAwaiter().GetResult();
        if (activeVersion is null || activeVersion.Id != ceremonyVersionId)
        {
            return CeremonyTrusteeContextValidationResult.Failure();
        }

        if (requireOwnerActor)
        {
            return string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal)
                ? CeremonyTrusteeContextValidationResult.Success(election, version, null)
                : CeremonyTrusteeContextValidationResult.Failure();
        }

        var trusteeState = repository.GetCeremonyTrusteeStateAsync(ceremonyVersionId, actorPublicAddress).GetAwaiter().GetResult();
        return trusteeState is null
            ? CeremonyTrusteeContextValidationResult.Failure()
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
            return ShareCustodyContextValidationResult.Failure();
        }

        if (!requireOwnerActor
            && !string.Equals(actorPublicAddress, trusteeUserAddress, StringComparison.Ordinal))
        {
            return ShareCustodyContextValidationResult.Failure();
        }

        var version = repository.GetCeremonyVersionAsync(ceremonyVersionId).GetAwaiter().GetResult();
        if (version is null || version.ElectionId != electionId || !version.IsActive)
        {
            return ShareCustodyContextValidationResult.Failure();
        }

        var trusteeState = repository.GetCeremonyTrusteeStateAsync(ceremonyVersionId, trusteeUserAddress).GetAwaiter().GetResult();
        if (trusteeState is null || trusteeState.State != ElectionTrusteeCeremonyState.CeremonyCompleted)
        {
            return ShareCustodyContextValidationResult.Failure();
        }

        var custodyRecord = repository.GetCeremonyShareCustodyRecordAsync(ceremonyVersionId, trusteeUserAddress).GetAwaiter().GetResult();
        if (custodyRecord is null)
        {
            return ShareCustodyContextValidationResult.Failure();
        }

        if (shareVersion is not null
            && !string.Equals(custodyRecord.ShareVersion, shareVersion, StringComparison.Ordinal))
        {
            return ShareCustodyContextValidationResult.Failure();
        }

        return ShareCustodyContextValidationResult.Success(election, version, custodyRecord);
    }

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

    private sealed record CeremonyTrusteeContextValidationResult(
        bool IsSuccess,
        ElectionRecord? Election,
        ElectionCeremonyVersionRecord? Version,
        ElectionCeremonyTrusteeStateRecord? TrusteeState)
    {
        public static CeremonyTrusteeContextValidationResult Success(
            ElectionRecord election,
            ElectionCeremonyVersionRecord version,
            ElectionCeremonyTrusteeStateRecord? trusteeState) =>
            new(true, election, version, trusteeState);

        public static CeremonyTrusteeContextValidationResult Failure() =>
            new(false, null, null, null);
    }

    private sealed record ShareCustodyContextValidationResult(
        bool IsSuccess,
        ElectionRecord? Election,
        ElectionCeremonyVersionRecord? Version,
        ElectionCeremonyShareCustodyRecord? CustodyRecord)
    {
        public static ShareCustodyContextValidationResult Success(
            ElectionRecord election,
            ElectionCeremonyVersionRecord version,
            ElectionCeremonyShareCustodyRecord custodyRecord) =>
            new(true, election, version, custodyRecord);

        public static ShareCustodyContextValidationResult Failure() =>
            new(false, null, null, null);
    }
}
