using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushShared.Bank.Model.Converters;

public class FundsTransactionIdConverter : JsonConverter<FundsTransactionId>
{
    public override FundsTransactionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var fundsTransactionIdElement = jsonDocument.RootElement;

        var fundsTransactionIdString = fundsTransactionIdElement.GetString();

        if (fundsTransactionIdString is null)
        {
            return FundsTransactionId.Empty;
        }

        return new FundsTransactionId(Guid.Parse(fundsTransactionIdString));
    }

    public override void Write(Utf8JsonWriter writer, FundsTransactionId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
