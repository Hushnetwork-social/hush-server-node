using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public sealed class ProtocolPackagePromotionService
{
    public const string SpecificationPackageFolderName = "Protocol-Specification-Package";
    public const string ProofPackageFolderName = "Protocol-Proof-And-Crypto-Review";
    public const string ReleaseManifestFileName = "ProtocolOmegaPackageManifest.json";
    public const string PackageManifestFileName = "PackageManifest.json";
    public const string PackageManifestSchemaFileName = "PackageManifest.schema.json";

    private static readonly DateTimeOffset FixedZipTimestamp = new(
        1980,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    private static readonly JsonSerializerOptions ReadableJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static readonly IReadOnlyList<string> RequiredSpecificationFiles =
    [
        "README.md",
        "Protocol-Omega-HushVoting-v1-Spec.md",
        "Roles-And-Trust-Paths.md",
        "Threat-Model-And-Assumptions.md",
        "Election-Lifecycle-And-State-Machine.md",
        "Ballot-Definition-And-Sealing.md",
        "Eligibility-Link-And-Checkoff.md",
        "SP-04-Challenge-Spoil-Ceremony.md",
        "Cryptographic-Primitives-And-Parameters.md",
        "Data-Objects-And-Canonicalization.md",
        "Circuit-And-Proof-Statements.md",
        "Ballot-Acceptance-And-Nullifiers.md",
        "Publication-Proof-And-Tally-Replay.md",
        "Trustee-Finalization-And-Result-Binding.md",
        "Election-Record-Schema.json",
        "Audit-Package-Schema.json",
        "Verifier-Contract.md",
        "Verifier-Result-Codes.md",
        "Sample-Election-Record.json",
        "Expected-Verifier-Output.json",
        "Tamper-Test-Catalog.md",
        "Known-Limitations-And-Non-Claims.md",
        "Versioning-And-Compatibility.md",
        "ChangeLog.md",
    ];

    public static readonly IReadOnlyList<string> RequiredProofFiles =
    [
        "README.md",
        "Protocol-Omega-Election-Proof.md",
        "Claim-Table.md",
        "Assumption-Table.md",
        "Adversary-Model.md",
        "Proof-Statement-To-Circuit-Map.md",
        "Artifact-And-Verifier-Map.md",
        "Known-Limitations-And-Non-Claims.md",
        "External-Reviewer-Package-Manifest.md",
        "External-Review-Questions.md",
        "External-Review-Status.md",
        "Claim-Wording-Policy.md",
    ];

    public ProtocolPackagePromotionResult Promote(ProtocolPackagePromotionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        var sourceState = AnalyzeSourceState(options);
        var existingCatalogEntries = SanitizeCatalogEntries(
            options.Paths.OfficialArtifactsRoot,
            ReadCatalogEntries(options.Paths.ServerCatalogPath));
        var promotionPlan = ResolvePromotionPlan(options, sourceState, existingCatalogEntries);

        if (options.ScaffoldMissingSourceFiles)
        {
            ScaffoldMissingFiles(options, promotionPlan.PackageVersion);
            sourceState = AnalyzeSourceState(options);
            promotionPlan = promotionPlan with
            {
                ApprovalStatus = ResolveApprovalStatus(sourceState),
            };
        }

        var missingFiles = sourceState.MissingSourceFiles.ToArray();
        if (missingFiles.Length > 0)
        {
            throw new ProtocolPackagePromotionException(
                "Protocol package promotion failed because required source files are missing.",
                missingFiles);
        }

        var officialVersionRoot = Path.Combine(options.Paths.OfficialArtifactsRoot, promotionPlan.PackageVersion);
        Directory.CreateDirectory(officialVersionRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(options.Paths.ServerCatalogPath)!);

        var writtenFiles = new List<string>();
        var specification = PromoteChildPackage(
            options,
            promotionPlan,
            officialVersionRoot,
            SpecificationPackageFolderName,
            ProtocolPackageKind.Specification,
            RequiredSpecificationFiles,
            writtenFiles);
        var proof = PromoteChildPackage(
            options,
            promotionPlan,
            officialVersionRoot,
            ProofPackageFolderName,
            ProtocolPackageKind.ProofAndCryptoReview,
            RequiredProofFiles,
            writtenFiles);

        var releaseHash = ComputeReleaseManifestHash(options, promotionPlan, specification.Manifest, proof.Manifest);
        var releaseManifest = ElectionModelFactory.CreateProtocolOmegaPackageReleaseManifest(
            options.PackageId,
            promotionPlan.PackageVersion,
            specification.Manifest.PackageHash,
            proof.Manifest.PackageHash,
            releaseHash,
            promotionPlan.ApprovalStatus,
            GetCompatibleProfileIds(),
            specification.Manifest.AccessLocations,
            proof.Manifest.AccessLocations,
            proof.Manifest.ExternalReviewStatus,
            promotionPlan.GeneratedAt);
        var releaseManifestPath = Path.Combine(officialVersionRoot, ReleaseManifestFileName);
        WriteJson(releaseManifestPath, releaseManifest);
        writtenFiles.Add(releaseManifestPath);

        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            options.PackageId,
            promotionPlan.PackageVersion,
            specification.Manifest.PackageHash,
            proof.Manifest.PackageHash,
            releaseManifest.ReleaseManifestHash,
            GetCompatibleProfileIds(),
            promotionPlan.ApprovalStatus,
            isLatestForCompatibleProfiles: promotionPlan.ApprovalStatus == ProtocolPackageApprovalStatus.ApprovedInternal,
            specification.Manifest.AccessLocations,
            proof.Manifest.AccessLocations,
            proof.Manifest.ExternalReviewStatus,
            promotionPlan.GeneratedAt);
        var catalogEntries = MergeCatalogEntry(existingCatalogEntries, catalogEntry);
        WriteJson(options.Paths.ServerCatalogPath, catalogEntries);
        writtenFiles.Add(options.Paths.ServerCatalogPath);
        writtenFiles.AddRange(MirrorOfficialArtifactsToWebsite(
            officialVersionRoot,
            options.Paths.WebsitePublicArtifactsRoot,
            promotionPlan.PackageVersion));

        return new ProtocolPackagePromotionResult(
            specification.Manifest,
            proof.Manifest,
            releaseManifest,
            catalogEntry,
            Array.Empty<string>(),
            sourceState.IncompleteSourceFiles,
            writtenFiles);
    }

    public IReadOnlyList<string> GetMissingSourceFiles(ProtocolPackagePromotionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return GetRequiredSourceFiles(options)
            .Where(x => !File.Exists(x.FullPath))
            .Select(x => x.RelativePath)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static ChildPromotionResult PromoteChildPackage(
        ProtocolPackagePromotionOptions options,
        ProtocolPackagePromotionPlan promotionPlan,
        string officialVersionRoot,
        string childFolderName,
        ProtocolPackageKind packageKind,
        IReadOnlyList<string> requiredFiles,
        List<string> writtenFiles)
    {
        var sourceFolder = Path.Combine(options.Paths.WorkingSourceRoot, childFolderName);
        var outputFolder = Path.Combine(officialVersionRoot, childFolderName);
        Directory.CreateDirectory(outputFolder);

        var fileEntries = requiredFiles
            .Select(relativePath =>
            {
                var sourcePath = Path.Combine(sourceFolder, relativePath);
                var outputPath = Path.Combine(outputFolder, relativePath);
                var bytes = ReadAndNormalizePackageSourceBytes(
                    sourcePath,
                    relativePath,
                    promotionPlan.PackageVersion,
                    out var sourceRewritten);
                if (sourceRewritten)
                {
                    writtenFiles.Add(sourcePath);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(outputPath, bytes);
                writtenFiles.Add(outputPath);

                return new PackageSourceFile(
                    NormalizeArchivePath(relativePath),
                    bytes,
                    ElectionModelFactory.CreateProtocolPackageFileHash(
                        NormalizeArchivePath(relativePath),
                        ComputeSha256Hex(bytes),
                        bytes.Length,
                        ResolveMediaType(relativePath)));
            })
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToArray();

        var archiveFileName = $"{childFolderName}.zip";
        var archivePath = Path.Combine(outputFolder, archiveFileName);
        var archiveBytes = BuildDeterministicArchive(fileEntries);
        File.WriteAllBytes(archivePath, archiveBytes);
        writtenFiles.Add(archivePath);

        var archiveHash = ComputeSha256Hex(archiveBytes);
        var accessLocation = ElectionModelFactory.CreateProtocolPackageAccessLocation(
            ProtocolPackageAccessLocationKind.PublicWebsite,
            "HushNetwork public protocol package",
            BuildPublicPackageUrl(options.PublicBaseUrl, promotionPlan.PackageVersion, childFolderName, archiveFileName),
            archiveHash);
        var manifest = ElectionModelFactory.CreateProtocolPackageManifest(
            $"{options.PackageId}-{ResolvePackageSuffix(packageKind)}",
            promotionPlan.PackageVersion,
            packageKind,
            promotionPlan.ApprovalStatus,
            archiveHash,
            archiveFileName,
            "1.0",
            GetCompatibleProfileIds(),
            fileEntries.Select(x => x.FileHash).ToArray(),
            [accessLocation],
            packageKind == ProtocolPackageKind.ProofAndCryptoReview
                ? ProtocolPackageExternalReviewStatus.NotReviewed
                : ProtocolPackageExternalReviewStatus.NotReviewed,
            promotionPlan.GeneratedAt);

        var schemaPath = Path.Combine(outputFolder, PackageManifestSchemaFileName);
        File.WriteAllText(schemaPath, BuildPackageManifestSchema(), Encoding.UTF8);
        writtenFiles.Add(schemaPath);

        var manifestPath = Path.Combine(outputFolder, PackageManifestFileName);
        WriteJson(manifestPath, manifest);
        writtenFiles.Add(manifestPath);

        return new ChildPromotionResult(manifest);
    }

    private static IReadOnlyList<string> MirrorOfficialArtifactsToWebsite(
        string officialVersionRoot,
        string? websitePublicArtifactsRoot,
        string packageVersion)
    {
        if (string.IsNullOrWhiteSpace(websitePublicArtifactsRoot))
        {
            return Array.Empty<string>();
        }

        var targetVersionRoot = ResolveChildPath(websitePublicArtifactsRoot, packageVersion);
        if (Directory.Exists(targetVersionRoot))
        {
            Directory.Delete(targetVersionRoot, recursive: true);
        }

        var writtenFiles = new List<string>();
        foreach (var sourceFile in Directory.EnumerateFiles(
                     officialVersionRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(officialVersionRoot, sourceFile);
            var targetFile = Path.Combine(targetVersionRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, overwrite: true);
            writtenFiles.Add(targetFile);
        }

        return writtenFiles;
    }

    private static string ResolveChildPath(string parentPath, string childName)
    {
        var parentFullPath = Path.GetFullPath(parentPath);
        var childFullPath = Path.GetFullPath(Path.Combine(parentFullPath, childName));
        var parentWithSeparator = parentFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? parentFullPath
            : $"{parentFullPath}{Path.DirectorySeparatorChar}";

        if (!childFullPath.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Package artifact target must stay inside the configured artifacts root.");
        }

        return childFullPath;
    }

    private static byte[] BuildDeterministicArchive(IReadOnlyList<PackageSourceFile> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.NoCompression);
                entry.LastWriteTime = FixedZipTimestamp;
                using var entryStream = entry.Open();
                entryStream.Write(file.Bytes);
            }
        }

        return stream.ToArray();
    }

    private static byte[] ReadAndNormalizePackageSourceBytes(
        string sourcePath,
        string relativePath,
        string packageVersion,
        out bool sourceRewritten)
    {
        sourceRewritten = false;
        var bytes = File.ReadAllBytes(sourcePath);
        if (!IsTextPackageSource(relativePath))
        {
            return bytes;
        }

        var originalText = Encoding.UTF8.GetString(bytes);
        var normalizedText = relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? Regex.Replace(
                originalText,
                "(\"packageVersion\"\\s*:\\s*\")([^\"]*)(\")",
                match => $"{match.Groups[1].Value}{packageVersion}{match.Groups[3].Value}")
            : Regex.Replace(
                originalText,
                "^Version:\\s*.+$",
                $"Version: {packageVersion}",
                RegexOptions.Multiline);

        if (string.Equals(originalText, normalizedText, StringComparison.Ordinal))
        {
            return bytes;
        }

        var normalizedBytes = Encoding.UTF8.GetBytes(normalizedText);
        File.WriteAllBytes(sourcePath, normalizedBytes);
        sourceRewritten = true;
        return normalizedBytes;
    }

    private static string ComputeReleaseManifestHash(
        ProtocolPackagePromotionOptions options,
        ProtocolPackagePromotionPlan promotionPlan,
        ProtocolPackageManifestRecord specificationManifest,
        ProtocolPackageManifestRecord proofManifest)
    {
        var hashPayload = new
        {
            options.PackageId,
            promotionPlan.PackageVersion,
            specificationPackageHash = specificationManifest.PackageHash,
            proofPackageHash = proofManifest.PackageHash,
            compatibleProfileIds = GetCompatibleProfileIds(),
            approvalStatus = promotionPlan.ApprovalStatus.ToString(),
            specificationAccessLocations = NormalizeAccessLocations(specificationManifest.AccessLocations),
            proofAccessLocations = NormalizeAccessLocations(proofManifest.AccessLocations),
            externalReviewStatus = proofManifest.ExternalReviewStatus.ToString(),
        };

        return ComputeSha256Hex(JsonSerializer.SerializeToUtf8Bytes(hashPayload, CanonicalJsonOptions));
    }

    private static IReadOnlyList<object> NormalizeAccessLocations(
        IReadOnlyList<ProtocolPackageAccessLocationRecord> accessLocations) =>
        accessLocations
            .OrderBy(x => x.LocationKind)
            .ThenBy(x => x.Label, StringComparer.Ordinal)
            .ThenBy(x => x.Location, StringComparer.Ordinal)
            .ThenBy(x => x.ContentHash, StringComparer.Ordinal)
            .Select(x => new
            {
                locationKind = x.LocationKind.ToString(),
                x.Label,
                x.Location,
                x.ContentHash,
            })
            .Cast<object>()
            .ToArray();

    private static SourcePackageState AnalyzeSourceState(ProtocolPackagePromotionOptions options)
    {
        var missingFiles = new List<string>();
        var incompleteFiles = new List<string>();
        var fileHashes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var sourceFile in GetRequiredSourceFiles(options))
        {
            if (!File.Exists(sourceFile.FullPath))
            {
                missingFiles.Add(sourceFile.RelativePath);
                continue;
            }

            var bytes = File.ReadAllBytes(sourceFile.FullPath);
            fileHashes[sourceFile.RelativePath] = ComputeSha256Hex(bytes);
            if (ContainsIncompleteMarker(bytes))
            {
                incompleteFiles.Add(sourceFile.RelativePath);
            }
        }

        return new SourcePackageState(
            missingFiles.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            incompleteFiles.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            fileHashes);
    }

    private static ProtocolPackagePromotionPlan ResolvePromotionPlan(
        ProtocolPackagePromotionOptions options,
        SourcePackageState sourceState,
        IReadOnlyList<ApprovedProtocolPackageCatalogEntryRecord> existingCatalogEntries)
    {
        var approvalStatus = ResolveApprovalStatus(sourceState);
        var generatedAt = options.GeneratedAt ?? DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(options.PackageVersion))
        {
            return new ProtocolPackagePromotionPlan(
                options.PackageVersion,
                approvalStatus,
                generatedAt);
        }

        var packageVersions = existingCatalogEntries
            .Where(x => string.Equals(x.PackageId, options.PackageId, StringComparison.OrdinalIgnoreCase))
            .Select(x => new
            {
                Entry = x,
                Version = TryParsePackageVersion(x.PackageVersion, out var parsedVersion)
                    ? parsedVersion
                    : null,
            })
            .Where(x => x.Version is not null)
            .OrderByDescending(x => x.Version!.Major)
            .ThenByDescending(x => x.Version!.Minor)
            .ThenByDescending(x => x.Version!.Patch)
            .ToArray();

        if (packageVersions.Length == 0)
        {
            return new ProtocolPackagePromotionPlan(
                FormatPackageVersion(1, approvalStatus == ProtocolPackageApprovalStatus.ApprovedInternal ? 2 : 1, 0),
                approvalStatus,
                generatedAt);
        }

        var latest = packageVersions[0];
        var latestVersion = latest.Version!;
        var latestArtifactState = TryReadOfficialArtifactState(
            options.Paths.OfficialArtifactsRoot,
            latest.Entry.PackageVersion);
        var fileSetChanged = latestArtifactState is not null &&
                             !SameFileSet(sourceState.FileHashes.Keys, latestArtifactState.FileHashes.Keys);
        var contentChanged = latestArtifactState is null ||
                             !SameContentHashes(sourceState.FileHashes, latestArtifactState.FileHashes);
        var previousHadIncompleteMarkers = latestArtifactState?.IncompleteSourceFiles.Count > 0;

        if (fileSetChanged)
        {
            return new ProtocolPackagePromotionPlan(
                FormatPackageVersion(
                    latestVersion.Major + 1,
                    approvalStatus == ProtocolPackageApprovalStatus.ApprovedInternal ? 2 : 1,
                    0),
                approvalStatus,
                generatedAt);
        }

        if (approvalStatus == ProtocolPackageApprovalStatus.ApprovedInternal)
        {
            if (latestVersion.Minor % 2 != 0 || previousHadIncompleteMarkers)
            {
                var nextEvenMinor = latestVersion.Minor % 2 == 0
                    ? latestVersion.Minor + 2
                    : latestVersion.Minor + 1;

                return new ProtocolPackagePromotionPlan(
                    FormatPackageVersion(latestVersion.Major, nextEvenMinor, 0),
                    approvalStatus,
                    generatedAt);
            }

            return new ProtocolPackagePromotionPlan(
                contentChanged
                    ? FormatPackageVersion(latestVersion.Major, latestVersion.Minor, latestVersion.Patch + 1)
                    : latest.Entry.PackageVersion,
                approvalStatus,
                generatedAt);
        }

        if (latestVersion.Minor % 2 == 0)
        {
            return new ProtocolPackagePromotionPlan(
                FormatPackageVersion(latestVersion.Major, latestVersion.Minor + 1, 1),
                approvalStatus,
                generatedAt);
        }

        return new ProtocolPackagePromotionPlan(
            contentChanged
                ? FormatPackageVersion(latestVersion.Major, latestVersion.Minor, latestVersion.Patch + 1)
                : latest.Entry.PackageVersion,
            approvalStatus,
            generatedAt);
    }

    private static ProtocolPackageApprovalStatus ResolveApprovalStatus(SourcePackageState sourceState) =>
        sourceState.MissingSourceFiles.Count == 0 && sourceState.IncompleteSourceFiles.Count == 0
            ? ProtocolPackageApprovalStatus.ApprovedInternal
            : ProtocolPackageApprovalStatus.DraftPrivate;

    private static SourcePackageState? TryReadOfficialArtifactState(
        string officialArtifactsRoot,
        string packageVersion)
    {
        var versionRoot = Path.Combine(officialArtifactsRoot, packageVersion);
        if (!Directory.Exists(versionRoot))
        {
            return null;
        }

        var fileHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var incompleteFiles = new List<string>();

        foreach (var packageFolder in new[] { SpecificationPackageFolderName, ProofPackageFolderName })
        {
            var manifestPath = Path.Combine(versionRoot, packageFolder, PackageManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            var manifest = JsonSerializer.Deserialize<ProtocolPackageManifestRecord>(
                File.ReadAllText(manifestPath),
                ReadableJsonOptions);
            if (manifest is null)
            {
                return null;
            }

            foreach (var file in manifest.Files)
            {
                var relativePath = $"{packageFolder}/{NormalizeArchivePath(file.RelativePath)}";
                fileHashes[relativePath] = file.Sha256Hash;
                var fullPath = Path.Combine(versionRoot, packageFolder, file.RelativePath);
                if (File.Exists(fullPath) && ContainsIncompleteMarker(File.ReadAllBytes(fullPath)))
                {
                    incompleteFiles.Add(relativePath);
                }
            }
        }

        return new SourcePackageState(
            Array.Empty<string>(),
            incompleteFiles.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            fileHashes);
    }

    private static bool SameFileSet(IEnumerable<string> current, IEnumerable<string> previous) =>
        current.OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(previous.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool SameContentHashes(
        IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string> previous)
    {
        if (!SameFileSet(current.Keys, previous.Keys))
        {
            return false;
        }

        return current.All(x =>
            previous.TryGetValue(x.Key, out var previousHash) &&
            string.Equals(x.Value, previousHash, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsIncompleteMarker(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes).Contains(
            "specified_not_implemented_yet",
            StringComparison.OrdinalIgnoreCase);

    private static bool TryParsePackageVersion(
        string packageVersion,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PackageVersionNumber? version)
    {
        version = null;
        var normalized = packageVersion.StartsWith('v')
            ? packageVersion[1..]
            : packageVersion;
        var parts = normalized.Split('.');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch))
        {
            return false;
        }

        version = new PackageVersionNumber(major, minor, patch);
        return true;
    }

    private static string FormatPackageVersion(int major, int minor, int patch) =>
        $"v{major}.{minor}.{patch}";

    private static void ScaffoldMissingFiles(
        ProtocolPackagePromotionOptions options,
        string packageVersion)
    {
        ScaffoldPackageFiles(
            Path.Combine(options.Paths.WorkingSourceRoot, SpecificationPackageFolderName),
            SpecificationPackageFolderName,
            packageVersion,
            RequiredSpecificationFiles);
        ScaffoldPackageFiles(
            Path.Combine(options.Paths.WorkingSourceRoot, ProofPackageFolderName),
            ProofPackageFolderName,
            packageVersion,
            RequiredProofFiles);
    }

    private static void ScaffoldPackageFiles(
        string packageFolder,
        string packageName,
        string packageVersion,
        IReadOnlyList<string> requiredFiles)
    {
        Directory.CreateDirectory(packageFolder);

        foreach (var relativePath in requiredFiles)
        {
            var filePath = Path.Combine(packageFolder, relativePath);
            if (File.Exists(filePath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var content = relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? BuildJsonSkeleton(packageName, relativePath, packageVersion)
                : BuildMarkdownSkeleton(packageName, relativePath, packageVersion);
            File.WriteAllText(filePath, content, Encoding.UTF8);
        }
    }

    private static IReadOnlyList<RequiredSourceFile> GetRequiredSourceFiles(
        ProtocolPackagePromotionOptions options) =>
    [
        .. RequiredSpecificationFiles.Select(x => new RequiredSourceFile(
            $"{SpecificationPackageFolderName}/{NormalizeArchivePath(x)}",
            Path.Combine(options.Paths.WorkingSourceRoot, SpecificationPackageFolderName, x))),
        .. RequiredProofFiles.Select(x => new RequiredSourceFile(
            $"{ProofPackageFolderName}/{NormalizeArchivePath(x)}",
            Path.Combine(options.Paths.WorkingSourceRoot, ProofPackageFolderName, x))),
    ];

    private static void ValidateOptions(ProtocolPackagePromotionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PackageId))
        {
            throw new ArgumentException("Package id is required.", nameof(options));
        }

        if (!string.IsNullOrWhiteSpace(options.PackageVersion) &&
            (Path.IsPathFullyQualified(options.PackageVersion) ||
             options.PackageVersion.Contains('/') ||
             options.PackageVersion.Contains('\\') ||
             options.PackageVersion is "." or ".." ||
             options.PackageVersion.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new ArgumentException("Package version must be a simple directory name.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.PublicBaseUrl))
        {
            throw new ArgumentException("Public base URL is required.", nameof(options));
        }
    }

    private static string BuildMarkdownSkeleton(
        string packageName,
        string relativePath,
        string packageVersion) =>
        $"""
        # {Path.GetFileNameWithoutExtension(relativePath).Replace('-', ' ')}

        Status: specified_not_implemented_yet
        Package: {packageName}
        Version: {packageVersion}

        This file is part of the Protocol Omega HushVoting v1 package skeleton. It must be completed
        before the package is marked ready for external review.
        """;

    private static string BuildJsonSkeleton(
        string packageName,
        string relativePath,
        string packageVersion)
    {
        var payload = new
        {
            status = "specified_not_implemented_yet",
            packageName,
            packageVersion,
            file = NormalizeArchivePath(relativePath),
            note = "Skeleton placeholder. Complete before ready-for-review status.",
        };

        return JsonSerializer.Serialize(payload, ReadableJsonOptions);
    }

    private static string BuildPackageManifestSchema()
    {
        var schema = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "Protocol Omega Package Manifest",
            ["type"] = "object",
            ["required"] = new[]
            {
                "packageId",
                "packageVersion",
                "packageKind",
                "packageStatus",
                "packageHash",
                "archiveFileName",
                "compatibleProfileIds",
                "files",
                "accessLocations",
            },
        };

        return JsonSerializer.Serialize(schema, ReadableJsonOptions);
    }

    private static IReadOnlyList<ApprovedProtocolPackageCatalogEntryRecord> MergeCatalogEntry(
        IReadOnlyList<ApprovedProtocolPackageCatalogEntryRecord> existingEntries,
        ApprovedProtocolPackageCatalogEntryRecord newEntry)
    {
        var newProfileIds = new HashSet<string>(newEntry.CompatibleProfileIds, StringComparer.OrdinalIgnoreCase);
        var merged = existingEntries
            .Where(x =>
                !string.Equals(x.PackageId, newEntry.PackageId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(x.PackageVersion, newEntry.PackageVersion, StringComparison.OrdinalIgnoreCase))
            .Select(x => newEntry.IsLatestForCompatibleProfiles &&
                         x.IsLatestForCompatibleProfiles &&
                         x.CompatibleProfileIds.Any(profileId => newProfileIds.Contains(profileId))
                ? x with { IsLatestForCompatibleProfiles = false }
                : x)
            .Append(newEntry)
            .OrderBy(x => x.PackageId, StringComparer.Ordinal)
            .ThenBy(x => x.PackageVersion, StringComparer.Ordinal)
            .ToArray();

        return merged;
    }

    private static IReadOnlyList<ApprovedProtocolPackageCatalogEntryRecord> SanitizeCatalogEntries(
        string officialArtifactsRoot,
        IReadOnlyList<ApprovedProtocolPackageCatalogEntryRecord> catalogEntries) =>
        catalogEntries
            .Select(entry =>
            {
                var artifactState = TryReadOfficialArtifactState(officialArtifactsRoot, entry.PackageVersion);
                return artifactState is not null &&
                       artifactState.IncompleteSourceFiles.Count > 0 &&
                       entry.ApprovalStatus == ProtocolPackageApprovalStatus.ApprovedInternal
                    ? entry with
                    {
                        ApprovalStatus = ProtocolPackageApprovalStatus.DraftPrivate,
                        IsLatestForCompatibleProfiles = false,
                    }
                    : entry;
            })
            .ToArray();

    private static IReadOnlyList<ApprovedProtocolPackageCatalogEntryRecord> ReadCatalogEntries(string catalogPath)
    {
        if (!File.Exists(catalogPath))
        {
            return Array.Empty<ApprovedProtocolPackageCatalogEntryRecord>();
        }

        var json = File.ReadAllText(catalogPath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ApprovedProtocolPackageCatalogEntryRecord>();
        }

        return JsonSerializer.Deserialize<ApprovedProtocolPackageCatalogEntryRecord[]>(
                json,
                ReadableJsonOptions) ??
            Array.Empty<ApprovedProtocolPackageCatalogEntryRecord>();
    }

    private static string BuildPublicPackageUrl(
        string publicBaseUrl,
        string packageVersion,
        string childFolderName,
        string archiveFileName) =>
        $"{publicBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(packageVersion)}/{childFolderName}/{archiveFileName}";

    private static string ComputeSha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string NormalizeArchivePath(string relativePath) =>
        relativePath.Replace('\\', '/');

    private static string ResolveMediaType(string relativePath) =>
        relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? "application/json"
            : "text/markdown";

    private static bool IsTextPackageSource(string relativePath) =>
        relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    private static string ResolvePackageSuffix(ProtocolPackageKind packageKind) =>
        packageKind switch
        {
            ProtocolPackageKind.Specification => "spec",
            ProtocolPackageKind.ProofAndCryptoReview => "proof",
            _ => throw new ArgumentOutOfRangeException(nameof(packageKind), packageKind, "Unsupported package kind."),
        };

    private static IReadOnlyList<string> GetCompatibleProfileIds() =>
    [
        ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId,
        ElectionSelectableProfileCatalog.AdminOnlyDevProfileId,
        ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
        ElectionSelectableProfileCatalog.TrusteeDevProfileId,
    ];

    private static void WriteJson<TValue>(string path, TValue value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, ReadableJsonOptions), Encoding.UTF8);
    }

    private sealed record PackageSourceFile(
        string RelativePath,
        byte[] Bytes,
        ProtocolPackageFileHashRecord FileHash);

    private sealed record ChildPromotionResult(ProtocolPackageManifestRecord Manifest);

    private sealed record ProtocolPackagePromotionPlan(
        string PackageVersion,
        ProtocolPackageApprovalStatus ApprovalStatus,
        DateTime GeneratedAt);

    private sealed record SourcePackageState(
        IReadOnlyList<string> MissingSourceFiles,
        IReadOnlyList<string> IncompleteSourceFiles,
        IReadOnlyDictionary<string, string> FileHashes);

    private sealed record PackageVersionNumber(int Major, int Minor, int Patch);

    private sealed record RequiredSourceFile(string RelativePath, string FullPath);
}
