using HushNode.Bank.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Bank.Storage;

public class BankDbContext(
    BankDbContextConfigurator bankDbContextConfigurator,
    DbContextOptions<BankDbContext> options) : DbContext(options)
{
    private readonly BankDbContextConfigurator _bankDbContextConfigurator = bankDbContextConfigurator;

    public DbSet<AddressBalance> AddressBalances { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _bankDbContextConfigurator.Configure(modelBuilder);
    }
}
