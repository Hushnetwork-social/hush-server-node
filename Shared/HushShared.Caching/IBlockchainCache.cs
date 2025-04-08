using HushShared.Blockchain.BlockModel;

namespace HushShared.Caching;

public interface IBlockchainCache
{
    BlockId PreviousBlockId { get; }

    BlockId CurrentBlockId { get; }

    BlockId NextBlockId { get; }

    BlockIndex LastBlockIndex { get; }

    bool BlockchainStateInDatabase { get; }

    IBlockchainCache SetBlockIndex(BlockIndex index);

    IBlockchainCache SetPreviousBlockId(BlockId id);

    IBlockchainCache SetCurrentBlockId(BlockId id);

    IBlockchainCache SetNextBlockId(BlockId id);

    IBlockchainCache IsBlockchainStateInDatabase();
}
