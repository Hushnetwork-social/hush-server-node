using HushNode.Credentials;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public class EncryptedElectionEnvelopeContentHandler(
    IElectionEnvelopeCryptoService envelopeCryptoService,
    ICreateElectionDraftValidationService createElectionDraftValidationService,
    UpdateElectionDraftContentHandler updateElectionDraftContentHandler,
    InviteElectionTrusteeContentHandler inviteElectionTrusteeContentHandler,
    ICredentialsProvider credentialsProvider) : ITransactionContentHandler
{
    private readonly IElectionEnvelopeCryptoService _envelopeCryptoService = envelopeCryptoService;
    private readonly ICreateElectionDraftValidationService _createElectionDraftValidationService =
        createElectionDraftValidationService;
    private readonly UpdateElectionDraftContentHandler _updateElectionDraftContentHandler =
        updateElectionDraftContentHandler;
    private readonly InviteElectionTrusteeContentHandler _inviteElectionTrusteeContentHandler =
        inviteElectionTrusteeContentHandler;
    private readonly ICredentialsProvider _credentialsProvider = credentialsProvider;

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
        switch (decryptedEnvelope.ActionType)
        {
            case EncryptedElectionEnvelopeActionTypes.CreateDraft:
                return IsValidCreateDraftAction(decryptedEnvelope, signatory);
            case EncryptedElectionEnvelopeActionTypes.UpdateDraft:
                return IsValidUpdateDraftAction(decryptedEnvelope, signatory);
            case EncryptedElectionEnvelopeActionTypes.InviteTrustee:
                return IsValidInviteTrusteeAction(decryptedEnvelope, signatory);
            default:
                return false;
        }
    }

    private bool IsValidCreateDraftAction(
        DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope,
        string signatory)
    {
        if (!string.Equals(
                decryptedEnvelope.ActionType,
                EncryptedElectionEnvelopeActionTypes.CreateDraft,
                StringComparison.Ordinal))
        {
            return false;
        }

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
}
