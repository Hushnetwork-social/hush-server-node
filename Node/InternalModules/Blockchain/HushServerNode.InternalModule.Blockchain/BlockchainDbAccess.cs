using HushServerNode.InternalModule.Blockchain.Cache;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.InternalModule.Blockchain;

public class BlockchainDbAccess : IBlockchainDbAccess
{
    private readonly IDbContextFactory<CacheBlockchainDbContext> _dbContextFactory;

    public BlockchainDbAccess(IDbContextFactory<CacheBlockchainDbContext> dbContextFactory)
    {
        this._dbContextFactory = dbContextFactory;
    }

    public async Task<bool> HasBlockchainStateAsync()
    {
        using var context = this._dbContextFactory.CreateDbContext();
        return await context.BlockchainState.AnyAsync();
    }

    public async Task<BlockchainState> GetBlockchainStateAsync()
    {
        using var context = this._dbContextFactory.CreateDbContext();
        return await context.BlockchainState.SingleAsync();
    }

    public async Task SaveBlockAndBlockchainStateAsync(BlockEntity block, BlockchainState blockchainState)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        var transaction = context.Database.BeginTransaction();

        if (context.BlockchainState.Any())
        {
            context.BlockchainState.Update(blockchainState);
        }
        else
        {
            context.BlockchainState.Add(blockchainState);
        }
        await context.SaveChangesAsync();

        context.BlockEntities.Add(block);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
