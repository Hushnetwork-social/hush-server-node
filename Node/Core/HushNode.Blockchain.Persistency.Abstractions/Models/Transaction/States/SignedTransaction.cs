using System.Text.Json;

namespace HushNode.Blockchain.Persistency.Abstractions.Models.Transaction.States;

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
