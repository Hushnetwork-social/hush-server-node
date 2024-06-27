using System.Threading.Tasks;

namespace HushServerNode.CacheService;

public interface IBlockchainCache
{
    void ConnectPersistentDatabase();

    Task<BlockchainState> GetBlockchainStateAsync();

    Task UpdateBlockchainState(BlockchainState blockchainState);

    Task SaveBlockAsync(string blockId, long blockHeight, string previousBlockId, string nextBlockId, string blockHash, string blockJson);

    Task UpdateBalanceAsync(string address, double value);

    double GetBalance(string address);

    Task UpdateProfile(Profile profile);

    Profile GetProfile(string address);
}
