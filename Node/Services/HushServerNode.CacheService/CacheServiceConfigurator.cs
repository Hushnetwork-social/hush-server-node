using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.CacheService;

public class CacheServiceConfigurator : IDbContextConfigurator
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new BlockEntityConfigurator());
        modelBuilder.ApplyConfiguration(new BlockchainStateConfiguration());
    }
}
