namespace PublicStateElectionPrerequisiteRegisterPromoter;

public sealed record PublicStateElectionPrerequisitePromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string OutputRoot)
{
    public const string SourceFolder = "Public-State-Election-Prerequisites";
    public const string SourceFileName = "public-state-prerequisite-register.json";
    public const string NegativeFixturesFileName = "public-state-prerequisite-negative-fixtures.json";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);

    public static PublicStateElectionPrerequisitePromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            SourceFolder);

        return new PublicStateElectionPrerequisitePromotionPaths(
            fullRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            Path.Combine(sourceRoot, "examples"),
            Path.Combine(
                fullRoot,
                "hush-documents",
                "PrivateServer_ElectronicVoting",
                SourceFolder,
                "package"));
    }
}
