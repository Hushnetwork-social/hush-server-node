using System.Text.Json.Serialization;
using HushShared.Elections.Model.Converters;

namespace HushShared.Elections.Model;

[JsonConverter(typeof(ElectionIdConverter))]
public readonly record struct ElectionId(Guid Value)
{
    public static ElectionId Empty { get; } = new(Guid.Empty);
    public static ElectionId NewElectionId => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
