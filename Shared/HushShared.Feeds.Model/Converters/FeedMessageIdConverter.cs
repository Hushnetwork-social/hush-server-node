using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushShared.Feeds.Model.Converters;

public class FeedMessageIdConverter : JsonConverter<FeedMessageId>
{
    public override FeedMessageId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var feedMessageIdElement = jsonDocument.RootElement;

        var feedMessageIdString = feedMessageIdElement.GetString();

        if (feedMessageIdString is null)
        {
            return FeedMessageId.Empty;
        }

        return new FeedMessageId(Guid.Parse(feedMessageIdString));
    }

    public override void Write(Utf8JsonWriter writer, FeedMessageId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
