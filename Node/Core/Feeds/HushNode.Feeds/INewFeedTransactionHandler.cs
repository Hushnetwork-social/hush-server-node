using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface INewFeedTransactionHandler
{
    Task HandleNewPersonalFeedTransactionAsync(ValidatedTransaction<NewPersonalFeedPayload> newPersonalFeedTransaction);
}