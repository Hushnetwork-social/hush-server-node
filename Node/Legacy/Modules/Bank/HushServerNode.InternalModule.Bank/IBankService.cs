using HushEcosystem.Model.Bank;
using HushServerNode.InternalModule.Bank.Cache;

namespace HushServerNode.InternalModule.Bank;

public interface IBankService
{
    Task UpdateBalanceAsync(string address, double value);

    Task UpdateFromAndToBalancesAsync(string fromAddress, double fromValue, string toAddress, double toValue);

    double GetBalance(string address);

    Task MintNonFungibleTokenAwait(MintNonFungibleToken mintNonFungibleTokenTransaction, long blockIndex);

    IEnumerable<NonFungibleTokenEntity> GetNonFungibleTokensByAddress(string address, long blockIndex);

    IEnumerable<NonFungibleTokenMetadata> GetNonFungibleTokenMetadata(string nonFungibleTokenId);
}
