using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.InternalModule.Bank.Cache;

public class CacheBankDbContext : BaseDbContext
{
    private readonly CacheBankDbContextConfigurator _cacheBankDbContextConfigurator;

    public DbSet<AddressBalance> AddressesBalance { get; set; }

    public CacheBankDbContext(
        CacheBankDbContextConfigurator cacheBankDbContextConfigurator,
        IConfiguration configuration) : base(configuration)
    {
        this._cacheBankDbContextConfigurator = cacheBankDbContextConfigurator;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        this._cacheBankDbContextConfigurator.Configure(modelBuilder);
    }
}
