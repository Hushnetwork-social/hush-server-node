namespace HushShared.HushVoting.Licensing.Model;

/// <summary>
/// One internal server mapping from a governance option + binding status to the exact runtime
/// ceremony profile ID. These records couple licence governance to election ceremony profiles and
/// are owned by the host adapter; the pure licensing model only declares the mapping shape and the
/// accepted v1 entries.
/// </summary>
public sealed record HushVotingProfileCompatibilityEntry(
    HushVotingGovernanceOptionId GovernanceOptionId,
    HushVotingBindingStatus BindingStatus,
    string RuntimeProfileId,
    bool DevOnly)
{
    public static HushVotingProfileCompatibilityEntry Create(
        HushVotingGovernanceOptionId governanceOptionId,
        HushVotingBindingStatus bindingStatus,
        string runtimeProfileId)
    {
        ArgumentNullException.ThrowIfNull(governanceOptionId);
        if (string.IsNullOrWhiteSpace(runtimeProfileId))
        {
            throw new ArgumentException("Runtime profile id is required.", nameof(runtimeProfileId));
        }

        var devOnly = bindingStatus == HushVotingBindingStatus.NonBinding;
        return new HushVotingProfileCompatibilityEntry(
            governanceOptionId,
            bindingStatus,
            runtimeProfileId,
            devOnly);
    }
}

/// <summary>
/// The exact v1 governance-option to ceremony-profile compatibility map. Zero customer trustees
/// maps to the internal admin circuit (1of1); fixed trustee schemes map to the DKG dev/prod
/// profiles. Enterprise has no executable mapping in v1.
/// </summary>
public static class HushVotingProfileCompatibilityV1
{
    public static readonly HushVotingProfileCompatibilityEntry NoCustomerTrusteesNonBinding = HushVotingProfileCompatibilityEntry.Create(
        HushVotingGovernanceOptionId.NoCustomerTrustees,
        HushVotingBindingStatus.NonBinding,
        "admin-dev-1of1");

    public static readonly HushVotingProfileCompatibilityEntry NoCustomerTrusteesBinding = HushVotingProfileCompatibilityEntry.Create(
        HushVotingGovernanceOptionId.NoCustomerTrustees,
        HushVotingBindingStatus.Binding,
        "admin-prod-1of1");

    public static readonly HushVotingProfileCompatibilityEntry Trustees3Of5NonBinding = HushVotingProfileCompatibilityEntry.Create(
        HushVotingGovernanceOptionId.Trustees3Of5,
        HushVotingBindingStatus.NonBinding,
        "dkg-dev-3of5");

    public static readonly HushVotingProfileCompatibilityEntry Trustees3Of5Binding = HushVotingProfileCompatibilityEntry.Create(
        HushVotingGovernanceOptionId.Trustees3Of5,
        HushVotingBindingStatus.Binding,
        "dkg-prod-3of5");

    public static readonly HushVotingProfileCompatibilityEntry Trustees7Of10NonBinding = HushVotingProfileCompatibilityEntry.Create(
        HushVotingGovernanceOptionId.Trustees7Of10,
        HushVotingBindingStatus.NonBinding,
        "dkg-dev-7of10");

    public static readonly HushVotingProfileCompatibilityEntry Trustees7Of10Binding = HushVotingProfileCompatibilityEntry.Create(
        HushVotingGovernanceOptionId.Trustees7Of10,
        HushVotingBindingStatus.Binding,
        "dkg-prod-7of10");

    public static readonly HushVotingProfileCompatibilityEntry Trustees8Of13NonBinding = HushVotingProfileCompatibilityEntry.Create(
        HushVotingGovernanceOptionId.Trustees8Of13,
        HushVotingBindingStatus.NonBinding,
        "dkg-dev-8of13");

    public static readonly HushVotingProfileCompatibilityEntry Trustees8Of13Binding = HushVotingProfileCompatibilityEntry.Create(
        HushVotingGovernanceOptionId.Trustees8Of13,
        HushVotingBindingStatus.Binding,
        "dkg-prod-8of13");

    /// <summary>All eight accepted v1 internal mappings (4 options x 2 binding modes).</summary>
    public static readonly IReadOnlyList<HushVotingProfileCompatibilityEntry> Entries =
    [
        NoCustomerTrusteesNonBinding,
        NoCustomerTrusteesBinding,
        Trustees3Of5NonBinding,
        Trustees3Of5Binding,
        Trustees7Of10NonBinding,
        Trustees7Of10Binding,
        Trustees8Of13NonBinding,
        Trustees8Of13Binding,
    ];

    /// <summary>Resolves the exact runtime profile for an accepted option + binding status.</summary>
    public static HushVotingProfileCompatibilityEntry? Resolve(
        HushVotingGovernanceOptionId optionId,
        HushVotingBindingStatus bindingStatus)
    {
        ArgumentNullException.ThrowIfNull(optionId);

        foreach (var entry in Entries)
        {
            if (entry.GovernanceOptionId == optionId && entry.BindingStatus == bindingStatus)
            {
                return entry;
            }
        }

        return null;
    }
}
