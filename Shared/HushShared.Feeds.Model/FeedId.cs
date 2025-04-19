using System.Text.Json.Serialization;
using HushShared.Feeds.Model.Converters;

namespace HushShared.Feeds.Model;

[JsonConverter(typeof(FeedIdConverter))]
public readonly record struct FeedId(Guid Value)
{
    public static FeedId Empty => new(Guid.Empty);

    public static FeedId NewFeedId => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
