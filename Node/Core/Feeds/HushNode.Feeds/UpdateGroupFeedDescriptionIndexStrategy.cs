using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class UpdateGroupFeedDescriptionIndexStrategy(IUpdateGroupFeedDescriptionTransactionHandler updateGroupFeedDescriptionTransactionHandler) : IIndexStrategy
{
    private readonly IUpdateGroupFeedDescriptionTransactionHandler _updateGroupFeedDescriptionTransactionHandler = updateGroupFeedDescriptionTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        UpdateGroupFeedDescriptionPayloadHandler.UpdateGroupFeedDescriptionPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._updateGroupFeedDescriptionTransactionHandler.HandleUpdateGroupFeedDescriptionTransactionAsync(
            (ValidatedTransaction<UpdateGroupFeedDescriptionPayload>)transaction);
}
