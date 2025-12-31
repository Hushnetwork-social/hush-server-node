using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushShared.Blockchain.TransactionModel.Converters;

public class AbstractTransactionConverter : JsonConverter<AbstractTransaction>
{
    public override AbstractTransaction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var payloadKindElement = jsonDocument.RootElement;
        var payloadKind = payloadKindElement.GetProperty("PayloadKind").GetString();

        payloadKindElement.TryGetProperty("ValidatorSignature", out var validatedSignature);

        foreach (var item in TransactionDeserializerHandler.Instance.SpecificDeserializers)
        {
            if (payloadKind is not null && item.CanDeserialize(payloadKind))
            {
                if (validatedSignature.ValueKind == JsonValueKind.Undefined)
                {
                    return item.DeserializeSignedTransaction(jsonDocument.RootElement.GetRawText());
                }
                else
                {
                    return item.DeserializeValidatedTransaction(jsonDocument.RootElement.GetRawText());
                }
            }
        }

        throw new InvalidOperationException();
    }

    public override void Write(Utf8JsonWriter writer, AbstractTransaction value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
