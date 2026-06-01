namespace PublicationCountingReplayPromoter;

public sealed record PublicationCountingReplayPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string PublicCorpusRoot)
{
    public const string SourceFolder = "Publication-Counting-Replay";
    public const string SourceFileName = "publication-counting-replay-source.json";
    public const string SourceSchemaFileName = "publication-counting-replay-source.schema.json";
    public const string PackageManifestSchemaFileName = "publication-counting-replay-package-manifest.schema.json";
    public const string PackageRelativeRoot = "hushvoting-v1/publication-counting-replay/v0.2.0";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);
    public string DefaultPackageRoot => Path.Combine(PublicCorpusRoot, PackageRelativeRoot);

    public static PublicationCountingReplayPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            SourceFolder);

        return new PublicationCountingReplayPromotionPaths(
            fullRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            Path.Combine(sourceRoot, "examples"),
            Path.Combine(fullRoot, "HushVoting-Verifier-Corpus"));
    }
}

