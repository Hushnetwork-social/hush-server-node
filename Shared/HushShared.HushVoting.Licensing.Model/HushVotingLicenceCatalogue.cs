namespace HushShared.HushVoting.Licensing.Model;

/// <summary>
/// Immutable, indexed catalogue snapshot. Holds the exact version, a deterministic display-ordered
/// plan list, ordinal index lookups by plan id, and the canonical governance compatibility entries.
/// A catalogue is only constructible from fully validated input; no partial catalogue escapes.
/// </summary>
public sealed class HushVotingLicenceCatalogue : IEquatable<HushVotingLicenceCatalogue>
{
    private readonly IReadOnlyDictionary<string, HushVotingLicencePlan> _plansByValue;

    public HushVotingLicenceCatalogue(
        HushVotingLicenceCatalogueVersion version,
        IReadOnlyList<HushVotingLicencePlan> plans,
        IReadOnlyList<HushVotingProfileCompatibilityEntry> profileCompatibility)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(profileCompatibility);

        if (!version.IsKnown)
        {
            throw new ArgumentException("Catalogue version must be a known closed value.", nameof(version));
        }

        if (plans.Count == 0)
        {
            throw new ArgumentException("A catalogue must contain at least one plan.", nameof(plans));
        }

        // Deterministic display order: explicit display order, then id ordinal as tiebreak.
        var ordered = plans
            .OrderBy(static plan => plan.DisplayOrder)
            .ThenBy(static plan => plan.Id.Value, StringComparer.Ordinal)
            .ToArray();

        var indexed = new Dictionary<string, HushVotingLicencePlan>(StringComparer.Ordinal);
        foreach (var plan in ordered)
        {
            if (plan.CatalogueVersion != version)
            {
                throw new ArgumentException(
                    $"Plan '{plan.Id.Value}' catalogue version does not match snapshot version.",
                    nameof(plans));
            }

            if (!indexed.TryAdd(plan.Id.Value, plan))
            {
                throw new ArgumentException(
                    $"Duplicate plan id '{plan.Id.Value}' in catalogue.",
                    nameof(plans));
            }
        }

        Version = version;
        Plans = ordered;
        _plansByValue = indexed;
        ProfileCompatibility = profileCompatibility;
    }

    public HushVotingLicenceCatalogueVersion Version { get; }

    /// <summary>Deterministic display-ordered immutable plan list.</summary>
    public IReadOnlyList<HushVotingLicencePlan> Plans { get; }

    /// <summary>Exact server-internal governance-option/binding to runtime profile mappings.</summary>
    public IReadOnlyList<HushVotingProfileCompatibilityEntry> ProfileCompatibility { get; }

    public HushVotingLicencePlan? FindPlan(HushVotingLicencePlanId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _plansByValue.TryGetValue(id.Value, out var plan) ? plan : null;
    }

    public bool ContainsPlan(HushVotingLicencePlanId id) => FindPlan(id) is not null;

    public bool Equals(HushVotingLicenceCatalogue? other) =>
        other is not null &&
        Version == other.Version &&
        Plans.SequenceEqual(other.Plans) &&
        ProfileCompatibility.SequenceEqual(other.ProfileCompatibility);

    public override bool Equals(object? obj) => Equals(obj as HushVotingLicenceCatalogue);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Version);
        foreach (var plan in Plans)
        {
            hash.Add(plan);
        }

        return hash.ToHashCode();
    }
}
