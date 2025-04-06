using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushShared.Feeds.Model.Converters;

public class FeedIdConverter : JsonConverter<FeedId>
{
    public override FeedId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var feedIdElement = jsonDocument.RootElement;

        var feedIdString = feedIdElement.GetString();

        if (feedIdString is null)
        {
            return FeedId.Empty;
        }

        return new FeedId(Guid.Parse(feedIdString));
    }

    public override void Write(Utf8JsonWriter writer, FeedId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
