using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.Bank;

public class RewardTransactionDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind)
    {
        return RewardPayloadHandler.RewardPayloadKind.ToString() == transactionKind;
    }

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON)
    {
        return JsonSerializer.Deserialize<SignedTransaction<RewardPayload>>(transactionJSON);
    }

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON)
    {
        return JsonSerializer.Deserialize<ValidatedTransaction<RewardPayload>>(transactionJSON);
    }
}
