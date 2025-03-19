using Olimpo.EntityFramework.Persistency;
using HushNode.Blockchain.Storage.Model;

namespace HushNode.Blockchain.Storage;

public interface IBlockchainStateRepository : IRepository
{
    Task<BlockchainState> GetCurrentStateAsync();
    
    Task SetBlockchainStateAsync(BlockchainState blockchainState);
}
