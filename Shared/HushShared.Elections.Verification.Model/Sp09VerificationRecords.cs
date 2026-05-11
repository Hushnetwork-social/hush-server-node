using HushShared.Elections.Model;

namespace HushShared.Elections.Verification.Model;

public static class ElectionSp09ProfileIds
{
    public const string ExternalReviewStatusSchema = "HushVotingExternalReviewStatus-v1";
    public const string ExternalReviewClaimTableSchema = "HushVotingExternalReviewClaimTable-v1";
    public const string ExternalReviewVerifierOutputSchema = "HushVotingExternalReviewVerifierOutput-v1";
    public const string ExternalExaminationProgramVersion = "SP09-P1";

    public const string ReviewScopeProtocolOmegaV1 = "protocol_proof_verifier_publication_path_v1";
    public const string ReviewTypeCryptographicSecurity = "private_third_party_crypto_protocol_review_v1";
    public const string ReviewPhaseProtocolProofP1 = "SP09-P1";

    public const string StatusNotStarted = "not_started";
    public const string StatusPackageReady = "package_ready";
    public const string StatusReviewerSelected = "reviewer_selected";
    public const string StatusInReview = "in_review";
    public const string StatusFindingsReceived = "findings_received";
    public const string StatusRemediationInProgress = "remediation_in_progress";
    public const string StatusRetestInProgress = "retest_in_progress";
    public const string StatusReviewedWithOpenFindings = "reviewed_with_open_findings";
    public const string StatusReviewedWithLimitations = "reviewed_with_limitations";
    public const string StatusReviewedForDeclaredScope = "reviewed_for_declared_scope";
    public const string StatusRequiresRedesign = "requires_redesign";

    public const string AvailabilityNotAvailable = "not_available";
    public const string AvailabilityPlanned = "planned";
    public const string AvailabilityAvailable = "available";

    public const string ClaimStateNotClaimed = "not_claimed";
    public const string ClaimStateProgramDefined = "program_defined";
    public const string ClaimStatePackageReady = "package_ready";
    public const string ClaimStateInReview = "in_review";
    public const string ClaimStateReviewedWithOpenFindings = "reviewed_with_open_findings";
    public const string ClaimStateReviewedWithLimitations = "reviewed_with_limitations";
    public const string ClaimStateReviewedForDeclaredScope = "reviewed_for_declared_scope";
    public const string ClaimStateBlockedRequiresRedesign = "blocked_requires_redesign";
    public const string ClaimStateNotApplicableToArtifactSet = "not_applicable_to_this_artifact_set";

    public const string ReviewStatusValidCheckCode = "REV-000";
    public const string ProgramMissingCheckCode = "REV-001";
    public const string ReviewNotCompleteCheckCode = "REV-002";
    public const string ScopeMismatchCheckCode = "REV-003";
    public const string ReportHashMismatchCheckCode = "REV-004";
    public const string OpenFindingsBlockClaimsCheckCode = "REV-005";
    public const string ClaimNotAllowedCheckCode = "REV-006";
    public const string PublicBoundaryViolationCheckCode = "REV-007";
    public const string RequiresRedesignCheckCode = "REV-008";

    public static IReadOnlyList<string> DetailedStatuses { get; } =
    [
        StatusNotStarted,
        StatusPackageReady,
        StatusReviewerSelected,
        StatusInReview,
        StatusFindingsReceived,
        StatusRemediationInProgress,
        StatusRetestInProgress,
        StatusReviewedWithOpenFindings,
        StatusReviewedWithLimitations,
        StatusReviewedForDeclaredScope,
        StatusRequiresRedesign,
    ];

    public static IReadOnlySet<string> DetailedStatusSet { get; } = new HashSet<string>(
        DetailedStatuses,
        StringComparer.Ordinal);

    public static IReadOnlyList<string> AvailabilityValues { get; } =
    [
        AvailabilityNotAvailable,
        AvailabilityPlanned,
        AvailabilityAvailable,
    ];

    public static IReadOnlySet<string> AvailabilityValueSet { get; } = new HashSet<string>(
        AvailabilityValues,
        StringComparer.Ordinal);

    public static IReadOnlyList<string> ClaimStates { get; } =
    [
        ClaimStateNotClaimed,
        ClaimStateProgramDefined,
        ClaimStatePackageReady,
        ClaimStateInReview,
        ClaimStateReviewedWithOpenFindings,
        ClaimStateReviewedWithLimitations,
        ClaimStateReviewedForDeclaredScope,
        ClaimStateBlockedRequiresRedesign,
        ClaimStateNotApplicableToArtifactSet,
    ];

    public static IReadOnlySet<string> ClaimStateSet { get; } = new HashSet<string>(
        ClaimStates,
        StringComparer.Ordinal);

    public static IReadOnlyList<string> ExternalReviewCheckCodes { get; } =
    [
        ReviewStatusValidCheckCode,
        ProgramMissingCheckCode,
        ReviewNotCompleteCheckCode,
        ScopeMismatchCheckCode,
        ReportHashMismatchCheckCode,
        OpenFindingsBlockClaimsCheckCode,
        ClaimNotAllowedCheckCode,
        PublicBoundaryViolationCheckCode,
        RequiresRedesignCheckCode,
    ];

    public static IReadOnlyDictionary<string, ElectionSp09ReviewCheckDefinitionRecord> ReviewCheckDefinitions { get; } =
        new Dictionary<string, ElectionSp09ReviewCheckDefinitionRecord>(StringComparer.Ordinal)
        {
            [ReviewStatusValidCheckCode] = new(
                ReviewStatusValidCheckCode,
                VerificationResultCodes.ExternalReviewStatusValid,
                VerificationCheckStatus.Pass,
                "External review status shape is valid and claim state matches available evidence."),
            [ProgramMissingCheckCode] = new(
                ProgramMissingCheckCode,
                VerificationResultCodes.ExternalReviewProgramMissing,
                VerificationCheckStatus.Fail,
                "A review program or review claim is referenced without the required program evidence."),
            [ReviewNotCompleteCheckCode] = new(
                ReviewNotCompleteCheckCode,
                VerificationResultCodes.ExternalReviewNotComplete,
                VerificationCheckStatus.Warn,
                "External review is not complete; warn unless a reviewed claim was made."),
            [ScopeMismatchCheckCode] = new(
                ScopeMismatchCheckCode,
                VerificationResultCodes.ExternalReviewScopeMismatch,
                VerificationCheckStatus.Fail,
                "The election or package artifact hashes are outside the reviewed scope."),
            [ReportHashMismatchCheckCode] = new(
                ReportHashMismatchCheckCode,
                VerificationResultCodes.ExternalReviewReportHashMismatch,
                VerificationCheckStatus.Fail,
                "Reviewer report or customer-safe summary hash does not match the status artifact."),
            [OpenFindingsBlockClaimsCheckCode] = new(
                OpenFindingsBlockClaimsCheckCode,
                VerificationResultCodes.ExternalReviewOpenFindingsBlockClaims,
                VerificationCheckStatus.Fail,
                "Open critical or high findings block strong external-review claims."),
            [ClaimNotAllowedCheckCode] = new(
                ClaimNotAllowedCheckCode,
                VerificationResultCodes.ExternalReviewClaimNotAllowed,
                VerificationCheckStatus.Fail,
                "A report, package, or UI claim is not allowed by the review status and evidence."),
            [PublicBoundaryViolationCheckCode] = new(
                PublicBoundaryViolationCheckCode,
                VerificationResultCodes.ExternalReviewPublicBoundaryViolation,
                VerificationCheckStatus.Fail,
                "Restricted review detail appears in public package artifacts."),
            [RequiresRedesignCheckCode] = new(
                RequiresRedesignCheckCode,
                VerificationResultCodes.ExternalReviewRequiresRedesign,
                VerificationCheckStatus.Fail,
                "The reviewer identified redesign work for the affected scope."),
        };

    public static IReadOnlyDictionary<string, string> AllowedWordingByClaimState { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ClaimStateNotClaimed] = "External examination is not claimed for this package.",
            [ClaimStateProgramDefined] = "External examination program is defined; no reviewer conclusion is available.",
            [ClaimStatePackageReady] = "External examination package is ready for review; no reviewer conclusion is available.",
            [ClaimStateInReview] = "External examination is in progress; no reviewer conclusion is available.",
            [ClaimStateReviewedWithOpenFindings] =
                "External review artifact exists for the declared scope, but open findings limit or block affected claims.",
            [ClaimStateReviewedWithLimitations] =
                "Reviewed for declared scope and version, with limitations documented.",
            [ClaimStateReviewedForDeclaredScope] = "Reviewed for declared scope and version.",
            [ClaimStateBlockedRequiresRedesign] =
                "Reviewer identified redesign work; external review claim is blocked for this scope.",
            [ClaimStateNotApplicableToArtifactSet] =
                "No applicable external review is available for this artifact set.",
        };

    public static IReadOnlyList<string> ForbiddenClaimPhrases { get; } =
    [
        "certified",
        "approved for public elections",
        "bug-free",
        "formally verified",
        "same assurance as swiss public e-voting",
        "externally audited",
    ];
}

public record ElectionSp09ReviewCheckDefinitionRecord(
    string CheckCode,
    string ResultCode,
    VerificationCheckStatus ViolationStatus,
    string Description);

public record ElectionSp09CustomerSafeReviewSummaryRecord(
    string LegacyStatus,
    string Availability,
    string ClaimState,
    string Wording);

public record ElectionSp09ExternalReviewStatusArtifactRecord(
    string Schema,
    string ElectionId,
    string ProgramVersion,
    string ReviewScope,
    string ReviewType,
    string ReviewPhase,
    string DetailedStatus,
    string Availability,
    string ClaimState,
    bool ReviewScopeMatchesElection,
    string PrimaryResultCode,
    string? PrimaryIssue,
    string? ReviewerEvidenceRef,
    string? ReportHashOrRestrictedRef,
    string? CustomerSafeSummaryHash,
    string? CustomerSafeSummaryUrl,
    string? KnownLimitationsVersion,
    string? KnownLimitationsHash,
    IReadOnlyList<ElectionSp09ReviewedArtifactRecord> ReviewedArtifacts,
    IReadOnlyList<ElectionSp09FindingSeverityCountRecord> FindingSummary,
    IReadOnlyList<string> PublicEvidenceFiles,
    IReadOnlyList<string> RestrictedEvidenceFiles,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string Schema { get; init; } = NormalizeRequiredValue(Schema, nameof(Schema));
    public string ElectionId { get; init; } = NormalizeRequiredValue(ElectionId, nameof(ElectionId));
    public string ProgramVersion { get; init; } = NormalizeRequiredValue(ProgramVersion, nameof(ProgramVersion));
    public string ReviewScope { get; init; } = NormalizeRequiredValue(ReviewScope, nameof(ReviewScope));
    public string ReviewType { get; init; } = NormalizeRequiredValue(ReviewType, nameof(ReviewType));
    public string ReviewPhase { get; init; } = NormalizeRequiredValue(ReviewPhase, nameof(ReviewPhase));
    public string DetailedStatus { get; init; } = NormalizeRequiredValue(DetailedStatus, nameof(DetailedStatus));
    public string Availability { get; init; } = NormalizeRequiredValue(Availability, nameof(Availability));
    public string ClaimState { get; init; } = NormalizeRequiredValue(ClaimState, nameof(ClaimState));
    public string PrimaryResultCode { get; init; } =
        NormalizeRequiredValue(PrimaryResultCode, nameof(PrimaryResultCode));
    public string? PrimaryIssue { get; init; } = NormalizeOptionalValue(PrimaryIssue);
    public string? ReviewerEvidenceRef { get; init; } = NormalizeOptionalValue(ReviewerEvidenceRef);
    public string? ReportHashOrRestrictedRef { get; init; } = NormalizeOptionalValue(ReportHashOrRestrictedRef);
    public string? CustomerSafeSummaryHash { get; init; } = NormalizeOptionalValue(CustomerSafeSummaryHash);
    public string? CustomerSafeSummaryUrl { get; init; } = NormalizeOptionalValue(CustomerSafeSummaryUrl);
    public string? KnownLimitationsVersion { get; init; } = NormalizeOptionalValue(KnownLimitationsVersion);
    public string? KnownLimitationsHash { get; init; } = NormalizeOptionalValue(KnownLimitationsHash);

    public IReadOnlyList<ElectionSp09ReviewedArtifactRecord> ReviewedArtifacts { get; init; } =
        ReviewedArtifacts?.ToArray() ?? [];

    public IReadOnlyList<ElectionSp09FindingSeverityCountRecord> FindingSummary { get; init; } =
        FindingSummary?.ToArray() ?? [];

    public IReadOnlyList<string> PublicEvidenceFiles { get; init; } = NormalizeStringList(PublicEvidenceFiles);
    public IReadOnlyList<string> RestrictedEvidenceFiles { get; init; } = NormalizeStringList(RestrictedEvidenceFiles);
    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } = NormalizeStringList(PublicPrivacyBoundary);

    internal static string NormalizeRequiredValue(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    internal static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static IReadOnlyList<string> NormalizeStringList(IReadOnlyList<string>? values) =>
        values is null
            ? Array.Empty<string>()
            : values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}

public record ElectionSp09ReviewedArtifactRecord(
    string ArtifactId,
    string ArtifactType,
    string ArtifactName,
    string ArtifactHash,
    string? ArtifactVersion,
    string ReviewScope)
{
    public string ArtifactId { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ArtifactId, nameof(ArtifactId));

    public string ArtifactType { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ArtifactType, nameof(ArtifactType));

    public string ArtifactName { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ArtifactName, nameof(ArtifactName));

    public string ArtifactHash { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ArtifactHash, nameof(ArtifactHash));

    public string? ArtifactVersion { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeOptionalValue(ArtifactVersion);

    public string ReviewScope { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ReviewScope, nameof(ReviewScope));
}

public record ElectionSp09FindingSeverityCountRecord(
    string Severity,
    int OpenCount,
    int FixedCount,
    int AcceptedLimitationCount)
{
    public string Severity { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(Severity, nameof(Severity));
}

public record ElectionSp09ExternalReviewClaimTableArtifactRecord(
    string Schema,
    string ProgramVersion,
    IReadOnlyList<ElectionSp09ExternalReviewClaimRecord> Claims,
    IReadOnlyList<string> ForbiddenPhrases,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string Schema { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(Schema, nameof(Schema));

    public string ProgramVersion { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ProgramVersion, nameof(ProgramVersion));

    public IReadOnlyList<ElectionSp09ExternalReviewClaimRecord> Claims { get; init; } =
        Claims?.ToArray() ?? [];

    public IReadOnlyList<string> ForbiddenPhrases { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeStringList(ForbiddenPhrases);

    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeStringList(PublicPrivacyBoundary);
}

public record ElectionSp09ExternalReviewClaimRecord(
    string ClaimId,
    string ClaimState,
    string AllowedWording,
    IReadOnlyList<string> HushControlledEvidenceRefs,
    IReadOnlyList<string> ExternalReviewEvidenceRefs,
    IReadOnlyList<string> AffectedFindingIds)
{
    public string ClaimId { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ClaimId, nameof(ClaimId));

    public string ClaimState { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ClaimState, nameof(ClaimState));

    public string AllowedWording { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(AllowedWording, nameof(AllowedWording));

    public IReadOnlyList<string> HushControlledEvidenceRefs { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeStringList(HushControlledEvidenceRefs);

    public IReadOnlyList<string> ExternalReviewEvidenceRefs { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeStringList(ExternalReviewEvidenceRefs);

    public IReadOnlyList<string> AffectedFindingIds { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeStringList(AffectedFindingIds);
}

public record ElectionSp09VerifierOutputArtifactRecord(
    string ElectionId,
    string VerifierProfileId,
    string Schema,
    DateTime VerifiedAt,
    IReadOnlyList<VerifierCheckResultRecord> Results)
{
    public string ElectionId { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public string VerifierProfileId { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(
            VerifierProfileId,
            nameof(VerifierProfileId));

    public string Schema { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(Schema, nameof(Schema));

    public IReadOnlyList<VerifierCheckResultRecord> Results { get; init; } =
        Results?.ToArray() ?? [];
}

public record ElectionSp09RestrictedFindingTrackerArtifactRecord(
    string ElectionId,
    string ProgramVersion,
    string ReviewScope,
    IReadOnlyList<ElectionSp09RestrictedFindingRecord> Findings)
{
    public string ElectionId { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public string ProgramVersion { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ProgramVersion, nameof(ProgramVersion));

    public string ReviewScope { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ReviewScope, nameof(ReviewScope));

    public IReadOnlyList<ElectionSp09RestrictedFindingRecord> Findings { get; init; } =
        Findings?.ToArray() ?? [];
}

public record ElectionSp09RestrictedFindingRecord(
    string FindingId,
    string Severity,
    string Status,
    string Disclosure,
    IReadOnlyList<string> AffectedClaims,
    string? RemediationRef,
    string? RetestStatus)
{
    public string FindingId { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(FindingId, nameof(FindingId));

    public string Severity { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(Severity, nameof(Severity));

    public string Status { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(Status, nameof(Status));

    public string Disclosure { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(Disclosure, nameof(Disclosure));

    public string? RemediationRef { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeOptionalValue(RemediationRef);

    public string? RetestStatus { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeOptionalValue(RetestStatus);

    public IReadOnlyList<string> AffectedClaims { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeStringList(AffectedClaims);
}

public record ElectionSp09RestrictedRetestEvidenceArtifactRecord(
    string ElectionId,
    string ProgramVersion,
    string ReviewScope,
    IReadOnlyList<ElectionSp09RestrictedRetestRecord> Retests)
{
    public string ElectionId { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public string ProgramVersion { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ProgramVersion, nameof(ProgramVersion));

    public string ReviewScope { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ReviewScope, nameof(ReviewScope));

    public IReadOnlyList<ElectionSp09RestrictedRetestRecord> Retests { get; init; } =
        Retests?.ToArray() ?? [];
}

public record ElectionSp09RestrictedRetestRecord(
    string RetestId,
    string FindingId,
    string RetestStatus,
    string? RetestEvidenceHash,
    string? RestrictedEvidenceRef)
{
    public string RetestId { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(RetestId, nameof(RetestId));

    public string FindingId { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(FindingId, nameof(FindingId));

    public string RetestStatus { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(RetestStatus, nameof(RetestStatus));

    public string? RetestEvidenceHash { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeOptionalValue(RetestEvidenceHash);

    public string? RestrictedEvidenceRef { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeOptionalValue(RestrictedEvidenceRef);
}

public record ElectionSp09RestrictedReportReferenceArtifactRecord(
    string ElectionId,
    string ProgramVersion,
    string ReviewScope,
    string DetailedStatus,
    string? ReportHash,
    string? RestrictedReportRef,
    string? CustomerSafeSummaryHash,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string ElectionId { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public string ProgramVersion { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ProgramVersion, nameof(ProgramVersion));

    public string ReviewScope { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(ReviewScope, nameof(ReviewScope));

    public string DetailedStatus { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeRequiredValue(DetailedStatus, nameof(DetailedStatus));

    public string? ReportHash { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeOptionalValue(ReportHash);

    public string? RestrictedReportRef { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeOptionalValue(RestrictedReportRef);

    public string? CustomerSafeSummaryHash { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeOptionalValue(CustomerSafeSummaryHash);

    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        ElectionSp09ExternalReviewStatusArtifactRecord.NormalizeStringList(PublicPrivacyBoundary);
}

public static class ElectionSp09ExternalReviewRules
{
    private static readonly IReadOnlySet<string> PlannedStatuses = new HashSet<string>(
        [
            ElectionSp09ProfileIds.StatusNotStarted,
            ElectionSp09ProfileIds.StatusPackageReady,
            ElectionSp09ProfileIds.StatusReviewerSelected,
            ElectionSp09ProfileIds.StatusInReview,
            ElectionSp09ProfileIds.StatusFindingsReceived,
            ElectionSp09ProfileIds.StatusRemediationInProgress,
            ElectionSp09ProfileIds.StatusRetestInProgress,
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> ReviewedStatuses = new HashSet<string>(
        [
            ElectionSp09ProfileIds.StatusReviewedWithOpenFindings,
            ElectionSp09ProfileIds.StatusReviewedWithLimitations,
            ElectionSp09ProfileIds.StatusReviewedForDeclaredScope,
        ],
        StringComparer.Ordinal);

    public static string ProjectAvailability(
        string? detailedStatus,
        bool reviewScopeMatchesElection = true)
    {
        var status = Normalize(detailedStatus);
        if (!reviewScopeMatchesElection ||
            string.Equals(status, ElectionSp09ProfileIds.StatusRequiresRedesign, StringComparison.Ordinal))
        {
            return ElectionSp09ProfileIds.AvailabilityNotAvailable;
        }

        if (PlannedStatuses.Contains(status))
        {
            return ElectionSp09ProfileIds.AvailabilityPlanned;
        }

        if (ReviewedStatuses.Contains(status))
        {
            return ElectionSp09ProfileIds.AvailabilityAvailable;
        }

        return ElectionSp09ProfileIds.AvailabilityNotAvailable;
    }

    public static string ProjectLegacyAvailability(ProtocolPackageExternalReviewStatus legacyStatus) =>
        legacyStatus switch
        {
            ProtocolPackageExternalReviewStatus.ReviewRequested => ElectionSp09ProfileIds.AvailabilityPlanned,
            ProtocolPackageExternalReviewStatus.ReviewInProgress => ElectionSp09ProfileIds.AvailabilityPlanned,
            ProtocolPackageExternalReviewStatus.ReviewedWithFindings => ElectionSp09ProfileIds.AvailabilityAvailable,
            ProtocolPackageExternalReviewStatus.ReviewedAccepted => ElectionSp09ProfileIds.AvailabilityAvailable,
            _ => ElectionSp09ProfileIds.AvailabilityNotAvailable,
        };

    public static string ProjectLegacyClaimState(ProtocolPackageExternalReviewStatus legacyStatus) =>
        legacyStatus switch
        {
            ProtocolPackageExternalReviewStatus.ReviewRequested => ElectionSp09ProfileIds.ClaimStatePackageReady,
            ProtocolPackageExternalReviewStatus.ReviewInProgress => ElectionSp09ProfileIds.ClaimStateInReview,
            ProtocolPackageExternalReviewStatus.ReviewedWithFindings =>
                ElectionSp09ProfileIds.ClaimStateReviewedWithOpenFindings,
            ProtocolPackageExternalReviewStatus.ReviewedAccepted =>
                ElectionSp09ProfileIds.ClaimStateReviewedForDeclaredScope,
            _ => ElectionSp09ProfileIds.ClaimStateProgramDefined,
        };

    public static string GetAllowedWordingForClaimState(string? claimState)
    {
        var normalized = Normalize(claimState);
        return ElectionSp09ProfileIds.AllowedWordingByClaimState.TryGetValue(normalized, out var wording)
            ? wording
            : ElectionSp09ProfileIds.AllowedWordingByClaimState[ElectionSp09ProfileIds.ClaimStateNotClaimed];
    }

    public static ElectionSp09CustomerSafeReviewSummaryRecord BuildCustomerSafeSummary(
        ProtocolPackageExternalReviewStatus legacyStatus)
    {
        var claimState = ProjectLegacyClaimState(legacyStatus);
        return new ElectionSp09CustomerSafeReviewSummaryRecord(
            legacyStatus.ToString(),
            ProjectLegacyAvailability(legacyStatus),
            claimState,
            GetAllowedWordingForClaimState(claimState));
    }

    public static bool IsSupportedDetailedStatus(string? detailedStatus) =>
        ElectionSp09ProfileIds.DetailedStatusSet.Contains(Normalize(detailedStatus));

    public static bool RequiresReviewerEvidence(string? detailedStatus) =>
        ReviewedStatuses.Contains(Normalize(detailedStatus));

    public static bool HasBlockingOpenFindings(IEnumerable<ElectionSp09FindingSeverityCountRecord> findingSummary)
    {
        ArgumentNullException.ThrowIfNull(findingSummary);

        return findingSummary.Any(x =>
            x.OpenCount > 0 &&
            (string.Equals(x.Severity, "critical", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Severity, "high", StringComparison.OrdinalIgnoreCase)));
    }

    public static bool IsReviewedClaimState(string? claimState)
    {
        var normalized = Normalize(claimState);
        return string.Equals(
                normalized,
                ElectionSp09ProfileIds.ClaimStateReviewedWithOpenFindings,
                StringComparison.Ordinal) ||
            string.Equals(
                normalized,
                ElectionSp09ProfileIds.ClaimStateReviewedWithLimitations,
                StringComparison.Ordinal) ||
            string.Equals(
                normalized,
                ElectionSp09ProfileIds.ClaimStateReviewedForDeclaredScope,
                StringComparison.Ordinal);
    }

    public static bool IsStrongReviewedClaimState(string? claimState)
    {
        var normalized = Normalize(claimState);
        return string.Equals(
                normalized,
                ElectionSp09ProfileIds.ClaimStateReviewedWithLimitations,
                StringComparison.Ordinal) ||
            string.Equals(
                normalized,
                ElectionSp09ProfileIds.ClaimStateReviewedForDeclaredScope,
                StringComparison.Ordinal);
    }

    public static string GetDefaultClaimState(string? detailedStatus, bool reviewScopeMatchesElection = true)
    {
        if (!reviewScopeMatchesElection)
        {
            return ElectionSp09ProfileIds.ClaimStateNotApplicableToArtifactSet;
        }

        return Normalize(detailedStatus) switch
        {
            ElectionSp09ProfileIds.StatusNotStarted => ElectionSp09ProfileIds.ClaimStateProgramDefined,
            ElectionSp09ProfileIds.StatusPackageReady => ElectionSp09ProfileIds.ClaimStatePackageReady,
            ElectionSp09ProfileIds.StatusReviewerSelected => ElectionSp09ProfileIds.ClaimStateInReview,
            ElectionSp09ProfileIds.StatusInReview => ElectionSp09ProfileIds.ClaimStateInReview,
            ElectionSp09ProfileIds.StatusFindingsReceived => ElectionSp09ProfileIds.ClaimStateInReview,
            ElectionSp09ProfileIds.StatusRemediationInProgress => ElectionSp09ProfileIds.ClaimStateInReview,
            ElectionSp09ProfileIds.StatusRetestInProgress => ElectionSp09ProfileIds.ClaimStateInReview,
            ElectionSp09ProfileIds.StatusReviewedWithOpenFindings =>
                ElectionSp09ProfileIds.ClaimStateReviewedWithOpenFindings,
            ElectionSp09ProfileIds.StatusReviewedWithLimitations =>
                ElectionSp09ProfileIds.ClaimStateReviewedWithLimitations,
            ElectionSp09ProfileIds.StatusReviewedForDeclaredScope =>
                ElectionSp09ProfileIds.ClaimStateReviewedForDeclaredScope,
            ElectionSp09ProfileIds.StatusRequiresRedesign =>
                ElectionSp09ProfileIds.ClaimStateBlockedRequiresRedesign,
            _ => ElectionSp09ProfileIds.ClaimStateNotClaimed,
        };
    }

    public static bool ContainsForbiddenClaimPhrase(string? text)
    {
        var value = Normalize(text).ToLowerInvariant();
        return value.Length > 0 &&
            ElectionSp09ProfileIds.ForbiddenClaimPhrases.Any(x => value.Contains(x, StringComparison.Ordinal));
    }

    public static IReadOnlyList<string> Validate(
        ElectionSp09ExternalReviewStatusArtifactRecord status,
        VerificationPackageView packageView = VerificationPackageView.PublicAnonymous)
    {
        ArgumentNullException.ThrowIfNull(status);

        var errors = new List<string>();
        if (!string.Equals(
                status.Schema,
                ElectionSp09ProfileIds.ExternalReviewStatusSchema,
                StringComparison.Ordinal))
        {
            errors.Add("schema must be HushVotingExternalReviewStatus-v1");
        }

        if (!IsSupportedDetailedStatus(status.DetailedStatus))
        {
            errors.Add("detailed review status is unsupported");
        }

        if (!ElectionSp09ProfileIds.AvailabilityValueSet.Contains(status.Availability))
        {
            errors.Add("review availability is unsupported");
        }

        if (!ElectionSp09ProfileIds.ClaimStateSet.Contains(status.ClaimState))
        {
            errors.Add("claim state is unsupported");
        }

        var expectedAvailability = ProjectAvailability(status.DetailedStatus, status.ReviewScopeMatchesElection);
        if (!string.Equals(status.Availability, expectedAvailability, StringComparison.Ordinal))
        {
            errors.Add($"review availability must be {expectedAvailability} for the current status and scope match");
        }

        ValidateClaimState(status, errors);
        ValidateReviewerEvidence(status, errors);
        ValidatePublicBoundary(status, packageView, errors);

        return errors.ToArray();
    }

    private static void ValidateClaimState(
        ElectionSp09ExternalReviewStatusArtifactRecord status,
        List<string> errors)
    {
        if (IsReviewedClaimState(status.ClaimState) &&
            !string.Equals(status.Availability, ElectionSp09ProfileIds.AvailabilityAvailable, StringComparison.Ordinal))
        {
            errors.Add("reviewed claim state requires available review evidence");
        }

        if (string.Equals(
                status.DetailedStatus,
                ElectionSp09ProfileIds.StatusRequiresRedesign,
                StringComparison.Ordinal) &&
            !string.Equals(
                status.ClaimState,
                ElectionSp09ProfileIds.ClaimStateBlockedRequiresRedesign,
                StringComparison.Ordinal))
        {
            errors.Add("requires_redesign must use blocked_requires_redesign claim state");
        }

        if (HasBlockingOpenFindings(status.FindingSummary) && IsStrongReviewedClaimState(status.ClaimState))
        {
            errors.Add("open critical/high findings block strong external-review claims");
        }
    }

    private static void ValidateReviewerEvidence(
        ElectionSp09ExternalReviewStatusArtifactRecord status,
        List<string> errors)
    {
        if (!RequiresReviewerEvidence(status.DetailedStatus))
        {
            return;
        }

        if (!status.ReviewScopeMatchesElection)
        {
            errors.Add("reviewed status cannot apply when the election artifacts are outside the reviewed scope");
        }

        if (string.IsNullOrWhiteSpace(status.ReviewerEvidenceRef))
        {
            errors.Add("reviewed status requires reviewer evidence reference");
        }

        if (string.IsNullOrWhiteSpace(status.ReportHashOrRestrictedRef))
        {
            errors.Add("reviewed status requires report hash or restricted report reference");
        }

        if (status.ReviewedArtifacts.Count == 0)
        {
            errors.Add("reviewed status requires at least one reviewed artifact hash");
        }
    }

    private static void ValidatePublicBoundary(
        ElectionSp09ExternalReviewStatusArtifactRecord status,
        VerificationPackageView packageView,
        List<string> errors)
    {
        var forbiddenFields = VerificationPrivacyBoundary.FindForbiddenPublicFields(status.PublicPrivacyBoundary);
        if (forbiddenFields.Count > 0)
        {
            errors.Add($"public privacy boundary contains forbidden fields: {string.Join(",", forbiddenFields)}");
        }

        if (packageView != VerificationPackageView.PublicAnonymous)
        {
            return;
        }

        var publicRestrictedEntries = status.PublicEvidenceFiles
            .Where(VerificationPrivacyBoundary.IsRestrictedArtifactPath)
            .ToArray();
        if (publicRestrictedEntries.Length > 0)
        {
            errors.Add(
                $"public external-review status references restricted evidence: {string.Join(",", publicRestrictedEntries)}");
        }

        if (status.RestrictedEvidenceFiles.Count > 0)
        {
            errors.Add("public external-review status must not include restricted evidence files");
        }

        if (!string.IsNullOrWhiteSpace(status.ReportHashOrRestrictedRef) &&
            VerificationPrivacyBoundary.IsRestrictedArtifactPath(status.ReportHashOrRestrictedRef))
        {
            errors.Add("public external-review status must not include a restricted report reference");
        }
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim();
}
