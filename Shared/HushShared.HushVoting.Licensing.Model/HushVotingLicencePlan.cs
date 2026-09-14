namespace HushShared.HushVoting.Licensing.Model;

/// <summary>Optional replacement/retirement metadata for a plan in a catalogue snapshot.</summary>
public sealed record HushVotingLicencePlanRetirement(
    string? ReplacementPlanId,
    string? SafeReason)
{
    public static HushVotingLicencePlanRetirement None { get; } = new(null, null);

    public bool IsRetired => ReplacementPlanId is not null || SafeReason is not null;
}

/// <summary>
/// Immutable v1 plan aggregate. Construction is validated: a plan cannot escape with an unknown id,
/// negative cap/rank/order, an empty display name, a forbidden family/availability combination, or a
/// null governance list. Exact v1 semantic policy is enforced by the catalogue validator, not here.
/// </summary>
public sealed class HushVotingLicencePlan : IEquatable<HushVotingLicencePlan>
{
    public HushVotingLicencePlan(
        HushVotingLicencePlanId id,
        HushVotingLicenceFamily family,
        string displayName,
        string safeDescription,
        int displayOrder,
        int upgradeRank,
        int? eligibleVoterCap,
        bool unlimitedElections,
        HushVotingLicenceTerm term,
        HushVotingLicenceAvailability availability,
        string? unavailableSafeReason,
        IReadOnlyList<HushVotingGovernanceOption> governanceOptions,
        HushVotingLicenceCatalogueVersion catalogueVersion,
        HushVotingLicencePlanRetirement? retirement = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(safeDescription);
        ArgumentNullException.ThrowIfNull(governanceOptions);
        ArgumentNullException.ThrowIfNull(catalogueVersion);

        if (!id.IsKnown)
        {
            throw new ArgumentException("A plan id must be a known closed value.", nameof(id));
        }

        if (!catalogueVersion.IsKnown)
        {
            throw new ArgumentException(
                "A plan catalogue version must be a known closed value.",
                nameof(catalogueVersion));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        if (displayOrder < 0 || upgradeRank < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(displayOrder),
                "Display order and upgrade rank must be non-negative.");
        }

        if (eligibleVoterCap is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eligibleVoterCap), "Cap must be non-negative or null.");
        }

        Id = id;
        Family = family;
        DisplayName = displayName;
        SafeDescription = safeDescription;
        DisplayOrder = displayOrder;
        UpgradeRank = upgradeRank;
        EligibleVoterCap = eligibleVoterCap;
        UnlimitedElections = unlimitedElections;
        Term = term;
        Availability = availability;
        UnavailableSafeReason = unavailableSafeReason;
        GovernanceOptions = governanceOptions;
        CatalogueVersion = catalogueVersion;
        Retirement = retirement ?? HushVotingLicencePlanRetirement.None;
    }

    public HushVotingLicencePlanId Id { get; }

    public HushVotingLicenceFamily Family { get; }

    public string DisplayName { get; }

    public string SafeDescription { get; }

    public int DisplayOrder { get; }

    public int UpgradeRank { get; }

    /// <summary>Eligible-voter cap per election, or null when customer-specific/not configured.</summary>
    public int? EligibleVoterCap { get; }

    public bool UnlimitedElections { get; }

    public HushVotingLicenceTerm Term { get; }

    public HushVotingLicenceAvailability Availability { get; }

    /// <summary>Safe non-sensitive unavailable reason (only meaningful when Availability == Unavailable).</summary>
    public string? UnavailableSafeReason { get; }

    /// <summary>Immutable governance options allowed for this plan.</summary>
    public IReadOnlyList<HushVotingGovernanceOption> GovernanceOptions { get; }

    public HushVotingLicenceCatalogueVersion CatalogueVersion { get; }

    public HushVotingLicencePlanRetirement Retirement { get; }

    public bool HasGovernanceOption(HushVotingGovernanceOptionId optionId)
    {
        ArgumentNullException.ThrowIfNull(optionId);
        return GovernanceOptions.Any(option => option.Id == optionId);
    }

    public HushVotingGovernanceOption? GetGovernanceOption(HushVotingGovernanceOptionId optionId)
    {
        ArgumentNullException.ThrowIfNull(optionId);
        return GovernanceOptions.FirstOrDefault(option => option.Id == optionId);
    }

    public bool Equals(HushVotingLicencePlan? other) =>
        other is not null &&
        Id == other.Id &&
        Family == other.Family &&
        string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
        string.Equals(SafeDescription, other.SafeDescription, StringComparison.Ordinal) &&
        DisplayOrder == other.DisplayOrder &&
        UpgradeRank == other.UpgradeRank &&
        EligibleVoterCap == other.EligibleVoterCap &&
        UnlimitedElections == other.UnlimitedElections &&
        Term == other.Term &&
        Availability == other.Availability &&
        string.Equals(UnavailableSafeReason, other.UnavailableSafeReason, StringComparison.Ordinal) &&
        CatalogueVersion == other.CatalogueVersion &&
        Retirement == other.Retirement &&
        GovernanceOptions.SequenceEqual(other.GovernanceOptions);

    public override bool Equals(object? obj) => Equals(obj as HushVotingLicencePlan);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Family);
        hash.Add(DisplayName, StringComparer.Ordinal);
        hash.Add(SafeDescription, StringComparer.Ordinal);
        hash.Add(DisplayOrder);
        hash.Add(UpgradeRank);
        hash.Add(EligibleVoterCap);
        hash.Add(UnlimitedElections);
        hash.Add(Term);
        hash.Add(Availability);
        hash.Add(UnavailableSafeReason, StringComparer.Ordinal);
        hash.Add(CatalogueVersion);
        hash.Add(Retirement);
        foreach (var option in GovernanceOptions)
        {
            hash.Add(option);
        }

        return hash.ToHashCode();
    }
}
