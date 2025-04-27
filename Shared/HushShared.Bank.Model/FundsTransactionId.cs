using System.Text.Json.Serialization;
using HushShared.Bank.Model.Converters;

namespace HushShared.Bank.Model;

[JsonConverter(typeof(FundsTransactionIdConverter))]
public readonly record struct FundsTransactionId(Guid Value)
{
    public static FundsTransactionId Empty => new(Guid.Empty);

    public static FundsTransactionId NewFundsTransactionId => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}