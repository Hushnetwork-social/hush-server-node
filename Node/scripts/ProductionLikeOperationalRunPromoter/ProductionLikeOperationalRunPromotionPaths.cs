namespace ProductionLikeOperationalRunPromoter;

public sealed record ProductionLikeOperationalRunPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string DefaultSourceInput,
    string FixtureCatalogPath,
    string OutputRoot)
{
    public const string SourceFolder = "Production-Like-Operational-Run";
    public const string OutputFolder = "Production-Like-Operational-Run-Evidence";
    public const string SourceFileName = "production-like-operational-run-source.json";
    public const string FixtureCatalogFileName = "fixture-catalog.json";

    public static ProductionLikeOperationalRunPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullWorkspaceRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            "Production-Like-Operational-Run");
        var examplesRoot = Path.Combine(sourceRoot, "examples");

        return new ProductionLikeOperationalRunPromotionPaths(
            fullWorkspaceRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            examplesRoot,
            Path.Combine(examplesRoot, "release-baseline", SourceFileName),
            Path.Combine(examplesRoot, FixtureCatalogFileName),
            Path.Combine(
                fullWorkspaceRoot,
                "hush-documents",
                "PrivateServer_ElectronicVoting",
                OutputFolder));
    }
}
