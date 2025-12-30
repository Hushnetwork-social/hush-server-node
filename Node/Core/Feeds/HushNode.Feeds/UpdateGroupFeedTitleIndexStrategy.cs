using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class UpdateGroupFeedTitleIndexStrategy(IUpdateGroupFeedTitleTransactionHandler updateGroupFeedTitleTransactionHandler) : IIndexStrategy
{
    private readonly IUpdateGroupFeedTitleTransactionHandler _updateGroupFeedTitleTransactionHandler = updateGroupFeedTitleTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        UpdateGroupFeedTitlePayloadHandler.UpdateGroupFeedTitlePayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._updateGroupFeedTitleTransactionHandler.HandleUpdateGroupFeedTitleTransactionAsync(
            (ValidatedTransaction<UpdateGroupFeedTitlePayload>)transaction);
}
