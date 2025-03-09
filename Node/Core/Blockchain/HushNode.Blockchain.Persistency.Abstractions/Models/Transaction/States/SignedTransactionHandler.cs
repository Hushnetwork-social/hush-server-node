namespace HushNode.Blockchain.Persistency.Abstractions.Models.Transaction.States;

public static class SignedTransactionHandler
{
    public static ValidatedTransaction<T> SignByValidator<T>(this SignedTransaction<T> signedTransaction, SignatureInfo validatorSignature)
        where T : ITransactionPayloadKind =>
        new(
            signedTransaction,
            validatorSignature);

    public static ValidatedTransaction<T> SignByValidator<T>(this SignedTransaction<T> signedTransaction, string publickey, string privateKey)
        where T : ITransactionPayloadKind =>
        new(
            signedTransaction,
            new SignatureInfo(publickey, signedTransaction.CreateSignature(privateKey)));
}
