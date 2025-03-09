using Olimpo.EntityFramework.Persistency;
using HushNode.Blockchain.Model;

namespace HushNode.Blockchain.Repositories;

public interface IBlockchainStateRepository : IRepository
{
    Task<BlockchainState> GetCurrentStateAsync();
    
    Task SetBlockchainStateAsync(BlockchainState blockchainState);
}
