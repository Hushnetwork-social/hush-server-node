namespace HushShared.HushVoting.Licensing.Model;

/// <summary>
/// Closed customer-visible governance option identifier. The internal admin circuit (1of1) is never
/// a customer option: zero customer trustees is represented by <see cref="NoCustomerTrustees"/>.
/// </summary>
public sealed class HushVotingGovernanceOptionId : IEquatable<HushVotingGovernanceOptionId>
{
    public const string NoCustomerTrusteesValue = "no-customer-trustees";
    public const string Trustees3Of5Value = "trustees-3of5";
    public const string Trustees7Of10Value = "trustees-7of10";
    public const string Trustees8Of13Value = "trustees-8of13";

    public const int MaxUtf8Bytes = 96;

    public static readonly HushVotingGovernanceOptionId NoCustomerTrustees =
        new(NoCustomerTrusteesValue, isKnown: true);

    public static readonly HushVotingGovernanceOptionId Trustees3Of5 =
        new(Trustees3Of5Value, isKnown: true);

    public static readonly HushVotingGovernanceOptionId Trustees7Of10 =
        new(Trustees7Of10Value, isKnown: true);

    public static readonly HushVotingGovernanceOptionId Trustees8Of13 =
        new(Trustees8Of13Value, isKnown: true);

    public static readonly IReadOnlyList<HushVotingGovernanceOptionId> Known =
    [
        NoCustomerTrustees,
        Trustees3Of5,
        Trustees7Of10,
        Trustees8Of13,
    ];

    private static readonly Dictionary<string, HushVotingGovernanceOptionId> KnownByValue = new(
        Known.ToDictionary(static id => id.Value, StringComparer.Ordinal),
        StringComparer.Ordinal);

    private HushVotingGovernanceOptionId(string value, bool isKnown)
    {
        Value = value;
        IsKnown = isKnown;
    }

    public string Value { get; }

    public bool IsKnown { get; }

    /// <summary>Parse an external wire value; unknown values are preserved as unsupported.</summary>
    public static HushVotingGovernanceOptionId FromExternal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HushVotingGovernanceOptionId(string.Empty, isKnown: false);
        }

        var trimmed = value.Trim();
        return KnownByValue.TryGetValue(trimmed, out var known)
            ? known
            : new HushVotingGovernanceOptionId(trimmed, isKnown: false);
    }

    public static HushVotingGovernanceOptionId? TryGetKnown(string? value)
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

    public bool Equals(HushVotingGovernanceOptionId? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as HushVotingGovernanceOptionId);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(HushVotingGovernanceOptionId? left, HushVotingGovernanceOptionId? right) =>
        Equals(left, right);

    public static bool operator !=(HushVotingGovernanceOptionId? left, HushVotingGovernanceOptionId? right) =>
        !Equals(left, right);
}
