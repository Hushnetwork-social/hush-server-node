namespace PublicStateElectionPrerequisiteRegisterPromoter;

public sealed record PublicStateGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash,
    string MediaType);

public sealed record PublicStateGeneratedPackage(
    string Status,
    IReadOnlyList<PublicStateGeneratedArtifact> Artifacts,
    PublicStateGateEvaluation GateEvaluation);
