using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface INewGroupFeedTransactionHandler
{
    Task HandleNewGroupFeedTransactionAsync(ValidatedTransaction<NewGroupFeedPayload> newGroupFeedTransaction);
}
