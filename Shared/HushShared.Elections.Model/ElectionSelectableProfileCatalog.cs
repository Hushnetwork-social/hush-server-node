namespace HushShared.Elections.Model;

public static class ElectionSelectableProfileCatalog
{
    public const string TrusteeProductionProfileId = "dkg-prod-3of5";
    public const string TrusteeDevProfileId = "dkg-dev-3of5";
    public const string TrusteeVeritas2KProductionProfileId = "dkg-prod-7of10";
    public const string TrusteeVeritas2KDevProfileId = "dkg-dev-7of10";
    public const string TrusteeVeritas10KProductionProfileId = "dkg-prod-8of13";
    public const string TrusteeVeritas10KDevProfileId = "dkg-dev-8of13";
    public const string TrusteeEnterpriseProfileIdPrefix = "dkg-enterprise-";
    public const string AdminOnlyProductionProfileId = "admin-prod-1of1";
    public const string AdminOnlyDevProfileId = "admin-dev-1of1";

    private static readonly DateTime BuiltInRegisteredAt = new(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc);

    public static string GetDefaultProfileId(
        ElectionGovernanceMode governanceMode,
        ElectionBindingStatus bindingStatus) =>
        governanceMode switch
        {
            ElectionGovernanceMode.AdminOnly => bindingStatus == ElectionBindingStatus.NonBinding
                ? AdminOnlyDevProfileId
                : AdminOnlyProductionProfileId,
            _ => bindingStatus == ElectionBindingStatus.NonBinding
                ? TrusteeDevProfileId
                : TrusteeProductionProfileId,
        };

    public static string NormalizeProfileId(
        ElectionGovernanceMode governanceMode,
        string? profileId)
    {
        var normalizedProfileId = profileId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedProfileId))
        {
            return string.Empty;
        }

        if (governanceMode != ElectionGovernanceMode.AdminOnly)
        {
            return normalizedProfileId;
        }

        return normalizedProfileId switch
        {
            TrusteeProductionProfileId => AdminOnlyProductionProfileId,
            TrusteeDevProfileId => AdminOnlyDevProfileId,
            _ => normalizedProfileId,
        };
    }

    public static bool ResolveDevOnlyFlag(
        ElectionGovernanceMode governanceMode,
        string? profileId,
        bool fallbackDevOnly)
    {
        var normalizedProfileId = NormalizeProfileId(governanceMode, profileId);
        return normalizedProfileId switch
        {
            TrusteeDevProfileId => true,
            TrusteeProductionProfileId => false,
            TrusteeVeritas2KDevProfileId => true,
            TrusteeVeritas2KProductionProfileId => false,
            TrusteeVeritas10KDevProfileId => true,
            TrusteeVeritas10KProductionProfileId => false,
            AdminOnlyDevProfileId => true,
            AdminOnlyProductionProfileId => false,
            _ => fallbackDevOnly,
        };
    }

    public static IReadOnlyList<ElectionCeremonyProfileRecord> GetSelectableProfiles(
        ElectionGovernanceMode governanceMode,
        IReadOnlyList<ElectionCeremonyProfileRecord> registryProfiles,
        bool includeDevProfiles = true)
    {
        var sourceProfiles = governanceMode == ElectionGovernanceMode.AdminOnly
            ? BuildAdminOnlyProfiles()
            : registryProfiles ?? Array.Empty<ElectionCeremonyProfileRecord>();

        return sourceProfiles
            .Where(profile => includeDevProfiles || !profile.DevOnly)
            .OrderBy(profile => profile.DevOnly)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static ElectionCeremonyProfileRecord? ResolveProfile(
        ElectionGovernanceMode governanceMode,
        string? profileId,
        IReadOnlyList<ElectionCeremonyProfileRecord> registryProfiles)
    {
        var normalizedProfileId = NormalizeProfileId(governanceMode, profileId);
        if (string.IsNullOrWhiteSpace(normalizedProfileId))
        {
            return null;
        }

        var registeredProfile = GetSelectableProfiles(governanceMode, registryProfiles, includeDevProfiles: true)
            .FirstOrDefault(profile => string.Equals(
                profile.ProfileId,
                normalizedProfileId,
                StringComparison.Ordinal));

        return registeredProfile ??
            (governanceMode == ElectionGovernanceMode.AdminOnly
                ? null
                : TryBuildEnterpriseProfile(normalizedProfileId));
    }

    public static string BuildEnterpriseProfileId(int requiredApprovalCount, int trusteeCount)
    {
        if (requiredApprovalCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredApprovalCount), "Required approval count must be at least 1.");
        }

        if (trusteeCount <= requiredApprovalCount)
        {
            throw new ArgumentOutOfRangeException(nameof(trusteeCount), "Trustee count must be greater than required approval count.");
        }

        return $"{TrusteeEnterpriseProfileIdPrefix}{requiredApprovalCount}of{trusteeCount}";
    }

    public static bool TryParseEnterpriseProfileId(
        string? profileId,
        out int requiredApprovalCount,
        out int trusteeCount)
    {
        requiredApprovalCount = 0;
        trusteeCount = 0;

        var normalizedProfileId = profileId?.Trim() ?? string.Empty;
        if (!normalizedProfileId.StartsWith(TrusteeEnterpriseProfileIdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var thresholdText = normalizedProfileId[TrusteeEnterpriseProfileIdPrefix.Length..];
        var parts = thresholdText.Split("of", StringSplitOptions.None);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out requiredApprovalCount) ||
            !int.TryParse(parts[1], out trusteeCount) ||
            requiredApprovalCount < 1 ||
            trusteeCount <= requiredApprovalCount)
        {
            requiredApprovalCount = 0;
            trusteeCount = 0;
            return false;
        }

        return true;
    }

    public static ElectionCeremonyProfileRecord? TryBuildEnterpriseProfile(string? profileId)
    {
        if (!TryParseEnterpriseProfileId(profileId, out var requiredApprovalCount, out var trusteeCount))
        {
            return null;
        }

        return new ElectionCeremonyProfileRecord(
            BuildEnterpriseProfileId(requiredApprovalCount, trusteeCount),
            "HushVoting! Enterprise",
            "Custom Enterprise trustee-governed profile with organization-defined approval and trustee counts.",
            "hush-enterprise-custom",
            $"omega-v1.0.0-enterprise-{requiredApprovalCount}of{trusteeCount}",
            trusteeCount,
            requiredApprovalCount,
            DevOnly: false,
            RegisteredAt: BuiltInRegisteredAt,
            LastUpdatedAt: BuiltInRegisteredAt);
    }

    public static ElectionCeremonyProfileRecord[] BuildAdminOnlyProfiles() =>
    [
        new ElectionCeremonyProfileRecord(
            AdminOnlyProductionProfileId,
            "Admin-only protected circuit",
            "Built-in protected circuit for admin-only elections with aggregate-only protected tally custody.",
            "built-in-admin",
            "omega-v1.0.0-admin-prod-1of1",
            TrusteeCount: 1,
            RequiredApprovalCount: 1,
            DevOnly: false,
            RegisteredAt: BuiltInRegisteredAt,
            LastUpdatedAt: BuiltInRegisteredAt),
        new ElectionCeremonyProfileRecord(
            AdminOnlyDevProfileId,
            "Admin-only open audit circuit",
            "Built-in dev/open circuit for explicit non-binding admin-only audit elections with readable ballots.",
            "built-in-admin",
            "omega-v1.0.0-admin-dev-1of1",
            TrusteeCount: 1,
            RequiredApprovalCount: 1,
            DevOnly: true,
            RegisteredAt: BuiltInRegisteredAt,
            LastUpdatedAt: BuiltInRegisteredAt),
    ];
}
