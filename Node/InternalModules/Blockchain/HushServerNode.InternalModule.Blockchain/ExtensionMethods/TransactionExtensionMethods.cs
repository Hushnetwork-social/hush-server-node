using HushEcosystem.Model.Bank;
using HushEcosystem.Model.Blockchain;

namespace HushServerNode.InternalModule.Blockchain;

public static class TransactionExtensionMethods
{
    public static VerifiedTransaction GetRewardTransaction(this IEnumerable<VerifiedTransaction> transactions)
    {
        var verifiedRewardTransaction = transactions
            .Where(x => x.SpecificTransaction.Id == RewardTransaction.TypeCode)
            .Single();

        return verifiedRewardTransaction;
    }
}
