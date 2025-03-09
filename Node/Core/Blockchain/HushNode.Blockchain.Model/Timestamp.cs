using System.Text.Json.Serialization;
using HushNode.Blockchain.Model.Converters;

namespace HushNode.Blockchain.Model;

[JsonConverter(typeof(TimestampConverter))]
public record Timestamp(DateTime Value)
{
    public static Timestamp Empty { get; } = new(DateTime.MinValue);
    public static Timestamp Current { get; } = new(DateTime.UtcNow);

    public override string ToString() => Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
}
