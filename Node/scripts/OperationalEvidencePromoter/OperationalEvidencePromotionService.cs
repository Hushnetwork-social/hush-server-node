using System.Text;
using System.Text.Json.Nodes;

namespace OperationalEvidencePromoter;

public sealed record OperationalEvidencePromotionOptions(
    OperationalEvidencePromotionPaths Paths,
    string? Mode,
    string? RunId,
    DateTimeOffset? GeneratedAt,
    string? PackageOutputRoot,
    string? RestrictedOutputRoot,
    bool ValidateOnly,
    bool AllowLiveCapture);

public sealed record OperationalEvidencePromotionResult(
    string Mode,
    string RunId,
    DateTimeOffset GeneratedAt,
    string Status,
    IReadOnlyList<string> WrittenFiles,
    OperationalEvidenceCheckSetResult CheckResult,
    IReadOnlyList<OperationalEvidenceGeneratedArtifact> Artifacts,
    IReadOnlyList<OperationalEvidenceMaterialFinding> ScanFindings);

public sealed class OperationalEvidencePromotionException(
    string message,
    IReadOnlyList<string> details) : InvalidOperationException(message)
{
    public IReadOnlyList<string> Details { get; } = details;
}

public sealed class OperationalEvidencePromotionService
{
    public const string ModeValidateOnly = "validate_only";
    public const string ModeCheckOnly = "check_only";
    public const string ModeRehearsalPackage = "rehearsal_package";

    public OperationalEvidencePromotionResult Promote(OperationalEvidencePromotionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.AllowLiveCapture)
        {
            throw new OperationalEvidencePromotionException(
                "Operational evidence promotion does not capture live provider evidence in FEAT-133 v1.",
                ["Use committed rehearsal sources or a separately approved future live-capture process."]);
        }

        ValidateWorkspaceShape(options.Paths.WorkspaceRoot);
        ValidateRootConfiguration(options.Paths);
        var runRelativePath = FindRunRelativePath(options.Paths, options.RunId);
        var run = OperationalEvidenceContracts.ReadJsonObject(
            Path.Combine(options.Paths.ExamplesRoot, runRelativePath),
            runRelativePath);
        ValidateRunSourcePaths(options.Paths, run);

        var schemaErrors = OperationalEvidenceContracts.ValidateSchemaSet(options.Paths.SchemasRoot);
        var sourceErrors = OperationalEvidenceContracts.ValidateSourceFixtureSet(options.Paths);
        var validationErrors = schemaErrors.Concat(sourceErrors).ToList();
        if (validationErrors.Count > 0)
        {
            throw new OperationalEvidencePromotionException("Operational evidence source validation failed.", validationErrors);
        }

        var generated = OperationalEvidenceArtifactGenerator.Generate(options.Paths, run, options.GeneratedAt);
        var mode = NormalizeMode(options);
        var packageOutputRoot = ResolvePackageOutputRoot(options, run, generated.RunId);
        var restrictedOutputRoot = ResolveRestrictedOutputRoot(options, run, generated.RunId);
        ValidateOutputRoot(options.Paths.WorkspaceRoot, packageOutputRoot, "package output root");
        ValidateOutputRoot(options.Paths.WorkspaceRoot, restrictedOutputRoot, "restricted output root");
        ValidateArtifactOutputPaths(generated.Artifacts, packageOutputRoot, restrictedOutputRoot);

        if (generated.BlocksAcceptedEvidence)
        {
            var details = generated.ScanFindings.Select(FormatFinding)
                .Concat(generated.CheckResult.Blockers.Select(blocker => $"blocker:{blocker}"))
                .Concat(generated.CheckResult.PlaceholderFindings.Select(placeholder => $"placeholder:{placeholder}"))
                .ToArray();
            if (details.Length > 0 && mode == ModeRehearsalPackage)
            {
                throw new OperationalEvidencePromotionException("Operational evidence generation is blocked.", details);
            }
        }

        return mode switch
        {
            ModeValidateOnly => BuildResult(mode, generated, []),
            ModeCheckOnly => BuildResult(mode, generated, []),
            ModeRehearsalPackage => BuildResult(
                mode,
                generated,
                WriteGeneratedArtifacts(generated.Artifacts, packageOutputRoot, restrictedOutputRoot)),
            _ => throw new OperationalEvidencePromotionException(
                $"Unsupported operational evidence mode: {mode}",
                ["Supported modes: validate_only, check_only, rehearsal_package."]),
        };
    }

    private static OperationalEvidencePromotionResult BuildResult(
        string mode,
        OperationalEvidenceGeneratedArtifactSet generated,
        IReadOnlyList<string> writtenFiles) =>
        new(
            mode,
            generated.RunId,
            generated.GeneratedAt,
            generated.GenerationStatus,
            writtenFiles,
            generated.CheckResult,
            generated.Artifacts,
            generated.ScanFindings);

    private static string NormalizeMode(OperationalEvidencePromotionOptions options)
    {
        if (options.ValidateOnly)
        {
            return ModeValidateOnly;
        }

        if (string.IsNullOrWhiteSpace(options.Mode))
        {
            return ModeValidateOnly;
        }

        return options.Mode.Trim();
    }

    private static string FindRunRelativePath(OperationalEvidencePromotionPaths paths, string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return OperationalEvidenceContracts.AcceptedRunFixture;
        }

        var runsRoot = Path.Combine(paths.ExamplesRoot, "runs");
        foreach (var file in Directory.EnumerateFiles(runsRoot, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(paths.ExamplesRoot, file).Replace('\\', '/');
            var run = OperationalEvidenceContracts.ReadJsonObject(file, relativePath);
            if (string.Equals(GetString(run, "runId"), runId, StringComparison.Ordinal))
            {
                return relativePath;
            }
        }

        throw new OperationalEvidencePromotionException(
            "Requested operational evidence run was not found.",
            [$"run-id={runId}"]);
    }

    private static void ValidateWorkspaceShape(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var missing = new[] { "hush-memory-bank", "hush-server-node", "hush-documents" }
            .Where(name => !Directory.Exists(Path.Combine(root, name)))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new OperationalEvidencePromotionException(
                "Workspace root is missing required repositories.",
                missing.Select(name => $"missing:{name}").ToArray());
        }
    }

    private static void ValidateRootConfiguration(OperationalEvidencePromotionPaths paths)
    {
        ValidateOutputRoot(paths.WorkspaceRoot, paths.SourceRoot, "source root");
        ValidateOutputRoot(paths.WorkspaceRoot, paths.RestrictedTemplateRoot, "restricted source root");
    }

    private static void ValidateRunSourcePaths(OperationalEvidencePromotionPaths paths, JsonObject run)
    {
        if (run["sourceRefs"] is not JsonObject sourceRefs)
        {
            throw new OperationalEvidencePromotionException("Operational run sourceRefs are missing.", ["sourceRefs"]);
        }

        var root = EnsureTrailingSeparator(Path.GetFullPath(paths.SourceRoot));
        var errors = new List<string>();
        foreach (var key in OperationalEvidenceContracts.RequiredSourceRefKeys)
        {
            var relativePath = GetString(sourceRefs, key);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                errors.Add($"sourceRefs.{key} is required.");
                continue;
            }

            if (Path.IsPathRooted(relativePath))
            {
                errors.Add($"sourceRefs.{key} must be relative: {relativePath}");
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(paths.SourceRoot, relativePath));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"sourceRefs.{key} escapes source root: {relativePath}");
            }
        }

        if (errors.Count > 0)
        {
            throw new OperationalEvidencePromotionException("Operational run source paths failed containment checks.", errors);
        }
    }

    private static string ResolvePackageOutputRoot(
        OperationalEvidencePromotionOptions options,
        JsonObject run,
        string runId)
    {
        if (!string.IsNullOrWhiteSpace(options.PackageOutputRoot))
        {
            return options.PackageOutputRoot;
        }

        var configured = GetString(run["outputRoots"] as JsonObject, "packageOutputRoot");
        return Path.GetFullPath(Path.Combine(options.Paths.WorkspaceRoot, configured ?? ".tmp-feat133-operational-package", runId));
    }

    private static string ResolveRestrictedOutputRoot(
        OperationalEvidencePromotionOptions options,
        JsonObject run,
        string runId)
    {
        if (!string.IsNullOrWhiteSpace(options.RestrictedOutputRoot))
        {
            return options.RestrictedOutputRoot;
        }

        var configured = GetString(run["outputRoots"] as JsonObject, "restrictedOutputRoot");
        return Path.GetFullPath(Path.Combine(
            options.Paths.WorkspaceRoot,
            configured ?? Path.Combine("hush-documents", "PrivateServer_ElectronicVoting", "Operational-Security", "FEAT-133-Operational-Evidence", runId)));
    }

    private static void ValidateOutputRoot(string workspaceRoot, string candidate, string label)
    {
        var root = EnsureTrailingSeparator(Path.GetFullPath(workspaceRoot));
        var fullCandidate = EnsureTrailingSeparator(Path.GetFullPath(candidate));
        if (!fullCandidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new OperationalEvidencePromotionException(
                $"Configured {label} escapes the workspace root.",
                [$"{label}: {fullCandidate}", $"workspace root: {root}"]);
        }
    }

    private static void ValidateArtifactOutputPaths(
        IReadOnlyList<OperationalEvidenceGeneratedArtifact> artifacts,
        string packageOutputRoot,
        string restrictedOutputRoot)
    {
        foreach (var artifact in artifacts)
        {
            var root = artifact.Visibility == OperationalEvidenceArtifactVisibility.Restricted
                ? restrictedOutputRoot
                : packageOutputRoot;
            ResolveOutputPath(root, artifact.RelativePath);
        }
    }

    private static IReadOnlyList<string> WriteGeneratedArtifacts(
        IReadOnlyList<OperationalEvidenceGeneratedArtifact> artifacts,
        string packageOutputRoot,
        string restrictedOutputRoot)
    {
        var written = new List<string>();
        foreach (var artifact in artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal))
        {
            var root = artifact.Visibility == OperationalEvidenceArtifactVisibility.Restricted
                ? restrictedOutputRoot
                : packageOutputRoot;
            var path = ResolveOutputPath(root, artifact.RelativePath);
            var bytes = new UTF8Encoding(false).GetBytes(artifact.Content);
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (!existing.SequenceEqual(bytes))
                {
                    throw new OperationalEvidencePromotionException(
                        "Existing generated output differs from deterministic FEAT-133 content.",
                        [path]);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }

            written.Add(path);
        }

        return written;
    }

    private static string ResolveOutputPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new OperationalEvidencePromotionException(
                "Generated output path must be relative.",
                [relativePath]);
        }

        var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new OperationalEvidencePromotionException(
                "Generated output path escapes configured root.",
                [relativePath]);
        }

        return path;
    }

    private static string? GetString(JsonObject? obj, string propertyName)
    {
        if (obj is null)
        {
            return null;
        }

        try
        {
            return obj[propertyName]?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string FormatFinding(OperationalEvidenceMaterialFinding finding) =>
        $"{finding.Boundary}:{finding.RelativePath}:{finding.Category}:{finding.Evidence}";

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
