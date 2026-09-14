using System.Text;

namespace HushShared.HushVoting.Licensing.Model;

/// <summary>
/// Closed, ordinal, culture-independent catalogue-version identifier. V1 uses the exact string
/// <c>hushvoting-licence-catalogue/v1.0.0</c>. Unknown external versions are preserved as
/// unsupported and never coerced.
/// </summary>
public sealed class HushVotingLicenceCatalogueVersion : IEquatable<HushVotingLicenceCatalogueVersion>
{
    public const string V1Value = "hushvoting-licence-catalogue/v1.0.0";
    public const int MaxUtf8Bytes = 96;

    /// <summary>Schema ID for the v1 catalogue manifest.</summary>
    public const string V1SchemaId = "hushvoting-licence-catalogue/v1";

    public static readonly HushVotingLicenceCatalogueVersion V1 = new(V1Value, isKnown: true);

    private static readonly Dictionary<string, HushVotingLicenceCatalogueVersion> KnownByValue = new(
        new Dictionary<string, HushVotingLicenceCatalogueVersion>(StringComparer.Ordinal)
        {
            [V1Value] = V1,
        },
        StringComparer.Ordinal);

    private HushVotingLicenceCatalogueVersion(string value, bool isKnown)
    {
        Value = value;
        IsKnown = isKnown;
    }

    public string Value { get; }

    public bool IsKnown { get; }

    public static HushVotingLicenceCatalogueVersion FromExternal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HushVotingLicenceCatalogueVersion(string.Empty, isKnown: false);
        }

        var trimmed = value.Trim();
        return KnownByValue.TryGetValue(trimmed, out var known)
            ? known
            : new HushVotingLicenceCatalogueVersion(trimmed, isKnown: false);
    }

    public static HushVotingLicenceCatalogueVersion? TryGetKnown(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (Encoding.UTF8.GetByteCount(trimmed) > MaxUtf8Bytes)
        {
            return null;
        }

        return KnownByValue.TryGetValue(trimmed, out var known) ? known : null;
    }

    public bool Equals(HushVotingLicenceCatalogueVersion? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as HushVotingLicenceCatalogueVersion);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(HushVotingLicenceCatalogueVersion? left, HushVotingLicenceCatalogueVersion? right) =>
        Equals(left, right);

    public static bool operator !=(HushVotingLicenceCatalogueVersion? left, HushVotingLicenceCatalogueVersion? right) =>
        !Equals(left, right);
}
