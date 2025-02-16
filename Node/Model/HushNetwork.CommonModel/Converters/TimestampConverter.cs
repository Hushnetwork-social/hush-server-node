using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushNetwork.CommonModel.Converters;

public class TimestampConverter : JsonConverter<Timestamp>
{
    public override Timestamp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var timestampElement = jsonDocument.RootElement;

        var timestampString = timestampElement.GetString();

        if (timestampString is null)
        {
            return Timestamp.Empty;
        }

        return new Timestamp(DateTime.Parse(timestampString, null, DateTimeStyles.AssumeUniversal));
    }

    public override void Write(Utf8JsonWriter writer, Timestamp value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
