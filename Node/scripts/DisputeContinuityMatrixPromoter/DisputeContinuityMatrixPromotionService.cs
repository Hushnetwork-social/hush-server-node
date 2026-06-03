namespace DisputeContinuityMatrixPromoter;

public sealed record DisputeContinuityMatrixPromotionOptions(
    DisputeContinuityMatrixPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly,
    bool PublicOnly = false);

public sealed record DisputeContinuityMatrixPromotionResult(
    string Mode,
    string Status,
    string PackageRoot,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<string> CheckedFiles,
    DisputeContinuityMatrixGeneratedPackage GeneratedPackage);

public sealed class DisputeContinuityMatrixPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public DisputeContinuityMatrixPromotionResult Promote(DisputeContinuityMatrixPromotionOptions options)
    {
        var mode = ResolveMode(options);
        var generated = DisputeContinuityMatrixArtifactGenerator.Generate(
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
            _ => throw new DisputeContinuityMatrixPromotionException(
                "Unsupported FEAT-165 dispute continuity matrix promotion mode.",
                [$"Mode '{mode}' is not supported. Use validate-only, check-only, or package."]),
        };
    }

    private static string ResolveMode(DisputeContinuityMatrixPromotionOptions options) =>
        options.ValidateOnly
            ? ModeValidateOnly
            : (options.Mode ?? ModeCheckOnly).Trim().ToLowerInvariant();

    private static DisputeContinuityMatrixPromotionResult WritePackage(
        string mode,
        DisputeContinuityMatrixGeneratedPackage generated)
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

        foreach (var artifact in generated.RestrictedArtifacts)
        {
            var path = ResolveArtifactPath(generated.RestrictedIndexRoot, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, artifact.Content);
            writtenFiles.Add(path);
        }

        return CreateResult(mode, generated, writtenFiles, []);
    }

    private static DisputeContinuityMatrixPromotionResult CheckPackage(
        string mode,
        DisputeContinuityMatrixGeneratedPackage generated)
    {
        if (!Directory.Exists(generated.PackageRoot))
        {
            throw new DisputeContinuityMatrixPromotionException(
                "FEAT-165 dispute continuity matrix package check failed.",
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
            var observed = DisputeContinuityMatrixContracts.NormalizeLineEndings(File.ReadAllText(path));
            var expected = DisputeContinuityMatrixContracts.NormalizeLineEndings(artifact.Content);
            if (!string.Equals(observed, expected, StringComparison.Ordinal))
            {
                errors.Add($"Generated artifact drift: {artifact.RelativePath}");
            }
        }

        foreach (var artifact in generated.RestrictedArtifacts)
        {
            var path = ResolveArtifactPath(generated.RestrictedIndexRoot, artifact.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Missing generated restricted artifact: {artifact.RelativePath}");
                continue;
            }

            checkedFiles.Add(path);
            var observed = DisputeContinuityMatrixContracts.NormalizeLineEndings(File.ReadAllText(path));
            var expected = DisputeContinuityMatrixContracts.NormalizeLineEndings(artifact.Content);
            if (!string.Equals(observed, expected, StringComparison.Ordinal))
            {
                errors.Add($"Generated restricted artifact drift: {artifact.RelativePath}");
            }
        }

        if (errors.Count > 0)
        {
            throw new DisputeContinuityMatrixPromotionException(
                "FEAT-165 dispute continuity matrix generated package drift detected.",
                errors);
        }

        return CreateResult(mode, generated, [], checkedFiles);
    }

    private static string ResolveArtifactPath(string packageRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        DisputeContinuityMatrixContracts.EnsurePathUnder(packageRoot, path, relativePath);
        return path;
    }

    private static DisputeContinuityMatrixPromotionResult CreateResult(
        string mode,
        DisputeContinuityMatrixGeneratedPackage generated,
        IReadOnlyList<string> writtenFiles,
        IReadOnlyList<string> checkedFiles) =>
        new(mode, generated.Status, generated.PackageRoot, writtenFiles, checkedFiles, generated);
}
