using FluentAssertions;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyReportReadinessProjectionBuilderTests
{
    private static readonly DateTime GeneratedAt = new(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_WithNoAnomalyManifest_ReturnsNoAnomalyHoldEvidence()
    {
        var electionId = ElectionId.NewElectionId;
        var summary = ElectionAnomalyPublicSummaryBuilder.Build(new(
            electionId.ToString(),
            RestrictedAnomalyIntakeManifest: null,
            RestrictedManifestArtifactId: null,
            GeneratedAt));

        var readiness = ElectionAnomalyReportReadinessProjectionBuilder.Build(new(
            summary,
            RestrictedAnomalyIntakeManifest: null,
            ElectionAnomalyPublicArtifactScanStatusIds.Passed));

        readiness.PublicSummarySchemaId.Should().Be(ElectionAnomalyPublicSummarySchemaIds.Current);
        readiness.SuppressionPolicyId.Should().Be(ElectionAnomalyPublicSummarySuppressionPolicyIds.Current);
        readiness.ForbiddenFieldScanStatusId.Should().Be(ElectionAnomalyPublicArtifactScanStatusIds.Passed);
        readiness.PackageReadinessStatusId.Should().Be(ElectionAnomalyPackageReadinessStatusIds.Ready);
        readiness.PackageReadinessBlockerIds.Should().BeEmpty();
        readiness.RetentionEvidenceStatusId.Should().Be(ElectionAnomalyRetentionEvidenceStatusIds.NoAnomalyHoldEvidence);
        readiness.RetentionEvidenceStatus.ReadinessBlocksValidationClaims.Should().BeFalse();
        readiness.ReportGenerationReadOnlyStatusId.Should().Be(ElectionAnomalyReportGenerationReadOnlyStatusIds.Validated);
    }

    [Fact]
    public void Build_WithOpenAnomalyCase_BlocksReadinessClaimsForPolicyReview()
    {
        var electionId = ElectionId.NewElectionId;
        var manifest = CreateManifest(
            electionId,
            CreateThread(ElectionAnomalyCaseStateIds.UnderReview));
        var summary = BuildSummary(electionId, manifest);

        var readiness = ElectionAnomalyReportReadinessProjectionBuilder.Build(new(
            summary,
            manifest,
            ElectionAnomalyPublicArtifactScanStatusIds.Passed));

        readiness.RetentionEvidenceStatusId.Should().Be(ElectionAnomalyRetentionEvidenceStatusIds.OpenCaseRequiresPolicyReview);
        readiness.OpenCaseCount.Should().Be(1);
        readiness.EscalatedCaseCount.Should().Be(0);
        readiness.RetentionEvidenceStatus.ReadinessBlocksValidationClaims.Should().BeTrue();
    }

    [Fact]
    public void Build_WithGovernedReference_RecordsGovernedHoldEvidenceWithoutActivatingHold()
    {
        var electionId = ElectionId.NewElectionId;
        var manifest = CreateManifest(
            electionId,
            CreateThread(
                ElectionAnomalyCaseStateIds.EscalatedToGovernedDecision,
                governedDecisionRef: "governed-proposal-1"));
        var summary = BuildSummary(electionId, manifest);

        var readiness = ElectionAnomalyReportReadinessProjectionBuilder.Build(new(
            summary,
            manifest,
            ElectionAnomalyPublicArtifactScanStatusIds.Passed));

        readiness.RetentionEvidenceStatusId.Should().Be(ElectionAnomalyRetentionEvidenceStatusIds.GovernedHoldReferenceRecorded);
        readiness.HasGovernedLifecycleEvidence.Should().BeTrue();
        readiness.EscalatedCaseCount.Should().Be(1);
        readiness.RetentionEvidenceStatus.GovernedDecisionRefs.Should().ContainSingle()
            .Which.Should().Be("governed-proposal-1");
        readiness.RetentionEvidenceStatus.ReadinessBlocksValidationClaims.Should().BeFalse();
    }

    [Fact]
    public void Build_WithLegalHoldRedactionReference_ReportsRestrictedRedactionHoldReference()
    {
        var electionId = ElectionId.NewElectionId;
        var manifest = CreateManifest(
            electionId,
            CreateThread(
                ElectionAnomalyCaseStateIds.ResolvedNonBlocking,
                redactions:
                [
                    new AnomalyIntakeManifestRedaction(
                        Guid.Parse("10000000-0000-0000-0000-000000000001"),
                        Guid.Parse("10000000-0000-0000-0000-000000000002"),
                        "sha256:redaction-event",
                        ElectionAnomalyRedactionTargetKindIds.AttachmentManifest,
                        "attachment-1",
                        ElectionAnomalyRedactionReasonIds.LegalHold,
                        "sha256:original",
                        ReplacementManifestHash: null,
                        TombstoneStatusId: null,
                        GeneratedAt,
                        Guid.Parse("10000000-0000-0000-0000-000000000003")),
                ]));
        var summary = BuildSummary(electionId, manifest);

        var readiness = ElectionAnomalyReportReadinessProjectionBuilder.Build(new(
            summary,
            manifest,
            ElectionAnomalyPublicArtifactScanStatusIds.Passed));

        readiness.RetentionEvidenceStatusId.Should().Be(ElectionAnomalyRetentionEvidenceStatusIds.RestrictedRedactionHoldReferencePresent);
        readiness.RetentionEvidenceStatus.RedactionHoldReferenceCount.Should().Be(1);
        readiness.RetentionEvidenceStatus.ReadinessBlocksValidationClaims.Should().BeTrue();
    }

    [Fact]
    public void Build_WithClosedAnomalyAndNoHoldEvidence_ReportsRetentionHoldNotImplemented()
    {
        var electionId = ElectionId.NewElectionId;
        var manifest = CreateManifest(
            electionId,
            CreateThread(ElectionAnomalyCaseStateIds.ResolvedNonBlocking));
        var summary = BuildSummary(electionId, manifest);

        var readiness = ElectionAnomalyReportReadinessProjectionBuilder.Build(new(
            summary,
            manifest,
            ElectionAnomalyPublicArtifactScanStatusIds.Passed));

        readiness.RetentionEvidenceStatusId.Should().Be(ElectionAnomalyRetentionEvidenceStatusIds.RetentionHoldNotImplemented);
        readiness.OpenCaseCount.Should().Be(0);
        readiness.EscalatedCaseCount.Should().Be(0);
        readiness.RetentionEvidenceStatus.ReadinessBlocksValidationClaims.Should().BeTrue();
    }

    [Fact]
    public void Build_WithPackageReadinessBlockers_ExposesSortedBlockerIds()
    {
        var electionId = ElectionId.NewElectionId;
        var manifest = CreateManifest(
            electionId,
            CreateThread(ElectionAnomalyCaseStateIds.ResolvedNonBlocking)) with
        {
            PackageReadinessStatusId = ElectionAnomalyPackageReadinessStatusIds.Blocked,
            PackageReadinessBlockerIds =
            [
                ElectionAnomalyPayloadAvailabilityStatusIds.PayloadMissing,
                ElectionAnomalyEvidenceScannerStatusIds.Pending,
            ],
        };
        var summary = BuildSummary(electionId, manifest);

        var readiness = ElectionAnomalyReportReadinessProjectionBuilder.Build(new(
            summary,
            manifest,
            ElectionAnomalyPublicArtifactScanStatusIds.Passed,
            ReportGenerationReadOnlyValidated: false));

        readiness.PackageReadinessStatusId.Should().Be(ElectionAnomalyPackageReadinessStatusIds.Blocked);
        readiness.PackageReadinessBlockerIds.Should().Equal(
            ElectionAnomalyPayloadAvailabilityStatusIds.PayloadMissing,
            ElectionAnomalyEvidenceScannerStatusIds.Pending);
        readiness.ReportGenerationReadOnlyStatusId.Should().Be(ElectionAnomalyReportGenerationReadOnlyStatusIds.NotValidated);
    }

    private static PublicAnomalySummary BuildSummary(ElectionId electionId, AnomalyIntakeManifest manifest) =>
        ElectionAnomalyPublicSummaryBuilder.Build(new(
            electionId.ToString(),
            manifest,
            RestrictedManifestArtifactId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            GeneratedAt));

    private static AnomalyIntakeManifest CreateManifest(
        ElectionId electionId,
        params AnomalyIntakeManifestThread[] threads) =>
        new(
            ElectionAnomalyManifestCanonicalizationIds.Current,
            electionId.ToString(),
            ElectionAnomalyEvidenceManifestScopeIds.Package,
            ElectionAnomalyPackageReadinessStatusIds.Ready,
            Array.Empty<string>(),
            threads);

    private static AnomalyIntakeManifestThread CreateThread(
        string caseStateId,
        string? governedDecisionRef = null,
        IReadOnlyList<AnomalyIntakeManifestRedaction>? redactions = null) =>
        new(
            Guid.NewGuid(),
            ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern,
            caseStateId,
            "sha256:thread",
            governedDecisionRef,
            HasOpenClarificationRequest: false,
            OpenClarificationRequestId: null,
            GeneratedAt,
            GeneratedAt,
            Attachments: Array.Empty<AnomalyIntakeManifestAttachment>(),
            Redactions: redactions ?? Array.Empty<AnomalyIntakeManifestRedaction>(),
            RecipientStatuses:
            [
                new AnomalyIntakeManifestRecipientStatus(
                    ElectionAnomalyRecipientRoleIds.ElectionOwner,
                    ElectionAnomalyRecipientWrapStatusIds.Available),
            ]);
}
