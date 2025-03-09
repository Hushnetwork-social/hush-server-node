using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushNode.Blockchain.Model.Block.Converters;

public class BlockIdConverter : JsonConverter<BlockId>
{
    public override BlockId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var blockIdElement = jsonDocument.RootElement;

        var blockIdString = blockIdElement.GetString();

        if (blockIdString is null)
        {
            return BlockId.Empty;
        }

        return new BlockId(Guid.Parse(blockIdString));
    }

    public override void Write(Utf8JsonWriter writer, BlockId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
