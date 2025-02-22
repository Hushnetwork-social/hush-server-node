namespace HushServerNode.InternalModule.Blockchain.Cache;

public interface IBlockchainStateContext
{
    Task<BlockchainState> GetBlockchainStateAsync();

    Task SaveBlockchainStateAsync(BlockchainState state);
}