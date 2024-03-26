using System.Collections.Generic;
using System.Threading.Tasks;
using HushEcosystem.Model.Blockchain;

namespace HushServerNode.Blockchain.IndexStrategies;

public class GroupTransactionsByAddressIndexStrategy : IIndexStrategy
{
    private readonly IBlockchainIndexDb _blockchainIndexDb;

    public GroupTransactionsByAddressIndexStrategy(IBlockchainIndexDb blockchainIndexDb)
    {
        this._blockchainIndexDb = blockchainIndexDb;
    }

    public bool CanHandle(VerifiedTransaction verifiedTransaction)
    {
        return true;
    }

    public Task Handle(VerifiedTransaction verifiedTransaction)
    {
        if (this._blockchainIndexDb.GroupedTransactions.ContainsKey(verifiedTransaction.SpecificTransaction.Issuer))
        {
            this._blockchainIndexDb.GroupedTransactions[verifiedTransaction.SpecificTransaction.Issuer].Add(verifiedTransaction);
        }
        else
        {
            this._blockchainIndexDb.GroupedTransactions.Add(verifiedTransaction.SpecificTransaction.Issuer, new List<VerifiedTransaction> { verifiedTransaction });
        }

        return Task.CompletedTask;
    }
}
