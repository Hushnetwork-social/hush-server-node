using Olimpo.EntityFramework.Persistency;
using HushNode.Blockchain.Storage.Model;
using HushShared.Caching;

namespace HushNode.Blockchain.Storage;

public interface IBlockchainStateRepository : IRepository
{
    Task<BlockchainState> GetCurrentStateAsync();

    Task InsertBlockchainStateAsync(IBlockchainCache blockchainCache);

    Task UpdateBlockchainStateAsync(IBlockchainCache blockchainCache);
}
