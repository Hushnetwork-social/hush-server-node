using System.Text.Json.Serialization;
using HushShared.Blockchain.Model.Converters;

namespace HushShared.Blockchain.Model;

[JsonConverter(typeof(TimestampConverter))]
public record Timestamp(DateTime Value)
{
    public static Timestamp Empty { get; } = new(DateTime.MinValue);
    public static Timestamp Current { get; } = new(DateTime.UtcNow);

    public override string ToString() => Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
}
