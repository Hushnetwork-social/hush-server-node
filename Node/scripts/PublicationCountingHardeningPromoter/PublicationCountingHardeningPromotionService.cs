namespace PublicationCountingHardeningPromoter;

public sealed record PublicationCountingHardeningPromotionOptions(
    PublicationCountingHardeningPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly);

public sealed record PublicationCountingHardeningPromotionResult(
    string Mode,
    string Status,
    string PackageRoot,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<string> CheckedFiles,
    PublicationCountingHardeningGeneratedPackage GeneratedPackage);

public sealed class PublicationCountingHardeningPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public PublicationCountingHardeningPromotionResult Promote(PublicationCountingHardeningPromotionOptions options)
    {
        var mode = ResolveMode(options);
        var generated = PublicationCountingHardeningArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            options.OutputRoot,
            options.GeneratedAt);

        return mode switch
        {
            ModeValidateOnly => CreateResult(mode, generated, [], []),
            ModeCheckOnly => CheckPackage(mode, generated),
            ModePackage => WritePackage(mode, generated),
            _ => throw new PublicationCountingHardeningPromotionException(
                "Unsupported FEAT-153 publication/counting promotion mode.",
                [$"Mode '{mode}' is not supported. Use validate-only, check-only, or package."]),
        };
    }

    private static string ResolveMode(PublicationCountingHardeningPromotionOptions options) =>
        options.ValidateOnly
            ? ModeValidateOnly
            : (options.Mode ?? ModeCheckOnly).Trim().ToLowerInvariant();

    private static PublicationCountingHardeningPromotionResult WritePackage(
        string mode,
        PublicationCountingHardeningGeneratedPackage generated)
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

    private static PublicationCountingHardeningPromotionResult CheckPackage(
        string mode,
        PublicationCountingHardeningGeneratedPackage generated)
    {
        if (!Directory.Exists(generated.PackageRoot))
        {
            return CreateResult(mode, generated, [], generated.Artifacts.Select(artifact => artifact.RelativePath).ToArray());
        }

        var drift = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = ResolveArtifactPath(generated.PackageRoot, artifact.RelativePath);
            if (!File.Exists(path))
            {
                drift.Add($"Missing generated artifact: {artifact.RelativePath}");
                continue;
            }

            var observed = PublicationCountingHardeningContracts.NormalizeLineEndings(File.ReadAllText(path));
            var expected = PublicationCountingHardeningContracts.NormalizeLineEndings(artifact.Content);
            if (!string.Equals(observed, expected, StringComparison.Ordinal))
            {
                drift.Add($"Generated artifact drift: {artifact.RelativePath}");
            }
        }

        if (drift.Count > 0)
        {
            throw new PublicationCountingHardeningPromotionException(
                "FEAT-153 publication/counting generated package drift detected.",
                drift);
        }

        return CreateResult(mode, generated, [], generated.Artifacts.Select(artifact => artifact.RelativePath).ToArray());
    }

    private static string ResolveArtifactPath(string packageRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        PublicationCountingHardeningContracts.EnsurePathUnder(packageRoot, path, relativePath);
        return path;
    }

    private static PublicationCountingHardeningPromotionResult CreateResult(
        string mode,
        PublicationCountingHardeningGeneratedPackage generated,
        IReadOnlyList<string> writtenFiles,
        IReadOnlyList<string> checkedFiles) =>
        new(mode, generated.Status, generated.PackageRoot, writtenFiles, checkedFiles, generated);
}
