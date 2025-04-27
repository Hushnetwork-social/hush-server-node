using HushNode.Bank.Model;

namespace HushNode.Bank.Storage;

public interface IBankStorageService
{
    Task<AddressBalance> RetrieveTokenBalanceForAddress(string publicSignAddress, string token);

    Task CreateTokenBalanceForAddress(AddressBalance addressBalance);

    Task UpdateTokenBalanceForAddress(AddressBalance addressBalance);

    Task PersistNewTokenBalances(
        string originPublicSigningAddress,
        string newOriginBalance,
        string tgargetPublicSigningAddress,
        string newTargetBalance,
        string token);
}