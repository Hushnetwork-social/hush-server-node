using HushNode.Bank.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Bank.Storage;

public interface IBalanceRepository : IRepository
{
    Task<AddressBalance> GetCurrentTokenBalanceAsync(string publicAddress, string token);

    Task CreateTokenBalanceAsync(AddressBalance tokenBalance);

    void UpdateTokenBalance(AddressBalance tokenBalance);
}