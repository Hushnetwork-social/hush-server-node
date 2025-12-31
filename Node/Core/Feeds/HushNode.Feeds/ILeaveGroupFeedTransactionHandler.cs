using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Handler interface for processing validated LeaveGroupFeed transactions.
/// </summary>
public interface ILeaveGroupFeedTransactionHandler
{
    /// <summary>
    /// Processes a validated LeaveGroupFeed transaction.
    /// Updates participant's leave timestamps and may trigger group deletion if last admin.
    /// </summary>
    Task HandleLeaveGroupFeedTransactionAsync(ValidatedTransaction<LeaveGroupFeedPayload> transaction);
}
