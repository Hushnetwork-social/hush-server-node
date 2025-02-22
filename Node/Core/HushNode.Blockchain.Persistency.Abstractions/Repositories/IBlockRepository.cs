using HushNode.Blockchain.Persistency.Abstractions.Models;

namespace HushNode.Blockchain.Persistency.Abstractions.Repositories;

public interface IBlockRepository
{
    // Task<Block> GetBlockByIdAsync(Guid blockId);
    
    // Task SaveBlockAsync(Block block);
    
    Task AddBlockchainBlockAsync(BlockchainBlock block);
}
