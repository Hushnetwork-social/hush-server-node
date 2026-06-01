namespace InternalAudit95ProtocolTraceabilityPromoter;

public sealed record InternalAudit95ProtocolTraceabilityGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash,
    string MediaType);

public sealed record InternalAudit95ProtocolTraceabilityGeneratedPackage(
    string Status,
    IReadOnlyList<InternalAudit95ProtocolTraceabilityGeneratedArtifact> Artifacts,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Diagnostics);
