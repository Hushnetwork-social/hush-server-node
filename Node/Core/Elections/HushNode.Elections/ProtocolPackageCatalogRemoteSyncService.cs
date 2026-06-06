using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public sealed record ProtocolPackageCatalogRemoteSyncOptions(
    bool Enabled,
    string ReleasesApiUrl,
    string PackageId,
    string ReleaseTagPrefix,
    string AssetNamePrefix,
    int MaxReleasesToInspect,
    int RequestTimeoutSeconds)
{
    public static ProtocolPackageCatalogRemoteSyncOptions Default =>
        new(
            Enabled: true,
            ReleasesApiUrl: "https://api.github.com/repos/Hushnetwork-social/protocol-omega-packages/releases",
            PackageId: "omega-hushvoting-v1",
            ReleaseTagPrefix: "ProtocolOmega-HushVoting-v1-",
            AssetNamePrefix: "Protocol-Omega-HushVoting-v1-Artifacts",
            MaxReleasesToInspect: 20,
            RequestTimeoutSeconds: 20);
}

public sealed record ProtocolPackageCatalogSyncResult(
    bool IsSuccess,
    bool IsChanged,
    string? PackageVersion,
    string Message)
{
    public static ProtocolPackageCatalogSyncResult Disabled() =>
        new(false, false, null, "Remote Protocol Omega package sync is disabled.");

    public static ProtocolPackageCatalogSyncResult Unchanged(string message) =>
        new(true, false, null, message);

    public static ProtocolPackageCatalogSyncResult Changed(string packageVersion, ProtocolPackageCatalogImportResult importResult) =>
        new(
            true,
            importResult.AddedEntries > 0 || importResult.UpdatedEntries > 0 || importResult.DemotedEntries > 0,
            packageVersion,
            $"Synced Protocol Omega package {packageVersion}.");

    public static ProtocolPackageCatalogSyncResult Failed(string message) =>
        new(false, false, null, message);
}

public interface IProtocolPackageCatalogSyncService
{
    Task<ProtocolPackageCatalogSyncResult> SyncLatestApprovedPackageForProfileAsync(
        string selectedProfileId,
        CancellationToken cancellationToken = default);
}

public sealed class NoopProtocolPackageCatalogSyncService : IProtocolPackageCatalogSyncService
{
    public static NoopProtocolPackageCatalogSyncService Instance { get; } = new();

    private NoopProtocolPackageCatalogSyncService()
    {
    }

    public Task<ProtocolPackageCatalogSyncResult> SyncLatestApprovedPackageForProfileAsync(
        string selectedProfileId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ProtocolPackageCatalogSyncResult.Unchanged("No Protocol Omega package sync service is configured."));
}

public sealed class ProtocolPackageCatalogRemoteSyncService(
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
    HttpClient httpClient,
    ProtocolPackageCatalogRemoteSyncOptions options,
    ILogger<ProtocolPackageCatalogRemoteSyncService> logger) : IProtocolPackageCatalogSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public async Task<ProtocolPackageCatalogSyncResult> SyncLatestApprovedPackageForProfileAsync(
        string selectedProfileId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProfileId = selectedProfileId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedProfileId))
        {
            return ProtocolPackageCatalogSyncResult.Unchanged("No selected Protocol Omega profile was provided.");
        }

        if (!options.Enabled)
        {
            return ProtocolPackageCatalogSyncResult.Disabled();
        }

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds)));

            var releases = await GetReleasesAsync(timeout.Token);
            foreach (var release in releases
                         .Select(x => new ReleaseCandidate(x, TryParseReleaseVersion(x.TagName)))
                         .Where(x => x.Version is not null)
                         .OrderByDescending(x => x.Version!.Major)
                         .ThenByDescending(x => x.Version!.Minor)
                         .ThenByDescending(x => x.Version!.Patch)
                         .Take(Math.Max(1, options.MaxReleasesToInspect)))
            {
                var version = release.Version!;
                if ((version.Minor % 2) != 0)
                {
                    continue;
                }

                var assetName = $"{options.AssetNamePrefix}-{version.Name}.zip";
                var asset = release.Release.Assets.FirstOrDefault(x =>
                    string.Equals(x.Name, assetName, StringComparison.Ordinal));
                if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                {
                    continue;
                }

                try
                {
                    var assetBytes = await DownloadAssetAsync(asset.BrowserDownloadUrl, timeout.Token);
                    ValidateAssetDigest(asset, assetBytes);
                    var manifest = ReadAndValidateReleaseManifest(version.Name, assetBytes);
                    if (!IsEligibleManifestForProfile(manifest, normalizedProfileId))
                    {
                        continue;
                    }

                    var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
                        manifest.PackageId,
                        manifest.PackageVersion,
                        manifest.SpecPackageHash,
                        manifest.ProofPackageHash,
                        manifest.ReleaseManifestHash,
                        manifest.CompatibleProfileIds,
                        manifest.ApprovalStatus,
                        isLatestForCompatibleProfiles: true,
                        manifest.SpecAccessLocations,
                        manifest.ProofAccessLocations,
                        manifest.ExternalReviewStatus,
                        manifest.ReleasedAt);
                    var importResult = await ProtocolPackageCatalog.ImportApprovedCatalogAsync(
                        unitOfWorkProvider,
                        [catalogEntry]);

                    logger.LogInformation(
                        "[ProtocolPackageCatalogRemoteSyncService] Synced remote Protocol Omega package {PackageVersion}. Added: {AddedEntries}. Updated: {UpdatedEntries}. Demoted: {DemotedEntries}",
                        manifest.PackageVersion,
                        importResult.AddedEntries,
                        importResult.UpdatedEntries,
                        importResult.DemotedEntries);

                    return ProtocolPackageCatalogSyncResult.Changed(manifest.PackageVersion, importResult);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(
                        ex,
                        "[ProtocolPackageCatalogRemoteSyncService] Skipping invalid Protocol Omega release asset {AssetName}.",
                        assetName);
                }
            }

            return ProtocolPackageCatalogSyncResult.Unchanged(
                $"No compatible approved Protocol Omega package release was found for profile {normalizedProfileId}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[ProtocolPackageCatalogRemoteSyncService] Remote Protocol Omega package sync failed.");
            return ProtocolPackageCatalogSyncResult.Failed(ex.Message);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(CancellationToken cancellationToken)
    {
        using var request = CreateGitHubRequest(options.ReleasesApiUrl);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<GitHubRelease[]>(stream, JsonOptions, cancellationToken) ??
            Array.Empty<GitHubRelease>();
    }

    private async Task<byte[]> DownloadAssetAsync(string assetUrl, CancellationToken cancellationToken)
    {
        using var request = CreateGitHubRequest(assetUrl);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static HttpRequestMessage CreateGitHubRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("HushServerNode-ProtocolPackageSync/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }

    private ProtocolOmegaPackageReleaseManifestRecord ReadAndValidateReleaseManifest(
        string versionName,
        byte[] assetBytes)
    {
        using var stream = new MemoryStream(assetBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var versionPrefix = $"{versionName}/";

        if (archive.Entries.Count == 0 ||
            archive.Entries.Any(x =>
                !string.IsNullOrWhiteSpace(x.Name) &&
                !x.FullName.StartsWith(versionPrefix, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Protocol Omega release asset must contain only the {versionName} package folder.");
        }

        var manifestBytes = ReadRequiredEntryBytes(
            archive,
            $"{versionPrefix}{ProtocolPackagePromotionService.ReleaseManifestFileName}");
        var manifest = JsonSerializer.Deserialize<ProtocolOmegaPackageReleaseManifestRecord>(
            StripUtf8Bom(manifestBytes),
            JsonOptions);
        if (manifest is null)
        {
            throw new InvalidOperationException("Protocol Omega release manifest could not be parsed.");
        }

        if (!string.Equals(manifest.PackageId, options.PackageId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Protocol Omega release package id {manifest.PackageId} does not match expected package id {options.PackageId}.");
        }

        if (!string.Equals(manifest.PackageVersion, versionName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Protocol Omega release manifest version {manifest.PackageVersion} does not match tag version {versionName}.");
        }

        if (manifest.ApprovalStatus != ProtocolPackageApprovalStatus.ApprovedInternal)
        {
            throw new InvalidOperationException(
                $"Protocol Omega release manifest status {manifest.ApprovalStatus} is not approved for internal use.");
        }

        var specArchiveBytes = ReadRequiredEntryBytes(
            archive,
            $"{versionPrefix}{ProtocolPackagePromotionService.SpecificationPackageFolderName}/{ProtocolPackagePromotionService.SpecificationPackageFolderName}.zip");
        AssertSha256Hash(specArchiveBytes, manifest.SpecPackageHash, "specification package archive");

        var proofArchiveBytes = ReadRequiredEntryBytes(
            archive,
            $"{versionPrefix}{ProtocolPackagePromotionService.ProofPackageFolderName}/{ProtocolPackagePromotionService.ProofPackageFolderName}.zip");
        AssertSha256Hash(proofArchiveBytes, manifest.ProofPackageHash, "proof package archive");

        foreach (var releaseFile in manifest.ReleaseFiles)
        {
            var fileBytes = ReadRequiredEntryBytes(archive, $"{versionPrefix}{releaseFile.RelativePath}");
            AssertSha256Hash(fileBytes, releaseFile.Sha256Hash, releaseFile.RelativePath);
            if (fileBytes.LongLength != releaseFile.SizeBytes)
            {
                throw new InvalidOperationException(
                    $"Protocol Omega release file {releaseFile.RelativePath} size does not match the release manifest.");
            }
        }

        return manifest;
    }

    private static bool IsEligibleManifestForProfile(
        ProtocolOmegaPackageReleaseManifestRecord manifest,
        string normalizedProfileId) =>
        manifest.ApprovalStatus == ProtocolPackageApprovalStatus.ApprovedInternal &&
        manifest.CompatibleProfileIds.Any(x =>
            string.Equals(x, normalizedProfileId, StringComparison.OrdinalIgnoreCase));

    private static byte[] ReadRequiredEntryBytes(ZipArchive archive, string relativePath)
    {
        var entry = archive.GetEntry(relativePath);
        if (entry is null)
        {
            throw new InvalidOperationException(
                $"Protocol Omega release asset is missing required entry {relativePath}.");
        }

        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static ReadOnlySpan<byte> StripUtf8Bom(byte[] bytes) =>
        bytes is [0xEF, 0xBB, 0xBF, ..]
            ? bytes.AsSpan(3)
            : bytes;

    private static void ValidateAssetDigest(GitHubReleaseAsset asset, byte[] assetBytes)
    {
        if (string.IsNullOrWhiteSpace(asset.Digest) ||
            !asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AssertSha256Hash(assetBytes, asset.Digest["sha256:".Length..], asset.Name);
    }

    private static void AssertSha256Hash(byte[] bytes, string expectedHash, string label)
    {
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Protocol Omega {label} hash mismatch. Expected {expectedHash}, got {actualHash}.");
        }
    }

    private ProtocolPackageVersion? TryParseReleaseVersion(string tagName)
    {
        if (!tagName.StartsWith(options.ReleaseTagPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var versionName = tagName[options.ReleaseTagPrefix.Length..];
        var parts = versionName.StartsWith('v')
            ? versionName[1..].Split('.')
            : Array.Empty<string>();
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch))
        {
            return null;
        }

        return new ProtocolPackageVersion(versionName, major, minor, patch);
    }

    private sealed record ProtocolPackageVersion(string Name, int Major, int Minor, int Patch);

    private sealed record ReleaseCandidate(GitHubRelease Release, ProtocolPackageVersion? Version);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAsset> Assets);

    private sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}
