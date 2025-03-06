using HushNode.InternalModules.Bank.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.InternalModules.Bank;

public class BankDbContext(
    BankDbContextConfigurator bankDbContextConfigurator,
    DbContextOptions<BankDbContext> options) : DbContext(options)
{
    private readonly BankDbContextConfigurator _bankDbContextConfigurator = bankDbContextConfigurator;

    public DbSet<AddressBalance> AddressBalances { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        this._bankDbContextConfigurator.Configure(modelBuilder);
    }
}
