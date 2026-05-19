using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace SecurityDependencySupportReadinessPromoter;

public sealed record SecurityDependencySupportPromotionOptions(
    SecurityDependencySupportPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? ReleaseId,
    string? Version,
    DateTimeOffset? GeneratedAt,
    string? OutputRoot,
    string? PublicationStatus,
    bool ValidateOnly);

public sealed record SecurityDependencySupportPromotionResult(
    string Mode,
    string ReleaseId,
    string Version,
    DateTimeOffset GeneratedAt,
    string Status,
    SecurityDependencySupportCheckSet CheckResult,
    IReadOnlyList<SecurityDependencySupportGeneratedArtifact> Artifacts,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<SecurityDependencySupportMaterialFinding> ScanFindings,
    string? ArchivePath);

public sealed class SecurityDependencySupportPromotionService
{
    public const string ModeValidateOnly = "validate_only";
    public const string ModeCheckOnly = "check_only";
    public const string ModePackage = "package";

    public SecurityDependencySupportPromotionResult Promote(SecurityDependencySupportPromotionOptions options)
    {
        var mode = options.ValidateOnly ? ModeValidateOnly : options.Mode ?? ModePackage;
        if (mode is not ModeValidateOnly and not ModeCheckOnly and not ModePackage)
        {
            throw new SecurityDependencySupportPromotionException($"Unsupported FEAT-134 promotion mode: {mode}");
        }

        var paths = options.OutputRoot is null
            ? options.Paths
            : options.Paths with { OutputRoot = Path.GetFullPath(options.OutputRoot) };
        var generatedAt = options.GeneratedAt ?? DateTimeOffset.UtcNow;
        var releaseId = options.ReleaseId ?? ResolveReleaseId(paths, options.SourceInput);
        var version = options.Version ?? ResolveVersion(paths, options.SourceInput);
        var publicationStatus = options.PublicationStatus ?? "not_for_publication";

        var schemaErrors = SecurityDependencySupportContracts.ValidateSchemaSet(paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new SecurityDependencySupportPromotionException(
                "FEAT-134 schema set validation failed.",
                schemaErrors);
        }

        var sourceErrors = SecurityDependencySupportContracts.ValidateSourceFixtureSet(paths, options.SourceInput, generatedAt);
        if (sourceErrors.Count > 0)
        {
            throw new SecurityDependencySupportPromotionException(
                "FEAT-134 source fixture validation failed.",
                sourceErrors);
        }

        var sources = SecurityDependencySupportContracts.LoadSources(paths, options.SourceInput);
        var checkResult = SecurityDependencySupportChecker.Evaluate(paths, sources, generatedAt);
        if (mode == ModeCheckOnly)
        {
            return new SecurityDependencySupportPromotionResult(
                mode,
                releaseId,
                version,
                generatedAt,
                checkResult.Status,
                checkResult,
                [],
                [],
                checkResult.ForbiddenMaterialFindings,
                null);
        }

        var generated = SecurityDependencySupportArtifactGenerator.Generate(
            paths,
            options.SourceInput,
            releaseId,
            version,
            generatedAt,
            publicationStatus);

        if (mode == ModeValidateOnly)
        {
            return new SecurityDependencySupportPromotionResult(
                mode,
                releaseId,
                version,
                generatedAt,
                generated.Status,
                generated.CheckResult,
                generated.Artifacts,
                [],
                generated.ScanFindings,
                null);
        }

        if (generated.ScanFindings.Count > 0)
        {
            throw new SecurityDependencySupportPromotionException(
                "FEAT-134 generated package contains forbidden private material.",
                generated.ScanFindings.Select(finding => $"{finding.Boundary}:{finding.RelativePath}:{finding.Category}:{finding.Evidence}"));
        }

        var packageOutputRoot = Path.Combine(paths.OutputRoot, SecurityDependencySupportArtifactGenerator.ExternalPackageFolder, releaseId);
        SecurityDependencySupportContracts.EnsurePathUnder(paths.OutputRoot, packageOutputRoot, "package output root");
        var artifactsWithoutManifest = generated.Artifacts
            .Where(artifact => artifact.RelativePath != SecurityDependencySupportArtifactGenerator.ManifestPath)
            .ToArray();
        var written = WriteArtifacts(packageOutputRoot, artifactsWithoutManifest);
        var archivePath = WriteArchive(paths, releaseId, version, generatedAt, artifactsWithoutManifest);
        written.Add(archivePath);
        var finalManifest = FinalizeManifest(
            generated.Artifacts.Single(artifact => artifact.RelativePath == SecurityDependencySupportArtifactGenerator.ManifestPath),
            SecurityDependencySupportContracts.Sha256FileHex(archivePath));
        written.AddRange(WriteArtifacts(packageOutputRoot, [finalManifest]));
        generated = generated with
        {
            Artifacts = artifactsWithoutManifest
                .Append(finalManifest)
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .ToArray(),
        };
        WriteCatalog(paths, releaseId, version, generated, packageOutputRoot, archivePath, generatedAt);
        written.Add(paths.CatalogPath);

        return new SecurityDependencySupportPromotionResult(
            mode,
            releaseId,
            version,
            generatedAt,
            generated.Status,
            generated.CheckResult,
            generated.Artifacts,
            written,
            generated.ScanFindings,
            archivePath);
    }

    private static List<string> WriteArtifacts(
        string packageOutputRoot,
        IReadOnlyList<SecurityDependencySupportGeneratedArtifact> artifacts)
    {
        var written = new List<string>();
        Directory.CreateDirectory(packageOutputRoot);

        foreach (var artifact in artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal))
        {
            var path = Path.GetFullPath(Path.Combine(packageOutputRoot, artifact.RelativePath));
            SecurityDependencySupportContracts.EnsurePathUnder(packageOutputRoot, path, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && File.ReadAllText(path) != artifact.Content)
            {
                throw new SecurityDependencySupportPromotionException(
                    "Existing generated security dependency support output differs from deterministic content.",
                    [artifact.RelativePath]);
            }

            File.WriteAllText(path, artifact.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            written.Add(path);
        }

        return written;
    }

    private static string WriteArchive(
        SecurityDependencySupportPromotionPaths paths,
        string releaseId,
        string version,
        DateTimeOffset generatedAt,
        IReadOnlyList<SecurityDependencySupportGeneratedArtifact> artifacts)
    {
        Directory.CreateDirectory(paths.ArchivesRoot);
        var archivePath = Path.Combine(paths.ArchivesRoot, $"{releaseId}-{version}-security-dependency-support.zip");
        SecurityDependencySupportContracts.EnsurePathUnder(paths.OutputRoot, archivePath, "archive path");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var artifact in artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal))
        {
            var entry = archive.CreateEntry(artifact.RelativePath.Replace('\\', '/'), CompressionLevel.Optimal);
            entry.LastWriteTime = generatedAt.ToUniversalTime();
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(artifact.Content);
        }

        return archivePath;
    }

    private static SecurityDependencySupportGeneratedArtifact FinalizeManifest(
        SecurityDependencySupportGeneratedArtifact manifestArtifact,
        string archiveHash)
    {
        var manifest = JsonNode.Parse(manifestArtifact.Content)?.AsObject() ??
            throw new SecurityDependencySupportPromotionException("FEAT-134 manifest is not a JSON object.");
        SecurityDependencySupportContracts.RequireObject(manifest, "archive")["sha256Hash"] = archiveHash;
        var hashes = SecurityDependencySupportContracts.RequireObject(manifest, "hashes");
        hashes["manifestHash"] = "computed_after_manifest_finalization";
        var manifestHash = SecurityDependencySupportContracts.Sha256Hex(
            SecurityDependencySupportContracts.CanonicalJson(manifest));
        hashes["manifestHash"] = manifestHash;
        var content = SecurityDependencySupportContracts.CanonicalJson(manifest);
        return new SecurityDependencySupportGeneratedArtifact(
            manifestArtifact.RelativePath,
            content,
            SecurityDependencySupportContracts.Sha256Hex(content));
    }

    private static void WriteCatalog(
        SecurityDependencySupportPromotionPaths paths,
        string releaseId,
        string version,
        SecurityDependencySupportGeneratedPackage generated,
        string packageOutputRoot,
        string archivePath,
        DateTimeOffset generatedAt)
    {
        Directory.CreateDirectory(paths.OutputRoot);
        var packageHash = generated.Artifacts.Single(artifact =>
            artifact.RelativePath == SecurityDependencySupportPromotionPaths.PackageFileName).Sha256Hash;
        JsonObject catalog;
        if (File.Exists(paths.CatalogPath))
        {
            catalog = SecurityDependencySupportContracts.ReadJsonObject(paths.CatalogPath, SecurityDependencySupportPromotionPaths.CatalogFileName);
        }
        else
        {
            catalog = new JsonObject
            {
                ["catalogVersion"] = "1.0",
                ["generatedAt"] = SecurityDependencySupportContracts.FormatTimestamp(generatedAt),
                ["packages"] = new JsonArray(),
                ["currentByReleaseScope"] = new JsonObject(),
                ["supersededPackages"] = new JsonArray(),
                ["hashConflictPolicy"] = "same releaseScopeId and version with different packageHash fails closed"
            };
        }

        var packages = SecurityDependencySupportContracts.RequireArray(catalog, "packages");
        var packageArtifact = generated.Artifacts.Single(artifact =>
            artifact.RelativePath == SecurityDependencySupportPromotionPaths.PackageFileName);
        var packageId = SecurityDependencySupportContracts.GetString(
            JsonNode.Parse(packageArtifact.Content)!.AsObject(),
            "packageId");
        var existing = packages
            .OfType<JsonObject>()
            .SingleOrDefault(entry =>
                SecurityDependencySupportContracts.GetString(entry, "releaseScopeId") == releaseId &&
                SecurityDependencySupportContracts.GetString(entry, "version") == version);
        if (existing is not null)
        {
            var existingHash = SecurityDependencySupportContracts.GetString(existing, "packageHash");
            if (!string.Equals(existingHash, packageHash, StringComparison.Ordinal))
            {
                throw new SecurityDependencySupportPromotionException(
                    "FEAT-134 catalog hash conflict detected for release/version.",
                    [$"{releaseId} {version} existing={existingHash} new={packageHash}"]);
            }
        }
        else
        {
            packages.Add(new JsonObject
            {
                ["packageId"] = packageId,
                ["releaseScopeId"] = releaseId,
                ["version"] = version,
                ["status"] = generated.Status,
                ["generatedAt"] = SecurityDependencySupportContracts.FormatTimestamp(generatedAt),
                ["packagePath"] = Path.GetRelativePath(paths.OutputRoot, Path.Combine(packageOutputRoot, SecurityDependencySupportPromotionPaths.PackageFileName)).Replace('\\', '/'),
                ["packageHash"] = packageHash,
                ["manifestPath"] = Path.GetRelativePath(paths.OutputRoot, Path.Combine(packageOutputRoot, SecurityDependencySupportArtifactGenerator.ManifestPath)).Replace('\\', '/'),
                ["archivePath"] = Path.GetRelativePath(paths.OutputRoot, archivePath).Replace('\\', '/'),
                ["archiveHash"] = SecurityDependencySupportContracts.Sha256FileHex(archivePath),
            });
        }

        var packageRelativePath = Path.GetRelativePath(
            paths.OutputRoot,
            Path.Combine(packageOutputRoot, SecurityDependencySupportPromotionPaths.PackageFileName)).Replace('\\', '/');

        SecurityDependencySupportContracts.RequireObject(catalog, "currentByReleaseScope")[releaseId] = new JsonObject
        {
            ["version"] = version,
            ["status"] = generated.Status,
            ["packageHash"] = packageHash,
            ["packagePath"] = packageRelativePath,
        };
        catalog["generatedAt"] = SecurityDependencySupportContracts.FormatTimestamp(generatedAt);
        File.WriteAllText(
            paths.CatalogPath,
            SecurityDependencySupportContracts.CanonicalJson(catalog),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ResolveReleaseId(SecurityDependencySupportPromotionPaths paths, string? sourceInput)
    {
        var source = SecurityDependencySupportContracts.LoadSources(paths, sourceInput);
        var releaseScope = SecurityDependencySupportContracts.RequireObject(source.Package, "releaseScope");
        return SecurityDependencySupportContracts.GetString(releaseScope, "releaseId", "HV-REL-UNKNOWN");
    }

    private static string ResolveVersion(SecurityDependencySupportPromotionPaths paths, string? sourceInput)
    {
        var source = SecurityDependencySupportContracts.LoadSources(paths, sourceInput);
        var releaseScope = SecurityDependencySupportContracts.RequireObject(source.Package, "releaseScope");
        return SecurityDependencySupportContracts.GetString(releaseScope, "version", "v0.0.0");
    }
}
