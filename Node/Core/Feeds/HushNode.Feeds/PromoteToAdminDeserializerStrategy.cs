using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class PromoteToAdminDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) =>
        PromoteToAdminPayloadHandler.PromoteToAdminPayloadKind.ToString() == transactionKind;

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<SignedTransaction<PromoteToAdminPayload>>(transactionJSON)!;

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<ValidatedTransaction<PromoteToAdminPayload>>(transactionJSON)!;
}
