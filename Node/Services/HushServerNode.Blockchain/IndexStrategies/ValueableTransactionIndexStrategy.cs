using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;
using HushServerNode.CacheService;

namespace HushServerNode.Blockchain.IndexStrategies;

public class ValueableTransactionIndexStrategy : IIndexStrategy
{
    private readonly IBlockchainCache _blockchainCache;
        
    public ValueableTransactionIndexStrategy(IBlockchainCache blockchainCache)
    {
        this._blockchainCache = blockchainCache;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction is IValueableTransaction)
        {
            return true;
        }

        return false;
    }

    public async Task Handle(VerifiedTransaction verifiedTransaction)
    {
        var valueableTransaction = verifiedTransaction.SpecificTransaction as IValueableTransaction;

        await this._blockchainCache.UpdateBalanceAsync(
            verifiedTransaction.SpecificTransaction.Issuer,
            valueableTransaction.Value);
    }
}
