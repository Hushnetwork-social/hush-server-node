using HushNode.Blockchain.Persistency.Abstractions.Models.Block;

namespace HushNode.Events;

public class BlockCreatedEvent(BlockId blockId)
{
    public BlockId BlockId { get; private set; } = blockId;
}
