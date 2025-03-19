using System.Text.Json.Serialization;
using HushShared.Blockchain.Model;

namespace HushShared.Blockchain.TransactionModel.States;

public record ValidatedTransaction<T>: SignedTransaction<T>
    where T: ITransactionPayloadKind
{
    public SignatureInfo ValidatorSignature { get; init; }

    public ValidatedTransaction(
        SignedTransaction<T> signedTransaction, 
        SignatureInfo validatorSignature) 
        : base(
            signedTransaction, 
            signedTransaction.UserSignature)
    {
        ValidatorSignature = validatorSignature;
    }

    [JsonConstructor]
    public ValidatedTransaction(
        TransactionId TransactionId,
        Guid PayloadKind,
        Timestamp TransactionTimeStamp,
        T Payload,
        long PayloadSize,
        SignatureInfo UserSignature,
        SignatureInfo ValidatorSignature)
        : base(
            new SignedTransaction<T>(
                new UnsignedTransaction<T>(
                    TransactionId,
                    PayloadKind,
                    TransactionTimeStamp,
                    Payload,
                    PayloadSize), 
                UserSignature),
            UserSignature)
    {
        this.ValidatorSignature = ValidatorSignature;
    }

    // public bool IsValidatorSignatureValid() => 
    //     this
    //         .ExtractSignedTransaction()
    //         .CheckValidatorSignature();

    // public bool IsUserSignatureValid() => 
    //     this
    //         .ExtractSignedTransaction()
    //         .CheckUserSignature();

}
