using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface INewPersonalFeedTransactionHandler
{
    Task HandleNewPersonalFeedTransactionAsync(ValidatedTransaction<NewPersonalFeedPayload> newPersonalFeedTransaction);
}