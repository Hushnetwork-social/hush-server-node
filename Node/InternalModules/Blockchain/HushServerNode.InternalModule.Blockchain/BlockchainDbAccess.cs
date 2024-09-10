using HushServerNode.Cache.Blockchain;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.InternalModule.Blockchain;

public class BlockchainDbAccess : IBlockchainDbAccess
{
    private readonly IDbContextFactory<CacheBlockchainDbContext> _dbContextFactory;

    public BlockchainDbAccess(IDbContextFactory<CacheBlockchainDbContext> dbContextFactory)
    {
        this._dbContextFactory = dbContextFactory;
    }

    public async Task<BlockchainState?> GetBlockchainStateAsync()
    {
        using var context = this._dbContextFactory.CreateDbContext();
        return await context.BlockchainState.SingleOrDefaultAsync();
    }

    public async Task SaveBlockAsync(
        string blockId, 
        long blockHeight, 
        string previousBlockId, 
        string nextBlockId, 
        string blockHash, 
        string blockJson)
    {
        var block = new BlockEntity
        {
            BlockId = blockId,
            Height = blockHeight,
            PreviousBlockId = previousBlockId,
            NextBlockId = nextBlockId,
            Hash = blockHash,
            BlockJson = blockJson
        };

        using (var context = this._dbContextFactory.CreateDbContext())
        {
            context.BlockEntities.Add(block);
            await context.SaveChangesAsync();
        }
    }

    public async Task UpdateBlockchainState(BlockchainState blockchainState)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        if (context.BlockchainState.Any())
        {
            context.BlockchainState.Update(blockchainState);
        }
        else
        {
            context.BlockchainState.Add(blockchainState);
        }

        await context.SaveChangesAsync();
    }
}
