using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Blockchain.TransactionModel;

public static class SignedTransactionExtensionMethods
{
    public static ValidatedTransaction<T> SignByValidator<T>(
        this SignedTransaction<T> signedTransaction, 
        SignatureInfo validatorSignature)
        where T : ITransactionPayloadKind =>
        new(
            signedTransaction,
            validatorSignature);

    public static ValidatedTransaction<T> SignByValidator<T>(
        this SignedTransaction<T> signedTransaction, 
        string publickey, 
        string privateKey)
        where T : ITransactionPayloadKind =>
        new(
            signedTransaction,
            new SignatureInfo(publickey, signedTransaction.CreateSignature(privateKey)));
}
