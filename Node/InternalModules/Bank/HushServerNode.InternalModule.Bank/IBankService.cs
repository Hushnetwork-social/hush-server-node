using HushEcosystem.Model.Bank;

namespace HushServerNode.InternalModule.Bank;

public interface IBankService
{
    Task UpdateBalanceAsync(string address, double value);

    Task UpdateFromAndToBalancesAsync(string fromAddress, double fromValue, string toAddress, double toValue);

    double GetBalance(string address);

    Task MintNonFungibleToken(MintNonFungibleToken mintNonFungibleTokenTransaction);
}
