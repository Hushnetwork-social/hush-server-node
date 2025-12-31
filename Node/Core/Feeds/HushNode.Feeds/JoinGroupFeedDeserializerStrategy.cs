using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Deserializer strategy for JoinGroupFeed transactions.
/// </summary>
public class JoinGroupFeedDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) =>
        JoinGroupFeedPayloadHandler.JoinGroupFeedPayloadKind.ToString() == transactionKind;

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<SignedTransaction<JoinGroupFeedPayload>>(transactionJSON)!;

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<ValidatedTransaction<JoinGroupFeedPayload>>(transactionJSON)!;
}
