using HushNode.Blockchain.Storage.Model;

namespace HushNode.Blockchain.Storage;

public interface IBlockchainStorageService
{
    Task<BlockchainState> RetrieveCurrentBlockchainStateAsync();

    Task PersisteBlockAndBlockState(BlockchainBlock blockchainBlock, BlockchainState blockchainState);
}
