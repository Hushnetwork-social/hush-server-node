namespace FailedFinalizeContinuityRehearsalPromoter;

public sealed record FailedFinalizeContinuityPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string DefaultSourceInput,
    string OutputRoot)
{
    public const string SourceFolder = "Failed-Finalize-Continuity-Rehearsal";
    public const string SourceFileName = "failed-finalize-continuity-source.json";

    public static FailedFinalizeContinuityPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullWorkspaceRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            SourceFolder);
        var examplesRoot = Path.Combine(sourceRoot, "examples");

        return new FailedFinalizeContinuityPromotionPaths(
            fullWorkspaceRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            examplesRoot,
            Path.Combine(examplesRoot, "release-baseline", SourceFileName),
            Path.Combine(
                fullWorkspaceRoot,
                "hush-documents",
                "PrivateServer_ElectronicVoting",
                SourceFolder));
    }
}
