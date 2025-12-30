using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class DeleteGroupFeedIndexStrategy(IDeleteGroupFeedTransactionHandler deleteGroupFeedTransactionHandler) : IIndexStrategy
{
    private readonly IDeleteGroupFeedTransactionHandler _deleteGroupFeedTransactionHandler = deleteGroupFeedTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        DeleteGroupFeedPayloadHandler.DeleteGroupFeedPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._deleteGroupFeedTransactionHandler.HandleDeleteGroupFeedTransactionAsync(
            (ValidatedTransaction<DeleteGroupFeedPayload>)transaction);
}
