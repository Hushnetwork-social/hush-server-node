using System.Text;

namespace RetentionLogPrivacyProofPromoter;

public enum RetentionLogPrivacyProofArtifactVisibility
{
    Public,
    Restricted,
}

public sealed record RetentionLogPrivacyProofGeneratedArtifact(
    string RelativePath,
    RetentionLogPrivacyProofArtifactVisibility Visibility,
    string MediaType,
    string Content)
{
    public long SizeBytes => Encoding.UTF8.GetByteCount(Content);

    public string Sha256Hash => RetentionLogPrivacyProofCanonicalJson.ComputeSha256(Content);
}

public sealed record RetentionLogPrivacyProofScanFinding(
    string RelativePath,
    string Boundary,
    string Category,
    string Evidence,
    string ClaimImpact);

public sealed record RetentionLogPrivacyProofCheckResult(
    string CheckId,
    string Status,
    string Severity,
    string Reason,
    IReadOnlyList<string> EvidenceRefs);

public sealed record RetentionLogPrivacyProofCheckSet(
    string Status,
    IReadOnlyList<RetentionLogPrivacyProofCheckResult> Checks,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RetentionLogPrivacyProofScanFinding> ScanFindings)
{
    public bool BlocksAcceptedEvidence => Blockers.Count > 0 || ScanFindings.Count > 0;
}

public sealed record RetentionLogPrivacyProofGeneratedPackage(
    string PackageId,
    DateTimeOffset GeneratedAt,
    string Status,
    RetentionLogPrivacyProofCheckSet CheckResult,
    IReadOnlyList<RetentionLogPrivacyProofGeneratedArtifact> Artifacts,
    IReadOnlyList<RetentionLogPrivacyProofScanFinding> ScanFindings)
{
    public RetentionLogPrivacyProofGeneratedArtifact GetArtifact(string relativePath) =>
        Artifacts.Single(artifact => string.Equals(artifact.RelativePath, relativePath, StringComparison.Ordinal));
}
