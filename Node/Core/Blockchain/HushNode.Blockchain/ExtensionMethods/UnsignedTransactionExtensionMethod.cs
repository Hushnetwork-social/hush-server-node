using Olimpo;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.Blockchain;

public static class UnsignedTransactionExtensionMethod
{
    public static SignedTransaction<T> SignByUser<T>(this UnsignedTransaction<T> unsignedTransaction, SignatureInfo userSignature)
        where T : ITransactionPayloadKind =>
        new(
            unsignedTransaction,
            userSignature);

    public static SignedTransaction<T> SignByUser<T>(this UnsignedTransaction<T> unsignedTransaction, string publickey, string privateKey)
        where T : ITransactionPayloadKind =>
        new(
            unsignedTransaction,
            new SignatureInfo(publickey, unsignedTransaction.CreateSignature(privateKey)));

    public static string CreateSignature<T>(this UnsignedTransaction<T> unsignedTransaction, string privateKey) 
        where T : ITransactionPayloadKind =>
        DigitalSignature.SignMessage(unsignedTransaction.ToJson(), privateKey);
}
