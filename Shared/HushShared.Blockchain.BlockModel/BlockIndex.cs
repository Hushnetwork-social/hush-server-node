using System.Text.Json.Serialization;
using HushShared.Blockchain.BlockModel.Converters;

namespace HushShared.Blockchain.BlockModel;

[JsonConverter(typeof(BlockIndexConverter))]
public record BlockIndex(long Value)
{
    public static BlockIndex Empty { get; } = new(-1);

    public override string ToString() => Value.ToString();
}
