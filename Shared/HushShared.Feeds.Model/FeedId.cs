using System.Text.Json.Serialization;
using HushShared.Feeds.Model.Converters;

namespace HushShared.Feeds.Model;

[JsonConverter(typeof(FeedIdConverter))]
public readonly record struct FeedId(Guid Value)
{
    public static FeedId Empty { get; } = new(Guid.Empty);

    public static FeedId NewFeedId { get; } = new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
