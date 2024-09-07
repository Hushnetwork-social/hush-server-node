using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.Cache.Bank;

public class CacheBankDbContextConfigurator : IDbContextConfigurator
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<AddressBalance>()
            .ToTable("BANK_AddressBalance")
            .HasKey(x => x.Address);
    }
}
