using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;

namespace HushServerNode.Blockchain.IndexStrategies;

public class ValueableTransactionIndexStrategy : IIndexStrategy
{
    private readonly IBlockchainIndexDb _blockchainIndexDb;
        
    public ValueableTransactionIndexStrategy(IBlockchainIndexDb blockchainIndexDb)
    {
        this._blockchainIndexDb = blockchainIndexDb;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction is IValueableTransaction)
        {
            return true;
        }

        return false;
    }

    public Task Handle(VerifiedTransaction verifiedTransaction)
    {
        if (verifiedTransaction.SpecificTransaction is IValueableTransaction valueableTransaction)
        {
            if (this._blockchainIndexDb.AddressBalance.ContainsKey(verifiedTransaction.SpecificTransaction.Issuer))
            {
                this._blockchainIndexDb.AddressBalance[verifiedTransaction.SpecificTransaction.Issuer] += valueableTransaction.Value;
            }
            else
            {
                this._blockchainIndexDb.AddressBalance.Add(verifiedTransaction.SpecificTransaction.Issuer, valueableTransaction.Value);
            }
        }

        return Task.CompletedTask;
    }
}
