using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface INewChatFeedTransactionHandler
{
    Task HandleNewChatFeedTransactionAsync(ValidatedTransaction<NewChatFeedPayload> newChatFeedTransaction);
}