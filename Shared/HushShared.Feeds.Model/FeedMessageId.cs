using System.Text.Json.Serialization;
using HushShared.Feeds.Model.Converters;

namespace HushShared.Feeds.Model;

[JsonConverter(typeof(FeedMessageIdConverter))]
public readonly struct FeedMessageId(Guid Value)
{
    public static FeedMessageId Empty { get; } = new(Guid.Empty);

    public static FeedMessageId NewFeedMessageId { get; } = new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}