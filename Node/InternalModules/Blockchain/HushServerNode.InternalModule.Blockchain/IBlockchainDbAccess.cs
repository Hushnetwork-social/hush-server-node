using HushServerNode.InternalModule.Blockchain.Cache;

namespace HushServerNode.InternalModule.Blockchain;

public interface IBlockchainDbAccess
{
    Task<BlockchainState?> GetBlockchainStateAsync();

    Task UpdateBlockchainState(BlockchainState blockchainState);

    Task SaveBlockAsync(string blockId,
        long blockHeight,
        string previousBlockId,
        string nextBlockId,
        string blockHash,
        string blockJson);
}
