namespace InternalAudit95ProtocolTraceabilityPromoter;

public sealed record InternalAudit95ProtocolTraceabilityPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string OutputRoot)
{
    public const string SourceFolder = "Internal-Audit-95-Protocol-Traceability";
    public const string SourceFileName = "internal-audit-95-protocol-traceability-source.json";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);

    public static InternalAudit95ProtocolTraceabilityPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            SourceFolder);

        return new InternalAudit95ProtocolTraceabilityPaths(
            fullRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            Path.Combine(sourceRoot, "examples"),
            Path.Combine(
                fullRoot,
                "hush-documents",
                "PrivateServer_ElectronicVoting",
                SourceFolder));
    }
}
