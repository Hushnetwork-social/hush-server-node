using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.Bank;

public class RewardTransactionDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) => 
        RewardPayloadHandler.RewardPayloadKind.ToString() == transactionKind;
    

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) => 
        JsonSerializer.Deserialize<SignedTransaction<RewardPayload>>(transactionJSON);

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) => 
        JsonSerializer.Deserialize<ValidatedTransaction<RewardPayload>>(transactionJSON);
}
