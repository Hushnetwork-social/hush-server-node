using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Deserializer strategy for AddMemberToGroupFeed transactions.
/// </summary>
public class AddMemberToGroupFeedDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) =>
        AddMemberToGroupFeedPayloadHandler.AddMemberToGroupFeedPayloadKind.ToString() == transactionKind;

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<SignedTransaction<AddMemberToGroupFeedPayload>>(transactionJSON)!;

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<ValidatedTransaction<AddMemberToGroupFeedPayload>>(transactionJSON)!;
}
