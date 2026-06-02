namespace PublicationCountingReplayPromoter;

public sealed record PublicationCountingReplayPromotionOptions(
    PublicationCountingReplayPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly);

public sealed record PublicationCountingReplayPromotionResult(
    string Mode,
    string Status,
    string PackageRoot,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<string> CheckedFiles,
    PublicationCountingReplayGeneratedPackage GeneratedPackage);

public sealed class PublicationCountingReplayPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    private readonly IPublicationCountingReplayProfileRunner _profileRunner;

    public PublicationCountingReplayPromotionService(
        IPublicationCountingReplayProfileRunner? profileRunner = null)
    {
        _profileRunner = profileRunner ?? new PublicationCountingReplayProfileRunner();
    }

    public PublicationCountingReplayPromotionResult Promote(PublicationCountingReplayPromotionOptions options)
    {
        var mode = ResolveMode(options);
        var generated = PublicationCountingReplayArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            options.OutputRoot,
            options.GeneratedAt,
            _profileRunner);

        return mode switch
        {
            ModeValidateOnly => CreateResult(mode, generated, [], []),
            ModePackage => WritePackage(mode, generated),
            ModeCheckOnly => CheckPackage(mode, generated),
            _ => throw new PublicationCountingReplayPromotionException(
                "Unsupported FEAT-160 publication/counting replay promotion mode.",
                [$"Mode '{mode}' is not supported. Use validate-only, check-only, or package."]),
        };
    }

    private static string ResolveMode(PublicationCountingReplayPromotionOptions options) =>
        options.ValidateOnly
            ? ModeValidateOnly
            : (options.Mode ?? ModeCheckOnly).Trim().ToLowerInvariant();

    private static PublicationCountingReplayPromotionResult WritePackage(
        string mode,
        PublicationCountingReplayGeneratedPackage generated)
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

    private static PublicationCountingReplayPromotionResult CheckPackage(
        string mode,
        PublicationCountingReplayGeneratedPackage generated)
    {
        if (!Directory.Exists(generated.PackageRoot))
        {
            throw new PublicationCountingReplayPromotionException(
                "FEAT-160 publication/counting replay package check failed.",
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
            var observed = PublicationCountingReplayContracts.NormalizeLineEndings(File.ReadAllText(path));
            var expected = PublicationCountingReplayContracts.NormalizeLineEndings(artifact.Content);
            if (!string.Equals(observed, expected, StringComparison.Ordinal))
            {
                errors.Add($"Generated artifact drift: {artifact.RelativePath}");
            }
        }

        if (errors.Count > 0)
        {
            throw new PublicationCountingReplayPromotionException(
                "FEAT-160 publication/counting replay generated package drift detected.",
                errors);
        }

        return CreateResult(mode, generated, [], checkedFiles);
    }

    private static string ResolveArtifactPath(string packageRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        PublicationCountingReplayContracts.EnsurePathUnder(packageRoot, path, relativePath);
        return path;
    }

    private static PublicationCountingReplayPromotionResult CreateResult(
        string mode,
        PublicationCountingReplayGeneratedPackage generated,
        IReadOnlyList<string> writtenFiles,
        IReadOnlyList<string> checkedFiles) =>
        new(mode, generated.Status, generated.PackageRoot, writtenFiles, checkedFiles, generated);
}
