using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewGroupFeedMessageIndexStrategy(
    INewGroupFeedMessageTransactionHandler transactionHandler)
    : IIndexStrategy
{
    private readonly INewGroupFeedMessageTransactionHandler _transactionHandler = transactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        NewGroupFeedMessagePayloadHandler.NewGroupFeedMessagePayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._transactionHandler.HandleGroupFeedMessageTransaction(
            (ValidatedTransaction<NewGroupFeedMessagePayload>)transaction);
}
