using System.Text.Json.Serialization;
using HushShared.Blockchain.TransactionModel.Converters;

namespace HushShared.Blockchain.TransactionModel;

[JsonConverter(typeof(TransactionIdConverter))]
public readonly record struct  TransactionId(Guid Value)
{
    public static TransactionId Empty { get; } = new(Guid.Empty);
    public static TransactionId NewTransactionId { get; } = new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
