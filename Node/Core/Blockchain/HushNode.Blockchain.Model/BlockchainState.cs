using HushNode.Blockchain.Model.Block;

namespace HushNode.Blockchain.Model;

public record BlockchainState
{
    public BlockchainStateId BlockchainStateId { get; init; }
    public BlockIndex BlockIndex { get; init; }
    public BlockId CurrentBlockId { get; init; }
    public BlockId PreviousBlockId { get; init; }
    public BlockId NextBlockId { get; init; }

    public BlockchainState(
        BlockchainStateId BlockchainStateId,
        BlockIndex BlockIndex,
        BlockId CurrentBlockId,
        BlockId PreviousBlockId,
        BlockId NextBlockId)
    {
        this.BlockchainStateId = BlockchainStateId;
        this.BlockIndex = BlockIndex;
        this.CurrentBlockId = CurrentBlockId;
        this.PreviousBlockId = PreviousBlockId;
        this.NextBlockId = NextBlockId;
    }

    public BlockchainState(BlockchainState original)
    {
        BlockchainStateId = original.BlockchainStateId;
        BlockIndex = original.BlockIndex;
        CurrentBlockId = original.CurrentBlockId;
        PreviousBlockId = original.PreviousBlockId;
        NextBlockId = original.NextBlockId;
    }

}

public record GenesisBlockchainState : BlockchainState
{
    public GenesisBlockchainState() : base(
        BlockchainStateId.NewBlockchainStateId,
        BlockIndexHandler.CreateNew(1),
        BlockId.GenesisBlockId,
        BlockId.Empty,
        BlockId.NewBlockId) {}
}