using System.Text.Json.Serialization;
using HushShared.Feeds.Model.Converters;

namespace HushShared.Feeds.Model;

[JsonConverter(typeof(FeedMessageIdConverter))]
public readonly record struct FeedMessageId(Guid Value)
{
    public static FeedMessageId Empty => new(Guid.Empty);

    public static FeedMessageId NewFeedMessageId => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}