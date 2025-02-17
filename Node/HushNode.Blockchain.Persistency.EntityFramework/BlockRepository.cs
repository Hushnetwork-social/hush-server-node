using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Persistency.Abstractions.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockRepository(BlockchainDbContext dbContext) : IBlockRepository
{
    private readonly BlockchainDbContext _dbContext = dbContext;

    public async Task<Block> GetBlockByIdAsync(Guid blockId) => 
        await _dbContext.Blocks
            .SingleAsync(x => x.BlockId == blockId);
}
