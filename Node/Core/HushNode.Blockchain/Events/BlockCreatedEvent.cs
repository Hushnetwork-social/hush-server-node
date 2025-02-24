using HushNode.Blockchain.Persistency.Abstractions.Models.Block;

namespace HushNode.Blockchain.Events;

public class BlockCreatedEvent(BlockId blockId)
{
    public BlockId BlockId { get; private set; } = blockId;
}
