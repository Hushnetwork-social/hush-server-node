using System.Security.Cryptography;
using System.Text;

namespace PublicStateElectionPrerequisiteRegisterPromoter;

public sealed record PublicStateElectionPrerequisitePromotionOptions(
    PublicStateElectionPrerequisitePromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly);

public sealed record PublicStateElectionPrerequisitePromotionResult(
    string Mode,
    string Status,
    string PackageRoot,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<string> CheckedFiles,
    PublicStateGeneratedPackage GeneratedPackage);

public sealed class PublicStateElectionPrerequisitePromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public PublicStateElectionPrerequisitePromotionResult Promote(
        PublicStateElectionPrerequisitePromotionOptions options)
    {
        ValidateSchemas(options.Paths);
        var mode = ResolveMode(options);
        var generated = PublicStateElectionPrerequisiteArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            options.GeneratedAt);
        var packageRoot = ResolvePackageRoot(options);

        return mode switch
        {
            ModeValidateOnly => CreateResult(mode, packageRoot, [], [], generated),
            ModePackage => WritePackage(mode, packageRoot, generated),
            ModeCheckOnly => CheckPackage(mode, packageRoot, generated),
            _ => throw new PublicStateElectionPrerequisitePromotionException(
                "Unsupported FEAT-149 public/state prerequisite promotion mode.",
                [$"Mode '{mode}' is not supported. Use validate-only, package, or check-only."]),
        };
    }

    private static void ValidateSchemas(PublicStateElectionPrerequisitePromotionPaths paths)
    {
        var schemaErrors = PublicStateElectionPrerequisiteContracts.ValidateSchemaSet(paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new PublicStateElectionPrerequisitePromotionException(
                "FEAT-149 public/state prerequisite schema validation failed.",
                schemaErrors);
        }
    }

    private static string ResolveMode(PublicStateElectionPrerequisitePromotionOptions options) =>
        options.ValidateOnly
            ? ModeValidateOnly
            : (options.Mode ?? ModeCheckOnly).Trim().ToLowerInvariant();

    private static string ResolvePackageRoot(PublicStateElectionPrerequisitePromotionOptions options) =>
        Path.GetFullPath(options.OutputRoot ?? options.Paths.OutputRoot);

    private static PublicStateElectionPrerequisitePromotionResult WritePackage(
        string mode,
        string packageRoot,
        PublicStateGeneratedPackage generated)
    {
        Directory.CreateDirectory(packageRoot);
        var writtenFiles = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = ResolveArtifactPath(packageRoot, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, artifact.Content);
            writtenFiles.Add(path);
        }

        return CreateResult(mode, packageRoot, writtenFiles, [], generated);
    }

    private static PublicStateElectionPrerequisitePromotionResult CheckPackage(
        string mode,
        string packageRoot,
        PublicStateGeneratedPackage generated)
    {
        if (!Directory.Exists(packageRoot))
        {
            throw new PublicStateElectionPrerequisitePromotionException(
                "FEAT-149 public/state prerequisite package check failed.",
                [$"Package root does not exist: {packageRoot}"]);
        }

        var errors = new List<string>();
        var checkedFiles = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = ResolveArtifactPath(packageRoot, artifact.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Missing package artifact: {artifact.RelativePath}");
                continue;
            }

            checkedFiles.Add(path);
            var observedHash = Sha256Hex(File.ReadAllText(path));
            if (!string.Equals(observedHash, artifact.Sha256Hash, StringComparison.Ordinal))
            {
                errors.Add($"Hash mismatch for {artifact.RelativePath}: expected {artifact.Sha256Hash}, observed {observedHash}");
            }
        }

        if (errors.Count > 0)
        {
            throw new PublicStateElectionPrerequisitePromotionException(
                "FEAT-149 public/state prerequisite package check failed.",
                errors);
        }

        return CreateResult(mode, packageRoot, [], checkedFiles, generated);
    }

    private static string ResolveArtifactPath(string packageRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
        PublicStateElectionPrerequisiteContracts.EnsurePathUnder(packageRoot, path, relativePath);
        return path;
    }

    private static PublicStateElectionPrerequisitePromotionResult CreateResult(
        string mode,
        string packageRoot,
        IReadOnlyList<string> writtenFiles,
        IReadOnlyList<string> checkedFiles,
        PublicStateGeneratedPackage generated) =>
        new(mode, generated.Status, packageRoot, writtenFiles, checkedFiles, generated);

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
