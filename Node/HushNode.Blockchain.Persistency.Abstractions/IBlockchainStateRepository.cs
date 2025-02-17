using HushNode.Blockchain.Persistency.Abstractions.Model;

namespace HushNode.Blockchain.Persistency.Abstractions;

public interface IBlockchainStateRepository
{
    Task<BlockchainState> GetCurrentStateAsync();
    // Task SaveStateAsync(BlockchainState state);
}
