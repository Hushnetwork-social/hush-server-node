namespace DisputeContinuityReadinessPromoter;

public sealed record DisputeContinuityReadinessPromotionOptions(
    DisputeContinuityReadinessPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly);

public sealed record DisputeContinuityReadinessPromotionResult(
    string Mode,
    string Status,
    IReadOnlyList<string> WrittenFiles,
    DisputeContinuityGeneratedPackage GeneratedPackage);

public sealed class DisputeContinuityReadinessPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public DisputeContinuityReadinessPromotionResult Promote(DisputeContinuityReadinessPromotionOptions options)
    {
        var schemaErrors = DisputeContinuityReadinessContracts.ValidateSchemaSet(options.Paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new DisputeContinuityReadinessPromotionException(
                "FEAT-139 dispute continuity readiness schema validation failed.",
                schemaErrors);
        }

        var mode = options.ValidateOnly ? ModeValidateOnly : options.Mode ?? ModeCheckOnly;
        var generated = DisputeContinuityReadinessArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            options.GeneratedAt);

        if (mode is not ModePackage)
        {
            return new DisputeContinuityReadinessPromotionResult(mode, generated.Status, [], generated);
        }

        var outputRoot = Path.GetFullPath(options.OutputRoot ?? options.Paths.OutputRoot);
        var packageRoot = Path.Combine(outputRoot, "package");
        Directory.CreateDirectory(packageRoot);
        var writtenFiles = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = Path.GetFullPath(Path.Combine(packageRoot, artifact.RelativePath));
            DisputeContinuityReadinessContracts.EnsurePathUnder(packageRoot, path, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, artifact.Content);
            writtenFiles.Add(path);
        }

        return new DisputeContinuityReadinessPromotionResult(mode, generated.Status, writtenFiles, generated);
    }
}
