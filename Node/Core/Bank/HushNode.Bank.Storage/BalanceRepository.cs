using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushNode.Bank.Model;

namespace HushNode.Bank.Storage;

public class BalanceRepository : RepositoryBase<BankDbContext>, IBalanceRepository
{
    public async Task<AddressBalance> GetCurrentTokenBalanceAsync(string publicAddress, string token) => 
        await Context.AddressBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.PublicAddress == publicAddress && x.Token == token) 
                ?? new AddressNoBalance(publicAddress, token);

    public async Task CreateTokenBalanceAsync(AddressBalance tokenBalance) =>
        await Context.AddAsync(tokenBalance);

    public void UpdateTokenBalance(AddressBalance tokenBalance) =>
        Context
            .Set<AddressBalance>()
            .Update(tokenBalance);
}
