namespace KmsCustodyRehearsalPromoter;

public sealed record KmsCustodyRehearsalPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string PublicCorpusRoot)
{
    public const string SourceFolder = "Kms-Custody-Rehearsal";
    public const string SourceFileName = "kms-custody-rehearsal-source.json";
    public const string SourceSchemaFileName = "kms-custody-rehearsal-source.schema.json";
    public const string PackageManifestSchemaFileName = "kms-custody-rehearsal-package-manifest.schema.json";
    public const string PackageRelativeRoot = "hushvoting-v1/kms-custody-rehearsal/v0.1.0";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);
    public string DefaultPackageRoot => Path.Combine(PublicCorpusRoot, PackageRelativeRoot);

    public static KmsCustodyRehearsalPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(fullRoot, SourceFolder);

        return new KmsCustodyRehearsalPromotionPaths(
            fullRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            Path.Combine(sourceRoot, "examples"),
            Path.Combine(fullRoot, "HushVoting-Verifier-Corpus"));
    }
}
