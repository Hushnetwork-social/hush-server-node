using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockRepository(BlockchainDbContext dbContext) : IBlockRepository
{
    private readonly BlockchainDbContext _dbContext = dbContext;

    // public Task<Block> GetBlockByIdAsync(Guid blockId) => throw new NotImplementedException();
        // await _dbContext.Blocks
        //     .SingleAsync(x => x.BlockId == blockId);
}
