using System.Text.Json.Serialization;
using HushNode.Blockchain.Persistency.Abstractions.Models.Converters;

namespace HushNode.Blockchain.Persistency.Abstractions.Models;

[JsonConverter(typeof(TimestampConverter))]
public record Timestamp(DateTime Value)
{
    public static Timestamp Empty { get; } = new(DateTime.MinValue);
    public static Timestamp Current { get; } = new(DateTime.UtcNow);

    public override string ToString() => Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
}
