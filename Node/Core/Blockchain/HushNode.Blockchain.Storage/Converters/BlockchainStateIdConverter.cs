using System.Text.Json;
using System.Text.Json.Serialization;
using HushNode.Blockchain.Storage.Model;

namespace HushNode.Blockchain.Storage.Converters;

public class BlockchainStateIdConverter : JsonConverter<BlockchainStateId>
{
    public override BlockchainStateId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var blockIdElement = jsonDocument.RootElement;

        var blockIdString = blockIdElement.GetString();

        if (blockIdString is null)
        {
            return BlockchainStateId.Empty;
        }

        return new BlockchainStateId(Guid.Parse(blockIdString));
    }

    public override void Write(Utf8JsonWriter writer, BlockchainStateId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
