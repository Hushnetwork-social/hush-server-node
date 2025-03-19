using HushNode.Blockchain.BlockModel.States;
using HushNode.Blockchain.Storage.Model;

namespace HushNode.Blockchain.Storage;

public static class FinalizedBlockExtensionMethods
{
    public static BlockchainBlock ToBlockchainBlock(this FinalizedBlock finalizedBlock) =>
        new(
            finalizedBlock.BlockId,
            finalizedBlock.BlockIndex,
            finalizedBlock.PreviousBlockId,
            finalizedBlock.NextBlockId,
            finalizedBlock.Hash,
            finalizedBlock.ToJson());
}
