namespace SecondProductionLikeOperationalRunPromoter;

public sealed record SecondProductionLikeOperationalRunPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string PackagesRoot)
{
    public const string SourceFolder = "Operational-Evidence-Second-Run";
    public const string SourceFileName = "second-production-like-run-source.json";
    public const string SourceSchemaFileName = "second-production-like-run-source.schema.json";
    public const string PackageManifestSchemaFileName = "second-production-like-run-package-manifest.schema.json";
    public const string PackageFamilyFolder = "second-production-like-run";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);

    public static SecondProductionLikeOperationalRunPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(fullRoot, SourceFolder);

        return new SecondProductionLikeOperationalRunPromotionPaths(
            fullRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            Path.Combine(sourceRoot, "examples"),
            Path.Combine(sourceRoot, "packages"));
    }
}
