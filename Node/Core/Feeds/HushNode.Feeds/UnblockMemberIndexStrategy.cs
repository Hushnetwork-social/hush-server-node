using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class UnblockMemberIndexStrategy(IUnblockMemberTransactionHandler unblockMemberTransactionHandler) : IIndexStrategy
{
    private readonly IUnblockMemberTransactionHandler _unblockMemberTransactionHandler = unblockMemberTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        UnblockMemberPayloadHandler.UnblockMemberPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._unblockMemberTransactionHandler.HandleUnblockMemberTransactionAsync(
            (ValidatedTransaction<UnblockMemberPayload>)transaction);
}
