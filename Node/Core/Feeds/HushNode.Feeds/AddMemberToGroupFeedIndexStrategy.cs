using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Index strategy for routing AddMemberToGroupFeed transactions to the handler.
/// </summary>
public class AddMemberToGroupFeedIndexStrategy(IAddMemberToGroupFeedTransactionHandler addMemberToGroupFeedTransactionHandler) : IIndexStrategy
{
    private readonly IAddMemberToGroupFeedTransactionHandler _addMemberToGroupFeedTransactionHandler = addMemberToGroupFeedTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        AddMemberToGroupFeedPayloadHandler.AddMemberToGroupFeedPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._addMemberToGroupFeedTransactionHandler.HandleAddMemberToGroupFeedTransactionAsync(
            (ValidatedTransaction<AddMemberToGroupFeedPayload>)transaction);
}
