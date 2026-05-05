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
    public const string RestrictedRosterCheckoff = "artifacts/restricted/roster-checkoff.json";

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

