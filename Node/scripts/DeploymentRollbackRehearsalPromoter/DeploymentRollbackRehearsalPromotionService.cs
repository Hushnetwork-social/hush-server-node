namespace DeploymentRollbackRehearsalPromoter;

public sealed record DeploymentRollbackRehearsalPromotionOptions(
    DeploymentRollbackRehearsalPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly,
    bool PublicOnly = false);

public sealed record DeploymentRollbackRehearsalPromotionResult(
    string Mode,
    string Status,
    string PackageRoot,
    string SecondCeremonyRoot,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<string> CheckedFiles,
    DeploymentRollbackRehearsalGeneratedPackage GeneratedPackage);

public sealed class DeploymentRollbackRehearsalPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public DeploymentRollbackRehearsalPromotionResult Promote(DeploymentRollbackRehearsalPromotionOptions options)
    {
        var mode = ResolveMode(options);
        var generated = DeploymentRollbackRehearsalArtifactGenerator.Generate(
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
            _ => throw new DeploymentRollbackRehearsalPromotionException(
                "Unsupported FEAT-162 deployment rollback rehearsal promotion mode.",
                [$"Mode '{mode}' is not supported. Use validate-only, check-only, or package."]),
        };
    }

    private static string ResolveMode(DeploymentRollbackRehearsalPromotionOptions options) =>
        options.ValidateOnly
            ? ModeValidateOnly
            : (options.Mode ?? ModeCheckOnly).Trim().ToLowerInvariant();

    private static DeploymentRollbackRehearsalPromotionResult WritePackage(
        string mode,
        DeploymentRollbackRehearsalGeneratedPackage generated)
    {
        var writtenFiles = new List<string>();
        WriteArtifacts(generated.PackageRoot, generated.Artifacts, writtenFiles);
        WriteArtifacts(generated.SecondCeremonyRoot, generated.SecondCeremonyArtifacts, writtenFiles);

        return CreateResult(mode, generated, writtenFiles, []);
    }

    private static DeploymentRollbackRehearsalPromotionResult CheckPackage(
        string mode,
        DeploymentRollbackRehearsalGeneratedPackage generated)
    {
        if (!Directory.Exists(generated.PackageRoot))
        {
            throw new DeploymentRollbackRehearsalPromotionException(
                "FEAT-162 deployment rollback rehearsal package check failed.",
                [$"Package root does not exist: {generated.PackageRoot}"]);
        }

        var errors = new List<string>();
        var checkedFiles = new List<string>();
        CheckArtifacts(generated.PackageRoot, generated.Artifacts, errors, checkedFiles);
        CheckArtifacts(generated.SecondCeremonyRoot, generated.SecondCeremonyArtifacts, errors, checkedFiles);

        if (errors.Count > 0)
        {
            throw new DeploymentRollbackRehearsalPromotionException(
                "FEAT-162 deployment rollback rehearsal generated package drift detected.",
                errors);
        }

        return CreateResult(mode, generated, [], checkedFiles);
    }

    private static void WriteArtifacts(
        string root,
        IReadOnlyList<DeploymentRollbackRehearsalArtifact> artifacts,
        List<string> writtenFiles)
    {
        Directory.CreateDirectory(root);
        foreach (var artifact in artifacts)
        {
            var path = ResolveArtifactPath(root, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, artifact.Content);
            writtenFiles.Add(path);
        }
    }

    private static void CheckArtifacts(
        string root,
        IReadOnlyList<DeploymentRollbackRehearsalArtifact> artifacts,
        List<string> errors,
        List<string> checkedFiles)
    {
        if (!Directory.Exists(root))
        {
            errors.Add($"Output root does not exist: {root}");
            return;
        }

        foreach (var artifact in artifacts)
        {
            var path = ResolveArtifactPath(root, artifact.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Missing generated artifact: {artifact.RelativePath}");
                continue;
            }

            checkedFiles.Add(path);
            var observed = DeploymentRollbackRehearsalContracts.NormalizeLineEndings(File.ReadAllText(path));
            var expected = DeploymentRollbackRehearsalContracts.NormalizeLineEndings(artifact.Content);
            if (!string.Equals(observed, expected, StringComparison.Ordinal))
            {
                errors.Add($"Generated artifact drift: {artifact.RelativePath}");
            }
        }
    }

    private static string ResolveArtifactPath(string packageRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        DeploymentRollbackRehearsalContracts.EnsurePathUnder(packageRoot, path, relativePath);
        return path;
    }

    private static DeploymentRollbackRehearsalPromotionResult CreateResult(
        string mode,
        DeploymentRollbackRehearsalGeneratedPackage generated,
        IReadOnlyList<string> writtenFiles,
        IReadOnlyList<string> checkedFiles) =>
        new(mode, generated.Status, generated.PackageRoot, generated.SecondCeremonyRoot, writtenFiles, checkedFiles, generated);
}
