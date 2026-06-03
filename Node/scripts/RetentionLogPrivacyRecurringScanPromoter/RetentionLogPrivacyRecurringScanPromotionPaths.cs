namespace RetentionLogPrivacyRecurringScanPromoter;

public sealed record RetentionLogPrivacyRecurringScanPromotionPaths(
    string WorkspaceRoot,
    string PublicRepositoryRoot,
    string SchemasRoot,
    string RulesRoot,
    string ExamplesRoot,
    string PackagesRoot)
{
    public const string SourceFileName = "retention-log-privacy-recurring-scan-source.json";
    public const string SourceSchemaFileName = "retention-log-privacy-recurring-scan-source.schema.json";
    public const string PackageManifestSchemaFileName = "retention-log-privacy-recurring-scan-package-manifest.schema.json";
    public const string ForbiddenMaterialCatalogFileName = "forbidden-material-catalog.json";
    public const string OutputFamilyRegistryFileName = "output-family-registry.json";
    public const string ResultCodesFileName = "result-codes.json";
    public const string NegativeFixtureCatalogFileName = "retention-log-privacy-recurring-scan-negative-fixtures.json";
    public const string PackageFamilyFolder = "retention-log-privacy-recurring-scan";

    public string DefaultSourcePath => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);

    public static RetentionLogPrivacyRecurringScanPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var publicRepo = Path.Combine(root, "Retention-Log-Privacy-Scans");
        return new RetentionLogPrivacyRecurringScanPromotionPaths(
            root,
            publicRepo,
            Path.Combine(publicRepo, "schemas"),
            Path.Combine(publicRepo, "rules"),
            Path.Combine(publicRepo, "examples"),
            Path.Combine(publicRepo, "packages"));
    }
}
