using System.Text.Json;
using System.Text.Json.Serialization;
using HushShared.Blockchain.Model;

namespace HushShared.Blockchain.TransactionModel.States;

public record SignedTransaction<T>: UnsignedTransaction<T>
    where T: ITransactionPayloadKind
{
    public SignatureInfo UserSignature { get; init; }
    public object? SignByValidator { get; set; }

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
        this.UserSignature = signature;
    }

    [JsonConstructor]
    public SignedTransaction(
        TransactionId TransactionId,
        Guid PayloadKind,
        Timestamp TransactionTimeStamp,
        T Payload,
        long PayloadSize,
        SignatureInfo UserSignature)
        : base(
            TransactionId,
            PayloadKind,
            TransactionTimeStamp,
            Payload,
            PayloadSize)
    {
        this.UserSignature = UserSignature;
    }

    public override string ToJson() => JsonSerializer.Serialize(this);
}
