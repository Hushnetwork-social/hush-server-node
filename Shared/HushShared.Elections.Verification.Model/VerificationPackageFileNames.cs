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

