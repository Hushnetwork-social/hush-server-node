using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewChatFeedIndexStrategy(INewChatFeedTransactionHandler newChatFeedTransactionHandler) : IIndexStrategy
{
    private readonly INewChatFeedTransactionHandler _newChatFeedTransactionHandler = newChatFeedTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) => 
        NewChatFeedPayloadHandler.NewChatFeedPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) => 
        await this._newChatFeedTransactionHandler.HandleNewChatFeedTransactionAsync(
            (ValidatedTransaction<NewChatFeedPayload>) transaction);
}
