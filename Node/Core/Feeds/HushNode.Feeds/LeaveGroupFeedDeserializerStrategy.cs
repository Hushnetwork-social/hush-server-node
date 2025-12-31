using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Deserializer strategy for LeaveGroupFeed transactions.
/// </summary>
public class LeaveGroupFeedDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) =>
        LeaveGroupFeedPayloadHandler.LeaveGroupFeedPayloadKind.ToString() == transactionKind;

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<SignedTransaction<LeaveGroupFeedPayload>>(transactionJSON)!;

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<ValidatedTransaction<LeaveGroupFeedPayload>>(transactionJSON)!;
}
