using HushNode.Blockchain.Persistency.Abstractions.Models;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Blockchain.Persistency.Abstractions.Repositories;

public interface IBlockchainStateRepository : IRepository
{
    Task<BlockchainState> GetCurrentStateAsync();
    
    Task SetBlockchainStateAsync(BlockchainState blockchainState);
}
