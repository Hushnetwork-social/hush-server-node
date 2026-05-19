using System.Text;

namespace OperationalEvidencePromoter;

public enum OperationalEvidenceArtifactVisibility
{
    Public,
    Restricted,
    Internal,
}

public sealed record OperationalEvidenceGeneratedArtifact(
    string RelativePath,
    OperationalEvidenceArtifactVisibility Visibility,
    string MediaType,
    string Content)
{
    public long SizeBytes => Encoding.UTF8.GetByteCount(Content);

    public string Sha256Hash => OperationalEvidenceCanonicalJson.ComputeSha256(Content);
}

public sealed record OperationalEvidenceGeneratedArtifactSet(
    string RunId,
    DateTimeOffset GeneratedAt,
    string GenerationStatus,
    OperationalEvidenceCheckSetResult CheckResult,
    IReadOnlyList<OperationalEvidenceGeneratedArtifact> Artifacts,
    IReadOnlyList<OperationalEvidenceMaterialFinding> ScanFindings)
{
    public bool BlocksAcceptedEvidence =>
        CheckResult.BlocksAcceptedEvidence || ScanFindings.Count > 0 || GenerationStatus == "blocked";

    public OperationalEvidenceGeneratedArtifact GetArtifact(string relativePath) =>
        Artifacts.Single(artifact => string.Equals(artifact.RelativePath, relativePath, StringComparison.Ordinal));
}
