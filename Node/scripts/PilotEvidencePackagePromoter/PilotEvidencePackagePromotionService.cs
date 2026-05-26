namespace PilotEvidencePackagePromoter;

public sealed record PilotEvidencePackagePromotionOptions(
    PilotEvidencePackagePromotionPaths Paths,
    string? Mode,
    string? SourceInput,
    string? OutputRoot,
    DateTimeOffset? GeneratedAt,
    bool ValidateOnly);

public sealed record PilotEvidencePackagePromotionResult(
    string Mode,
    string Status,
    IReadOnlyList<string> WrittenFiles,
    PilotEvidenceGeneratedPackage GeneratedPackage);

public sealed class PilotEvidencePackagePromotionService
{
    public const string ModeCheckOnly = "check-only";
    public const string ModePackage = "package";
    public const string ModeValidateOnly = "validate-only";

    public PilotEvidencePackagePromotionResult Promote(PilotEvidencePackagePromotionOptions options)
    {
        var schemaErrors = PilotEvidencePackageContracts.ValidateSchemaSet(options.Paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new PilotEvidencePackagePromotionException(
                "FEAT-141 pilot evidence schema validation failed.",
                schemaErrors);
        }

        var mode = options.ValidateOnly ? ModeValidateOnly : options.Mode ?? ModeCheckOnly;
        if (mode is not ModeValidateOnly and not ModeCheckOnly and not ModePackage)
        {
            throw new PilotEvidencePackagePromotionException($"Unsupported FEAT-141 promoter mode: {mode}");
        }

        var generated = PilotEvidencePackageArtifactGenerator.Generate(
            options.Paths,
            options.SourceInput,
            options.GeneratedAt);

        var outputRoot = Path.GetFullPath(options.OutputRoot ?? options.Paths.OutputRoot);
        var packageRoot = Path.Combine(outputRoot, "package");

        if (mode == ModeCheckOnly)
        {
            ValidateExistingArtifactsWhenPresent(packageRoot, generated);
            return new PilotEvidencePackagePromotionResult(mode, generated.Status, [], generated);
        }

        if (mode == ModeValidateOnly)
        {
            return new PilotEvidencePackagePromotionResult(mode, generated.Status, [], generated);
        }

        Directory.CreateDirectory(packageRoot);
        var writtenFiles = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = Path.GetFullPath(Path.Combine(packageRoot, artifact.RelativePath));
            PilotEvidencePackageContracts.EnsurePathUnder(packageRoot, path, artifact.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, artifact.Content);
            writtenFiles.Add(path);
        }

        return new PilotEvidencePackagePromotionResult(mode, generated.Status, writtenFiles, generated);
    }

    private static void ValidateExistingArtifactsWhenPresent(
        string packageRoot,
        PilotEvidenceGeneratedPackage generated)
    {
        if (!Directory.Exists(packageRoot))
        {
            return;
        }

        var errors = new List<string>();
        foreach (var artifact in generated.Artifacts)
        {
            var path = Path.Combine(packageRoot, artifact.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Missing generated artifact: {artifact.RelativePath}");
                continue;
            }

            var actual = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            var expected = artifact.Content.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                errors.Add($"Generated artifact mismatch: {artifact.RelativePath}");
            }
        }

        if (errors.Count > 0)
        {
            throw new PilotEvidencePackagePromotionException(
                "FEAT-141 check-only validation failed.",
                errors);
        }
    }
}
