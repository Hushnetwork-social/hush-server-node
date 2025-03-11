using HushNode.Bank.Model;
using HushNode.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Bank.Storage;

public class BankDbContextConfigurator : IDbContextConfigurator
{
    public void Configure(ModelBuilder modelBuilder)
    {
        ConfigureAddressBalance(modelBuilder);
    }

    private static void ConfigureAddressBalance(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<AddressBalance>()
            .ToTable("AddressBalance", "Bank");

        modelBuilder
            .Entity<AddressBalance>()
            .HasKey(x => new { x.PublicAddress, x.Token });
    }
}
