using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.InternalModule.Blockchain.Cache;

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

        modelBuilder
            .Entity<SettingsEntity>()
            .ToTable("BLOCKCHAIN_SettingsEntity")
            .HasKey(x => new { x.SettingsId, x.SettingsType } );
}
}
