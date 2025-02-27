using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockRepository : RepositoryBase<BlockchainDbContext>, IBlockRepository
{
    public async Task AddBlockchainBlockAsync(BlockchainBlock block) => 
        await this.Context.Blocks.AddAsync(block);

    // public Task<Block> GetBlockByIdAsync(Guid blockId) => throw new NotImplementedException();
        // await _dbContext.Blocks
        //     .SingleAsync(x => x.BlockId == blockId);
}
