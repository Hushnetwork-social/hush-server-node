using HushNode.Blockchain.Model.Block.States;

namespace HushNode.Events;

public class BlockCreatedEvent(FinalizedBlock block)
{
    public FinalizedBlock Block { get; private set; } = block;
}
