using Olimpo.EntityFramework.Persistency;
using HushNode.Blockchain.Storage.Model;

namespace HushNode.Blockchain.Storage;

public class BlockRepository : RepositoryBase<BlockchainDbContext>, IBlockRepository
{
    public async Task AddBlockchainBlockAsync(BlockchainBlock block) => 
        await Context.Blocks.AddAsync(block);

    // public Task<Block> GetBlockByIdAsync(Guid blockId) => throw new NotImplementedException();
        // await _dbContext.Blocks
        //     .SingleAsync(x => x.BlockId == blockId);
}
