namespace ProductionRolloutReadinessPromoter;

public sealed record ProductionRolloutGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash,
    string MediaType);

public sealed record ProductionRolloutPublicOutputFinding(
    string RelativePath,
    string Category,
    string Evidence);

public sealed record ProductionRolloutGeneratedPackage(
    string Status,
    IReadOnlyList<ProductionRolloutGeneratedArtifact> Artifacts,
    ProductionRolloutGateEvaluation GateEvaluation,
    IReadOnlyList<string> AuditFailures,
    IReadOnlyList<string> PublicOutputFailures,
    IReadOnlyList<ProductionRolloutPublicOutputFinding> PublicOutputFindings);
