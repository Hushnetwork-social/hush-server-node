using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushShared.Reactions.Model.Converters;

public class ReactionIdConverter : JsonConverter<ReactionId>
{
    public override ReactionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        var reactionIdElement = jsonDocument.RootElement;

        var reactionIdString = reactionIdElement.GetString();

        if (reactionIdString is null)
        {
            return ReactionId.Empty;
        }

        return new ReactionId(Guid.Parse(reactionIdString));
    }

    public override void Write(Utf8JsonWriter writer, ReactionId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
