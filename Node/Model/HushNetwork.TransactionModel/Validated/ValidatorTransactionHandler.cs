using HushNetwork.TransactionModel.Signed;
using HushNetwork.TransactionModel.Unsigned;

namespace HushNetwork.TransactionModel.Validated;

public static class ValidatorTransactionHandler
{
    public static SignedTransaction<T> ExtractSignedTransaction<T>(this ValidatedTransaction<T> validatedTransaction)
        where T: ITransactionPayloadKind => 
            new(
                new UnsignedTransaction<T>(
                    validatedTransaction.TransactionId, 
                    validatedTransaction.PayloadKind,
                    validatedTransaction.TransactionTimeStamp, 
                    validatedTransaction.Payload,
                    validatedTransaction.PayloadSize), 
                validatedTransaction.UserSignature);
}
