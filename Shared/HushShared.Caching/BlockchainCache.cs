using HushShared.Blockchain.BlockModel;

namespace HushShared.Caching;

public class BlockchainCache : IBlockchainCache
{
    public BlockIndex LastBlockIndex { get; private set; } = BlockIndex.Empty;

    public BlockId PreviousBlockId { get; private set; } = BlockId.Empty;

    public BlockId CurrentBlockId { get; private set; } = BlockId.Empty;

    public BlockId NextBlockId { get; private set; } = BlockId.Empty;

    public bool BlockchainStateInDatabase { get; private set; } = false;

    public IBlockchainCache SetBlockIndex(BlockIndex index)
    {
        this.LastBlockIndex = index;
        return this;
    }

    public IBlockchainCache SetPreviousBlockId(BlockId id)
    {
        this.PreviousBlockId = id;
        return this;
    }

    public IBlockchainCache SetCurrentBlockId(BlockId id)
    {
        this.CurrentBlockId = id;
        return this;
    }

    public IBlockchainCache SetNextBlockId(BlockId id)
    {
        this.NextBlockId = id;
        return this;
    }

    public IBlockchainCache IsBlockchainStateInDatabase()
    {
        this.BlockchainStateInDatabase = true;
        return this;
    }
}