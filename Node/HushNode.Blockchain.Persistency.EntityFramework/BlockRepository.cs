using HushNode.Blockchain.Persistency.Abstractions.Repositories;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockRepository(BlockchainDbContext dbContext) : IBlockRepository
{
    private readonly BlockchainDbContext _dbContext = dbContext;

    // public Task<Block> GetBlockByIdAsync(Guid blockId) => throw new NotImplementedException();
        // await _dbContext.Blocks
        //     .SingleAsync(x => x.BlockId == blockId);
}
