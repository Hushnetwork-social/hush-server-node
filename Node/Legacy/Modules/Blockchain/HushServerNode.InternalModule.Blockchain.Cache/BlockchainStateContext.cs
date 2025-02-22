using Microsoft.EntityFrameworkCore;

namespace HushServerNode.InternalModule.Blockchain.Cache;

public class BlockchainStateContext(IDbContextFactory<CacheBlockchainDbContext> dbContextFactory) : IBlockchainStateContext
{
    private readonly IDbContextFactory<CacheBlockchainDbContext> _dbContextFactory = dbContextFactory;

    public async Task<BlockchainState> GetBlockchainStateAsync()
    {
        using var context = this._dbContextFactory.CreateDbContext();
        return await context.BlockchainState.SingleAsync();
    }

    public async Task SaveBlockchainStateAsync(BlockchainState blockchainState)
    {
        // TODO [AboimPinto]: This save could be part of a transaction. When the Block is save, this information is save also.
        //                    To avoid strange behaviors, this could be part of the same transaction.

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
