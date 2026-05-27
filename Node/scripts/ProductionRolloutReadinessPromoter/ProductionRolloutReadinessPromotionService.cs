namespace ProductionRolloutReadinessPromoter;

public sealed record ProductionRolloutReadinessPromotionOptions(
    ProductionRolloutReadinessPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly);

public sealed record ProductionRolloutReadinessPromotionResult(
    string Mode,
    string Status,
    string PackageRoot,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<string> CheckedFiles,
    ProductionRolloutGeneratedPackage GeneratedPackage);

public sealed class ProductionRolloutReadinessPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public ProductionRolloutReadinessPromotionResult Promote(ProductionRolloutReadinessPromotionOptions options)
    {
        ValidateSchemas(options.Paths);
        var mode = ResolveMode(options);
        var generated = ProductionRolloutReadinessArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            options.GeneratedAt);
        var packageRoot = ResolvePackageRoot(options);

        return mode switch
        {
            ModeValidateOnly => CreateResult(mode, packageRoot, [], [], generated),
            ModePackage => WritePackage(mode, packageRoot, generated),
            ModeCheckOnly => CheckPackage(mode, packageRoot, generated),
            _ => throw new ProductionRolloutReadinessPromotionException(
                "Unsupported FEAT-148 production rollout promotion mode.",
                [$"Mode '{mode}' is not supported. Use validate-only, package, or check-only."]),
        };
    }

    private static void ValidateSchemas(ProductionRolloutReadinessPromotionPaths paths)
    {
        var schemaErrors = ProductionRolloutReadinessContracts.ValidateSchemaSet(paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new ProductionRolloutReadinessPromotionException(
                "FEAT-148 production rollout schema validation failed.",
                schemaErrors);
        }
    }

    private static string ResolveMode(ProductionRolloutReadinessPromotionOptions options) =>
        options.ValidateOnly
            ? ModeValidateOnly
            : (options.Mode ?? ModeCheckOnly).Trim().ToLowerInvariant();

    private static string ResolvePackageRoot(ProductionRolloutReadinessPromotionOptions options)
    {
        var outputRoot = Path.GetFullPath(options.OutputRoot ?? options.Paths.OutputRoot);
        var packageRoot = Path.GetFullPath(Path.Combine(outputRoot, "package"));
        ProductionRolloutReadinessContracts.EnsurePathUnder(outputRoot, packageRoot, "production rollout package root");
        return packageRoot;
    }

    private static ProductionRolloutReadinessPromotionResult WritePackage(
        string mode,
        string packageRoot,
        ProductionRolloutGeneratedPackage generated)
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

    private static ProductionRolloutReadinessPromotionResult CheckPackage(
        string mode,
        string packageRoot,
        ProductionRolloutGeneratedPackage generated)
    {
        if (!Directory.Exists(packageRoot))
        {
            throw new ProductionRolloutReadinessPromotionException(
                "FEAT-148 production rollout package check failed.",
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
            var observedHash = ProductionRolloutReadinessContracts.Sha256Hex(File.ReadAllText(path));
            if (!string.Equals(observedHash, artifact.Sha256Hash, StringComparison.Ordinal))
            {
                errors.Add($"Hash mismatch for {artifact.RelativePath}: expected {artifact.Sha256Hash}, observed {observedHash}");
            }
        }

        if (errors.Count > 0)
        {
            throw new ProductionRolloutReadinessPromotionException(
                "FEAT-148 production rollout package check failed.",
                errors);
        }

        return CreateResult(mode, packageRoot, [], checkedFiles, generated);
    }

    private static string ResolveArtifactPath(string packageRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
        ProductionRolloutReadinessContracts.EnsurePathUnder(packageRoot, path, relativePath);
        return path;
    }

    private static ProductionRolloutReadinessPromotionResult CreateResult(
        string mode,
        string packageRoot,
        IReadOnlyList<string> writtenFiles,
        IReadOnlyList<string> checkedFiles,
        ProductionRolloutGeneratedPackage generated) =>
        new(mode, generated.Status, packageRoot, writtenFiles, checkedFiles, generated);
}
