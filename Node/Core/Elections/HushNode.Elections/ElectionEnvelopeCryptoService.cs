using System.Text.Json;
using HushNode.Credentials;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Olimpo;

namespace HushNode.Elections;

public interface IElectionEnvelopeCryptoService
{
    DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>>? TryDecryptSigned(
        AbstractTransaction transaction);

    DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>>? TryDecryptValidated(
        AbstractTransaction transaction);
}

public sealed record DecryptedElectionEnvelope<TTransaction>(
    TTransaction Transaction,
    string ActionType,
    string ActionPayloadJson)
    where TTransaction : AbstractTransaction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TAction? DeserializeAction<TAction>() =>
        JsonSerializer.Deserialize<TAction>(ActionPayloadJson, JsonOptions);
}

public class ElectionEnvelopeCryptoService(
    ICredentialsProvider credentialsProvider) : IElectionEnvelopeCryptoService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICredentialsProvider _credentialsProvider = credentialsProvider;

    public DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>>? TryDecryptSigned(
        AbstractTransaction transaction)
    {
        if (transaction is not SignedTransaction<EncryptedElectionEnvelopePayload> signedTransaction)
        {
            return null;
        }

        return TryDecryptEnvelope(signedTransaction);
    }

    public DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>>? TryDecryptValidated(
        AbstractTransaction transaction)
    {
        if (transaction is not ValidatedTransaction<EncryptedElectionEnvelopePayload> validatedTransaction)
        {
            return null;
        }

        return TryDecryptEnvelope(validatedTransaction);
    }

    private DecryptedElectionEnvelope<TTransaction>? TryDecryptEnvelope<TTransaction>(TTransaction transaction)
        where TTransaction : SignedTransaction<EncryptedElectionEnvelopePayload>
    {
        var payload = transaction.Payload;
        if (!string.Equals(
                payload.EnvelopeVersion,
                EncryptedElectionEnvelopePayloadHandler.CurrentEnvelopeVersion,
                StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var nodePrivateEncryptKey = _credentialsProvider.GetCredentials().PrivateEncryptKey;
            var electionPrivateKey = EncryptKeys.Decrypt(
                payload.NodeEncryptedElectionPrivateKey,
                nodePrivateEncryptKey);
            var innerJson = EncryptKeys.Decrypt(payload.EncryptedPayload, electionPrivateKey);
            var actionEnvelope = JsonSerializer.Deserialize<EncryptedElectionActionEnvelope>(innerJson, JsonOptions);

            if (actionEnvelope is null)
            {
                return null;
            }

            return new DecryptedElectionEnvelope<TTransaction>(
                transaction,
                actionEnvelope.ActionType,
                actionEnvelope.ActionPayload.GetRawText());
        }
        catch
        {
            return null;
        }
    }
}
