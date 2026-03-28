using HushNode.Credentials;
using HushNode.Elections.Storage;
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
    ElectionCeremonyOptions ceremonyOptions) : ITransactionContentHandler
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
    private readonly ElectionCeremonyOptions _ceremonyOptions = ceremonyOptions;

    public bool CanValidate(Guid transactionKind) =>
        EncryptedElectionEnvelopePayloadHandler.EncryptedElectionEnvelopePayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var decryptedEnvelope = _envelopeCryptoService.TryDecryptSigned(transaction);
        if (decryptedEnvelope is null)
        {
            return null;
        }

        var signatory = decryptedEnvelope.Transaction.UserSignature?.Signatory;
        if (string.IsNullOrWhiteSpace(signatory))
        {
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
            EncryptedElectionEnvelopeActionTypes.InviteTrustee =>
                IsValidInviteTrusteeAction(decryptedEnvelope, signatory),
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
            election.GovernanceMode != ElectionGovernanceMode.TrusteeThreshold ||
            election.LifecycleState != ElectionLifecycleState.Closed ||
            !election.TallyReadyAt.HasValue)
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
