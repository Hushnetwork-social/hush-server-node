using FluentAssertions;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionSp09ContractTests
{
    [Fact]
    public void ExternalReviewProfileIds_ShouldExposeCanonicalV1Selection()
    {
        ElectionSp09ProfileIds.ExternalExaminationProgramVersion.Should().Be("SP09-P1");
        ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1.Should().Be("protocol_proof_verifier_publication_path_v1");
        ElectionSp09ProfileIds.ReviewTypeCryptographicSecurity.Should().Be("private_third_party_crypto_protocol_review_v1");
        ElectionSp09ProfileIds.ReviewPhaseProtocolProofP1.Should().Be("SP09-P1");
        ElectionSp09ProfileIds.DetailedStatuses.Should().Equal(
            "not_started",
            "package_ready",
            "reviewer_selected",
            "in_review",
            "findings_received",
            "remediation_in_progress",
            "retest_in_progress",
            "reviewed_with_open_findings",
            "reviewed_with_limitations",
            "reviewed_for_declared_scope",
            "requires_redesign");
        ElectionSp09ProfileIds.AvailabilityValues.Should().Equal("not_available", "planned", "available");
        ElectionSp09ProfileIds.ExternalReviewCheckCodes.Should().Equal(
            "REV-000",
            "REV-001",
            "REV-002",
            "REV-003",
            "REV-004",
            "REV-005",
            "REV-006",
            "REV-007",
            "REV-008");
    }

    [Theory]
    [InlineData(ElectionSp09ProfileIds.StatusNotStarted, ElectionSp09ProfileIds.AvailabilityPlanned)]
    [InlineData(ElectionSp09ProfileIds.StatusPackageReady, ElectionSp09ProfileIds.AvailabilityPlanned)]
    [InlineData(ElectionSp09ProfileIds.StatusReviewerSelected, ElectionSp09ProfileIds.AvailabilityPlanned)]
    [InlineData(ElectionSp09ProfileIds.StatusInReview, ElectionSp09ProfileIds.AvailabilityPlanned)]
    [InlineData(ElectionSp09ProfileIds.StatusFindingsReceived, ElectionSp09ProfileIds.AvailabilityPlanned)]
    [InlineData(ElectionSp09ProfileIds.StatusRemediationInProgress, ElectionSp09ProfileIds.AvailabilityPlanned)]
    [InlineData(ElectionSp09ProfileIds.StatusRetestInProgress, ElectionSp09ProfileIds.AvailabilityPlanned)]
    [InlineData(ElectionSp09ProfileIds.StatusReviewedWithOpenFindings, ElectionSp09ProfileIds.AvailabilityAvailable)]
    [InlineData(ElectionSp09ProfileIds.StatusReviewedWithLimitations, ElectionSp09ProfileIds.AvailabilityAvailable)]
    [InlineData(ElectionSp09ProfileIds.StatusReviewedForDeclaredScope, ElectionSp09ProfileIds.AvailabilityAvailable)]
    [InlineData(ElectionSp09ProfileIds.StatusRequiresRedesign, ElectionSp09ProfileIds.AvailabilityNotAvailable)]
    [InlineData("legacy_missing_status", ElectionSp09ProfileIds.AvailabilityNotAvailable)]
    public void DetailedStatuses_ShouldProjectToCustomerSafeAvailability(
        string detailedStatus,
        string expectedAvailability)
    {
        ElectionSp09ExternalReviewRules.ProjectAvailability(detailedStatus).Should().Be(expectedAvailability);
    }

    [Fact]
    public void ReviewedScopeMismatch_ShouldProjectToNotAvailableAndBlockReviewedClaim()
    {
        ElectionSp09ExternalReviewRules.ProjectAvailability(
                ElectionSp09ProfileIds.StatusReviewedForDeclaredScope,
                reviewScopeMatchesElection: false)
            .Should()
            .Be(ElectionSp09ProfileIds.AvailabilityNotAvailable);
        ElectionSp09ExternalReviewRules.GetDefaultClaimState(
                ElectionSp09ProfileIds.StatusReviewedForDeclaredScope,
                reviewScopeMatchesElection: false)
            .Should()
            .Be(ElectionSp09ProfileIds.ClaimStateNotApplicableToArtifactSet);

        var status = CreateStatus(
            ElectionSp09ProfileIds.StatusReviewedForDeclaredScope,
            reviewScopeMatchesElection: false,
            claimState: ElectionSp09ProfileIds.ClaimStateReviewedForDeclaredScope);

        ElectionSp09ExternalReviewRules.Validate(status).Should()
            .Contain(x => x.Contains("reviewed claim state requires available review evidence", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyCoarseReviewStatuses_ShouldMapWithoutCreatingCertificationClaims()
    {
        ElectionSp09ExternalReviewRules.ProjectLegacyAvailability(ProtocolPackageExternalReviewStatus.NotReviewed)
            .Should()
            .Be(ElectionSp09ProfileIds.AvailabilityNotAvailable);
        ElectionSp09ExternalReviewRules.ProjectLegacyAvailability(ProtocolPackageExternalReviewStatus.ReviewRequested)
            .Should()
            .Be(ElectionSp09ProfileIds.AvailabilityPlanned);
        ElectionSp09ExternalReviewRules.ProjectLegacyAvailability(ProtocolPackageExternalReviewStatus.ReviewInProgress)
            .Should()
            .Be(ElectionSp09ProfileIds.AvailabilityPlanned);
        ElectionSp09ExternalReviewRules.ProjectLegacyAvailability(ProtocolPackageExternalReviewStatus.ReviewedWithFindings)
            .Should()
            .Be(ElectionSp09ProfileIds.AvailabilityAvailable);
        ElectionSp09ExternalReviewRules.ProjectLegacyAvailability(ProtocolPackageExternalReviewStatus.ReviewedAccepted)
            .Should()
            .Be(ElectionSp09ProfileIds.AvailabilityAvailable);
    }

    [Theory]
    [InlineData(
        ProtocolPackageExternalReviewStatus.NotReviewed,
        ElectionSp09ProfileIds.AvailabilityNotAvailable,
        ElectionSp09ProfileIds.ClaimStateProgramDefined)]
    [InlineData(
        ProtocolPackageExternalReviewStatus.ReviewRequested,
        ElectionSp09ProfileIds.AvailabilityPlanned,
        ElectionSp09ProfileIds.ClaimStatePackageReady)]
    [InlineData(
        ProtocolPackageExternalReviewStatus.ReviewInProgress,
        ElectionSp09ProfileIds.AvailabilityPlanned,
        ElectionSp09ProfileIds.ClaimStateInReview)]
    [InlineData(
        ProtocolPackageExternalReviewStatus.ReviewedWithFindings,
        ElectionSp09ProfileIds.AvailabilityAvailable,
        ElectionSp09ProfileIds.ClaimStateReviewedWithOpenFindings)]
    [InlineData(
        ProtocolPackageExternalReviewStatus.ReviewedAccepted,
        ElectionSp09ProfileIds.AvailabilityAvailable,
        ElectionSp09ProfileIds.ClaimStateReviewedForDeclaredScope)]
    public void CustomerSafeSummary_ShouldUseAllowedWordingWithoutCertificationClaims(
        ProtocolPackageExternalReviewStatus legacyStatus,
        string expectedAvailability,
        string expectedClaimState)
    {
        var summary = ElectionSp09ExternalReviewRules.BuildCustomerSafeSummary(legacyStatus);

        summary.Availability.Should().Be(expectedAvailability);
        summary.ClaimState.Should().Be(expectedClaimState);
        summary.Wording.Should().Be(ElectionSp09ProfileIds.AllowedWordingByClaimState[expectedClaimState]);
        ElectionSp09ExternalReviewRules.ContainsForbiddenClaimPhrase(summary.Wording).Should().BeFalse();
    }

    [Fact]
    public void ReviewedStatuses_ShouldRequireReviewerProducedEvidence()
    {
        var status = CreateStatus(
            ElectionSp09ProfileIds.StatusReviewedForDeclaredScope,
            includeReviewerEvidence: false,
            reviewedArtifacts: []);

        var errors = ElectionSp09ExternalReviewRules.Validate(status);

        errors.Should().Contain(x => x.Contains("reviewer evidence reference", StringComparison.Ordinal));
        errors.Should().Contain(x => x.Contains("report hash or restricted report reference", StringComparison.Ordinal));
        errors.Should().Contain(x => x.Contains("reviewed artifact hash", StringComparison.Ordinal));
    }

    [Fact]
    public void ReviewedWithLimitationsStatus_ShouldValidateWhenScopedReviewerEvidenceExists()
    {
        var status = CreateStatus(ElectionSp09ProfileIds.StatusReviewedWithLimitations);

        ElectionSp09ExternalReviewRules.Validate(status).Should().BeEmpty();
    }

    [Fact]
    public void OpenCriticalOrHighFindings_ShouldBlockStrongExternalReviewClaims()
    {
        var status = CreateStatus(
            ElectionSp09ProfileIds.StatusReviewedWithLimitations,
            findingSummary:
            [
                new ElectionSp09FindingSeverityCountRecord("high", OpenCount: 1, FixedCount: 0, AcceptedLimitationCount: 0),
            ]);

        ElectionSp09ExternalReviewRules.Validate(status).Should()
            .Contain(x => x.Contains("open critical/high findings block strong", StringComparison.Ordinal));
    }

    [Fact]
    public void RequiresRedesign_ShouldProjectToBlockedNotAvailableClaimState()
    {
        ElectionSp09ExternalReviewRules.ProjectAvailability(ElectionSp09ProfileIds.StatusRequiresRedesign)
            .Should()
            .Be(ElectionSp09ProfileIds.AvailabilityNotAvailable);
        ElectionSp09ExternalReviewRules.GetDefaultClaimState(ElectionSp09ProfileIds.StatusRequiresRedesign)
            .Should()
            .Be(ElectionSp09ProfileIds.ClaimStateBlockedRequiresRedesign);

        var status = CreateStatus(
            ElectionSp09ProfileIds.StatusRequiresRedesign,
            claimState: ElectionSp09ProfileIds.ClaimStatePackageReady,
            includeReviewerEvidence: false,
            reviewedArtifacts: []);

        ElectionSp09ExternalReviewRules.Validate(status).Should()
            .Contain(x => x.Contains("requires_redesign must use blocked_requires_redesign", StringComparison.Ordinal));
    }

    [Fact]
    public void ReviewCheckDefinitions_ShouldExposeStablePassWarnFailSemantics()
    {
        ElectionSp09ProfileIds.ReviewCheckDefinitions.Keys.Should()
            .BeEquivalentTo(ElectionSp09ProfileIds.ExternalReviewCheckCodes);
        ElectionSp09ProfileIds.ReviewCheckDefinitions[ElectionSp09ProfileIds.ReviewStatusValidCheckCode]
            .ViolationStatus.Should()
            .Be(VerificationCheckStatus.Pass);
        ElectionSp09ProfileIds.ReviewCheckDefinitions[ElectionSp09ProfileIds.ReviewNotCompleteCheckCode]
            .ViolationStatus.Should()
            .Be(VerificationCheckStatus.Warn);

        ElectionSp09ProfileIds.ExternalReviewCheckCodes
            .Except(
                [
                    ElectionSp09ProfileIds.ReviewStatusValidCheckCode,
                    ElectionSp09ProfileIds.ReviewNotCompleteCheckCode,
                ],
                StringComparer.Ordinal)
            .Should()
            .AllSatisfy(code =>
                ElectionSp09ProfileIds.ReviewCheckDefinitions[code].ViolationStatus.Should()
                    .Be(VerificationCheckStatus.Fail));
    }

    [Fact]
    public void VerificationResultCodes_ShouldExposeStableExternalReviewCodes()
    {
        var codes = new[]
        {
            VerificationResultCodes.ExternalReviewStatusValid,
            VerificationResultCodes.ExternalReviewProgramMissing,
            VerificationResultCodes.ExternalReviewNotComplete,
            VerificationResultCodes.ExternalReviewScopeMismatch,
            VerificationResultCodes.ExternalReviewReportHashMismatch,
            VerificationResultCodes.ExternalReviewOpenFindingsBlockClaims,
            VerificationResultCodes.ExternalReviewClaimNotAllowed,
            VerificationResultCodes.ExternalReviewPublicBoundaryViolation,
            VerificationResultCodes.ExternalReviewRequiresRedesign,
        };

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(x => x.Should().StartWith("external_review_"));
    }

    [Fact]
    public void AllowedWording_ShouldRejectCertificationAndAuditLanguage()
    {
        ElectionSp09ProfileIds.AllowedWordingByClaimState.Values.Should()
            .AllSatisfy(x =>
                ElectionSp09ExternalReviewRules.ContainsForbiddenClaimPhrase(x).Should().BeFalse());

        ElectionSp09ExternalReviewRules.ContainsForbiddenClaimPhrase("Certified for public elections")
            .Should()
            .BeTrue();
        ElectionSp09ExternalReviewRules.ContainsForbiddenClaimPhrase("Externally audited and bug-free")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Sp09PackageFileNames_ShouldSeparatePublicAndRestrictedReviewArtifacts()
    {
        VerificationPackageFileNames.Sp09ExternalReviewStatus.Should()
            .Be("artifacts/election-record/external-review-status.json");
        VerificationPackageFileNames.Sp09ExternalReviewClaimTable.Should()
            .Be("artifacts/election-record/external-review-claim-table.json");
        VerificationPackageFileNames.Sp09ExternalReviewVerifierOutput.Should()
            .Be("artifacts/election-record/external-review-verifier-output.json");
        VerificationPackageFileNames.RestrictedSp09FindingTracker.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedSp09RetestEvidence.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedSp09ReportReference.Should().StartWith("artifacts/restricted/");
    }

    [Theory]
    [InlineData("fullReportBody")]
    [InlineData("reviewerWorkpaper")]
    [InlineData("findingBody")]
    [InlineData("retestEvidenceBody")]
    [InlineData("confidentialReportUrl")]
    public void PublicPrivacyBoundary_ShouldRejectRestrictedReviewFields(string fieldName)
    {
        VerificationPrivacyBoundary.IsForbiddenInPublicPackage(fieldName).Should().BeTrue();
    }

    [Fact]
    public void PublicStatusArtifacts_ShouldRejectRestrictedEvidenceRefs()
    {
        var status = CreateStatus(
            ElectionSp09ProfileIds.StatusPackageReady,
            restrictedEvidenceFiles:
            [
                VerificationPackageFileNames.RestrictedSp09FindingTracker,
            ]);

        ElectionSp09ExternalReviewRules.Validate(status).Should()
            .Contain(x => x.Contains("must not include restricted evidence files", StringComparison.Ordinal));
    }

    private static ElectionSp09ExternalReviewStatusArtifactRecord CreateStatus(
        string detailedStatus,
        bool reviewScopeMatchesElection = true,
        string? claimState = null,
        bool includeReviewerEvidence = true,
        IReadOnlyList<ElectionSp09ReviewedArtifactRecord>? reviewedArtifacts = null,
        IReadOnlyList<ElectionSp09FindingSeverityCountRecord>? findingSummary = null,
        IReadOnlyList<string>? restrictedEvidenceFiles = null)
    {
        var availability = ElectionSp09ExternalReviewRules.ProjectAvailability(
            detailedStatus,
            reviewScopeMatchesElection);
        var requiresReviewerEvidence = ElectionSp09ExternalReviewRules.RequiresReviewerEvidence(detailedStatus);
        var resolvedReviewedArtifacts = reviewedArtifacts ??
            (requiresReviewerEvidence
                ?
                [
                    new ElectionSp09ReviewedArtifactRecord(
                        ArtifactId: "protocol-package-manifest",
                        ArtifactType: "protocol_package_manifest",
                        ArtifactName: "ProtocolOmegaPackageManifest.json",
                        ArtifactHash: "sha256:protocol-package",
                        ArtifactVersion: "v1.1.9",
                        ReviewScope: ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1),
                ]
                : []);

        return new ElectionSp09ExternalReviewStatusArtifactRecord(
            Schema: ElectionSp09ProfileIds.ExternalReviewStatusSchema,
            ElectionId: Guid.NewGuid().ToString("D"),
            ProgramVersion: ElectionSp09ProfileIds.ExternalExaminationProgramVersion,
            ReviewScope: ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1,
            ReviewType: ElectionSp09ProfileIds.ReviewTypeCryptographicSecurity,
            ReviewPhase: ElectionSp09ProfileIds.ReviewPhaseProtocolProofP1,
            DetailedStatus: detailedStatus,
            Availability: availability,
            ClaimState: claimState ?? ElectionSp09ExternalReviewRules.GetDefaultClaimState(
                detailedStatus,
                reviewScopeMatchesElection),
            ReviewScopeMatchesElection: reviewScopeMatchesElection,
            PrimaryResultCode: availability == ElectionSp09ProfileIds.AvailabilityPlanned
                ? VerificationResultCodes.ExternalReviewNotComplete
                : VerificationResultCodes.ExternalReviewStatusValid,
            PrimaryIssue: null,
            ReviewerEvidenceRef: includeReviewerEvidence && requiresReviewerEvidence ? "reviewer:engagement-42" : null,
            ReportHashOrRestrictedRef: includeReviewerEvidence && requiresReviewerEvidence
                ? "sha256:external-review-report"
                : null,
            CustomerSafeSummaryHash: includeReviewerEvidence && requiresReviewerEvidence
                ? "sha256:customer-summary"
                : null,
            CustomerSafeSummaryUrl: null,
            KnownLimitationsVersion: "limitations-v1",
            KnownLimitationsHash: "sha256:limitations",
            ReviewedArtifacts: resolvedReviewedArtifacts,
            FindingSummary: findingSummary ?? [],
            PublicEvidenceFiles:
            [
                VerificationPackageFileNames.Sp09ExternalReviewStatus,
                VerificationPackageFileNames.Sp09ExternalReviewClaimTable,
            ],
            RestrictedEvidenceFiles: restrictedEvidenceFiles ?? [],
            PublicPrivacyBoundary:
            [
                "no_full_report_body",
                "no_finding_body",
                "no_reviewer_workpaper",
                "no_retest_evidence_body",
            ]);
    }
}
