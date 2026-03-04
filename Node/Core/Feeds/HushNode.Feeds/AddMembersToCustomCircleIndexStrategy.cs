using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class AddMembersToCustomCircleIndexStrategy(IAddMembersToCustomCircleTransactionHandler addMembersToCustomCircleTransactionHandler) : IIndexStrategy
{
    private readonly IAddMembersToCustomCircleTransactionHandler _addMembersToCustomCircleTransactionHandler = addMembersToCustomCircleTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        AddMembersToCustomCirclePayloadHandler.AddMembersToCustomCirclePayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._addMembersToCustomCircleTransactionHandler.HandleAddMembersToCustomCircleTransactionAsync(
            (ValidatedTransaction<AddMembersToCustomCirclePayload>)transaction);
}
