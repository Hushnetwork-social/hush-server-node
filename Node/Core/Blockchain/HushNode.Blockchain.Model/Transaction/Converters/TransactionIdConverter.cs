using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushNode.Blockchain.Model.Transaction.Converters;

public class TransactionIdConverter : JsonConverter<TransactionId>
{
    public override TransactionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var transactionIdElement = jsonDocument.RootElement;

        var transactionIdString = transactionIdElement.GetString();

        if (transactionIdString is null)
        {
            return TransactionId.Empty;
        }

        return new TransactionId(Guid.Parse(transactionIdString));
    }

    public override void Write(Utf8JsonWriter writer, TransactionId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
