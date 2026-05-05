namespace HushShared.Elections.Verification.Model;

public enum VerificationPackageView
{
    PublicAnonymous = 0,
    RestrictedOwnerAuditor = 1,
}

public enum VerificationArtifactVisibility
{
    Public = 0,
    Restricted = 1,
}

public enum VerificationCheckStatus
{
    Pass = 0,
    Warn = 1,
    Fail = 2,
    NotApplicable = 3,
}

public enum VerificationOverallStatus
{
    Pass = 0,
    Warn = 1,
    Fail = 2,
    NotAvailable = 3,
}

public enum VerificationEvidenceRequirement
{
    Required = 0,
    Optional = 1,
    PendingLaterFeature = 2,
}

public record ElectionVerificationPackageRecord(
    string PackageId,
    string ElectionId,
    string PackageVersion,
    VerificationPackageView PackageView,
    string VerifierProfileId,
    string PackageHash,
    DateTime CreatedAt,
    AuditPackageManifestRecord AuditPackageManifest,
    VerifierInputManifestRecord VerifierInputManifest,
    VerifierProfileRecord VerifierProfile,
    ElectionRecordReferenceRecord ElectionRecord);

public record ElectionRecordReferenceRecord(
    string ElectionId,
    string LifecycleState,
    string ProtocolPackageId,
    string ProtocolPackageVersion,
    string ProtocolPackageStatus,
    string ProtocolSpecificationHash,
    string ProtocolProofPackageHash,
    string ProtocolReleaseManifestHash,
    IReadOnlyList<VerificationAccessLocationRecord> AccessLocations);

public record AuditPackageManifestRecord(
    string ManifestVersion,
    string PackageId,
    string ElectionId,
    VerificationPackageView PackageView,
    string VerifierProfileId,
    DateTime CreatedAt,
    IReadOnlyList<AuditPackageManifestEntryRecord> Entries);

public record AuditPackageManifestEntryRecord(
    string Path,
    string Sha256Hash,
    long SizeBytes,
    string MediaType,
    VerificationArtifactVisibility Visibility,
    VerificationEvidenceRequirement Requirement,
    IReadOnlyList<string> RequiredProfileIds);

public record VerifierInputManifestRecord(
    string ManifestVersion,
    string PackageId,
    string ElectionId,
    VerificationPackageView PackageView,
    string VerifierProfileId,
    string AuditPackageManifestHash,
    IReadOnlyList<string> RootFiles,
    IReadOnlyList<string> ArtifactDirectories);

public record VerifierProfileRecord(
    string ProfileId,
    string DisplayName,
    bool AllowsDraftProtocolPackage,
    bool AllowsPendingLaterFeatureEvidence,
    bool RequiresRestrictedEvidence,
    bool RequiresHighAssuranceEvidence,
    IReadOnlyList<string> RequiredCheckCodes);

public record VerifierOutputRecord(
    string OutputVersion,
    string PackageId,
    string ElectionId,
    string VerifierProfileId,
    VerificationOverallStatus OverallStatus,
    int ExitCode,
    DateTime VerifiedAt,
    IReadOnlyList<VerifierCheckResultRecord> Results);

public record VerifierCheckResultRecord(
    string CheckCode,
    VerificationCheckStatus Status,
    string ResultCode,
    string Message,
    IReadOnlyDictionary<string, string> Evidence);

public record VerificationAccessLocationRecord(
    string Kind,
    string Location,
    string? ContentHash);

