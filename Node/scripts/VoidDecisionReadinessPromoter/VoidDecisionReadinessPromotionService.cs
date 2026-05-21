namespace VoidDecisionReadinessPromoter;

public sealed record VoidDecisionReadinessPromotionOptions(
    VoidDecisionReadinessPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly);

public sealed record VoidDecisionReadinessPromotionResult(
    string Mode,
    string Status,
    IReadOnlyList<string> WrittenFiles,
    VoidDecisionReadinessGeneratedPackage GeneratedPackage);

public sealed class VoidDecisionReadinessPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public VoidDecisionReadinessPromotionResult Promote(VoidDecisionReadinessPromotionOptions options)
    {
        var schemaErrors = VoidDecisionReadinessContracts.ValidateSchemaSet(options.Paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new VoidDecisionReadinessPromotionException(
                "FEAT-138 void readiness schema validation failed.",
                schemaErrors);
        }

        var mode = options.ValidateOnly ? ModeValidateOnly : options.Mode ?? ModeCheckOnly;
        var generated = VoidDecisionReadinessArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            options.GeneratedAt);

        if (mode is not ModePackage)
        {
            return new VoidDecisionReadinessPromotionResult(mode, generated.Status, [], generated);
        }

        var outputRoot = Path.GetFullPath(options.OutputRoot ?? options.Paths.OutputRoot);
        var packageRoot = Path.Combine(outputRoot, "release-baseline");
        Directory.CreateDirectory(packageRoot);
        var writtenFiles = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = Path.GetFullPath(Path.Combine(packageRoot, artifact.RelativePath));
            VoidDecisionReadinessContracts.EnsurePathUnder(packageRoot, path, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, artifact.Content);
            writtenFiles.Add(path);
        }

        return new VoidDecisionReadinessPromotionResult(mode, generated.Status, writtenFiles, generated);
    }
}
