using HushEcosystem.Model.Blockchain;
using HushServerNode.InternalModule.Blockchain.Cache;

namespace HushServerNode.InternalModule.Blockchain;

public static class BlockchainStatusExtensions
{
    public static BlockchainState ToBlockchainState(this IBlockchainStatus status)
    {
        var blockchainStatus = (BlockchainStatus)status;

        var blockchainState = blockchainStatus.BlockchainState;
        blockchainState.LastBlockIndex = status.BlockIndex;
        blockchainState.CurrentPreviousBlockId = status.PreviousBlockId;
        blockchainState.CurrentBlockId = status.BlockId;
        blockchainState.CurrentNextBlockId = status.NextBlockId;

        return blockchainState;
    }
}
