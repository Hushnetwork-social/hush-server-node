using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.InternalModule.Bank.Cache;

public class CacheBankDbContextConfigurator : IDbContextConfigurator
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<AddressBalance>()
            .ToTable("BANK_AddressBalance")
            .HasKey(x => x.Address);

        modelBuilder
            .Entity<NonFungibleTokenEntity>()
            .ToTable("BANK_NFT")
            .HasKey(x => x.NonFungibleTokenId);

        modelBuilder
            .Entity<NonFungibleTokenEntity>()
            .HasIndex(x => x.OwnerPublicAddress);

        modelBuilder
            .Entity<NonFungibleTokenMetadata>()
            .ToTable("BANK_NFT_Metadata")
            .HasKey(x => new { x.MetadataKey, x.NonFungibleTokenId });
    }
}
