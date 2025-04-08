using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewPersonalFeedIndexStrategy(INewFeedTransactionHandler newFeedTransactionHandler) : IIndexStrategy
{
    private readonly INewFeedTransactionHandler _newFeedTransactionHandler = newFeedTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) => 
        NewPersonalFeedPayloadHandler.NewPersonalFeedPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction)
    {
        await this._newFeedTransactionHandler
            .HandleNewPersonalFeedTransactionAsync((ValidatedTransaction<NewPersonalFeedPayload>)transaction);
    }
}
