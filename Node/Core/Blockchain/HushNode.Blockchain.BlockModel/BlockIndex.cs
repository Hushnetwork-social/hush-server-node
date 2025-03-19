using System.Text.Json.Serialization;
using HushNode.Blockchain.Model.Block.Converters;

namespace HushNode.Blockchain.Model.Block;

[JsonConverter(typeof(BlockIndexConverter))]
public record BlockIndex(long Value)
{
    public static BlockIndex Empty { get; } = new(-1);

    public override string ToString() => Value.ToString();
}
