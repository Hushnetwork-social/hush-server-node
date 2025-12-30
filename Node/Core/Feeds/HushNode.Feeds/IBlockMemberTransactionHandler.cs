using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public interface IBlockMemberTransactionHandler
{
    Task HandleBlockMemberTransactionAsync(ValidatedTransaction<BlockMemberPayload> blockMemberTransaction);
}
