using FluentAssertions;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionSp10OperationalSecurityContractTests
{
    [Fact]
    public void OperationalSecurityProfileIds_ShouldExposeCanonicalV1Selection()
    {
        ElectionSp10ProfileIds.OperationalSecurityProgramVersion.Should().Be("SP10-P1");
        ElectionSp10ProfileIds.DeploymentProfileManagedAwsContainerV1.Should()
            .Be("hushvoting_managed_aws_container_v1");
        ElectionSp10ProfileIds.CustodyModes.Should().Equal(
            "aws_kms_per_election_envelope_v1",
            "trustee_local_secure_vault_v1");
        ElectionSp10ProfileIds.OperationalEvidenceStates.Should().Equal(
            "not_available",
            "development_placeholder",
            "managed_profile_declared",
            "managed_profile_evidence_available",
            "managed_profile_exception_declared",
            "blocked");
        ElectionSp10ProfileIds.OperationalCheckCodes.Should().Equal(
            "OPS-000",
            "OPS-001",
            "OPS-002",
            "OPS-003",
            "OPS-004",
            "OPS-005",
            "OPS-006",
            "OPS-007",
            "OPS-008");
    }

    [Theory]
    [InlineData(ElectionSp10ProfileIds.EvidenceStateNotAvailable, true, false)]
    [InlineData(ElectionSp10ProfileIds.EvidenceStateDevelopmentPlaceholder, true, false)]
    [InlineData(ElectionSp10ProfileIds.EvidenceStateManagedProfileDeclared, false, false)]
    [InlineData(ElectionSp10ProfileIds.EvidenceStateManagedProfileEvidenceAvailable, false, true)]
    [InlineData(ElectionSp10ProfileIds.EvidenceStateManagedProfileExceptionDeclared, true, false)]
    [InlineData(ElectionSp10ProfileIds.EvidenceStateBlocked, true, false)]
    public void OperationalEvidenceState_ShouldKeepHighAssuranceAndReadinessSeparate(
        string evidenceState,
        bool expectedBlocksHighAssurance,
        bool expectedAllowsOperationalClaim)
    {
        ElectionSp10OperationalSecurityRules.BlocksHighAssurance(evidenceState)
            .Should()
            .Be(expectedBlocksHighAssurance);
        ElectionSp10OperationalSecurityRules.IsHighAssuranceOperationalClaimAllowed(evidenceState)
            .Should()
            .Be(expectedAllowsOperationalClaim);
    }

    [Fact]
    public void OperationalCheckDefinitions_ShouldExposeStablePassWarnFailSemantics()
    {
        ElectionSp10ProfileIds.OperationalCheckDefinitions.Keys.Should()
            .BeEquivalentTo(ElectionSp10ProfileIds.OperationalCheckCodes);
        ElectionSp10ProfileIds.OperationalCheckDefinitions[
                ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode]
            .ViolationStatus.Should()
            .Be(VerificationCheckStatus.Pass);

        ElectionSp10ProfileIds.OperationalCheckCodes
            .Except([ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode], StringComparer.Ordinal)
            .Should()
            .AllSatisfy(code =>
                ElectionSp10ProfileIds.OperationalCheckDefinitions[code].ViolationStatus.Should()
                    .Be(VerificationCheckStatus.Fail));
    }

    [Fact]
    public void VerificationResultCodes_ShouldExposeStableOperationalSecurityCodes()
    {
        var codes = new[]
        {
            VerificationResultCodes.OperationalSecurityEvidenceValid,
            VerificationResultCodes.OperationalSecurityEvidenceMissing,
            VerificationResultCodes.OperationalSecurityDevelopmentPlaceholder,
            VerificationResultCodes.OperationalSecurityProfileDeclared,
            VerificationResultCodes.OperationalSecurityExceptionDeclared,
            VerificationResultCodes.OperationalSecurityBlocked,
            VerificationResultCodes.OperationalSecurityReleaseBindingMissing,
            VerificationResultCodes.OperationalSecurityAccessSnapshotMissing,
            VerificationResultCodes.OperationalSecurityCustodyModeMissing,
            VerificationResultCodes.OperationalSecurityExecutorKeyLifecycleMissing,
            VerificationResultCodes.OperationalSecurityForbiddenMaterial,
            VerificationResultCodes.OperationalSecurityBackupRestoreMissing,
            VerificationResultCodes.OperationalSecurityIncidentDeclarationMissing,
            VerificationResultCodes.OperationalSecurityAuditorRoomMissing,
        };

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(x => x.Should().StartWith("operational_security_"));
    }

    [Fact]
    public void OperationalStatus_ShouldValidateCompleteManagedEvidenceWithoutCompletingFeat106()
    {
        var status = CreateOperationalStatus(
            ElectionSp10ProfileIds.EvidenceStateManagedProfileEvidenceAvailable);

        ElectionSp10OperationalSecurityRules.Validate(status).Should().BeEmpty();
    }

    [Fact]
    public void OperationalStatus_ShouldRejectFeat106ReadinessCollapse()
    {
        var status = CreateOperationalStatus(
            ElectionSp10ProfileIds.EvidenceStateManagedProfileEvidenceAvailable,
            doesNotCompleteFeat106: false);

        ElectionSp10OperationalSecurityRules.Validate(status).Should()
            .Contain(x => x.Contains("FEAT-106 readiness separate", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationalStatus_ShouldRequireManagedEvidenceForHighAssuranceOperationalClaim()
    {
        var status = CreateOperationalStatus(
            ElectionSp10ProfileIds.EvidenceStateManagedProfileEvidenceAvailable,
            releaseManifestHash: null,
            immutableDeploymentRef: null,
            custodyMode: null,
            incidentStatus: null);

        var errors = ElectionSp10OperationalSecurityRules.Validate(status);

        errors.Should().Contain(x => x.Contains("release manifest hash", StringComparison.Ordinal));
        errors.Should().Contain(x => x.Contains("supported custody mode", StringComparison.Ordinal));
        errors.Should().Contain(x => x.Contains("incident", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationalAllowedWording_ShouldRejectReadinessCertificationAndSwissParityLanguage()
    {
        ElectionSp10ProfileIds.AllowedWordingByEvidenceState.Values.Should()
            .AllSatisfy(x =>
                ElectionSp10OperationalSecurityRules.ContainsForbiddenClaimPhrase(x).Should().BeFalse());

        ElectionSp10OperationalSecurityRules.ContainsForbiddenClaimPhrase("FEAT-106 complete")
            .Should()
            .BeTrue();
        ElectionSp10OperationalSecurityRules.ContainsForbiddenClaimPhrase("Certified for public elections")
            .Should()
            .BeTrue();
        ElectionSp10OperationalSecurityRules.ContainsForbiddenClaimPhrase("Same assurance as Swiss public e-voting")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void OperationalPackageFileNames_ShouldSeparatePublicAndRestrictedArtifacts()
    {
        VerificationPackageFileNames.Sp10OperationalSecuritySummary.Should()
            .Be("artifacts/election-record/operational-security-summary.json");
        VerificationPackageFileNames.Sp10OperationalDeploymentEvidence.Should()
            .Be("artifacts/election-record/operational-deployment-evidence.json");
        VerificationPackageFileNames.Sp10OperationalCustodyEvidence.Should()
            .Be("artifacts/election-record/operational-custody-evidence.json");
        VerificationPackageFileNames.Sp10OperationalVerifierOutput.Should()
            .Be("artifacts/election-record/operational-verifier-output.json");
        VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedSp10LoggingEvidence.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedSp10BackupRestoreEvidence.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedSp10IncidentEvidence.Should().StartWith("artifacts/restricted/");
        VerificationPackageFileNames.RestrictedSp10AuditorRoomAccessLog.Should().StartWith("artifacts/restricted/");
    }

    [Theory]
    [InlineData("rawLogLine")]
    [InlineData("kmsPlaintextKey")]
    [InlineData("executorPrivateKey")]
    [InlineData("iamPolicyDocument")]
    [InlineData("incidentWorkpaper")]
    public void PublicPrivacyBoundary_ShouldRejectOperationalRestrictedFields(string fieldName)
    {
        VerificationPrivacyBoundary.IsForbiddenInPublicPackage(fieldName).Should().BeTrue();
    }

    [Fact]
    public void PublicOperationalStatus_ShouldRejectRestrictedEvidenceRefs()
    {
        var status = CreateOperationalStatus(
            ElectionSp10ProfileIds.EvidenceStateManagedProfileDeclared,
            restrictedEvidenceFiles:
            [
                VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot,
            ]);

        ElectionSp10OperationalSecurityRules.Validate(status).Should()
            .Contain(x => x.Contains("must not include restricted evidence files", StringComparison.Ordinal));
    }

    [Fact]
    public void RegulatoryProfileIds_ShouldExposeCanonicalClaimStatesAndChecks()
    {
        ElectionSp11ProfileIds.RegulatoryTrackerVersion.Should().Be("SP11-P1");
        ElectionSp11ProfileIds.ClaimStates.Should().Equal(
            "allowed_now",
            "allowed_with_limitation",
            "blocked_until_review",
            "blocked_until_certification",
            "forbidden");
        ElectionSp11ProfileIds.RegulatoryCheckCodes.Should().Equal(
            "REG-000",
            "REG-001",
            "REG-002",
            "REG-003",
            "REG-004");
    }

    [Fact]
    public void RegulatoryCheckDefinitions_ShouldExposeStablePassWarnFailSemantics()
    {
        ElectionSp11ProfileIds.RegulatoryCheckDefinitions.Keys.Should()
            .BeEquivalentTo(ElectionSp11ProfileIds.RegulatoryCheckCodes);
        ElectionSp11ProfileIds.RegulatoryCheckDefinitions[
                ElectionSp11ProfileIds.RegulatoryClaimShapeValidCheckCode]
            .ViolationStatus.Should()
            .Be(VerificationCheckStatus.Pass);
        ElectionSp11ProfileIds.RegulatoryCheckDefinitions[
                ElectionSp11ProfileIds.ClaimAllowedByRegisterCheckCode]
            .ViolationStatus.Should()
            .Be(VerificationCheckStatus.Pass);
        ElectionSp11ProfileIds.RegulatoryCheckDefinitions[
                ElectionSp11ProfileIds.StaleTrackerWarningCheckCode]
            .ViolationStatus.Should()
            .Be(VerificationCheckStatus.Warn);
        ElectionSp11ProfileIds.RegulatoryCheckDefinitions[
                ElectionSp11ProfileIds.BlockedCertificationClaimCheckCode]
            .ViolationStatus.Should()
            .Be(VerificationCheckStatus.Fail);
    }

    [Fact]
    public void RegulatoryClaim_ShouldValidateAsMarketIntelligenceNotLegalApproval()
    {
        var claim = CreateRegulatoryClaim(ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation);

        ElectionSp11RegulatoryRules.Validate(
                claim,
                observedAt: DateTimeOffset.Parse("2026-06-01T00:00:00Z"))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void RegulatoryClaim_ShouldRejectLegalAdviceAndCertificationLanguage()
    {
        var claim = CreateRegulatoryClaim(
            ElectionSp11ProfileIds.ClaimStateAllowedNow,
            isLegalAdvice: true,
            allowedWording: "Certified and legally approved for public elections.");

        var errors = ElectionSp11RegulatoryRules.Validate(
            claim,
            observedAt: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));

        errors.Should().Contain(x => x.Contains("not legal advice", StringComparison.Ordinal));
        errors.Should().Contain(x => x.Contains("unsupported legal", StringComparison.Ordinal));
    }

    [Fact]
    public void RegulatoryClaim_ShouldRejectAuthorityGatedSwissPublicElectionClaimWithoutEvidence()
    {
        var claim = CreateRegulatoryClaim(
            ElectionSp11ProfileIds.ClaimStateAllowedNow,
            claimId: "swiss_public_election_authority_parity",
            requiresAuthorityEvidence: true,
            authorityEvidenceRef: null);

        ElectionSp11RegulatoryRules.Validate(
                claim,
                observedAt: DateTimeOffset.Parse("2026-06-01T00:00:00Z"))
            .Should()
            .Contain(x => x.Contains("authority evidence", StringComparison.Ordinal));
    }

    [Fact]
    public void RegulatoryClaim_ShouldWarnWhenTrackerIsStale()
    {
        var claim = CreateRegulatoryClaim(
            ElectionSp11ProfileIds.ClaimStateBlockedUntilReview,
            nextReviewAt: DateTimeOffset.Parse("2026-05-01T00:00:00Z"));

        ElectionSp11RegulatoryRules.Validate(
                claim,
                observedAt: DateTimeOffset.Parse("2026-06-01T00:00:00Z"))
            .Should()
            .Contain(x => x.Contains("stale", StringComparison.Ordinal));
    }

    [Fact]
    public void PublicRegulatoryClaim_ShouldRejectRestrictedWorkpaperRefs()
    {
        var claim = CreateRegulatoryClaim(
            ElectionSp11ProfileIds.ClaimStateBlockedUntilCertification,
            restrictedWorkpaperRef: VerificationPackageFileNames.RestrictedSp11RegulatoryJurisdictionWorkpaper,
            restrictedEvidenceFiles:
            [
                VerificationPackageFileNames.RestrictedSp11RegulatoryJurisdictionWorkpaper,
            ]);

        var errors = ElectionSp11RegulatoryRules.Validate(
            claim,
            observedAt: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));

        errors.Should().Contain(x => x.Contains("restricted workpaper", StringComparison.Ordinal));
        errors.Should().Contain(x => x.Contains("must not include restricted evidence files", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("regulatoryWorkpaper")]
    [InlineData("jurisdictionWorkpaper")]
    [InlineData("authorityPrivateCorrespondence")]
    public void PublicPrivacyBoundary_ShouldRejectRegulatoryRestrictedFields(string fieldName)
    {
        VerificationPrivacyBoundary.IsForbiddenInPublicPackage(fieldName).Should().BeTrue();
    }

    private static ElectionSp10OperationalSecurityStatusArtifactRecord CreateOperationalStatus(
        string evidenceState,
        bool doesNotCompleteFeat106 = true,
        string? releaseManifestHash = "sha256:release-manifest",
        string? immutableDeploymentRef = "ghcr.io/hush/server@sha256:server",
        string? custodyMode = ElectionSp10ProfileIds.CustodyModeAwsKmsPerElectionEnvelopeV1,
        string? incidentStatus = ElectionSp10ProfileIds.IncidentStatusNoIncidentDeclared,
        IReadOnlyList<string>? restrictedEvidenceFiles = null) =>
        new(
            Schema: ElectionSp10ProfileIds.OperationalSecuritySummarySchema,
            ElectionId: Guid.NewGuid().ToString("D"),
            ProgramVersion: ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
            DeploymentProfileId: ElectionSp10ProfileIds.DeploymentProfileManagedAwsContainerV1,
            EvidenceState: evidenceState,
            DoesNotCompleteFeat106Readiness: doesNotCompleteFeat106,
            Feat106ReadinessCaveat: "Operational evidence is separate from rollout readiness.",
            ReleaseEvidenceMode: ElectionSp08ProfileIds.EvidenceModeOfficial,
            ReleaseManifestHash: releaseManifestHash,
            ImmutableDeploymentRef: immutableDeploymentRef,
            CustodyMode: custodyMode,
            ExecutorKeyLifecycle: ElectionSp10ProfileIds.ExecutorKeyLifecycleEphemeralMemoryV1,
            AccessSnapshotHashOrRestrictedRef: "sha256:access-snapshot",
            BackupRestoreHashOrRestrictedRef: "sha256:backup-restore",
            IncidentStatus: incidentStatus,
            AuditorRoomAccessLogHashOrRestrictedRef: "sha256:auditor-room-access-log",
            BlocksHighAssurance: ElectionSp10OperationalSecurityRules.BlocksHighAssurance(evidenceState),
            PrimaryResultCode: ElectionSp10OperationalSecurityRules.GetPrimaryResultCode(evidenceState),
            PrimaryIssue: null,
            PublicEvidenceFiles:
            [
                VerificationPackageFileNames.Sp10OperationalSecuritySummary,
                VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
                VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
            ],
            RestrictedEvidenceFiles: restrictedEvidenceFiles ?? [],
            PublicPrivacyBoundary:
            [
                "no_raw_logs",
                "no_ip_addresses",
                "no_executor_private_keys",
                "no_vote_choice",
            ]);

    private static ElectionSp11RegulatoryClaimStateArtifactRecord CreateRegulatoryClaim(
        string claimState,
        string claimId = "organizational_remote_voting_claim",
        bool isLegalAdvice = false,
        bool requiresAuthorityEvidence = false,
        string? authorityEvidenceRef = "authority:reference",
        string? restrictedWorkpaperRef = null,
        string? allowedWording = null,
        DateTimeOffset? nextReviewAt = null,
        IReadOnlyList<string>? restrictedEvidenceFiles = null) =>
        new(
            Schema: ElectionSp11ProfileIds.RegulatoryClaimStateSchema,
            JurisdictionId: "ch",
            ClaimId: claimId,
            TrackerVersion: ElectionSp11ProfileIds.RegulatoryTrackerVersion,
            ClaimState: claimState,
            SourceCheckedAt: DateTimeOffset.Parse("2026-05-12T00:00:00Z"),
            NextReviewAt: nextReviewAt ?? DateTimeOffset.Parse("2026-08-12T00:00:00Z"),
            SourceRef: "regulatory-tracking/ch.md",
            Owner: "hush-regulatory-owner",
            IsLegalAdvice: isLegalAdvice,
            RequiresAuthorityEvidence: requiresAuthorityEvidence,
            AuthorityEvidenceRef: authorityEvidenceRef,
            RestrictedWorkpaperRef: restrictedWorkpaperRef,
            AllowedWording: allowedWording ?? ElectionSp11RegulatoryRules.GetAllowedWordingForClaimState(claimState),
            PublicEvidenceFiles:
            [
                VerificationPackageFileNames.Sp11RegulatoryClaimState,
            ],
            RestrictedEvidenceFiles: restrictedEvidenceFiles ?? [],
            PublicPrivacyBoundary:
            [
                "no_regulatory_workpapers",
                "no_authority_private_correspondence",
            ]);
}
