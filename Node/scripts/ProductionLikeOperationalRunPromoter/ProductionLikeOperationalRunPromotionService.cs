using System.Globalization;
using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public sealed record ProductionLikeOperationalRunPromotionOptions(
    ProductionLikeOperationalRunPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly);

public sealed record ProductionLikeOperationalRunPromotionResult(
    string Mode,
    string Status,
    string PackageRoot,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<string> CheckedFiles,
    ProductionLikeOperationalRunGeneratedPackage GeneratedPackage);

public sealed class ProductionLikeOperationalRunPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public ProductionLikeOperationalRunPromotionResult Promote(ProductionLikeOperationalRunPromotionOptions options)
    {
        ValidateSchemas(options.Paths);
        var mode = ResolveMode(options);
        var packageRoot = ResolvePackageRoot(options);
        var generatedAt = ResolveGeneratedAt(options, mode, packageRoot);
        var generated = ProductionLikeOperationalRunArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            generatedAt);

        return mode switch
        {
            ModeValidateOnly => CreateResult(mode, packageRoot, [], [], generated),
            ModePackage => WritePackage(mode, packageRoot, generated),
            ModeCheckOnly => CheckPackage(mode, packageRoot, generated),
            _ => throw new ProductionLikeOperationalRunPromotionException(
                "Unsupported FEAT-154 production-like operational run promotion mode.",
                [$"Mode '{mode}' is not supported. Use validate-only, package, or check-only."]),
        };
    }

    private static void ValidateSchemas(ProductionLikeOperationalRunPromotionPaths paths)
    {
        var schemaErrors = ProductionLikeOperationalRunContracts.ValidateSchemaSet(paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new ProductionLikeOperationalRunPromotionException(
                "FEAT-154 production-like operational run schema validation failed.",
                schemaErrors);
        }
    }

    private static string ResolveMode(ProductionLikeOperationalRunPromotionOptions options) =>
        options.ValidateOnly
            ? ModeValidateOnly
            : (options.Mode ?? ModeCheckOnly).Trim().ToLowerInvariant();

    private static DateTimeOffset? ResolveGeneratedAt(
        ProductionLikeOperationalRunPromotionOptions options,
        string mode,
        string packageRoot) =>
        options.GeneratedAt ?? (mode == ModeCheckOnly
            ? TryReadExistingManifestGeneratedAt(packageRoot)
            : null);

    private static DateTimeOffset? TryReadExistingManifestGeneratedAt(string packageRoot)
    {
        var manifestPath = Path.Combine(packageRoot, ProductionLikeOperationalRunArtifactGenerator.ManifestPath);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
        var generatedAt = ProductionLikeOperationalRunContracts.GetString(manifest, "generatedAt");
        if (string.IsNullOrWhiteSpace(generatedAt))
        {
            throw new ProductionLikeOperationalRunPromotionException(
                "FEAT-154 production-like operational run package check failed.",
                [$"Existing manifest is missing generatedAt: {manifestPath}"]);
        }

        return DateTimeOffset.Parse(
            generatedAt,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static string ResolvePackageRoot(ProductionLikeOperationalRunPromotionOptions options)
    {
        var outputRoot = ResolveOutputRoot(options);
        var packageRoot = Path.GetFullPath(Path.Combine(outputRoot, "package"));
        ProductionLikeOperationalRunContracts.EnsurePathUnder(outputRoot, packageRoot, "production-like operational run package root");
        return packageRoot;
    }

    private static string ResolveOutputRoot(ProductionLikeOperationalRunPromotionOptions options)
    {
        var outputRoot = string.IsNullOrWhiteSpace(options.OutputRoot)
            ? options.Paths.OutputRoot
            : options.OutputRoot;
        var combined = Path.IsPathRooted(outputRoot)
            ? outputRoot
            : Path.Combine(options.Paths.WorkspaceRoot, outputRoot);
        var fullOutputRoot = Path.GetFullPath(combined);
        ProductionLikeOperationalRunContracts.EnsurePathUnder(
            options.Paths.WorkspaceRoot,
            fullOutputRoot,
            "production-like operational run output root");
        return fullOutputRoot;
    }

    private static ProductionLikeOperationalRunPromotionResult WritePackage(
        string mode,
        string packageRoot,
        ProductionLikeOperationalRunGeneratedPackage generated)
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

    private static ProductionLikeOperationalRunPromotionResult CheckPackage(
        string mode,
        string packageRoot,
        ProductionLikeOperationalRunGeneratedPackage generated)
    {
        if (!Directory.Exists(packageRoot))
        {
            throw new ProductionLikeOperationalRunPromotionException(
                "FEAT-154 production-like operational run package check failed.",
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
            var observedHash = ProductionLikeOperationalRunContracts.Sha256Hex(File.ReadAllText(path));
            if (!string.Equals(observedHash, artifact.Sha256Hash, StringComparison.Ordinal))
            {
                errors.Add($"Hash mismatch for {artifact.RelativePath}: expected {artifact.Sha256Hash}, observed {observedHash}");
            }
        }

        if (errors.Count > 0)
        {
            throw new ProductionLikeOperationalRunPromotionException(
                "FEAT-154 production-like operational run package check failed.",
                errors);
        }

        return CreateResult(mode, packageRoot, [], checkedFiles, generated);
    }

    private static string ResolveArtifactPath(string packageRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
        ProductionLikeOperationalRunContracts.EnsurePathUnder(packageRoot, path, relativePath);
        return path;
    }

    private static ProductionLikeOperationalRunPromotionResult CreateResult(
        string mode,
        string packageRoot,
        IReadOnlyList<string> writtenFiles,
        IReadOnlyList<string> checkedFiles,
        ProductionLikeOperationalRunGeneratedPackage generated) =>
        new(mode, generated.Status, packageRoot, writtenFiles, checkedFiles, generated);
}
