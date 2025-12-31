using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface IUnbanFromGroupFeedTransactionHandler
{
    Task HandleUnbanFromGroupFeedTransactionAsync(ValidatedTransaction<UnbanFromGroupFeedPayload> unbanTransaction);
}
