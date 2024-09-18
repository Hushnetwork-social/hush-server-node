using HushServerNode.InternalModule.Blockchain.Cache;

namespace HushServerNode.InternalModule.Blockchain;

public interface IBlockchainDbAccess
{
    Task<BlockchainState> GetBlockchainStateAsync();

    Task<bool> HasBlockchainStateAsync();

    Task SaveBlockAndBlockchainStateAsync(BlockEntity block, BlockchainState blockchainState);
}
