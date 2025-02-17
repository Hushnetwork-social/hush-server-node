using HushNode.Blockchain.Persistency.Abstractions.Model;

namespace HushNode.Blockchain.Persistency.Abstractions;

public interface IBlockRepository
{
    Task<Block> GetBlockByIdAsync(Guid blockId);
    
    // Task SaveBlockAsync(Block block);
    
}
