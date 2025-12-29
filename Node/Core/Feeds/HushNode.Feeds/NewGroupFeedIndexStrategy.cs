using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class NewGroupFeedIndexStrategy(INewGroupFeedTransactionHandler newGroupFeedTransactionHandler) : IIndexStrategy
{
    private readonly INewGroupFeedTransactionHandler _newGroupFeedTransactionHandler = newGroupFeedTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        NewGroupFeedPayloadHandler.NewGroupFeedPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._newGroupFeedTransactionHandler.HandleNewGroupFeedTransactionAsync(
            (ValidatedTransaction<NewGroupFeedPayload>)transaction);
}
