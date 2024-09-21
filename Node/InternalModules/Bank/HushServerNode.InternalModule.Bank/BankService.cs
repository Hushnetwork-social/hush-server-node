using Microsoft.EntityFrameworkCore;
using HushEcosystem;
using HushServerNode.InternalModule.Bank.Cache;

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
                    Balance = value.DoubleToString()
                };

                context.AddressesBalance.Add(addressBalance);
            }
            else
            {
                addressBalance.Balance = (addressBalance.Balance.StringToDouble() + value).DoubleToString();
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

        return addressBalance.Balance.StringToDouble();
    }
}
