using HushEcosystem.Model.Blockchain;

namespace HushServerNode.InternalModule.Blockchain.ExtensionMethods;

public static class TransactionExtensionMethods
{
    public static VerifiedTransaction GetRewardTransaction(this IEnumerable<VerifiedTransaction> transactions)
    {
        var verifiedRewardTransaction = transactions
            .Where(x => x.SpecificTransaction.Id == BlockCreationTransaction.TypeCode)
            .Single();

        return verifiedRewardTransaction;
    }
}
