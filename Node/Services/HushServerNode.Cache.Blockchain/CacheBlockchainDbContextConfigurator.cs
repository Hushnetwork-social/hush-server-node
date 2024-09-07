using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.Cache.Blockchain;

public class CacheBlockchainDbContextConfigurator : IDbContextConfigurator
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<BlockchainState>()
            .ToTable("BLOCKCHAIN_BlockchainState")
            .HasKey(x => x.BlockchainStateId);

        modelBuilder
            .Entity<BlockEntity>()
            .ToTable("BLOCKCHAIN_BlockEntity")
            .HasKey(x => x.BlockId);
}
}
