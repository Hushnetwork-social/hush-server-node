using HushServerNode.InternalModule.Bank.Cache;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.InternalModule.Bank;

public class BankService : IBankService
{
    private readonly IDbContextFactory<CacheBankDbContext> _dbContextFactory;

    public BankService(IDbContextFactory<CacheBankDbContext> dbContextFactory)
    {
        this._dbContextFactory = dbContextFactory;
    }

    public async Task UpdateBalanceAsync(string address, double value)
    {
        using (var context = this._dbContextFactory.CreateDbContext())
        {
            var addressBalance = context.AddressesBalance
                .SingleOrDefault(a => a.Address == address);

            if (addressBalance == null)
            {
                addressBalance = new AddressBalance
                {
                    Address = address,
                    Balance = value
                };

                context.AddressesBalance.Add(addressBalance);
            }
            else
            {
                addressBalance.Balance += value;
                context.AddressesBalance.Update(addressBalance);
            }

            await context.SaveChangesAsync();
        }
    }

    public double GetBalance(string address)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        var addressBalance = context.AddressesBalance
            .SingleOrDefault(a => a.Address == address);

        if (addressBalance == null)
        {
            return 0;
        }

        return addressBalance.Balance;
    }
}
