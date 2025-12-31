using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface INewGroupFeedMessageTransactionHandler
{
    Task HandleGroupFeedMessageTransaction(ValidatedTransaction<NewGroupFeedMessagePayload> validatedTransaction);
}
