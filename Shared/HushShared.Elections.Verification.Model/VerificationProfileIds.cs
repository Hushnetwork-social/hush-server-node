namespace HushShared.Elections.Verification.Model;

public static class VerificationProfileIds
{
    public const string DevelopmentCurrentV1 = "development_current_v1";
    public const string PublicAnonymousV1 = "public_anonymous_v1";
    public const string RestrictedOwnerAuditorV1 = "restricted_owner_auditor_v1";
    public const string HighAssuranceV1 = "high_assurance_v1";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [
            DevelopmentCurrentV1,
            PublicAnonymousV1,
            RestrictedOwnerAuditorV1,
            HighAssuranceV1,
        ],
        StringComparer.Ordinal);
}

public static class VerificationResultCodes
{
    public const string PackageStructureValid = "package_structure_valid";
    public const string PackageManifestValid = "package_manifest_valid";
    public const string PackageManifestMissingArtifact = "package_manifest_missing_artifact";
    public const string PackageManifestArtifactHashMismatch = "package_manifest_artifact_hash_mismatch";
    public const string PackageUnreadable = "package_unreadable";
    public const string PackageUnparseable = "package_unparseable";
    public const string VerifierProfilePackageMismatch = "verifier_profile_package_mismatch";
    public const string ElectionIdMismatch = "election_id_mismatch";
    public const string ElectionNotFinalized = "election_not_finalized";
    public const string ProtocolPackageDraftPrivate = "protocol_package_draft_private";
    public const string AcceptedBallotInventoryHashMismatch = "accepted_ballot_inventory_hash_mismatch";
    public const string AcceptedBallotDuplicateNullifier = "accepted_ballot_duplicate_nullifier";
    public const string PublishedBallotStreamHashMismatch = "published_ballot_stream_hash_mismatch";
    public const string PublishedBallotSequenceInvalid = "published_ballot_sequence_invalid";
    public const string PublicRestrictedFieldLeak = "public_restricted_field_leak";
    public const string RestrictedEvidenceMissing = "restricted_evidence_missing";
    public const string RestrictedExportUnauthorized = "restricted_export_unauthorized";
    public const string UnsupportedLiveDependency = "unsupported_live_dependency";
    public const string FutureEvidencePending = "future_evidence_pending";
    public const string PublicationProofPendingFeat117 = "not_implemented_pending_FEAT_117";
    public const string ReleaseIntegrityPendingFeat118 = "not_implemented_pending_FEAT_118";
    public const string ChallengeSpoilPendingFeat114 = "not_implemented_pending_FEAT_114";
}

public static class VerificationExitCodes
{
    public const int Pass = 0;
    public const int Fail = 1;
    public const int UnreadableOrUnparseable = 2;
    public const int ProfileOrPackageMismatch = 3;
    public const int InternalVerifierError = 4;

    public static int FromOverallStatus(VerificationOverallStatus status) =>
        status switch
        {
            VerificationOverallStatus.Pass => Pass,
            VerificationOverallStatus.Warn => Pass,
            VerificationOverallStatus.Fail => Fail,
            VerificationOverallStatus.NotAvailable => UnreadableOrUnparseable,
            _ => InternalVerifierError,
        };
}
