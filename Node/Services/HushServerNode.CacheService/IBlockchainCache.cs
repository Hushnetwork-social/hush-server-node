using System.Threading.Tasks;

namespace HushServerNode.CacheService;

public interface IBlockchainCache
{
    void ConnectPersistentDatabase();

    Task<BlockchainState> GetBlockchainStateAsync();

    Task UpdateBlockchainState(BlockchainState blockchainState);
}
