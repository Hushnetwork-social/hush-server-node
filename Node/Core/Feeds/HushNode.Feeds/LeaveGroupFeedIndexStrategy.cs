using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Index strategy for routing LeaveGroupFeed transactions to the handler.
/// </summary>
public class LeaveGroupFeedIndexStrategy(ILeaveGroupFeedTransactionHandler leaveGroupFeedTransactionHandler) : IIndexStrategy
{
    private readonly ILeaveGroupFeedTransactionHandler _leaveGroupFeedTransactionHandler = leaveGroupFeedTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        LeaveGroupFeedPayloadHandler.LeaveGroupFeedPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._leaveGroupFeedTransactionHandler.HandleLeaveGroupFeedTransactionAsync(
            (ValidatedTransaction<LeaveGroupFeedPayload>)transaction);
}
