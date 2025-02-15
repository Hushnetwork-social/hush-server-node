using HushNetwork.Shared.Model.Block;
using HushServerNode.InternalModule.Blockchain.Cache;

namespace HushServerNode.InternalModule.Blockchain;

public interface IBlockchainStateManager
{
    BlockchainStateId BlockchainStateId { get; }
    BlockIndex LastBlockIndex { get; }
    BlockId CurrentBlockId { get; }
    BlockId PreviousBlockId { get; }
    BlockId NextBlockId { get; }

    Task LoadBlockchainStateAsync();
    void UpdateBlockchainState(
        BlockIndex blockIndex, 
        BlockId previousBlockId, 
        BlockId currentBlockId, 
        BlockId nextBlockId);

    Task SaveBlockchainStateAsync();
}