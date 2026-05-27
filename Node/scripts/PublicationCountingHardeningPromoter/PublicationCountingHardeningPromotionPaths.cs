namespace PublicationCountingHardeningPromoter;

public sealed record PublicationCountingHardeningPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string PublicCorpusRoot)
{
    public const string SourceFolder = "Publication-Counting-Hardening";
    public const string SourceFileName = "publication-counting-hardening-source.json";
    public const string SchemaFileName = "publication-counting-hardening-source.schema.json";
    public const string PackageRelativeRoot = "hushvoting-v1/publication-counting-hardening/v0.1.0";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);
    public string DefaultPackageRoot => Path.Combine(PublicCorpusRoot, PackageRelativeRoot);

    public static PublicationCountingHardeningPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            SourceFolder);

        return new PublicationCountingHardeningPromotionPaths(
            fullRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            Path.Combine(sourceRoot, "examples"),
            Path.Combine(fullRoot, "HushVoting-Verifier-Corpus"));
    }
}

