using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public class BlockMemberIndexStrategy(IBlockMemberTransactionHandler blockMemberTransactionHandler) : IIndexStrategy
{
    private readonly IBlockMemberTransactionHandler _blockMemberTransactionHandler = blockMemberTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        BlockMemberPayloadHandler.BlockMemberPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await this._blockMemberTransactionHandler.HandleBlockMemberTransactionAsync(
            (ValidatedTransaction<BlockMemberPayload>)transaction);
}
