using System.Text.Json.Serialization;
using HushNetwork.CommonModel;
using HushNetwork.TransactionModel.Signed;
using HushNetwork.TransactionModel.Unsigned;

namespace HushNetwork.TransactionModel.Validated;

// Step 3: Validator validates the transaction
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

    // public override bool CheckSignature()
    // {
    //     var signedTransaction = new SignedTransaction<T>(
    //         new UnsignedTransaction<T>(
    //             this.TransactionId, 
    //             this.PayloadKind,
    //             this.TransactionTimeStamp, 
    //             this.Payload,
    //             this.PayloadSize), 
    //         this.UserSignature);

    //     var isValidatorSignatureSignatureValid = SigningKeys.VerifySignature(
    //         signedTransaction.ToJson(), 
    //         this.ValidatorSignature.Signature, 
    //         this.ValidatorSignature.Signatory);

    //     var usignedTransaction = new UnsignedTransaction<T>(
    //         this.TransactionId, 
    //             this.PayloadKind,
    //             this.TransactionTimeStamp, 
    //             this.Payload,
    //             this.PayloadSize);

    //     var isUserSignatureSignatureValid = SigningKeys.VerifySignature(
    //         usignedTransaction.ToJson(), 
    //         this.UserSignature.Signature, 
    //         this.UserSignature.Signatory);

    //     return isValidatorSignatureSignatureValid && isUserSignatureSignatureValid;
    // }

    public bool IsValidatorSignatureValid() => 
        this
            .ExtractSignedTransaction()
            .CheckValidatorSignature();

    public bool IsUserSignatureValid() => 
        this
            .ExtractSignedTransaction()
            .CheckUserSignature();

}