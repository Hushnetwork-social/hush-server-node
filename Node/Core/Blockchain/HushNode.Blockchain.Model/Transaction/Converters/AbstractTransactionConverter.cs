using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushNode.Blockchain.Model.Transaction.Converters;

public class AbstractTransactionConverter : JsonConverter<AbstractTransaction>
{
    public override AbstractTransaction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var payloadKindElement = jsonDocument.RootElement;
        var payloadKind = payloadKindElement.GetProperty("PayloadKind").GetString();

        // TODO [AboimPinto]: Use the strategy pattern to deserialize each payload kind

        // if (payloadKind == RewardPayloadHandler.RewardPayloadKind.ToString())
        // {
        //     return JsonSerializer.Deserialize<ValidatedTransaction<RewardPayload>>(jsonDocument.RootElement.GetRawText());
        // }
        // else if (payloadKind == EmptyPayloadHandler.EmptyPayloadKind.ToString())
        // {
        //     return JsonSerializer.Deserialize<ValidatedTransaction<EmptyPayload>>(jsonDocument.RootElement.GetRawText());
        // }

        throw new InvalidOperationException();
    }

    public override void Write(Utf8JsonWriter writer, AbstractTransaction value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
