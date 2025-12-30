using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface IDeleteGroupFeedTransactionHandler
{
    Task HandleDeleteGroupFeedTransactionAsync(ValidatedTransaction<DeleteGroupFeedPayload> deleteGroupFeedTransaction);
}
