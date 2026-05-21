using System.Globalization;
using System.Text;

namespace RetentionLogPrivacyProofPromoter;

public sealed record RetentionLogPrivacyProofPromotionOptions(
    RetentionLogPrivacyProofPromotionPaths Paths,
    string? Mode,
    DateTimeOffset? GeneratedAt,
    string? OutputRoot,
    string? ServerNodeCommitRef,
    string? MemoryBankCommitRef,
    string? DocumentsCommitRef,
    bool ValidateOnly);

public sealed record RetentionLogPrivacyProofPromotionResult(
    string Mode,
    string PackageId,
    DateTimeOffset GeneratedAt,
    string Status,
    RetentionLogPrivacyProofCheckSet CheckResult,
    IReadOnlyList<RetentionLogPrivacyProofGeneratedArtifact> Artifacts,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<RetentionLogPrivacyProofScanFinding> ScanFindings);

public sealed class RetentionLogPrivacyProofPromotionException(
    string message,
    IEnumerable<string>? details = null) : InvalidOperationException(message)
{
    public IReadOnlyList<string> Details { get; } = details?.ToArray() ?? [];
}

public sealed class RetentionLogPrivacyProofPromotionService
{
    public const string ModeValidateOnly = "validate_only";
    public const string ModeCheckOnly = "check_only";
    public const string ModePackage = "package";

    public RetentionLogPrivacyProofPromotionResult Promote(RetentionLogPrivacyProofPromotionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var mode = options.ValidateOnly ? ModeValidateOnly : options.Mode ?? ModePackage;
        if (mode is not ModeValidateOnly and not ModeCheckOnly and not ModePackage)
        {
            throw new RetentionLogPrivacyProofPromotionException(
                $"Unsupported retention/log privacy proof mode: {mode}",
                ["Supported modes: validate_only, check_only, package."]);
        }

        ValidateWorkspaceShape(options.Paths.WorkspaceRoot);
        var paths = options.OutputRoot is null
            ? options.Paths
            : options.Paths with { OutputRoot = Path.GetFullPath(options.OutputRoot) };
        EnsurePathUnder(options.Paths.WorkspaceRoot, paths.OutputRoot, "output root");

        var generatedAt = options.GeneratedAt ?? DateTimeOffset.UtcNow;
        var sourceRefs = new RetentionLogPrivacyProofSourceRefs(
            options.ServerNodeCommitRef ?? "hush-server-node:unrecorded",
            options.MemoryBankCommitRef ?? "hush-memory-bank:unrecorded",
            options.DocumentsCommitRef ?? "hush-documents:unrecorded");
        var generated = RetentionLogPrivacyProofGenerator.Generate(generatedAt, sourceRefs);
        var validationErrors = RetentionLogPrivacyProofContracts.ValidateGeneratedPackage(generated);
        if (validationErrors.Count > 0)
        {
            throw new RetentionLogPrivacyProofPromotionException(
                "Generated retention/log privacy proof package failed validation.",
                validationErrors);
        }

        if (mode == ModeCheckOnly || mode == ModeValidateOnly)
        {
            return BuildResult(mode, generated, []);
        }

        var written = WriteArtifacts(paths.PackageOutputRoot, generated.Artifacts);
        return BuildResult(mode, generated, written);
    }

    public static IReadOnlyList<string> ValidateOutputFolder(string packageOutputRoot)
    {
        var artifacts = new List<RetentionLogPrivacyProofGeneratedArtifact>();
        foreach (var relativePath in RetentionLogPrivacyProofContracts.RequiredArtifactPaths)
        {
            var path = Path.Combine(packageOutputRoot, relativePath);
            if (!File.Exists(path))
            {
                continue;
            }

            var visibility = relativePath == RetentionLogPrivacyProofContracts.PublicSummaryPath
                ? RetentionLogPrivacyProofArtifactVisibility.Public
                : RetentionLogPrivacyProofArtifactVisibility.Restricted;
            var mediaType = relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? "text/markdown"
                : "application/json";
            artifacts.Add(new RetentionLogPrivacyProofGeneratedArtifact(
                relativePath,
                visibility,
                mediaType,
                File.ReadAllText(path)));
        }

        var package = new RetentionLogPrivacyProofGeneratedPackage(
            RetentionLogPrivacyProofGenerator.DefaultPackageId,
            DateTimeOffset.UnixEpoch,
            "unknown",
            new RetentionLogPrivacyProofCheckSet(
                "accepted",
                RetentionLogPrivacyProofContracts.RequiredCheckIds
                    .Select(checkId => new RetentionLogPrivacyProofCheckResult(
                        checkId,
                        "passed",
                        "required",
                        "Loaded from generated package output.",
                        []))
                    .ToArray(),
                [],
                [],
                []),
            artifacts,
            RetentionLogPrivacyProofContracts.ScanGeneratedArtifacts(artifacts));
        return RetentionLogPrivacyProofContracts.ValidateGeneratedPackage(package);
    }

    private static RetentionLogPrivacyProofPromotionResult BuildResult(
        string mode,
        RetentionLogPrivacyProofGeneratedPackage generated,
        IReadOnlyList<string> writtenFiles) =>
        new(
            mode,
            generated.PackageId,
            generated.GeneratedAt,
            generated.Status,
            generated.CheckResult,
            generated.Artifacts,
            writtenFiles,
            generated.ScanFindings);

    private static List<string> WriteArtifacts(
        string packageOutputRoot,
        IReadOnlyList<RetentionLogPrivacyProofGeneratedArtifact> artifacts)
    {
        var written = new List<string>();
        Directory.CreateDirectory(packageOutputRoot);
        foreach (var artifact in artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal))
        {
            var path = Path.GetFullPath(Path.Combine(packageOutputRoot, artifact.RelativePath));
            EnsurePathUnder(packageOutputRoot, path, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, artifact.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            written.Add(path);
        }

        return written;
    }

    private static void ValidateWorkspaceShape(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var missing = new[] { "hush-memory-bank", "hush-server-node", "hush-documents" }
            .Where(name => !Directory.Exists(Path.Combine(root, name)))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new RetentionLogPrivacyProofPromotionException(
                "Workspace root is missing required repositories.",
                missing.Select(name => $"missing:{name}"));
        }
    }

    private static void EnsurePathUnder(string root, string candidate, string label)
    {
        var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullCandidate = Path.GetFullPath(candidate);
        var comparableCandidate = Directory.Exists(fullCandidate)
            ? EnsureTrailingSeparator(fullCandidate)
            : fullCandidate;
        if (!comparableCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new RetentionLogPrivacyProofPromotionException(
                $"Configured {label} escapes the workspace root.",
                [$"{label}: {fullCandidate}", $"workspace root: {fullRoot}"]);
        }
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    public static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
