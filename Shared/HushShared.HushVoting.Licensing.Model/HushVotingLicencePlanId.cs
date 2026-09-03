using System.Globalization;

namespace HushShared.HushVoting.Licensing.Model;

/// <summary>
/// Closed, ordinal, culture-independent plan identifier. Carries the five v1 stable constants and
/// a safe unknown state for external boundaries. An unknown external value is preserved as
/// unsupported and never coerced to Direct Free or any known plan.
/// </summary>
public sealed class HushVotingLicencePlanId : IEquatable<HushVotingLicencePlanId>
{
    public const string DirectFreeValue = "hushvoting.direct.free";
    public const string Veritas500Value = "hushvoting.veritas.500";
    public const string Veritas2000Value = "hushvoting.veritas.2000";
    public const string Veritas10000Value = "hushvoting.veritas.10000";
    public const string EnterpriseValue = "hushvoting.enterprise";

    /// <summary>Maximum UTF-8 byte length allowed for any plan ID (schema bound).</summary>
    public const int MaxUtf8Bytes = 96;

    public static readonly HushVotingLicencePlanId DirectFree = new(DirectFreeValue, isKnown: true);
    public static readonly HushVotingLicencePlanId Veritas500 = new(Veritas500Value, isKnown: true);
    public static readonly HushVotingLicencePlanId Veritas2000 = new(Veritas2000Value, isKnown: true);
    public static readonly HushVotingLicencePlanId Veritas10000 = new(Veritas10000Value, isKnown: true);
    public static readonly HushVotingLicencePlanId Enterprise = new(EnterpriseValue, isKnown: true);

    public static readonly IReadOnlyList<HushVotingLicencePlanId> Known = new[]
    {
        DirectFree,
        Veritas500,
        Veritas2000,
        Veritas10000,
        Enterprise,
    };

    private static readonly Dictionary<string, HushVotingLicencePlanId> KnownByValue = new(
        Known.ToDictionary(static id => id.Value, StringComparer.Ordinal),
        StringComparer.Ordinal);

    private HushVotingLicencePlanId(string value, bool isKnown)
    {
        Value = value;
        IsKnown = isKnown;
    }

    public string Value { get; }

    /// <summary>True when the value is one of the exact v1 stable plans.</summary>
    public bool IsKnown { get; }

    /// <summary>
    /// Parses an external wire value. Known values resolve to their constant; anything else is
    /// preserved as an unsupported plan (never coerced, never defaulted).
    /// </summary>
    public static HushVotingLicencePlanId FromExternal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HushVotingLicencePlanId(string.Empty, isKnown: false);
        }

        var trimmed = value.Trim();
        return KnownByValue.TryGetValue(trimmed, out var known)
            ? known
            : new HushVotingLicencePlanId(trimmed, isKnown: false);
    }

    /// <summary>
    /// Returns the known plan constant for a value that is required to be canonical, or null when
    /// the value is unknown/empty/oversized. Callers must treat null as a safe unsupported value.
    /// </summary>
    public static HushVotingLicencePlanId? TryGetKnown(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (System.Text.Encoding.UTF8.GetByteCount(trimmed) > MaxUtf8Bytes)
        {
            return null;
        }

        return KnownByValue.TryGetValue(trimmed, out var known) ? known : null;
    }

    public bool Equals(HushVotingLicencePlanId? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as HushVotingLicencePlanId);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(HushVotingLicencePlanId? left, HushVotingLicencePlanId? right) =>
        Equals(left, right);

    public static bool operator !=(HushVotingLicencePlanId? left, HushVotingLicencePlanId? right) =>
        !Equals(left, right);
}
