namespace GovernedOutcomeProducer;

public sealed record GovernedOutcomeProducerPromotionOptions(
    GovernedOutcomeProducerPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly);

public sealed record GovernedOutcomeProducerPromotionResult(
    string Mode,
    string Status,
    IReadOnlyList<string> WrittenFiles,
    GovernedOutcomeGeneratedPackage GeneratedPackage);

public sealed class GovernedOutcomeProducerPromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public GovernedOutcomeProducerPromotionResult Promote(GovernedOutcomeProducerPromotionOptions options)
    {
        var schemaErrors = GovernedOutcomeProducerContracts.ValidateSchemaSet(options.Paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new GovernedOutcomeProducerException(
                "FEAT-146 governed outcome producer schema validation failed.",
                schemaErrors);
        }

        var mode = options.ValidateOnly ? ModeValidateOnly : options.Mode ?? ModeCheckOnly;
        var generated = GovernedOutcomeProducerArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            options.GeneratedAt);

        if (mode is not ModePackage)
        {
            return new GovernedOutcomeProducerPromotionResult(mode, generated.Status, [], generated);
        }

        var outputRoot = Path.GetFullPath(options.OutputRoot ?? options.Paths.OutputRoot);
        var packageRoot = Path.Combine(outputRoot, "package");
        Directory.CreateDirectory(packageRoot);
        var writtenFiles = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = Path.GetFullPath(Path.Combine(packageRoot, artifact.RelativePath));
            GovernedOutcomeProducerContracts.EnsurePathUnder(packageRoot, path, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, artifact.Content);
            writtenFiles.Add(path);
        }

        return new GovernedOutcomeProducerPromotionResult(mode, generated.Status, writtenFiles, generated);
    }
}
