namespace RetentionLogPrivacyRecurringScanPromoter;

public sealed record RetentionLogPrivacyRecurringScanPromotionOptions(
    RetentionLogPrivacyRecurringScanPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly,
    bool PublicOnly = false);

public sealed record RetentionLogPrivacyRecurringScanPromotionResult(
    string Mode,
    string Status,
    string PackageRoot,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<string> CheckedFiles,
    RetentionLogPrivacyRecurringScanGeneratedPackage GeneratedPackage);

public sealed class RetentionLogPrivacyRecurringScanPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public RetentionLogPrivacyRecurringScanPromotionResult Promote(RetentionLogPrivacyRecurringScanPromotionOptions options)
    {
        var mode = ResolveMode(options);
        var generated = RetentionLogPrivacyRecurringScanArtifactGenerator.Generate(
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
            _ => throw new RetentionLogPrivacyRecurringScanPromotionException(
                "Unsupported FEAT-164 retention/log privacy recurring scan promotion mode.",
                [$"Mode '{mode}' is not supported. Use validate-only, check-only, or package."]),
        };
    }

    private static string ResolveMode(RetentionLogPrivacyRecurringScanPromotionOptions options) =>
        options.ValidateOnly
            ? ModeValidateOnly
            : (options.Mode ?? ModeCheckOnly).Trim().ToLowerInvariant();

    private static RetentionLogPrivacyRecurringScanPromotionResult WritePackage(
        string mode,
        RetentionLogPrivacyRecurringScanGeneratedPackage generated)
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

    private static RetentionLogPrivacyRecurringScanPromotionResult CheckPackage(
        string mode,
        RetentionLogPrivacyRecurringScanGeneratedPackage generated)
    {
        if (!Directory.Exists(generated.PackageRoot))
        {
            throw new RetentionLogPrivacyRecurringScanPromotionException(
                "FEAT-164 retention/log privacy recurring scan package check failed.",
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
            var observed = RetentionLogPrivacyRecurringScanContracts.NormalizeLineEndings(File.ReadAllText(path));
            var expected = RetentionLogPrivacyRecurringScanContracts.NormalizeLineEndings(artifact.Content);
            if (!string.Equals(observed, expected, StringComparison.Ordinal))
            {
                errors.Add($"Generated artifact drift: {artifact.RelativePath}");
            }
        }

        if (errors.Count > 0)
        {
            throw new RetentionLogPrivacyRecurringScanPromotionException(
                "FEAT-164 retention/log privacy recurring scan generated package drift detected.",
                errors);
        }

        return CreateResult(mode, generated, [], checkedFiles);
    }

    private static string ResolveArtifactPath(string packageRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        RetentionLogPrivacyRecurringScanContracts.EnsurePathUnder(packageRoot, path, relativePath);
        return path;
    }

    private static RetentionLogPrivacyRecurringScanPromotionResult CreateResult(
        string mode,
        RetentionLogPrivacyRecurringScanGeneratedPackage generated,
        IReadOnlyList<string> writtenFiles,
        IReadOnlyList<string> checkedFiles) =>
        new(mode, generated.Status, generated.PackageRoot, writtenFiles, checkedFiles, generated);
}
