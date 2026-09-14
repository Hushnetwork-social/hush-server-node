namespace HushShared.HushVoting.Licensing.Model;

/// <summary>
/// Client-safe projection of one plan for later authenticated APIs. Contains only approved product
/// fields; never internal profile ids, provider keys, database ids, paths, digests, prices, payment
/// fields, internal notes, readiness/legal claims, identities, or history.
/// </summary>
public sealed class HushVotingLicencePlanProjection : IEquatable<HushVotingLicencePlanProjection>
{
    public HushVotingLicencePlanProjection(
        HushVotingLicencePlanId planId,
        HushVotingLicenceFamily family,
        string displayName,
        string safeDescription,
        int displayOrder,
        int? eligibleVoterCap,
        bool unlimitedElections,
        HushVotingLicenceTerm term,
        HushVotingLicenceAvailability availability,
        string? unavailableSafeReason,
        IReadOnlyList<HushVotingGovernanceOptionProjection> governanceOptions,
        HushVotingLicenceCatalogueVersion catalogueVersion)
    {
        ArgumentNullException.ThrowIfNull(planId);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(safeDescription);
        ArgumentNullException.ThrowIfNull(governanceOptions);
        ArgumentNullException.ThrowIfNull(catalogueVersion);

        PlanId = planId;
        Family = family;
        DisplayName = displayName;
        SafeDescription = safeDescription;
        DisplayOrder = displayOrder;
        EligibleVoterCap = eligibleVoterCap;
        UnlimitedElections = unlimitedElections;
        Term = term;
        Availability = availability;
        UnavailableSafeReason = unavailableSafeReason;
        GovernanceOptions = governanceOptions;
        CatalogueVersion = catalogueVersion;
    }

    public HushVotingLicencePlanId PlanId { get; }

    public HushVotingLicenceFamily Family { get; }

    public string DisplayName { get; }

    public string SafeDescription { get; }

    public int DisplayOrder { get; }

    public int? EligibleVoterCap { get; }

    public bool UnlimitedElections { get; }

    public HushVotingLicenceTerm Term { get; }

    public HushVotingLicenceAvailability Availability { get; }

    public string? UnavailableSafeReason { get; }

    public IReadOnlyList<HushVotingGovernanceOptionProjection> GovernanceOptions { get; }

    public HushVotingLicenceCatalogueVersion CatalogueVersion { get; }

    public bool Equals(HushVotingLicencePlanProjection? other) =>
        other is not null &&
        PlanId == other.PlanId &&
        Family == other.Family &&
        string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
        string.Equals(SafeDescription, other.SafeDescription, StringComparison.Ordinal) &&
        DisplayOrder == other.DisplayOrder &&
        EligibleVoterCap == other.EligibleVoterCap &&
        UnlimitedElections == other.UnlimitedElections &&
        Term == other.Term &&
        Availability == other.Availability &&
        string.Equals(UnavailableSafeReason, other.UnavailableSafeReason, StringComparison.Ordinal) &&
        CatalogueVersion == other.CatalogueVersion &&
        GovernanceOptions.SequenceEqual(other.GovernanceOptions);

    public override bool Equals(object? obj) => Equals(obj as HushVotingLicencePlanProjection);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PlanId);
        hash.Add(Family);
        hash.Add(DisplayName, StringComparer.Ordinal);
        hash.Add(SafeDescription, StringComparer.Ordinal);
        hash.Add(DisplayOrder);
        hash.Add(EligibleVoterCap);
        hash.Add(UnlimitedElections);
        hash.Add(Term);
        hash.Add(Availability);
        hash.Add(UnavailableSafeReason, StringComparer.Ordinal);
        hash.Add(CatalogueVersion);
        foreach (var option in GovernanceOptions)
        {
            hash.Add(option);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Client-safe governance option projection (customer-facing only).</summary>
public sealed record HushVotingGovernanceOptionProjection(
    HushVotingGovernanceOptionId Id,
    int CustomerTrusteeCount,
    int RequiredApprovalCount,
    string SafeLabel,
    IReadOnlySet<HushVotingBindingStatus> SupportedBindingStatuses)
{
    public static HushVotingGovernanceOptionProjection From(HushVotingGovernanceOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return new HushVotingGovernanceOptionProjection(
            option.Id,
            option.CustomerTrusteeCount,
            option.RequiredApprovalCount,
            option.SafeLabel,
            option.SupportedBindingStatuses);
    }
}
