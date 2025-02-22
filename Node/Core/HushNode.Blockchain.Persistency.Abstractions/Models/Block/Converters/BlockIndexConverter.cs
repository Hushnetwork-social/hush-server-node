using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushNode.Blockchain.Persistency.Abstractions.Models.Block.Converters;

public class BlockIndexConverter : JsonConverter<BlockIndex>
{
    public override BlockIndex Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var blockIndexElement = jsonDocument.RootElement;

        var blockIndexString = blockIndexElement.GetString();

        if (blockIndexString is null)
        {
            return BlockIndex.Empty;
        }

        return new BlockIndex(long.Parse(blockIndexString));
    }

    public override void Write(Utf8JsonWriter writer, BlockIndex value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
