using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushNode.InternalModules.Bank.Model;

namespace HushNode.InternalModules.Bank;

public class BalanceRepository : RepositoryBase<BankDbContext>, IBalanceRepository
{
    public async Task<AddressBalance> GetCurrentTokenBalanceAsync(string publicAddress, string token) => 
        await this.Context.AddressBalances
            .SingleOrDefaultAsync(x => x.PublicAddress == publicAddress && x.Token == token) 
                ?? new AddressNoBalance(publicAddress, token);

    public async Task CreateTokenBalanceAsync(AddressBalance tokenBalance) =>
        await this.Context.AddAsync(tokenBalance);

    public void UpdateTokenBalance(AddressBalance tokenBalance) => 
        this.Context
            .Set<AddressBalance>()
            .Update(tokenBalance);
}
