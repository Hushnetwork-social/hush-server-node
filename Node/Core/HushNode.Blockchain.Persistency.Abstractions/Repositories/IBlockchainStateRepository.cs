using HushNode.Blockchain.Persistency.Abstractions.Models;

namespace HushNode.Blockchain.Persistency.Abstractions.Repositories;

public interface IBlockchainStateRepository : IRepository
{
    Task<BlockchainState> GetCurrentStateAsync();
    
    Task SetBlockchainStateAsync(BlockchainState blockchainState);
}
