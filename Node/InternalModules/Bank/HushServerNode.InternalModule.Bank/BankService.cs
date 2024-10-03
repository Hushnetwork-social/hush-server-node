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
        using var context = this._dbContextFactory.CreateDbContext();
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

    public async Task UpdateFromAndToBalancesAsync(string fromAddress, double fromValue, string toAddress, double toValue)
    {
        using var context = this._dbContextFactory.CreateDbContext();
        var transaction = context.Database.BeginTransaction();

        var fromAddressBalance = context.AddressesBalance.SingleOrDefault(x => x.Address == fromAddress);
        if (fromAddressBalance == null)
        {
            fromAddressBalance = new AddressBalance
            {
                Address = fromAddress,
                Balance = fromValue.DoubleToString()
            };

            context.AddressesBalance.Add(fromAddressBalance);
        }
        else
        {
            fromAddressBalance.Balance = (fromAddressBalance.Balance.StringToDouble() + fromValue).DoubleToString();
            context.AddressesBalance.Update(fromAddressBalance);
        }

        // await context.SaveChangesAsync();

        var toAddressBalance = context.AddressesBalance.SingleOrDefault(x => x.Address == toAddress);
        if (toAddressBalance == null)
        {
            toAddressBalance = new AddressBalance
            {
                Address = toAddress,
                Balance = toValue.DoubleToString()
            };

            context.AddressesBalance.Add(toAddressBalance);
        }
        else
        {
            toAddressBalance.Balance = (toAddressBalance.Balance.StringToDouble() + toValue).DoubleToString();
            context.AddressesBalance.Update(toAddressBalance);
        }

        await context.SaveChangesAsync();

        await transaction.CommitAsync();
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
