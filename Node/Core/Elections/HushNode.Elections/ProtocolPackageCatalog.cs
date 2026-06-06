using System.Reactive.Subjects;
using System.Text.Json;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public sealed record ProtocolPackageCatalogOptions(
    string ApprovedCatalogRelativePath,
    bool FailOnMissingCatalog)
{
    public static ProtocolPackageCatalogOptions Default =>
        new(ProtocolPackageCatalog.DefaultApprovedCatalogRelativePath, FailOnMissingCatalog: false);
}

public sealed record ProtocolPackageCatalogImportResult(
    int EntryCount,
    int CurrentEntryCount,
    int ApprovedOpenEntryCount,
    int AddedEntries,
    int UpdatedEntries,
    int DemotedEntries);

public static class ProtocolPackageCatalog
{
    public const string DefaultApprovedCatalogRelativePath = "ProtocolPackages/ApprovedProtocolPackageCatalog.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ResolveApprovedCatalogPath(ProtocolPackageCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuredPath = string.IsNullOrWhiteSpace(options.ApprovedCatalogRelativePath)
            ? DefaultApprovedCatalogRelativePath
            : options.ApprovedCatalogRelativePath.Trim();

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);
    }

    public static IReadOnlyList<ApprovedProtocolPackageCatalogEntryRecord> LoadApprovedCatalog(string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        if (!File.Exists(catalogPath))
        {
            throw new InvalidOperationException($"Protocol package catalog was not found: {catalogPath}");
        }

        var entries = JsonSerializer.Deserialize<ApprovedProtocolPackageCatalogEntryRecord[]>(
            File.ReadAllText(catalogPath),
            JsonOptions);

        if (entries is null)
        {
            throw new InvalidOperationException($"Protocol package catalog could not be parsed: {catalogPath}");
        }

        ValidateCatalog(entries, catalogPath);
        return entries;
    }

    public static async Task<ProtocolPackageCatalogImportResult> ImportApprovedCatalogAsync(
        IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
        IReadOnlyList<ApprovedProtocolPackageCatalogEntryRecord> shippedEntries)
    {
        ArgumentNullException.ThrowIfNull(unitOfWorkProvider);
        ArgumentNullException.ThrowIfNull(shippedEntries);
        ValidateCatalog(shippedEntries, "runtime protocol package catalog import");

        using var unitOfWork = unitOfWorkProvider.CreateWritable();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var existingEntries = (await repository.GetApprovedProtocolPackageCatalogEntriesAsync()).ToList();

        var addedEntries = 0;
        var updatedEntries = 0;
        var demotedEntries = 0;

        foreach (var shippedEntry in shippedEntries)
        {
            var existingEntry = existingEntries.FirstOrDefault(x => SamePackageVersion(x, shippedEntry));
            if (existingEntry is null)
            {
                await repository.SaveApprovedProtocolPackageCatalogEntryAsync(shippedEntry);
                existingEntries.Add(shippedEntry);
                addedEntries++;
                continue;
            }

            if (!EntriesDiffer(existingEntry, shippedEntry))
            {
                continue;
            }

            await repository.UpdateApprovedProtocolPackageCatalogEntryAsync(shippedEntry);
            var index = existingEntries.FindIndex(x => SamePackageVersion(x, shippedEntry));
            existingEntries[index] = shippedEntry;
            updatedEntries++;
        }

        foreach (var latestShippedEntry in shippedEntries.Where(x => x.IsLatestForCompatibleProfiles))
        {
            var staleLatestEntries = existingEntries
                .Where(x =>
                    !SamePackageVersion(x, latestShippedEntry) &&
                    string.Equals(x.PackageId, latestShippedEntry.PackageId, StringComparison.OrdinalIgnoreCase) &&
                    x.IsLatestForCompatibleProfiles)
                .ToArray();

            foreach (var staleLatestEntry in staleLatestEntries)
            {
                var demotedEntry = staleLatestEntry with
                {
                    IsLatestForCompatibleProfiles = false,
                };
                await repository.UpdateApprovedProtocolPackageCatalogEntryAsync(demotedEntry);
                var index = existingEntries.FindIndex(x => SamePackageVersion(x, demotedEntry));
                existingEntries[index] = demotedEntry;
                demotedEntries++;
            }
        }

        if (addedEntries > 0 || updatedEntries > 0 || demotedEntries > 0)
        {
            await unitOfWork.CommitAsync();
        }

        var currentEntries = shippedEntries.Count(x =>
            x.ApprovalStatus != ProtocolPackageApprovalStatus.Retired &&
            x.IsLatestForCompatibleProfiles);
        var approvedOpenEntries = shippedEntries.Count(x => x.IsApprovedForElectionOpen);
        return new ProtocolPackageCatalogImportResult(
            shippedEntries.Count,
            currentEntries,
            approvedOpenEntries,
            addedEntries,
            updatedEntries,
            demotedEntries);
    }

    private static void ValidateCatalog(
        IReadOnlyList<ApprovedProtocolPackageCatalogEntryRecord> entries,
        string catalogPath)
    {
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"Protocol package catalog '{catalogPath}' contains no entries.");
        }

        var duplicateEntry = entries
            .GroupBy(x => $"{x.PackageId}::{x.PackageVersion}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicateEntry is not null)
        {
            var entry = duplicateEntry.First();
            throw new InvalidOperationException(
                $"Protocol package catalog '{catalogPath}' contains duplicate package entry '{entry.PackageId}' version '{entry.PackageVersion}'.");
        }
    }

    private static bool SamePackageVersion(
        ApprovedProtocolPackageCatalogEntryRecord left,
        ApprovedProtocolPackageCatalogEntryRecord right) =>
        string.Equals(left.PackageId, right.PackageId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.PackageVersion, right.PackageVersion, StringComparison.OrdinalIgnoreCase);

    private static bool EntriesDiffer(
        ApprovedProtocolPackageCatalogEntryRecord left,
        ApprovedProtocolPackageCatalogEntryRecord right) =>
        !string.Equals(left.SpecPackageHash, right.SpecPackageHash, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(left.ProofPackageHash, right.ProofPackageHash, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(left.ReleaseManifestHash, right.ReleaseManifestHash, StringComparison.OrdinalIgnoreCase) ||
        left.ApprovalStatus != right.ApprovalStatus ||
        left.ApprovedAt != right.ApprovedAt ||
        left.IsLatestForCompatibleProfiles != right.IsLatestForCompatibleProfiles ||
        left.ExternalReviewStatus != right.ExternalReviewStatus ||
        !left.CompatibleProfileIds.SequenceEqual(right.CompatibleProfileIds, StringComparer.OrdinalIgnoreCase) ||
        !left.SpecAccessLocations.SequenceEqual(right.SpecAccessLocations) ||
        !left.ProofAccessLocations.SequenceEqual(right.ProofAccessLocations);
}

public sealed class ProtocolPackageCatalogBootstrapper(
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
    ProtocolPackageCatalogOptions options,
    ILogger<ProtocolPackageCatalogBootstrapper> logger) : IBootstrapper
{
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly ProtocolPackageCatalogOptions _options = options;
    private readonly ILogger<ProtocolPackageCatalogBootstrapper> _logger = logger;

    public Subject<string> BootstrapFinished { get; } = new();

    public int Priority { get; set; } = 13;

    public async Task Startup()
    {
        var catalogPath = ProtocolPackageCatalog.ResolveApprovedCatalogPath(_options);
        if (!File.Exists(catalogPath))
        {
            var message = $"Protocol package catalog was not found: {catalogPath}";
            if (_options.FailOnMissingCatalog)
            {
                throw new InvalidOperationException(message);
            }

            _logger.LogWarning("[ProtocolPackageCatalogBootstrapper] {Message}", message);
            BootstrapFinished.OnNext(nameof(ProtocolPackageCatalogBootstrapper));
            return;
        }

        var shippedEntries = ProtocolPackageCatalog.LoadApprovedCatalog(catalogPath);
        var importResult = await ProtocolPackageCatalog.ImportApprovedCatalogAsync(
            _unitOfWorkProvider,
            shippedEntries);
        _logger.LogInformation(
            "[ProtocolPackageCatalogBootstrapper] Loaded protocol package catalog {CatalogPath}. Entries: {EntryCount}. Current refs: {CurrentEntries}. Approved for open: {ApprovedOpenEntries}. Added: {AddedEntries}. Updated: {UpdatedEntries}. Demoted: {DemotedEntries}",
            catalogPath,
            importResult.EntryCount,
            importResult.CurrentEntryCount,
            importResult.ApprovedOpenEntryCount,
            importResult.AddedEntries,
            importResult.UpdatedEntries,
            importResult.DemotedEntries);

        if (importResult.CurrentEntryCount == 0)
        {
            _logger.LogWarning(
                "[ProtocolPackageCatalogBootstrapper] Catalog {CatalogPath} has no current Protocol Omega package refs for election open.",
                catalogPath);
        }

        BootstrapFinished.OnNext(nameof(ProtocolPackageCatalogBootstrapper));
    }

    public void Shutdown()
    {
    }
}
