namespace HushNode.Blockchain.Persistency.Abstractions.Model;

public record BlockchainState(
    BlockchainStateId BlockchainStateId,
    BlockIndex BlockIndex,
    BlockId CurrentBlockId,
    BlockId PreviousBlockId,
    BlockId NextBlockId);

public record GenesisBlockchainState : BlockchainState
{
    public GenesisBlockchainState() : base(
        BlockchainStateId.NewBlockchainStateId,
        BlockIndexHandler.CreateNew(1),
        BlockId.NewBlockId,
        BlockId.Empty,
        BlockId.NewBlockId) {}
}