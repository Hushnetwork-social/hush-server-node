using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler for persisting GroupFeedKeyRotation transactions to the database.
/// Creates KeyGeneration entity with all encrypted member keys.
/// </summary>
public interface IGroupFeedKeyRotationTransactionHandler
{
    Task HandleKeyRotationTransactionAsync(ValidatedTransaction<GroupFeedKeyRotationPayload> keyRotationTransaction);
}
