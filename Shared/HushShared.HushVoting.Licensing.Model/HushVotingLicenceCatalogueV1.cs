namespace HushShared.HushVoting.Licensing.Model;

/// <summary>
/// Exact accepted v1 catalogue factory. This is the single canonical in-code mirror of the release
/// manifest for tests and fixture generation. The release-controlled JSON is the authoritative
/// runtime source; this factory exists so domain tests and the loader share one exact model shape.
/// </summary>
public static class HushVotingLicenceCatalogueV1
{
    public const string DisplayNameDirectFree = "HushVoting! Direct Free";
    public const string DisplayNameVeritas500 = "HushVoting! Veritas 500";
    public const string DisplayNameVeritas2000 = "HushVoting! Veritas 2k";
    public const string DisplayNameVeritas10000 = "HushVoting! Veritas 10k";
    public const string DisplayNameEnterprise = "HushVoting! Enterprise";

    public const string SafeDescriptionDirectFree =
        "Admin-controlled elections for up to 100 eligible voters, with no customer trustees.";
    public const string SafeDescriptionVeritas500 =
        "Elections for up to 500 eligible voters, with no trustees or a fixed 3-of-5 trustee ceremony.";
    public const string SafeDescriptionVeritas2000 =
        "Elections for up to 2,000 eligible voters, with no trustees or a fixed 3-of-5 or 7-of-10 trustee ceremony.";
    public const string SafeDescriptionVeritas10000 =
        "Elections for up to 10,000 eligible voters, with no trustees or a fixed 3-of-5, 7-of-10, or 8-of-13 trustee ceremony.";
    public const string SafeDescriptionEnterprise =
        "Customer-specific voter and trustee configuration. Contact provider - not yet available.";

    public const string EnterpriseUnavailableReason =
        "Customer-specific configuration is not yet available in v1.";

    private static readonly HushVotingLicenceGovernanceOptions Governance = new();

    private static HushVotingLicencePlan Plan(
        string idValue,
        HushVotingLicenceFamily family,
        string displayName,
        string safeDescription,
        int displayOrder,
        int upgradeRank,
        int? cap,
        HushVotingLicenceTerm term,
        HushVotingLicenceAvailability availability,
        string? unavailableReason,
        IReadOnlyList<HushVotingGovernanceOption> options) =>
        new(
            HushVotingLicencePlanId.FromExternal(idValue),
            family,
            displayName,
            safeDescription,
            displayOrder,
            upgradeRank,
            cap,
            unlimitedElections: true,
            term,
            availability,
            unavailableReason,
            options,
            HushVotingLicenceCatalogueVersion.V1);

    public static IReadOnlyList<HushVotingLicencePlan> CreatePlans() =>
    [
        Plan(
            HushVotingLicencePlanId.DirectFreeValue,
            HushVotingLicenceFamily.Direct,
            DisplayNameDirectFree,
            SafeDescriptionDirectFree,
            displayOrder: 10,
            upgradeRank: 0,
            cap: 100,
            HushVotingLicenceTerm.Perpetual,
            HushVotingLicenceAvailability.Default,
            unavailableReason: null,
            Governance.DirectFree),

        Plan(
            HushVotingLicencePlanId.Veritas500Value,
            HushVotingLicenceFamily.Veritas,
            DisplayNameVeritas500,
            SafeDescriptionVeritas500,
            displayOrder: 20,
            upgradeRank: 1000,
            cap: 500,
            HushVotingLicenceTerm.OneCalendarYear,
            HushVotingLicenceAvailability.AutomaticUpgrade,
            unavailableReason: null,
            Governance.Veritas500),

        Plan(
            HushVotingLicencePlanId.Veritas2000Value,
            HushVotingLicenceFamily.Veritas,
            DisplayNameVeritas2000,
            SafeDescriptionVeritas2000,
            displayOrder: 30,
            upgradeRank: 2000,
            cap: 2000,
            HushVotingLicenceTerm.OneCalendarYear,
            HushVotingLicenceAvailability.AutomaticUpgrade,
            unavailableReason: null,
            Governance.Veritas2000),

        Plan(
            HushVotingLicencePlanId.Veritas10000Value,
            HushVotingLicenceFamily.Veritas,
            DisplayNameVeritas10000,
            SafeDescriptionVeritas10000,
            displayOrder: 40,
            upgradeRank: 3000,
            cap: 10000,
            HushVotingLicenceTerm.OneCalendarYear,
            HushVotingLicenceAvailability.AutomaticUpgrade,
            unavailableReason: null,
            Governance.Veritas10000),

        Plan(
            HushVotingLicencePlanId.EnterpriseValue,
            HushVotingLicenceFamily.Enterprise,
            DisplayNameEnterprise,
            SafeDescriptionEnterprise,
            displayOrder: 50,
            upgradeRank: 4000,
            cap: null,
            HushVotingLicenceTerm.OneCalendarYear,
            HushVotingLicenceAvailability.Unavailable,
            unavailableReason: EnterpriseUnavailableReason,
            Governance.Enterprise),
    ];

    /// <summary>Builds the canonical immutable v1 catalogue snapshot.</summary>
    public static HushVotingLicenceCatalogue CreateCatalogue() =>
        new(
            HushVotingLicenceCatalogueVersion.V1,
            CreatePlans(),
            HushVotingProfileCompatibilityV1.Entries);
}

/// <summary>Exact per-plan governance option sets for v1 (cumulative for Veritas).</summary>
public sealed class HushVotingLicenceGovernanceOptions
{
    private static readonly IReadOnlySet<HushVotingBindingStatus> BothModes =
        new HashSet<HushVotingBindingStatus>(
        [
            HushVotingBindingStatus.NonBinding,
            HushVotingBindingStatus.Binding,
        ]);

    private static HushVotingGovernanceOption NoCustomerTrustees() =>
        new(
            HushVotingGovernanceOptionId.NoCustomerTrustees,
            customerTrusteeCount: 0,
            requiredApprovalCount: 0,
            safeLabel: "No customer trustees",
            BothModes);

    private static HushVotingGovernanceOption Trustees3Of5() =>
        new(
            HushVotingGovernanceOptionId.Trustees3Of5,
            customerTrusteeCount: 5,
            requiredApprovalCount: 3,
            safeLabel: "3 of 5 trustees",
            BothModes);

    private static HushVotingGovernanceOption Trustees7Of10() =>
        new(
            HushVotingGovernanceOptionId.Trustees7Of10,
            customerTrusteeCount: 10,
            requiredApprovalCount: 7,
            safeLabel: "7 of 10 trustees",
            BothModes);

    private static HushVotingGovernanceOption Trustees8Of13() =>
        new(
            HushVotingGovernanceOptionId.Trustees8Of13,
            customerTrusteeCount: 13,
            requiredApprovalCount: 8,
            safeLabel: "8 of 13 trustees",
            BothModes);

    public IReadOnlyList<HushVotingGovernanceOption> DirectFree => [NoCustomerTrustees()];

    public IReadOnlyList<HushVotingGovernanceOption> Veritas500 =>
        [NoCustomerTrustees(), Trustees3Of5()];

    public IReadOnlyList<HushVotingGovernanceOption> Veritas2000 =>
        [NoCustomerTrustees(), Trustees3Of5(), Trustees7Of10()];

    public IReadOnlyList<HushVotingGovernanceOption> Veritas10000 =>
        [NoCustomerTrustees(), Trustees3Of5(), Trustees7Of10(), Trustees8Of13()];

    /// <summary>Enterprise has no executable governance options in v1.</summary>
    public IReadOnlyList<HushVotingGovernanceOption> Enterprise =>
        Array.Empty<HushVotingGovernanceOption>();
}
