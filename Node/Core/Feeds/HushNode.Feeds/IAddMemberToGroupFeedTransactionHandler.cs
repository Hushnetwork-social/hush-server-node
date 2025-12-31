using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler interface for processing validated AddMemberToGroupFeed transactions.
/// </summary>
public interface IAddMemberToGroupFeedTransactionHandler
{
    /// <summary>
    /// Processes a validated AddMemberToGroupFeed transaction.
    /// Creates participant entity for the new member.
    /// </summary>
    Task HandleAddMemberToGroupFeedTransactionAsync(ValidatedTransaction<AddMemberToGroupFeedPayload> transaction);
}
