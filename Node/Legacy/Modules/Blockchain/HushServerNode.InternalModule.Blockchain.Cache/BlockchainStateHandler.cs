using HushNetwork.Shared.Model.Block;

namespace HushServerNode.InternalModule.Blockchain.Cache;

public static class BlockchainStateHandler
{
    public static BlockchainState CreateNew(
        BlockchainStateId blockchainStateId,
        BlockIndex blockIndex,
        BlockId previousBlockId,
        BlockId currentBlockId,
        BlockId nextBlockId) => 
        new(
            blockchainStateId,
            blockIndex,
            previousBlockId,
            currentBlockId,
            nextBlockId);
}
