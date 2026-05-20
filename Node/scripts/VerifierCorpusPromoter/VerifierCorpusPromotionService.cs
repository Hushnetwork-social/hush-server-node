using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace VerifierCorpusPromoter;

public sealed record VerifierCorpusPromotionOptions(
    VerifierCorpusPromotionPaths Paths,
    string PublicOutputRoot,
    string CorpusVersion,
    DateTimeOffset GeneratedAt,
    bool ValidateOnly,
    string PublicRepositoryRef,
    string VerifierSourceRef,
    string VerifierHash,
    bool WindowsReviewerReplayValidated = false,
    bool LinuxReviewerReplayValidated = false);

public sealed record VerifierCorpusPromotionResult(
    string Mode,
    string OutputRoot,
    string CorpusVersion,
    DateTimeOffset GeneratedAt,
    string ManifestHash,
    string FixtureIndexHash,
    string NoSecretScanStatus,
    int FixtureCount,
    int ScanFindingCount,
    int UnexpectedScanFindingCount,
    IReadOnlyList<string> WrittenFiles);

public sealed class VerifierCorpusPromotionException : Exception
{
    public VerifierCorpusPromotionException(string message, IEnumerable<string>? details = null)
        : base(message)
    {
        Details = details?.ToArray() ?? [];
    }

    public IReadOnlyList<string> Details { get; }
}

public sealed class VerifierCorpusPromotionService
{
    public const string ModeValidateOnly = "validate_only";
    public const string ModeGenerate = "generate";
    public const string CorpusIndexFileName = "corpus-index.json";

    public async Task<VerifierCorpusPromotionResult> PromoteAsync(
        VerifierCorpusPromotionOptions options,
        CancellationToken cancellationToken = default)
    {
        var schemaErrors = VerifierCorpusContracts.ValidateSchemaSet(options.Paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new VerifierCorpusPromotionException("FEAT-135 schema set validation failed.", schemaErrors);
        }

        var sourceErrors = VerifierCorpusContracts.ValidateSourceFixtureSet(options.Paths);
        if (sourceErrors.Count > 0)
        {
            throw new VerifierCorpusPromotionException("FEAT-135 source fixture validation failed.", sourceErrors);
        }

        var publicOutputRoot = ValidatePublicOutputRoot(options.Paths.WorkspaceRoot, options.PublicOutputRoot);
        var repositoryRoot = options.ValidateOnly
            ? Path.Combine(Path.GetTempPath(), $"hush-verifier-corpus-validate-{Guid.NewGuid():N}")
            : publicOutputRoot;
        var corpusRepositoryRelativePath = BuildCorpusRepositoryRelativePath(options.CorpusVersion);
        var outputRoot = Path.Combine(
            repositoryRoot,
            corpusRepositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));

        var beforeWriteSnapshot = options.ValidateOnly && Directory.Exists(publicOutputRoot)
            ? SnapshotFileWriteTimes(publicOutputRoot)
            : new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!options.ValidateOnly)
            {
                PreparePublicRepositoryRoot(publicOutputRoot, outputRoot);
            }

            var generation = await new VerifierCorpusGenerator().GenerateAsync(
                new VerifierCorpusGenerationOptions(
                    outputRoot,
                    options.CorpusVersion,
                    options.GeneratedAt,
                    PublicRepositoryRef: options.PublicRepositoryRef,
                    VerifierSourceRef: options.VerifierSourceRef,
                    VerifierHash: options.VerifierHash,
                    WindowsReviewerReplayValidated: options.WindowsReviewerReplayValidated,
                    LinuxReviewerReplayValidated: options.LinuxReviewerReplayValidated,
                    RepositoryRelativePath: corpusRepositoryRelativePath),
                cancellationToken);

            if (generation.NoSecretScanStatus != "pass")
            {
                throw new VerifierCorpusPromotionException(
                    "FEAT-135 public corpus scan found unexpected public material.",
                    generation.ScanFindings
                        .Where(x => !x.ExpectedTamperFixture)
                        .Select(x => $"{x.RelativePath}:{x.Category}"));
            }

            if (options.ValidateOnly)
            {
                var afterSnapshot = Directory.Exists(publicOutputRoot)
                    ? SnapshotFileWriteTimes(publicOutputRoot)
                    : new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                if (!SnapshotsEqual(beforeWriteSnapshot, afterSnapshot))
                {
                    throw new VerifierCorpusPromotionException("Validate-only mode changed the public output root.");
                }
            }
            else
            {
                await WriteRepositoryRootFilesAsync(
                    publicOutputRoot,
                    corpusRepositoryRelativePath,
                    options,
                    generation,
                    cancellationToken);
            }

            return new VerifierCorpusPromotionResult(
                options.ValidateOnly ? ModeValidateOnly : ModeGenerate,
                options.ValidateOnly ? publicOutputRoot : outputRoot,
                options.CorpusVersion,
                options.GeneratedAt,
                generation.ManifestHash,
                generation.FixtureIndexHash,
                generation.NoSecretScanStatus,
                generation.Fixtures.Count,
                generation.ScanFindings.Count,
                generation.ScanFindings.Count(x => !x.ExpectedTamperFixture),
                options.ValidateOnly ? [] : EnumerateGeneratedFiles(publicOutputRoot));
        }
        finally
        {
            if (options.ValidateOnly && Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    public static string BuildCorpusRepositoryRelativePath(string corpusVersion) =>
        $"{VerifierCorpusGenerator.DefaultCorpusFamily}/{ValidateCorpusVersion(corpusVersion)}";

    private static string ValidateCorpusVersion(string corpusVersion)
    {
        if (string.IsNullOrWhiteSpace(corpusVersion))
        {
            throw new VerifierCorpusPromotionException("--corpus-version is required.");
        }

        var normalized = corpusVersion.Trim();
        if (normalized.Contains('/') || normalized.Contains('\\') || normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new VerifierCorpusPromotionException("Corpus version must be a simple version folder name.", [normalized]);
        }

        return normalized;
    }

    private static void PreparePublicRepositoryRoot(string publicOutputRoot, string versionOutputRoot)
    {
        Directory.CreateDirectory(publicOutputRoot);
        RemoveKnownGeneratedRootPaths(publicOutputRoot);

        if (Directory.Exists(versionOutputRoot))
        {
            EnsurePathUnder(publicOutputRoot, versionOutputRoot, "corpus version output root");
            Directory.Delete(versionOutputRoot, recursive: true);
        }
    }

    private static void RemoveKnownGeneratedRootPaths(string publicOutputRoot)
    {
        foreach (var relativePath in new[]
                 {
                     "packages",
                     "fixtures",
                     "expected-results",
                     "validation",
                     "readiness",
                     "handoff",
                     "corpus-manifest.json",
                 })
        {
            var path = Path.Combine(publicOutputRoot, relativePath);
            if (Directory.Exists(path))
            {
                EnsurePathUnder(publicOutputRoot, path, "legacy generated root directory");
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                EnsurePathUnder(publicOutputRoot, path, "legacy generated root file");
                File.Delete(path);
            }
        }
    }

    private static async Task WriteRepositoryRootFilesAsync(
        string publicOutputRoot,
        string corpusRepositoryRelativePath,
        VerifierCorpusPromotionOptions options,
        VerifierCorpusGenerationResult generation,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(publicOutputRoot);
        var index = BuildCorpusIndex(publicOutputRoot, corpusRepositoryRelativePath, options, generation);
        await File.WriteAllTextAsync(
            Path.Combine(publicOutputRoot, CorpusIndexFileName),
            VerifierCorpusGenerator.CanonicalJson(index),
            Encoding.UTF8,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(publicOutputRoot, "README.md"),
            BuildRepositoryReadme(corpusRepositoryRelativePath, generation),
            Encoding.UTF8,
            cancellationToken);
    }

    private static JsonObject BuildCorpusIndex(
        string publicOutputRoot,
        string corpusRepositoryRelativePath,
        VerifierCorpusPromotionOptions options,
        VerifierCorpusGenerationResult generation)
    {
        var versions = new JsonArray();
        var familyRoot = Path.Combine(publicOutputRoot, VerifierCorpusGenerator.DefaultCorpusFamily);
        if (Directory.Exists(familyRoot))
        {
            foreach (var manifestPath in Directory.EnumerateFiles(familyRoot, "corpus-manifest.json", SearchOption.AllDirectories)
                         .OrderBy(path => Path.GetRelativePath(publicOutputRoot, path), StringComparer.Ordinal))
            {
                var relativeManifestPath = Path.GetRelativePath(publicOutputRoot, manifestPath).Replace('\\', '/');
                var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
                versions.Add(new JsonObject
                {
                    ["corpusFamily"] = manifest["corpusFamily"]?.GetValue<string>() ?? VerifierCorpusGenerator.DefaultCorpusFamily,
                    ["corpusVersion"] = manifest["corpusVersion"]?.GetValue<string>() ?? Path.GetFileName(Path.GetDirectoryName(manifestPath)),
                    ["path"] = relativeManifestPath,
                    ["manifestHash"] = $"sha256:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))).ToLowerInvariant()}",
                    ["status"] = manifest["status"]?.GetValue<string>() ?? "unknown",
                    ["goodPackageHash"] = manifest["goodSample"]?["packageHash"]?.GetValue<string>() ?? string.Empty,
                    ["fixtureIndexHash"] = manifest["fixtureIndex"]?["sha256Hash"]?.GetValue<string>() ?? string.Empty,
                });
            }
        }

        return new JsonObject
        {
            ["schemaVersion"] = "hushvoting-verifier-corpus-index.v1",
            ["repository"] = "https://github.com/Hushnetwork-social/HushVoting-Verifier-Corpus",
            ["latest"] = new JsonObject
            {
                ["corpusFamily"] = VerifierCorpusGenerator.DefaultCorpusFamily,
                ["corpusVersion"] = options.CorpusVersion,
                ["path"] = $"{corpusRepositoryRelativePath}/corpus-manifest.json",
                ["manifestHash"] = generation.ManifestHash,
                ["status"] = generation.NoSecretScanStatus == "pass" ? "accepted" : "blocked",
                ["goodPackageHash"] = generation.GoodSample.PackageHash,
                ["fixtureIndexHash"] = generation.FixtureIndexHash,
            },
            ["versions"] = versions,
        };
    }

    private static string BuildRepositoryReadme(
        string corpusRepositoryRelativePath,
        VerifierCorpusGenerationResult generation) =>
        $$"""
        # HushVoting Verifier Corpus

        This repository contains versioned synthetic public verifier corpora for HushVoting.

        Current latest corpus:

        - path: `{{corpusRepositoryRelativePath}}/`
        - manifest: `{{corpusRepositoryRelativePath}}/corpus-manifest.json`
        - manifest hash: `{{generation.ManifestHash}}`
        - fixture index hash: `{{generation.FixtureIndexHash}}`

        Open the version folder README for runnable PowerShell and Bash verifier commands.

        Repository index:

        - `corpus-index.json`
        """;

    private static string ValidatePublicOutputRoot(string workspaceRoot, string publicOutputRoot)
    {
        if (string.IsNullOrWhiteSpace(publicOutputRoot))
        {
            throw new VerifierCorpusPromotionException("--public-output-root is required.");
        }

        var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        var fullOutputRoot = Path.GetFullPath(publicOutputRoot);
        EnsurePathUnder(fullWorkspaceRoot, fullOutputRoot, "public output root");

        if (!string.Equals(Path.GetFileName(fullOutputRoot), "HushVoting-Verifier-Corpus", StringComparison.OrdinalIgnoreCase))
        {
            throw new VerifierCorpusPromotionException(
                "Public output root must be the local HushVoting-Verifier-Corpus checkout.",
                [fullOutputRoot]);
        }

        return fullOutputRoot;
    }

    private static void EnsurePathUnder(string root, string path, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new VerifierCorpusPromotionException($"{label} escapes workspace root.", [fullPath]);
        }
    }

    private static IReadOnlyList<string> EnumerateGeneratedFiles(string outputRoot) =>
        Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(outputRoot, path).Replace('\\', '/').StartsWith(".git/", StringComparison.Ordinal))
            .OrderBy(path => Path.GetRelativePath(outputRoot, path), StringComparer.Ordinal)
            .ToArray();

    private static Dictionary<string, DateTime> SnapshotFileWriteTimes(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Replace('\\', '/').StartsWith(".git/", StringComparison.Ordinal))
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.GetLastWriteTimeUtc,
                StringComparer.OrdinalIgnoreCase);

    private static bool SnapshotsEqual(
        IReadOnlyDictionary<string, DateTime> before,
        IReadOnlyDictionary<string, DateTime> after)
    {
        if (before.Count != after.Count)
        {
            return false;
        }

        foreach (var (path, writeTime) in before)
        {
            if (!after.TryGetValue(path, out var afterWriteTime) || afterWriteTime != writeTime)
            {
                return false;
            }
        }

        return true;
    }
}
