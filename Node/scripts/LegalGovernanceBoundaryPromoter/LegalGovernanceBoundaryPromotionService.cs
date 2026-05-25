namespace LegalGovernanceBoundaryPromoter;

public sealed record LegalGovernanceBoundaryPromotionOptions(
    LegalGovernanceBoundaryPromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly);

public sealed record LegalGovernanceBoundaryPromotionResult(
    string Mode,
    string Status,
    IReadOnlyList<string> WrittenFiles,
    LegalGovernanceBoundaryGeneratedPackage GeneratedPackage);

public sealed class LegalGovernanceBoundaryPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public LegalGovernanceBoundaryPromotionResult Promote(LegalGovernanceBoundaryPromotionOptions options)
    {
        var schemaErrors = LegalGovernanceBoundaryContracts.ValidateSchemaSet(options.Paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new LegalGovernanceBoundaryPromotionException(
                "FEAT-140 legal governance boundary schema validation failed.",
                schemaErrors);
        }

        var mode = options.ValidateOnly ? ModeValidateOnly : options.Mode ?? ModeCheckOnly;
        var generated = LegalGovernanceBoundaryArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            options.GeneratedAt);

        if (mode is not ModePackage)
        {
            return new LegalGovernanceBoundaryPromotionResult(mode, generated.Status, [], generated);
        }

        var outputRoot = Path.GetFullPath(options.OutputRoot ?? options.Paths.OutputRoot);
        var packageRoot = Path.Combine(outputRoot, "package");
        Directory.CreateDirectory(packageRoot);
        var writtenFiles = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = Path.GetFullPath(Path.Combine(packageRoot, artifact.RelativePath));
            LegalGovernanceBoundaryContracts.EnsurePathUnder(packageRoot, path, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, artifact.Content);
            writtenFiles.Add(path);
        }

        return new LegalGovernanceBoundaryPromotionResult(mode, generated.Status, writtenFiles, generated);
    }
}
