using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface IBanFromGroupFeedTransactionHandler
{
    Task HandleBanFromGroupFeedTransactionAsync(ValidatedTransaction<BanFromGroupFeedPayload> banTransaction);
}
