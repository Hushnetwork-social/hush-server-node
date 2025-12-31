using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler interface for processing validated JoinGroupFeed transactions.
/// </summary>
public interface IJoinGroupFeedTransactionHandler
{
    /// <summary>
    /// Processes a validated JoinGroupFeed transaction.
    /// Creates or updates participant entity and triggers key rotation.
    /// </summary>
    Task HandleJoinGroupFeedTransactionAsync(ValidatedTransaction<JoinGroupFeedPayload> transaction);
}
