using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.InternalModule.Authentication.Cache;

public class CacheAuthenticationDbContext : BaseDbContext
{
    private readonly CacheAuthenticationDbContextConfigurator _cacheAuthenticationDbContextConfigurator;

    public DbSet<Profile> Profiles { get; set; }

    public CacheAuthenticationDbContext(
        CacheAuthenticationDbContextConfigurator cacheAuthenticationDbContextConfigurator,
        IConfiguration configuration) : base(configuration) 
    {
        this._cacheAuthenticationDbContextConfigurator = cacheAuthenticationDbContextConfigurator;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        this._cacheAuthenticationDbContextConfigurator.Configure(modelBuilder);
    }
}
