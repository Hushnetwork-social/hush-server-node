namespace DisputeContinuityMatrixPromoter;

public sealed record DisputeContinuityMatrixPromotionPaths(
    string WorkspaceRoot,
    string PublicRepositoryRoot,
    string SchemasRoot,
    string ScenariosRoot,
    string ExamplesRoot,
    string PackagesRoot)
{
    public const string SourceFileName = "dispute-continuity-matrix-source.json";
    public const string SourceSchemaFileName = "dispute-continuity-matrix-source.schema.json";
    public const string PackageManifestSchemaFileName = "dispute-continuity-matrix-package-manifest.schema.json";
    public const string ScenarioCatalogFileName = "dispute-continuity-scenario-catalog.json";
    public const string ResultCodesFileName = "verifier-challenge-result-codes.json";
    public const string NegativeFixtureCatalogFileName = "dispute-continuity-matrix-negative-fixtures.json";
    public const string PackageFamilyFolder = "dispute-continuity-matrix";
    public const string DefaultMatrixRunId = "FEAT165-DISPUTE-CONTINUITY-MATRIX-20260603-001";

    public string DefaultSourcePath => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);

    public string RestrictedEvidenceIndexRoot => Path.Combine(
        WorkspaceRoot,
        "hush-documents",
        "PrivateServer_ElectronicVoting",
        "Dispute-Continuity-Scenario-Matrix",
        DefaultMatrixRunId);

    public static DisputeContinuityMatrixPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var publicRepo = Path.Combine(root, "Dispute-Continuity-Matrix");
        return new DisputeContinuityMatrixPromotionPaths(
            root,
            publicRepo,
            Path.Combine(publicRepo, "schemas"),
            Path.Combine(publicRepo, "scenarios"),
            Path.Combine(publicRepo, "examples"),
            Path.Combine(publicRepo, "packages"));
    }
}
