using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Index strategy for routing JoinGroupFeed transactions to the handler.
/// </summary>
public class JoinGroupFeedIndexStrategy(IJoinGroupFeedTransactionHandler joinGroupFeedTransactionHandler) : IIndexStrategy
{
    private readonly IJoinGroupFeedTransactionHandler _joinGroupFeedTransactionHandler = joinGroupFeedTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        JoinGroupFeedPayloadHandler.JoinGroupFeedPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._joinGroupFeedTransactionHandler.HandleJoinGroupFeedTransactionAsync(
            (ValidatedTransaction<JoinGroupFeedPayload>)transaction);
}
