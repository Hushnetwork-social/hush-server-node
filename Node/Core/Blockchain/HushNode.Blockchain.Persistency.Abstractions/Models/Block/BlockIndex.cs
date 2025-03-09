using System.Text.Json.Serialization;
using HushNode.Blockchain.Persistency.Abstractions.Models.Block.Converters;

namespace HushNode.Blockchain.Persistency.Abstractions.Models.Block;

[JsonConverter(typeof(BlockIndexConverter))]
public record BlockIndex(long Value)
{
    public static BlockIndex Empty { get; } = new(-1);

    public override string ToString() => Value.ToString();
}
