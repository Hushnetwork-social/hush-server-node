using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class FullIdentityDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) => 
        FullIdentityPayloadHandler.FullIdentityPayloadKind.ToString() == transactionKind;

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) =>
        Deserialize<SignedTransaction<FullIdentityPayload>>(transactionJSON);

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) =>
        Deserialize<ValidatedTransaction<FullIdentityPayload>>(transactionJSON);

    private static T Deserialize<T>(string transactionJSON) where T : AbstractTransaction
    {
        try
        {
            return JsonSerializer.Deserialize<T>(transactionJSON)!;
        }
        catch (FormatException)
        {
            // FEAT-011 Tasks 3.1/3.2: UUID/date converters can throw FormatException.
            // Translate only this identity parsing failure into the RPC's existing
            // typed rejection boundary, without retaining the untrusted scalar.
            throw new JsonException("Identity transaction scalar could not be parsed.");
        }
    }
}
