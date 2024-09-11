using HushEcosystem.Model.Blockchain;

namespace HushServerNode.InternalModule.Blockchain.ExtensionMethods;

public static class BlockExtensions
{
    public static string GetBlockGeneratorAddress(this IBlock block)
    {
        var verifiedRewardTransaction = block.Transactions.GetRewardTransaction();
        var blockGeneratorAddress = verifiedRewardTransaction.SpecificTransaction.Issuer;

        return blockGeneratorAddress;

    }  
}
