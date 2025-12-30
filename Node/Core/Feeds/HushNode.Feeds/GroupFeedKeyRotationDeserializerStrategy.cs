using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Deserializer strategy for GroupFeedKeyRotation transactions.
/// Converts JSON serialized transactions into typed SignedTransaction or ValidatedTransaction objects.
/// </summary>
public class GroupFeedKeyRotationDeserializerStrategy : ITransactionDeserializerStrategy
{
    public bool CanDeserialize(string transactionKind) =>
        GroupFeedKeyRotationPayloadHandler.GroupFeedKeyRotationPayloadKind.ToString() == transactionKind;

    public AbstractTransaction DeserializeSignedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<SignedTransaction<GroupFeedKeyRotationPayload>>(transactionJSON)!;

    public AbstractTransaction DeserializeValidatedTransaction(string transactionJSON) =>
        JsonSerializer.Deserialize<ValidatedTransaction<GroupFeedKeyRotationPayload>>(transactionJSON)!;
}
