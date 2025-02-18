using System.Text.Json.Serialization;
using HushNode.Blockchain.Persistency.Abstractions.Converters;

namespace HushNode.Blockchain.Persistency.Abstractions.Model;

[JsonConverter(typeof(BlockIndexConverter))]
public record BlockIndex(long Value)
{
    public static BlockIndex Empty { get; } = new(-1);

    public override string ToString() => Value.ToString();
}
