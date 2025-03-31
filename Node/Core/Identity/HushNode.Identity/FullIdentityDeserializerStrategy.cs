using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class FullIdentityDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind)
    {
        return FullIdentityPayloadHandler.FullIdentityPayloadKind.ToString() == transactionKind;
    }

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON)
    {
        return JsonSerializer.Deserialize<SignedTransaction<FullIdentityPayload>>(transactionJSON);
    }

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON)
    {
        return JsonSerializer.Deserialize<ValidatedTransaction<FullIdentityPayload>>(transactionJSON);
    }
}
