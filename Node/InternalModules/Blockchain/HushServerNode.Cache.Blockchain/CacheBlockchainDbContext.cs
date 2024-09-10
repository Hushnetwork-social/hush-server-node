using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.Cache.Blockchain;

public class CacheBlockchainDbContext : BaseDbContext
{
    private readonly CacheBlockchainDbContextConfigurator _cacheBlockchainDbContextConfigurator;

    public DbSet<BlockchainState> BlockchainState { get; set; }

    public DbSet<BlockEntity> BlockEntities { get; set; }

    public CacheBlockchainDbContext(
        CacheBlockchainDbContextConfigurator cacheBlockchainDbContextConfigurator,
        IConfiguration configuration) : base(configuration)
    {
        this._cacheBlockchainDbContextConfigurator = cacheBlockchainDbContextConfigurator;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        this._cacheBlockchainDbContextConfigurator.Configure(modelBuilder);
    }
}
