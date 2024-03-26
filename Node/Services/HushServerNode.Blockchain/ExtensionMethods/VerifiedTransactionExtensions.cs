using HushEcosystem.Model.Blockchain;

namespace HushServerNode.Blockchain.ExtensionMethods;

public static class VerifiedTransactionExtensions
{
    public static string GetTransactionIssuer(this VerifiedTransaction verifiedTransaction)
    {
        return verifiedTransaction.SpecificTransaction.Issuer;
    }
}
