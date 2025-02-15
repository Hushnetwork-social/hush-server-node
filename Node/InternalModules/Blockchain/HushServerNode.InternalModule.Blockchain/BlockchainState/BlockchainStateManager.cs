using HushNetwork.Shared.Model.Block;
using HushServerNode.InternalModule.Blockchain.Cache;

namespace HushServerNode.InternalModule.Blockchain;

public class BlockchainStateManager : IBlockchainStateManager
{
    private readonly IBlockchainStateContext _blockchainStateContext;

    public BlockchainStateId BlockchainStateId { get; private set; } = BlockchainStateId.Empty;

    public BlockIndex LastBlockIndex { get; private set; } = BlockIndex.Empty;
    
    public BlockId CurrentBlockId { get; private set; } = BlockId.Empty;

    public BlockId PreviousBlockId { get; private set; } = BlockId.Empty;

    public BlockId NextBlockId { get; private set; } = BlockId.Empty;

    public BlockchainStateManager(IBlockchainStateContext blockchainStateContext)
    {
        this._blockchainStateContext = blockchainStateContext;
    }

    public async Task LoadBlockchainStateAsync()
    {
        var blockchainState = await this._blockchainStateContext.GetBlockchainStateAsync();

        var state = blockchainState switch
        {
            null => new 
            { 
                BlockchainStateId = BlockchainStateId.NewBlockchainStateId,
                LastBlockIndex = new BlockIndex(1),
                CurrentBlockId = BlockId.GenesisBlockId,
                PreviousBlockId = BlockId.Empty,
                NextBlockId = BlockId.NewBlockId
            },
            _ => new 
            { 
                BlockchainStateId = blockchainState.BlockchainStateId,
                LastBlockIndex = blockchainState.LastBlockIndex,
                CurrentBlockId = blockchainState.CurrentBlockId,
                PreviousBlockId = blockchainState.PreviousBlockId,
                NextBlockId = blockchainState.NextBlockId
            }
        };

        this.BlockchainStateId = state.BlockchainStateId;
        this.LastBlockIndex = state.LastBlockIndex;
        this.CurrentBlockId = state.CurrentBlockId;
        this.PreviousBlockId = state.PreviousBlockId;
        this.NextBlockId = state.NextBlockId;
    }

    public void UpdateBlockchainState(
        BlockIndex blockIndex, 
        BlockId previousBlockId, 
        BlockId currentBlockId, 
        BlockId nextBlockId)
    {
        if (this.BlockchainStateId == BlockchainStateId.Empty)
        {
            this.BlockchainStateId = BlockchainStateId.NewBlockchainStateId;
        }
        
        this.LastBlockIndex = blockIndex;
        this.CurrentBlockId = currentBlockId;
        this.PreviousBlockId = previousBlockId;
        this.NextBlockId = nextBlockId;

    }

    public async Task SaveBlockchainStateAsync()
    {
        var blockchainState = BlockchainStateHandler.CreateNew(
            this.BlockchainStateId,
            this.LastBlockIndex, 
            this.PreviousBlockId, 
            this.CurrentBlockId, 
            this.NextBlockId);

        await this._blockchainStateContext.SaveBlockchainStateAsync(blockchainState);
    }

}
