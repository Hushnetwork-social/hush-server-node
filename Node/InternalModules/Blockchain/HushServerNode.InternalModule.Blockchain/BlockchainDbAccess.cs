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

    public async Task SaveSettingsAsync(SettingsEntity settingsEntity)
    {
        using var context = this._dbContextFactory.CreateDbContext();

        var settings = context.SettingsEntities.SingleOrDefault(x => 
            x.SettingsId == settingsEntity.SettingsId || 
            x.SettingsType == settingsEntity.SettingsType);

        if (settings == null)
        {
            context.SettingsEntities.Add(settingsEntity);
        }
        else
        {
            settings.Value = settingsEntity.Value;
            settings.ValidUntilBlock = settings.ValidUntilBlock;
            context.SettingsEntities.Update(settings);
        }

        await context.SaveChangesAsync();
    }

    public string GetSettings(string table, string key)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        var setting = context.SettingsEntities.SingleOrDefault(x => x.SettingsType == table && x.Key == key);

        // TODO [AboimPinto] Get a valid settings from the index database.

        if (setting == null)
        {
            // TODO [AboimPinto]: What to do in this situation?
            throw new InvalidOperationException($"the key {key} was not found in the settings table {table}");
        }
        else
        {
            return setting.Value;
        }
    }
}
