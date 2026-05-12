namespace HushShared.Elections.Verification.Model;

public static class VerificationPackageFileNames
{
    public const string ElectionRecord = "ElectionRecord.json";
    public const string AuditPackageManifest = "AuditPackageManifest.json";
    public const string VerifierInputManifest = "VerifierInputManifest.json";
    public const string VerifierProfile = "VerifierProfile.json";
    public const string VerifierOutput = "verifier-output/VerifierOutput.json";
    public const string VerifierSummary = "verifier-output/VerifierSummary.md";

    public const string ReportPackageDirectory = "artifacts/report-package";
    public const string ElectionRecordDirectory = "artifacts/election-record";
    public const string RestrictedDirectory = "artifacts/restricted";

    public const string AcceptedBallotSet = "artifacts/election-record/accepted-ballot-set.json";
    public const string PublishedBallotStream = "artifacts/election-record/published-ballot-stream.json";
    public const string TallyReplay = "artifacts/election-record/tally-replay.json";
    public const string TrusteeReleaseEvidence = "artifacts/election-record/trustee-release-evidence.json";
    public const string ResultBinding = "artifacts/election-record/result-binding.json";
    public const string Sp04Evidence = "artifacts/election-record/sp04-evidence.json";
    public const string Sp04ReceiptCommitments = "artifacts/election-record/sp04-receipt-commitments.json";
    public const string Sp05EligibilityPolicy = "artifacts/election-record/eligibility-policy.json";
    public const string Sp05EligibilitySummary = "artifacts/election-record/eligibility-summary.json";
    public const string Sp05EligibilityVerifierOutput = "artifacts/election-record/eligibility-verifier-output.json";
    public const string Sp06TrusteeControlProfile = "artifacts/election-record/trustee-control-profile.json";
    public const string Sp06TrusteeControlSummary = "artifacts/election-record/trustee-control-summary.json";
    public const string Sp06TrusteeVerifierOutput = "artifacts/election-record/trustee-verifier-output.json";
    public const string Sp07PublicationProofTranscript = "artifacts/election-record/publication-proof-transcript.json";
    public const string Sp07PublicationProofVerifierOutput = "artifacts/election-record/publication-proof-verifier-output.json";
    public const string Sp07WitnessDeletionReceipt = "artifacts/election-record/witness-deletion-receipt.json";
    public const string Sp08ReleaseIntegrity = "artifacts/election-record/release-integrity.json";
    public const string Sp08ReleaseManifest = "artifacts/election-record/release-manifest.json";
    public const string Sp08ReleaseIntegrityVerifierOutput = "artifacts/election-record/release-integrity-verifier-output.json";
    public const string Sp09ExternalReviewStatus = "artifacts/election-record/external-review-status.json";
    public const string Sp09ExternalReviewClaimTable = "artifacts/election-record/external-review-claim-table.json";
    public const string Sp09ExternalReviewVerifierOutput = "artifacts/election-record/external-review-verifier-output.json";
    public const string Sp10OperationalSecuritySummary = "artifacts/election-record/operational-security-summary.json";
    public const string Sp10OperationalDeploymentEvidence = "artifacts/election-record/operational-deployment-evidence.json";
    public const string Sp10OperationalCustodyEvidence = "artifacts/election-record/operational-custody-evidence.json";
    public const string Sp10OperationalVerifierOutput = "artifacts/election-record/operational-verifier-output.json";
    public const string Sp11RegulatoryClaimState = "artifacts/election-record/regulatory-claim-state.json";
    public const string RestrictedRosterCheckoff = "artifacts/restricted/roster-checkoff.json";
    public const string RestrictedRosterImportEvidence = "artifacts/restricted/roster-import-evidence.json";
    public const string RestrictedRoster = "artifacts/restricted/restricted-roster.json";
    public const string RestrictedLinkingEvidence = "artifacts/restricted/restricted-linking-evidence.json";
    public const string RestrictedActivationEvents = "artifacts/restricted/restricted-activation-events.json";
    public const string RestrictedCheckoffLedger = "artifacts/restricted/restricted-checkoff-ledger.json";
    public const string RestrictedDisputes = "artifacts/restricted/restricted-disputes.json";
    public const string RestrictedSp04CeremonyRecords = "artifacts/restricted/sp04-ceremony-records.json";
    public const string RestrictedSp04PreparedBallotCommitments = "artifacts/restricted/sp04-prepared-ballot-commitments.json";
    public const string RestrictedSp04SpoilMarkers = "artifacts/restricted/sp04-spoil-markers.json";
    public const string RestrictedSp06TrusteeControlDomains = "artifacts/restricted/trustee-control-domains.json";
    public const string RestrictedSp06TrusteeReleaseArtifacts = "artifacts/restricted/trustee-release-artifacts.json";
    public const string RestrictedSp07PublicationProofSession = "artifacts/restricted/publication-proof-session.json";
    public const string RestrictedSp07WitnessDeletionLog = "artifacts/restricted/publication-proof-deletion-log.json";
    public const string RestrictedSp09FindingTracker = "artifacts/restricted/external-review-finding-tracker.json";
    public const string RestrictedSp09RetestEvidence = "artifacts/restricted/external-review-retest-evidence.json";
    public const string RestrictedSp09ReportReference = "artifacts/restricted/external-review-report-reference.json";
    public const string RestrictedSp10AccessControlSnapshot = "artifacts/restricted/operational-access-control-snapshot.json";
    public const string RestrictedSp10LoggingEvidence = "artifacts/restricted/operational-logging-evidence.json";
    public const string RestrictedSp10BackupRestoreEvidence = "artifacts/restricted/operational-backup-restore-evidence.json";
    public const string RestrictedSp10IncidentEvidence = "artifacts/restricted/operational-incident-evidence.json";
    public const string RestrictedSp10AuditorRoomAccessLog = "artifacts/restricted/auditor-room-access-log.json";
    public const string RestrictedSp11RegulatoryJurisdictionWorkpaper = "artifacts/restricted/regulatory-jurisdiction-workpaper.json";

    public static IReadOnlyList<string> RootFiles { get; } =
    [
        ElectionRecord,
        AuditPackageManifest,
        VerifierInputManifest,
        VerifierProfile,
    ];

    public static IReadOnlyList<string> ArtifactDirectories { get; } =
    [
        ReportPackageDirectory,
        ElectionRecordDirectory,
    ];

    public static IReadOnlyList<string> RestrictedArtifactDirectories { get; } =
    [
        ReportPackageDirectory,
        ElectionRecordDirectory,
        RestrictedDirectory,
    ];
}

