using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class BanFromGroupFeedIndexStrategy(IBanFromGroupFeedTransactionHandler banFromGroupFeedTransactionHandler) : IIndexStrategy
{
    private readonly IBanFromGroupFeedTransactionHandler _banFromGroupFeedTransactionHandler = banFromGroupFeedTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        BanFromGroupFeedPayloadHandler.BanFromGroupFeedPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._banFromGroupFeedTransactionHandler.HandleBanFromGroupFeedTransactionAsync(
            (ValidatedTransaction<BanFromGroupFeedPayload>)transaction);
}
