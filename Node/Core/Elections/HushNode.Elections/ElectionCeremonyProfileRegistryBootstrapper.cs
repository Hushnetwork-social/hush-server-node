using System.Reactive.Subjects;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public sealed class ElectionCeremonyProfileRegistryBootstrapper(
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
    ElectionCeremonyOptions ceremonyOptions,
    ILogger<ElectionCeremonyProfileRegistryBootstrapper> logger) : IBootstrapper
{
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly ElectionCeremonyOptions _ceremonyOptions = ceremonyOptions;
    private readonly ILogger<ElectionCeremonyProfileRegistryBootstrapper> _logger = logger;

    public Subject<string> BootstrapFinished { get; } = new();

    public int Priority { get; set; } = 12;

    public async Task Startup()
    {
        var manifestPath = ElectionCeremonyProfileCatalog.ResolveApprovedRegistryPath(_ceremonyOptions);
        var manifest = ElectionCeremonyProfileCatalog.LoadManifest(manifestPath);
        var shippedProfiles = ElectionCeremonyProfileCatalog.BuildRecords(manifest);

        using var unitOfWork = _unitOfWorkProvider.CreateWritable();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var existingProfiles = await repository.GetCeremonyProfilesAsync();

        var addedProfiles = 0;
        var updatedProfiles = 0;

        foreach (var shippedProfile in shippedProfiles)
        {
            var existingProfile = existingProfiles.FirstOrDefault(x =>
                string.Equals(x.ProfileId, shippedProfile.ProfileId, StringComparison.Ordinal));

            if (existingProfile is null)
            {
                await repository.SaveCeremonyProfileAsync(shippedProfile);
                addedProfiles++;
                continue;
            }

            if (!ProfilesDiffer(existingProfile, shippedProfile))
            {
                continue;
            }

            await repository.UpdateCeremonyProfileAsync(existingProfile with
            {
                DisplayName = shippedProfile.DisplayName,
                Description = shippedProfile.Description,
                ProviderKey = shippedProfile.ProviderKey,
                ProfileVersion = shippedProfile.ProfileVersion,
                TrusteeCount = shippedProfile.TrusteeCount,
                RequiredApprovalCount = shippedProfile.RequiredApprovalCount,
                DevOnly = shippedProfile.DevOnly,
                LastUpdatedAt = DateTime.UtcNow,
            });
            updatedProfiles++;
        }

        if (addedProfiles > 0 || updatedProfiles > 0)
        {
            await unitOfWork.CommitAsync();
        }

        _logger.LogInformation(
            "[ElectionCeremonyProfileRegistryBootstrapper] Loaded ceremony profile catalog {ManifestPath}. Added: {AddedProfiles}. Updated: {UpdatedProfiles}. Dev profiles enabled: {EnableDevProfiles}",
            manifestPath,
            addedProfiles,
            updatedProfiles,
            _ceremonyOptions.EnableDevCeremonyProfiles);

        BootstrapFinished.OnNext(nameof(ElectionCeremonyProfileRegistryBootstrapper));
    }

    public void Shutdown()
    {
    }

    private static bool ProfilesDiffer(ElectionCeremonyProfileRecord existing, ElectionCeremonyProfileRecord shipped) =>
        !string.Equals(existing.DisplayName, shipped.DisplayName, StringComparison.Ordinal) ||
        !string.Equals(existing.Description, shipped.Description, StringComparison.Ordinal) ||
        !string.Equals(existing.ProviderKey, shipped.ProviderKey, StringComparison.Ordinal) ||
        !string.Equals(existing.ProfileVersion, shipped.ProfileVersion, StringComparison.Ordinal) ||
        existing.TrusteeCount != shipped.TrusteeCount ||
        existing.RequiredApprovalCount != shipped.RequiredApprovalCount ||
        existing.DevOnly != shipped.DevOnly;
}
