using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Index strategy for GroupFeedKeyRotation transactions.
/// Invokes the transaction handler when a KeyRotation transaction is indexed from a block.
/// </summary>
public class GroupFeedKeyRotationIndexStrategy(
    IGroupFeedKeyRotationTransactionHandler keyRotationTransactionHandler) : IIndexStrategy
{
    private readonly IGroupFeedKeyRotationTransactionHandler _keyRotationTransactionHandler = keyRotationTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        GroupFeedKeyRotationPayloadHandler.GroupFeedKeyRotationPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._keyRotationTransactionHandler.HandleKeyRotationTransactionAsync(
            (ValidatedTransaction<GroupFeedKeyRotationPayload>)transaction);
}
