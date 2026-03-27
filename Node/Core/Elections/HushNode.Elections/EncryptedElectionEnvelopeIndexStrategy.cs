using System.Text;
using System.Text.Json;
using HushNode.Elections.Storage;
using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class EncryptedElectionEnvelopeIndexStrategy(
    IElectionEnvelopeCryptoService envelopeCryptoService,
    ICreateElectionDraftTransactionHandler createElectionDraftTransactionHandler,
    IUpdateElectionDraftTransactionHandler updateElectionDraftTransactionHandler,
    IInviteElectionTrusteeTransactionHandler inviteElectionTrusteeTransactionHandler,
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider) : IIndexStrategy
{
    private readonly IElectionEnvelopeCryptoService _envelopeCryptoService = envelopeCryptoService;
    private readonly ICreateElectionDraftTransactionHandler _createElectionDraftTransactionHandler =
        createElectionDraftTransactionHandler;
    private readonly IUpdateElectionDraftTransactionHandler _updateElectionDraftTransactionHandler =
        updateElectionDraftTransactionHandler;
    private readonly IInviteElectionTrusteeTransactionHandler _inviteElectionTrusteeTransactionHandler =
        inviteElectionTrusteeTransactionHandler;
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public bool CanHandle(AbstractTransaction transaction) =>
        EncryptedElectionEnvelopePayloadHandler.EncryptedElectionEnvelopePayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction)
    {
        var decryptedEnvelope = _envelopeCryptoService.TryDecryptValidated(transaction);
        if (decryptedEnvelope is null)
        {
            return;
        }

        switch (decryptedEnvelope.ActionType)
        {
            case EncryptedElectionEnvelopeActionTypes.CreateDraft:
                await HandleCreateDraftAsync(decryptedEnvelope);
                return;
            case EncryptedElectionEnvelopeActionTypes.UpdateDraft:
                await HandleUpdateDraftAsync(decryptedEnvelope);
                return;
            case EncryptedElectionEnvelopeActionTypes.InviteTrustee:
                await HandleInviteTrusteeAsync(decryptedEnvelope);
                return;
            default:
                return;
        }
    }

    private async Task HandleCreateDraftAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var createDraftAction = decryptedEnvelope.DeserializeAction<CreateElectionDraftActionPayload>();
        if (createDraftAction is null)
        {
            return;
        }

        var payload = new CreateElectionDraftPayload(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            createDraftAction.OwnerPublicAddress,
            createDraftAction.SnapshotReason,
            createDraftAction.Draft);
        var payloadSize = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(payload));
        var syntheticTransaction = new ValidatedTransaction<CreateElectionDraftPayload>(
            decryptedEnvelope.Transaction.TransactionId,
            CreateElectionDraftPayloadHandler.CreateElectionDraftPayloadKind,
            decryptedEnvelope.Transaction.TransactionTimeStamp,
            payload,
            payloadSize,
            decryptedEnvelope.Transaction.UserSignature,
            decryptedEnvelope.Transaction.ValidatorSignature);

        await _createElectionDraftTransactionHandler.HandleCreateElectionDraftTransaction(syntheticTransaction);
        await SaveElectionEnvelopeAccessAsync(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            createDraftAction.OwnerPublicAddress,
            decryptedEnvelope.Transaction.Payload.ActorEncryptedElectionPrivateKey,
            decryptedEnvelope.Transaction.TransactionTimeStamp.Value,
            decryptedEnvelope.Transaction.TransactionId.Value);
    }

    private async Task HandleUpdateDraftAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var updateDraftAction = decryptedEnvelope.DeserializeAction<UpdateElectionDraftActionPayload>();
        if (updateDraftAction is null)
        {
            return;
        }

        var payload = new UpdateElectionDraftPayload(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            updateDraftAction.ActorPublicAddress,
            updateDraftAction.SnapshotReason,
            updateDraftAction.Draft);
        var payloadSize = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(payload));
        var syntheticTransaction = new ValidatedTransaction<UpdateElectionDraftPayload>(
            decryptedEnvelope.Transaction.TransactionId,
            UpdateElectionDraftPayloadHandler.UpdateElectionDraftPayloadKind,
            decryptedEnvelope.Transaction.TransactionTimeStamp,
            payload,
            payloadSize,
            decryptedEnvelope.Transaction.UserSignature,
            decryptedEnvelope.Transaction.ValidatorSignature);

        await _updateElectionDraftTransactionHandler.HandleUpdateElectionDraftTransaction(syntheticTransaction);
    }

    private async Task HandleInviteTrusteeAsync(
        DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>> decryptedEnvelope)
    {
        var inviteTrusteeAction = decryptedEnvelope.DeserializeAction<InviteElectionTrusteeActionPayload>();
        if (inviteTrusteeAction is null)
        {
            return;
        }

        var payload = new InviteElectionTrusteePayload(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            inviteTrusteeAction.InvitationId,
            inviteTrusteeAction.ActorPublicAddress,
            inviteTrusteeAction.TrusteeUserAddress,
            inviteTrusteeAction.TrusteeDisplayName);
        var payloadSize = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(payload));
        var syntheticTransaction = new ValidatedTransaction<InviteElectionTrusteePayload>(
            decryptedEnvelope.Transaction.TransactionId,
            InviteElectionTrusteePayloadHandler.InviteElectionTrusteePayloadKind,
            decryptedEnvelope.Transaction.TransactionTimeStamp,
            payload,
            payloadSize,
            decryptedEnvelope.Transaction.UserSignature,
            decryptedEnvelope.Transaction.ValidatorSignature);

        await _inviteElectionTrusteeTransactionHandler.HandleInviteElectionTrusteeTransaction(syntheticTransaction);
        await SaveElectionEnvelopeAccessAsync(
            decryptedEnvelope.Transaction.Payload.ElectionId,
            inviteTrusteeAction.TrusteeUserAddress,
            inviteTrusteeAction.TrusteeEncryptedElectionPrivateKey,
            decryptedEnvelope.Transaction.TransactionTimeStamp!.Value,
            decryptedEnvelope.Transaction.TransactionId!.Value);
    }

    private async Task SaveElectionEnvelopeAccessAsync(
        ElectionId electionId,
        string actorPublicAddress,
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
}
