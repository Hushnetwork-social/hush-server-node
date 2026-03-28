using System.Text.Json;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public static class ElectionCeremonyProfileCatalog
{
    public const string ExpectedVersion = "omega-v1.0.0";
    public const string DevProfileId = "dkg-dev-3of5";
    public const string ProductionProfileId = "dkg-prod-3of5";
    public const int InitialTrusteeCount = 5;
    public const int InitialRequiredApprovalCount = 3;

    public static string GetDefaultRegistryRelativePath() =>
        Path.Combine("ceremony-profiles", ExpectedVersion, "approved-ceremony-profiles.json");

    public static string ResolveApprovedRegistryPath(ElectionCeremonyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuredPath = string.IsNullOrWhiteSpace(options.ApprovedRegistryRelativePath)
            ? GetDefaultRegistryRelativePath()
            : options.ApprovedRegistryRelativePath.Trim();

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);
    }

    public static ElectionCeremonyProfileCatalogManifest LoadManifest(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"Ceremony profile catalog manifest was not found: {manifestPath}");
        }

        var manifest = JsonSerializer.Deserialize<ElectionCeremonyProfileCatalogManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (manifest is null)
        {
            throw new InvalidOperationException($"Ceremony profile catalog manifest could not be parsed: {manifestPath}");
        }

        ValidateManifest(manifest, manifestPath);
        return manifest;
    }

    public static IReadOnlyList<ElectionCeremonyProfileRecord> BuildRecords(
        ElectionCeremonyProfileCatalogManifest manifest,
        DateTime? registeredAt = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var timestamp = registeredAt ?? DateTime.UtcNow;
        return manifest.Profiles
            .Select(profile => ElectionModelFactory.CreateCeremonyProfile(
                profile.ProfileId,
                profile.DisplayName,
                profile.Description,
                profile.ProviderKey,
                profile.ProfileVersion,
                profile.TrusteeCount,
                profile.RequiredApprovalCount,
                profile.DevOnly,
                timestamp))
            .ToArray();
    }

    private static void ValidateManifest(ElectionCeremonyProfileCatalogManifest manifest, string manifestPath)
    {
        if (!string.Equals(manifest.Version, ExpectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Ceremony profile catalog manifest '{manifestPath}' has unexpected version '{manifest.Version}'.");
        }

        if (manifest.Profiles is null || manifest.Profiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"Ceremony profile catalog manifest '{manifestPath}' contains no profiles.");
        }

        var duplicateProfileId = manifest.Profiles
            .GroupBy(x => x.ProfileId, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateProfileId is not null)
        {
            throw new InvalidOperationException(
                $"Ceremony profile catalog manifest '{manifestPath}' contains duplicate profile id '{duplicateProfileId.Key}'.");
        }

        ValidateRolloutPair(manifest, manifestPath);
    }

    private static void ValidateRolloutPair(ElectionCeremonyProfileCatalogManifest manifest, string manifestPath)
    {
        var devProfile = manifest.Profiles.FirstOrDefault(x => string.Equals(x.ProfileId, DevProfileId, StringComparison.Ordinal));
        if (devProfile is null)
        {
            throw new InvalidOperationException(
                $"Ceremony profile catalog manifest '{manifestPath}' does not include the required dev profile '{DevProfileId}'.");
        }

        var productionProfile = manifest.Profiles.FirstOrDefault(x => string.Equals(x.ProfileId, ProductionProfileId, StringComparison.Ordinal));
        if (productionProfile is null)
        {
            throw new InvalidOperationException(
                $"Ceremony profile catalog manifest '{manifestPath}' does not include the required production-like profile '{ProductionProfileId}'.");
        }

        ValidateRolloutShape(devProfile, expectedDevOnly: true, manifestPath);
        ValidateRolloutShape(productionProfile, expectedDevOnly: false, manifestPath);
    }

    private static void ValidateRolloutShape(
        ElectionCeremonyProfileCatalogEntry profile,
        bool expectedDevOnly,
        string manifestPath)
    {
        if (profile.TrusteeCount != InitialTrusteeCount || profile.RequiredApprovalCount != InitialRequiredApprovalCount)
        {
            throw new InvalidOperationException(
                $"Ceremony profile '{profile.ProfileId}' in '{manifestPath}' must describe the shipped {InitialRequiredApprovalCount}-of-{InitialTrusteeCount} rollout.");
        }

        if (profile.DevOnly != expectedDevOnly)
        {
            throw new InvalidOperationException(
                $"Ceremony profile '{profile.ProfileId}' in '{manifestPath}' has unexpected dev-only flag '{profile.DevOnly}'.");
        }
    }
}

public sealed record ElectionCeremonyProfileCatalogManifest(
    string Version,
    string GeneratedBy,
    IReadOnlyList<ElectionCeremonyProfileCatalogEntry> Profiles);

public sealed record ElectionCeremonyProfileCatalogEntry(
    string ProfileId,
    string DisplayName,
    string Description,
    string ProviderKey,
    string ProfileVersion,
    int TrusteeCount,
    int RequiredApprovalCount,
    bool DevOnly);
