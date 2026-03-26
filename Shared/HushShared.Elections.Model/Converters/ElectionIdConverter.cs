using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushShared.Elections.Model.Converters;

public sealed class ElectionIdConverter : JsonConverter<ElectionId>
{
    public override ElectionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return ElectionId.Empty;
        }

        return ElectionIdHandler.CreateFromString(value);
    }

    public override void Write(Utf8JsonWriter writer, ElectionId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
