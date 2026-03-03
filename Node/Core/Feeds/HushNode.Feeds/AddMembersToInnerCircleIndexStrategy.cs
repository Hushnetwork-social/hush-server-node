using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class AddMembersToInnerCircleIndexStrategy(IAddMembersToInnerCircleTransactionHandler addMembersToInnerCircleTransactionHandler) : IIndexStrategy
{
    private readonly IAddMembersToInnerCircleTransactionHandler _addMembersToInnerCircleTransactionHandler = addMembersToInnerCircleTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        AddMembersToInnerCirclePayloadHandler.AddMembersToInnerCirclePayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._addMembersToInnerCircleTransactionHandler.HandleAddMembersToInnerCircleTransactionAsync(
            (ValidatedTransaction<AddMembersToInnerCirclePayload>)transaction);
}
