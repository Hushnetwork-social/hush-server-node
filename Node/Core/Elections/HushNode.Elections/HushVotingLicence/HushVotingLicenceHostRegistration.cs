using HushShared.Elections.Model;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HushNode.Elections.HushVotingLicence;

/// <summary>Immutable snapshot published once after full catalogue validation succeeds.</summary>
public sealed class HushVotingLicenceSnapshot
{
    public HushVotingLicenceSnapshot(HushVotingLicenceCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        Catalogue = catalogue;
        PublishedAtUtc = DateTime.UtcNow;
    }

    public HushVotingLicenceCatalogue Catalogue { get; }

    public DateTime PublishedAtUtc { get; }

    /// <summary>Client-safe projections for later authenticated APIs (deterministic display order).</summary>
    public IReadOnlyList<HushVotingLicencePlanProjection> SafeProjections =>
        HushVotingLicencePlanProjectionMapper.ProjectAll(Catalogue);
}

/// <summary>
/// Ceremony-registry cross-validation adapter. This is the ONLY host-side layer that couples licence
/// profile mappings to the approved election ceremony-profile registry records. It validates that
/// each required admin/DKG runtime profile exists exactly once with matching devOnly semantics,
/// trustee count, and threshold, and that Enterprise has no executable v1 mapping.
/// </summary>
public static class HushVotingLicenceProfileRegistryValidator
{
    public static HushVotingLicenceCatalogueValidationResult ValidateAgainstRegistry(
        HushVotingLicenceCatalogue catalogue,
        IReadOnlyList<ElectionCeremonyProfileRecord> registryProfiles)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(registryProfiles);

        var failures = new List<HushVotingLicenceValidationFailure>();
        var registryById = registryProfiles
            .GroupBy(static p => p.ProfileId, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.ToArray(), StringComparer.Ordinal);

        foreach (var entry in catalogue.ProfileCompatibility)
        {
            var path = $"/profileCompatibility/{entry.RuntimeProfileId}";
            if (!registryById.TryGetValue(entry.RuntimeProfileId, out var matches))
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatProfileMissing,
                    path,
                    $"Required runtime profile '{entry.RuntimeProfileId}' is absent from the approved ceremony-profile registry."));
                continue;
            }

            if (matches.Length != 1)
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatProfileMismatch,
                    path,
                    $"Runtime profile '{entry.RuntimeProfileId}' must exist exactly once in the registry."));
                continue;
            }

            var profile = matches[0];
            if (profile.DevOnly != entry.DevOnly)
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatProfileMismatch,
                    path,
                    $"Profile '{entry.RuntimeProfileId}' devOnly flag does not match binding status."));
            }

            var customer = FindOption(catalogue, entry);
            if (customer is not null &&
                (profile.TrusteeCount != customer.CustomerTrusteeCount ||
                 profile.RequiredApprovalCount != customer.RequiredApprovalCount))
            {
                // Zero-customer-trustee maps to the internal admin circuit (1of1); DKG schemes must
                // match the customer-visible trustee/threshold metadata exactly.
                if (entry.GovernanceOptionId != HushVotingGovernanceOptionId.NoCustomerTrustees)
                {
                    failures.Add(new HushVotingLicenceValidationFailure(
                        HushVotingLicenceValidationCodes.LicCatProfileMismatch,
                        path,
                        $"Profile '{entry.RuntimeProfileId}' trustee/threshold does not match the plan option."));
                }
            }
        }

        // Enterprise must have no executable v1 mapping.
        var enterprise = catalogue.FindPlan(HushVotingLicencePlanId.Enterprise);
        if (enterprise is { GovernanceOptions.Count: > 0 })
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatGovernanceInvalid,
                "/plans/hushvoting.enterprise/governanceOptions",
                "Enterprise must have no executable governance mapping in v1."));
        }

        return HushVotingLicenceCatalogueValidationResult.FromFailures(failures);
    }

    private static HushVotingGovernanceOption? FindOption(
        HushVotingLicenceCatalogue catalogue,
        HushVotingProfileCompatibilityEntry entry)
    {
        foreach (var plan in catalogue.Plans)
        {
            var option = plan.GetGovernanceOption(entry.GovernanceOptionId);
            if (option is not null)
            {
                return option;
            }
        }

        return null;
    }
}

/// <summary>
/// Host registration for the HushVoting licence snapshot: options + immutable snapshot singleton.
/// Registration itself never loads files; the startup bootstrapper performs the load and fails
/// readiness on any error (no fallback).
/// </summary>
public static class HushVotingLicenceHostBuild
{
    public static IServiceCollection AddHushVotingLicenceCatalogue(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<HushVotingLicenceOptions>()
            .Bind(configuration.GetSection(HushVotingLicenceOptions.SectionName))
            .Validate(options =>
                !string.IsNullOrWhiteSpace(options.CatalogueRelativePath) &&
                !string.IsNullOrWhiteSpace(options.RequiredCatalogueVersion),
                "HushVotingLicences requires CatalogueRelativePath and RequiredCatalogueVersion.");

        services.AddSingleton<HushVotingLicenceCatalogueBootstrapper>();
        services.AddSingleton<Olimpo.IBootstrapper, HushVotingLicenceCatalogueBootstrapper>(
            sp => sp.GetRequiredService<HushVotingLicenceCatalogueBootstrapper>());
        services.AddSingleton(sp => sp.GetRequiredService<HushVotingLicenceCatalogueBootstrapper>().Snapshot);
        return services;
    }
}
