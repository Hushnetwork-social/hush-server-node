using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class UnbanFromGroupFeedIndexStrategy(IUnbanFromGroupFeedTransactionHandler unbanFromGroupFeedTransactionHandler) : IIndexStrategy
{
    private readonly IUnbanFromGroupFeedTransactionHandler _unbanFromGroupFeedTransactionHandler = unbanFromGroupFeedTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        UnbanFromGroupFeedPayloadHandler.UnbanFromGroupFeedPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._unbanFromGroupFeedTransactionHandler.HandleUnbanFromGroupFeedTransactionAsync(
            (ValidatedTransaction<UnbanFromGroupFeedPayload>)transaction);
}
