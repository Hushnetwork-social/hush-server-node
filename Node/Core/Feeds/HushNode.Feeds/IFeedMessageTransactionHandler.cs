using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface IFeedMessageTransactionHandler
{
    Task HandleFeedMessageTransaction(ValidatedTransaction<NewFeedMessagePayload> validatedTransaction);
}