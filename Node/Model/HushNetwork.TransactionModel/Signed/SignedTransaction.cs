using System.Text.Json;
using HushNetwork.CommonModel;
using HushNetwork.TransactionModel.Unsigned;

namespace HushNetwork.TransactionModel.Signed;

// Step 2: Client signs the transaction
public record SignedTransaction<T>: UnsignedTransaction<T>
    where T: ITransactionPayloadKind
{
    public SignatureInfo UserSignature { get; init; }

    public SignedTransaction(
        UnsignedTransaction<T> unsignedTransaction, 
        SignatureInfo signature)
      : base(
            unsignedTransaction.TransactionId, 
            unsignedTransaction.PayloadKind,
            unsignedTransaction.TransactionTimeStamp, 
            unsignedTransaction.Payload,
            unsignedTransaction.PayloadSize)
    {
        UserSignature = signature;
    }

    public override string ToJson() => JsonSerializer.Serialize(this);
}


