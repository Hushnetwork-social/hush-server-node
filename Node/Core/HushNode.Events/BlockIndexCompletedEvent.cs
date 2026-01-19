using HushShared.Blockchain.BlockModel;

namespace HushNode.Events;

/// <summary>
/// Event raised when all indexing strategies have completed processing
/// all transactions in a block. This guarantees that all derived data
/// (balances, feeds, identities, etc.) are fully persisted and queryable.
/// </summary>
public class BlockIndexCompletedEvent(BlockIndex blockIndex)
{
    public BlockIndex BlockIndex { get; } = blockIndex;
}
