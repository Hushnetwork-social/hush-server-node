namespace ProductionLikeOperationalRunPromoter;

public sealed record ProductionLikeOperationalRunGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash,
    string MediaType);

public sealed record ProductionLikeOperationalRunPublicOutputFinding(
    string RelativePath,
    string Category,
    string Evidence);

public sealed record ProductionLikeOperationalRunGeneratedPackage(
    string Status,
    IReadOnlyList<ProductionLikeOperationalRunGeneratedArtifact> Artifacts,
    ProductionLikeOperationalRunGateEvaluation GateEvaluation,
    IReadOnlyList<string> PackageFailures,
    IReadOnlyList<ProductionLikeOperationalRunPublicOutputFinding> PublicOutputFindings);
