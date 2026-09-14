using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushShared.Blockchain.TransactionModel.Converters;

public class AbstractTransactionConverter : JsonConverter<AbstractTransaction>
{
    public override AbstractTransaction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var payloadKindElement = jsonDocument.RootElement;
        // Missing/wrong-type discriminators are malformed input, not lookup/setup errors.
        // Keep them within the RPC's existing typed JsonException rejection boundary.
        if (payloadKindElement.ValueKind != JsonValueKind.Object ||
            !payloadKindElement.TryGetProperty("PayloadKind", out var kindElement) ||
            kindElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Transaction payload kind must be a string.");
        }
        var payloadKind = kindElement.GetString();

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

        // FEAT-011 Task 3.1: unsupported wire kinds are parse failures. The
        // RPC maps JsonException to its existing typed rejection response.
        throw new JsonException("Unsupported transaction payload kind.");
    }

    public override void Write(Utf8JsonWriter writer, AbstractTransaction value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
