using HushNode.InternalModules.Bank.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.InternalModules.Bank;

public interface IBalanceRepository : IRepository
{
    Task<AddressBalance> GetCurrentTokenBalanceAsync(string publicAddress, string token);

    Task CreateTokenBalanceAsync(AddressBalance tokenBalance);

    void UpdateTokenBalance(AddressBalance tokenBalance);
}