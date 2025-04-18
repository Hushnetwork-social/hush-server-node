using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewFeedMessageIndexStrategy(
    IFeedMessageTransactionHandler feedMessageTransactionHandler) 
    : IIndexStrategy
{
    private readonly IFeedMessageTransactionHandler _feedMessageTransactionHandler = feedMessageTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) => 
        await this._feedMessageTransactionHandler.HandleFeedMessageTransaction((ValidatedTransaction<NewFeedMessagePayload>)transaction);
}
