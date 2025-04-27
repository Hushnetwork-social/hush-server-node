using System.Text.Json;
using HushShared.Bank.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.Bank;

public class SendFundsDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) => 
        SendFundsPayloadHandler.SendFundsPayloadKind.ToString() == transactionKind;

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) => 
        JsonSerializer.Deserialize<SignedTransaction<SendFundsPayload>>(transactionJSON);

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) => 
        JsonSerializer.Deserialize<ValidatedTransaction<SendFundsPayload>>(transactionJSON);
}
