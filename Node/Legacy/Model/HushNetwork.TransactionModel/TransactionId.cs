using System.Text.Json.Serialization;
using HushNetwork.Shared.Model.Transaction.Converters;

namespace HushNetwork.TransactionModel;

[JsonConverter(typeof(TransactionIdConverter))]
public readonly record struct  TransactionId(Guid Value)
{
    public static TransactionId Empty { get; } = new(Guid.Empty);
    public static TransactionId NewTransactionId { get; } = new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}


