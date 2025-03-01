using HushNode.Blockchain.Persistency.Abstractions.Models.Block.States;

namespace HushNode.Events;

public class BlockCreatedEvent(FinalizedBlock block)
{
    public FinalizedBlock Block { get; private set; } = block;
}
