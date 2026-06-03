namespace SecondProductionLikeOperationalRunPromoter;

public sealed record SecondProductionLikeOperationalRunPromotionOptions(
    SecondProductionLikeOperationalRunPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly,
    bool PublicOnly = false);

public sealed record SecondProductionLikeOperationalRunPromotionResult(
    string Mode,
    string Status,
    string PackageRoot,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<string> CheckedFiles,
    SecondProductionLikeOperationalRunGeneratedPackage GeneratedPackage);

public sealed class SecondProductionLikeOperationalRunPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public SecondProductionLikeOperationalRunPromotionResult Promote(SecondProductionLikeOperationalRunPromotionOptions options)
    {
        var mode = ResolveMode(options);
        var generated = SecondProductionLikeOperationalRunArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            options.OutputRoot,
            options.GeneratedAt,
            options.PublicOnly);

        return mode switch
        {
            ModeValidateOnly => CreateResult(mode, generated, [], []),
            ModePackage => WritePackage(mode, generated),
            ModeCheckOnly => CheckPackage(mode, generated),
            _ => throw new SecondProductionLikeOperationalRunPromotionException(
                "Unsupported FEAT-163 second production-like run promotion mode.",
                [$"Mode '{mode}' is not supported. Use validate-only, check-only, or package."]),
        };
    }

    private static string ResolveMode(SecondProductionLikeOperationalRunPromotionOptions options) =>
        options.ValidateOnly
            ? ModeValidateOnly
            : (options.Mode ?? ModeCheckOnly).Trim().ToLowerInvariant();

    private static SecondProductionLikeOperationalRunPromotionResult WritePackage(
        string mode,
        SecondProductionLikeOperationalRunGeneratedPackage generated)
    {
        Directory.CreateDirectory(generated.PackageRoot);
        var writtenFiles = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = ResolveArtifactPath(generated.PackageRoot, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, artifact.Content);
            writtenFiles.Add(path);
        }

        return CreateResult(mode, generated, writtenFiles, []);
    }

    private static SecondProductionLikeOperationalRunPromotionResult CheckPackage(
        string mode,
        SecondProductionLikeOperationalRunGeneratedPackage generated)
    {
        if (!Directory.Exists(generated.PackageRoot))
        {
            throw new SecondProductionLikeOperationalRunPromotionException(
                "FEAT-163 second production-like run package check failed.",
                [$"Package root does not exist: {generated.PackageRoot}"]);
        }

        var errors = new List<string>();
        var checkedFiles = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = ResolveArtifactPath(generated.PackageRoot, artifact.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Missing generated artifact: {artifact.RelativePath}");
                continue;
            }

            checkedFiles.Add(path);
            var observed = SecondProductionLikeOperationalRunContracts.NormalizeLineEndings(File.ReadAllText(path));
            var expected = SecondProductionLikeOperationalRunContracts.NormalizeLineEndings(artifact.Content);
            if (!string.Equals(observed, expected, StringComparison.Ordinal))
            {
                errors.Add($"Generated artifact drift: {artifact.RelativePath}");
            }
        }

        if (errors.Count > 0)
        {
            throw new SecondProductionLikeOperationalRunPromotionException(
                "FEAT-163 second production-like run generated package drift detected.",
                errors);
        }

        return CreateResult(mode, generated, [], checkedFiles);
    }

    private static string ResolveArtifactPath(string packageRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        SecondProductionLikeOperationalRunContracts.EnsurePathUnder(packageRoot, path, relativePath);
        return path;
    }

    private static SecondProductionLikeOperationalRunPromotionResult CreateResult(
        string mode,
        SecondProductionLikeOperationalRunGeneratedPackage generated,
        IReadOnlyList<string> writtenFiles,
        IReadOnlyList<string> checkedFiles) =>
        new(mode, generated.Status, generated.PackageRoot, writtenFiles, checkedFiles, generated);
}
