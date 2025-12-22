using System.Text.Json.Serialization;
using HushShared.Reactions.Model.Converters;

namespace HushShared.Reactions.Model;

[JsonConverter(typeof(ReactionIdConverter))]
public readonly record struct ReactionId(Guid Value)
{
    public static ReactionId Empty => new(Guid.Empty);

    public static ReactionId NewReactionId => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
