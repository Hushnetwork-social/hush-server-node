namespace ProductionRolloutReadinessPromoter;

public sealed record ProductionRolloutGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash,
    string MediaType);

public sealed record ProductionRolloutGeneratedPackage(
    string Status,
    IReadOnlyList<ProductionRolloutGeneratedArtifact> Artifacts,
    ProductionRolloutGateEvaluation GateEvaluation,
    IReadOnlyList<string> AuditFailures);
