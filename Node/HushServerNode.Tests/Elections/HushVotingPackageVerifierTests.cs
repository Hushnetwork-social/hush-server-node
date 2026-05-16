using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.PublicationProof;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class HushVotingPackageVerifierTests
{
    private static readonly JsonSerializerOptions RustWorkerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public async Task Verify_ValidDevelopmentPackage_ShouldWriteDeterministicOutputAndExitSuccessfully()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.ExitCode.Should().Be(0);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Warn);
        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.EligibilityEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp08ProfileIds.EvidenceModeAllowedCheckCode &&
            x.ResultCode == VerificationResultCodes.ReleaseIntegrityEvidencePending &&
            x.Status == VerificationCheckStatus.Warn);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.ReviewStatusValidCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewStatusValid &&
            x.Status == VerificationCheckStatus.Pass);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.ReviewNotCompleteCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewNotComplete &&
            x.Status == VerificationCheckStatus.Warn);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp10ProfileIds.ReleaseDeploymentBindingCheckCode &&
            x.Status == VerificationCheckStatus.Warn);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp10ProfileIds.ForbiddenMaterialScanCheckCode &&
            x.ResultCode == VerificationResultCodes.OperationalSecurityEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
        File.Exists(Path.Combine(package.PackagePath, "verifier-output", "VerifierOutput.json"))
            .Should()
            .BeTrue();
        File.Exists(Path.Combine(package.PackagePath, "verifier-output", "VerifierSummary.md"))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task Verify_Sp09ReviewedClaimWithoutReviewerEvidence_ShouldFail()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var status = await ReadPackageArtifactAsync<ElectionSp09ExternalReviewStatusArtifactRecord>(
            package.PackagePath,
            VerificationPackageFileNames.Sp09ExternalReviewStatus);
        await WritePackageArtifactAsync(
            package.PackagePath,
            VerificationPackageFileNames.Sp09ExternalReviewStatus,
            status with
            {
                DetailedStatus = ElectionSp09ProfileIds.StatusReviewedForDeclaredScope,
                Availability = ElectionSp09ProfileIds.AvailabilityAvailable,
                ClaimState = ElectionSp09ProfileIds.ClaimStateReviewedForDeclaredScope,
                PrimaryResultCode = VerificationResultCodes.ExternalReviewStatusValid,
            });
        await RefreshAuditManifestAsync(package.PackagePath);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.ReviewNotCompleteCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewNotComplete &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_Sp09FalseClaimWording_ShouldFail()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var claimTable = await ReadPackageArtifactAsync<ElectionSp09ExternalReviewClaimTableArtifactRecord>(
            package.PackagePath,
            VerificationPackageFileNames.Sp09ExternalReviewClaimTable);
        await WritePackageArtifactAsync(
            package.PackagePath,
            VerificationPackageFileNames.Sp09ExternalReviewClaimTable,
            claimTable with
            {
                Claims =
                [
                    claimTable.Claims[0] with { AllowedWording = "Certified for public elections." },
                    .. claimTable.Claims.Skip(1),
                ],
            });
        await RefreshAuditManifestAsync(package.PackagePath);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.ClaimNotAllowedCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewClaimNotAllowed &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_Sp09ScopeMismatch_ShouldFail()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var status = await CreateReviewedSp09StatusAsync(package.PackagePath) with
        {
            ReviewScopeMatchesElection = false,
            Availability = ElectionSp09ProfileIds.AvailabilityAvailable,
        };
        await WritePackageArtifactAsync(package.PackagePath, VerificationPackageFileNames.Sp09ExternalReviewStatus, status);
        await RefreshAuditManifestAsync(package.PackagePath);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.ScopeMismatchCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewScopeMismatch &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_Sp09ReportHashMismatch_ShouldFail()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var status = await CreateReviewedSp09StatusAsync(package.PackagePath) with
        {
            CustomerSafeSummaryHash = "not-a-sha256-hash",
        };
        await WritePackageArtifactAsync(package.PackagePath, VerificationPackageFileNames.Sp09ExternalReviewStatus, status);
        await RefreshAuditManifestAsync(package.PackagePath);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.ReportHashMismatchCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewReportHashMismatch &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_Sp09PublicBoundaryLeak_ShouldFail()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var status = await ReadPackageArtifactAsync<ElectionSp09ExternalReviewStatusArtifactRecord>(
            package.PackagePath,
            VerificationPackageFileNames.Sp09ExternalReviewStatus);
        await WritePackageArtifactAsync(
            package.PackagePath,
            VerificationPackageFileNames.Sp09ExternalReviewStatus,
            status with { PublicPrivacyBoundary = ["fullReportBody"] });
        await RefreshAuditManifestAsync(package.PackagePath);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.PublicBoundaryViolationCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewPublicBoundaryViolation &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_Sp09OpenFindingsBlockStrongClaim_ShouldFail()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var status = await CreateReviewedSp09StatusAsync(package.PackagePath) with
        {
            FindingSummary =
            [
                new ElectionSp09FindingSeverityCountRecord("high", OpenCount: 1, FixedCount: 0, AcceptedLimitationCount: 0),
            ],
        };
        await WritePackageArtifactAsync(package.PackagePath, VerificationPackageFileNames.Sp09ExternalReviewStatus, status);
        await RefreshAuditManifestAsync(package.PackagePath);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.OpenFindingsBlockClaimsCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewOpenFindingsBlockClaims &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_Sp09RequiresRedesignReviewedClaim_ShouldFail()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var status = await CreateReviewedSp09StatusAsync(package.PackagePath) with
        {
            DetailedStatus = ElectionSp09ProfileIds.StatusRequiresRedesign,
            Availability = ElectionSp09ProfileIds.AvailabilityNotAvailable,
            ClaimState = ElectionSp09ProfileIds.ClaimStateReviewedForDeclaredScope,
            PrimaryResultCode = VerificationResultCodes.ExternalReviewRequiresRedesign,
        };
        await WritePackageArtifactAsync(package.PackagePath, VerificationPackageFileNames.Sp09ExternalReviewStatus, status);
        await RefreshAuditManifestAsync(package.PackagePath);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp09ProfileIds.RequiresRedesignCheckCode &&
            x.ResultCode == VerificationResultCodes.ExternalReviewRequiresRedesign &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_HighAssurancePackageMissingPublicationProof_ShouldFailClosed()
    {
        using var package = CreatePackage(VerificationProfileIds.HighAssuranceV1);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(1);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Fail);
        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PublicationProofEvidencePending &&
            x.Status == VerificationCheckStatus.Fail);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp08ProfileIds.EvidenceModeAllowedCheckCode &&
            x.ResultCode == VerificationResultCodes.ReleaseIntegrityEvidenceModeNotAllowed &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_HighAssurancePackageWithSp07Evidence_ShouldPassStructuralSp07AndWarnExternalReview()
    {
        using var package = CreateHighAssuranceTrusteePackageWithSp07Evidence();

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(0);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Warn);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "VFY-SP07-000" &&
            x.ResultCode == VerificationResultCodes.PublicationProofEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PublicationProofExternalReviewPending &&
            x.Status == VerificationCheckStatus.Warn);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp08ProfileIds.ReleaseIntegrityAcceptedCheckCode &&
            x.ResultCode == VerificationResultCodes.ReleaseIntegrityEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
        result.Output.Results
            .Where(x => ElectionSp10ProfileIds.OperationalCheckCodes.Contains(x.CheckCode))
            .Should()
            .OnlyContain(x => x.Status == VerificationCheckStatus.Pass);
    }

    [Fact]
    public async Task Verify_Sp10FalseReadinessClaim_ShouldFail()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var status = await ReadPackageArtifactAsync<ElectionSp10OperationalSecurityStatusArtifactRecord>(
            package.PackagePath,
            VerificationPackageFileNames.Sp10OperationalSecuritySummary);
        await WritePackageArtifactAsync(
            package.PackagePath,
            VerificationPackageFileNames.Sp10OperationalSecuritySummary,
            status with
            {
                DoesNotCompleteFeat106Readiness = false,
                Feat106ReadinessCaveat = "FEAT-106 complete and certified for public elections.",
            });
        await RefreshAuditManifestAsync(package.PackagePath);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp10ProfileIds.ForbiddenMaterialScanCheckCode &&
            x.ResultCode == VerificationResultCodes.OperationalSecurityForbiddenMaterial &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_Sp11AllowedRegulatoryClaim_ShouldRunOptionalRegChecks()
    {
        using var package = CreatePackageWithRegulatoryClaim(
            ElectionVerificationPackageExportServiceTests.CreateSp11RegulatoryClaimState());

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp11ProfileIds.RegulatoryClaimShapeValidCheckCode &&
            x.ResultCode == VerificationResultCodes.RegulatoryClaimShapeValid &&
            x.Status == VerificationCheckStatus.Pass);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp11ProfileIds.ClaimAllowedByRegisterCheckCode &&
            x.ResultCode == VerificationResultCodes.RegulatoryClaimAllowedByRegister &&
            x.Status == VerificationCheckStatus.Pass);
    }

    [Fact]
    public async Task Verify_Sp11BlockedCertificationClaim_ShouldFail()
    {
        using var package = CreatePackageWithRegulatoryClaim(
            ElectionVerificationPackageExportServiceTests.CreateSp11RegulatoryClaimState(
                ElectionSp11ProfileIds.ClaimStateBlockedUntilCertification,
                requiresAuthorityEvidence: true));

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp11ProfileIds.BlockedCertificationClaimCheckCode &&
            x.ResultCode == VerificationResultCodes.RegulatoryClaimBlockedCertification &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_Sp11StaleRegulatoryTracker_ShouldWarn()
    {
        using var package = CreatePackageWithRegulatoryClaim(
            ElectionVerificationPackageExportServiceTests.CreateSp11RegulatoryClaimState());
        var claim = await ReadPackageArtifactAsync<ElectionSp11RegulatoryClaimStateArtifactRecord>(
            package.PackagePath,
            VerificationPackageFileNames.Sp11RegulatoryClaimState);
        await WritePackageArtifactAsync(
            package.PackagePath,
            VerificationPackageFileNames.Sp11RegulatoryClaimState,
            claim with
            {
                SourceCheckedAt = DateTimeOffset.UtcNow.AddDays(-60),
                NextReviewAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
        await RefreshAuditManifestAsync(package.PackagePath);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.Output.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp11ProfileIds.StaleTrackerWarningCheckCode &&
            x.ResultCode == VerificationResultCodes.RegulatoryTrackerStale &&
            x.Status == VerificationCheckStatus.Warn);
    }

    [Fact]
    public async Task Verify_RestrictedPackageWithAnomalyManifest_ShouldValidateManifestAndEvidenceGraph()
    {
        using var package = CreateRestrictedPackageWithAnomalyManifest();

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.RestrictedOwnerAuditorV1));

        result.ExitCode.Should().Be(0);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "ANOM-000" &&
            x.ResultCode == VerificationResultCodes.AnomalyEvidenceManifestValid &&
            x.Status == VerificationCheckStatus.Pass);
    }

    [Fact]
    public async Task Verify_RestrictedPackageWithTamperedAnomalyManifestHash_ShouldFailSemanticHashCheck()
    {
        using var package = CreateRestrictedPackageWithAnomalyManifest();
        await MutateJsonArtifactAndRefreshPackageAsync(
            package.PackagePath,
            VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest,
            root => root["manifestHash"] = $"sha256:{new string('0', 64)}");

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.RestrictedOwnerAuditorV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "ANOM-002" &&
            x.ResultCode == VerificationResultCodes.AnomalyEvidenceManifestHashMismatch &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_RestrictedPackageWithTamperedAnomalyAttachmentRow_ShouldFailSemanticHashCheck()
    {
        using var package = CreateRestrictedPackageWithAnomalyManifest();
        await MutateJsonArtifactAndRefreshPackageAsync(
            package.PackagePath,
            VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest,
            root => MutateFirstAnomalyAttachment(root, attachment => attachment["contentHash"] = $"sha256:{new string('2', 64)}"));

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.RestrictedOwnerAuditorV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "ANOM-002" &&
            x.ResultCode == VerificationResultCodes.AnomalyEvidenceManifestHashMismatch &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_RestrictedPackageWithTamperedAnomalyRedactionRow_ShouldFailSemanticHashCheck()
    {
        using var package = CreateRestrictedPackageWithAnomalyManifest();
        await MutateJsonArtifactAndRefreshPackageAsync(
            package.PackagePath,
            VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest,
            root => MutateFirstAnomalyRedaction(root, redaction => redaction["reasonCodeId"] = ElectionAnomalyRedactionReasonIds.OperationalSafety));

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.RestrictedOwnerAuditorV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "ANOM-002" &&
            x.ResultCode == VerificationResultCodes.AnomalyEvidenceManifestHashMismatch &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_RestrictedPackageWithTamperedAnomalyRecipientStatus_ShouldFailSemanticHashCheck()
    {
        using var package = CreateRestrictedPackageWithAnomalyManifest();
        await MutateJsonArtifactAndRefreshPackageAsync(
            package.PackagePath,
            VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest,
            root => MutateFirstAnomalyRecipientStatus(root, status => status["wrapStatusId"] = ElectionAnomalyRecipientWrapStatusIds.Missing));

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.RestrictedOwnerAuditorV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "ANOM-002" &&
            x.ResultCode == VerificationResultCodes.AnomalyEvidenceManifestHashMismatch &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_RestrictedPackageWithAuditorUnsafeAnomalyIdentityField_ShouldFailPrivacyCheck()
    {
        using var package = CreateRestrictedPackageWithAnomalyManifest();
        await MutateJsonArtifactAndRefreshPackageAsync(
            package.PackagePath,
            VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest,
            root => root["submitterActorPublicAddress"] = "HUSH-SUBMITTER-PRIVATE-ADDRESS");

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.RestrictedOwnerAuditorV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "ANOM-005" &&
            x.ResultCode == VerificationResultCodes.AnomalyEvidenceManifestPrivacyViolation &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_RestrictedPackageWithTamperedAnomalyEvidenceGraphNode_ShouldFailGraphCheck()
    {
        using var package = CreateRestrictedPackageWithAnomalyManifest();
        await MutateJsonArtifactAndRefreshPackageAsync(
            package.PackagePath,
            VerificationPackageFileNames.ReportPackageEvidenceGraph,
            root => root["restrictedAnomalyIntakeManifest"]!.AsObject()["manifestHash"] = $"sha256:{new string('1', 64)}");

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.RestrictedOwnerAuditorV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "ANOM-003" &&
            x.ResultCode == VerificationResultCodes.AnomalyEvidenceGraphMismatch &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_RestrictedPackageWithTamperedAnomalyEvidenceGraphIds_ShouldFailGraphCheck()
    {
        using var package = CreateRestrictedPackageWithAnomalyManifest();
        await MutateJsonArtifactAndRefreshPackageAsync(
            package.PackagePath,
            VerificationPackageFileNames.ReportPackageEvidenceGraph,
            root =>
            {
                var node = root["restrictedAnomalyIntakeManifest"]!.AsObject();
                node["attachmentManifestIds"] = new JsonArray("99999999-9999-9999-9999-999999999999");
            });

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.RestrictedOwnerAuditorV1));

        result.ExitCode.Should().Be(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "ANOM-003" &&
            x.ResultCode == VerificationResultCodes.AnomalyEvidenceGraphMismatch &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_PublicAnonymousPackageWithHighAssuranceSp07Evidence_ShouldPassStructuralSp07()
    {
        using var package = CreateHighAssuranceTrusteePackageWithSp07Evidence(
            packageProfileId: VerificationProfileIds.PublicAnonymousV1);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.PublicAnonymousV1));

        result.ExitCode.Should().Be(0);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "VFY-SP07-000" &&
            x.ResultCode == VerificationResultCodes.PublicationProofEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
        result.Output.Results.Should().NotContain(x =>
            x.CheckCode == "VFY-SP07-010" &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_HighAssurancePackageWithCanonicalSp07Evidence_ShouldInvokePublicProofVerifier()
    {
        using var package = CreateHighAssuranceTrusteePackageWithSp07Evidence(
            includeCanonicalProofVerifierInput: true);
        var publicProofVerifier = new FakeSp07PackagePublicProofVerifier(passed: true);

        var result = await new HushVotingPackageVerifier(publicProofVerifier).VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(0);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Warn);
        publicProofVerifier.Requests.Should().HaveCount(1);
        publicProofVerifier.Requests[0].CanonicalProofBytesHex.Should().NotBeNullOrWhiteSpace();
        publicProofVerifier.Requests[0].AcceptedBallotSetHash.Should().Be(
            await ReadPackageAcceptedBallotSetHashAsync(package.PackagePath));
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "VFY-SP07-070" &&
            x.ResultCode == VerificationResultCodes.PublicationProofEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
    }

    [Fact]
    public async Task Verify_HighAssurancePackageWithManifestSp07Evidence_ShouldInvokePublicProofVerifierForEveryChunk()
    {
        using var package = CreateHighAssuranceTrusteePackageWithSp07Evidence(
            includeManifestProofVerifierInput: true);
        var publicProofVerifier = new FakeSp07PackagePublicProofVerifier(passed: true);

        var result = await new HushVotingPackageVerifier(publicProofVerifier).VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(0);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Warn);
        publicProofVerifier.Requests.Should().HaveCount(2);
        publicProofVerifier.Requests.Select(x => x.ChunkId).Should().Equal("chunk-0001", "chunk-0002");
        publicProofVerifier.Requests.Select(x => x.AcceptedBallotSetHash).Should().AllBe(
            await ReadPackageAcceptedBallotSetHashAsync(package.PackagePath));
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "VFY-SP07-070" &&
            x.ResultCode == VerificationResultCodes.PublicationProofEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass &&
            x.Evidence.GetValueOrDefault("verified_chunk_count") == "2");
    }

    [Fact]
    public async Task Verify_HighAssurancePackageWithManifestSp07Evidence_WhenChunkStatementHashDiffersFromPackageStatement_ShouldFail()
    {
        using var package = CreateHighAssuranceTrusteePackageWithSp07Evidence(
            includeManifestProofVerifierInput: true);
        var transcriptPath = ResolvePackagePath(
            package.PackagePath,
            VerificationPackageFileNames.Sp07PublicationProofTranscript);
        var transcript = JsonSerializer.Deserialize<ElectionSp07PublicationProofTranscriptArtifactRecord>(
            await File.ReadAllTextAsync(transcriptPath),
            VerificationJson.Options)!;
        var manifest = JsonSerializer.Deserialize<ElectionSp07PublicationProofManifestArtifactRecord>(
            transcript.ProofBytes,
            VerificationJson.Options)!;
        var tamperedManifest = manifest with
        {
            Chunks =
            [
                manifest.Chunks[0] with { StatementHashSha512 = new string('f', 128) },
                manifest.Chunks[1],
            ],
        };
        await File.WriteAllTextAsync(
            transcriptPath,
            JsonSerializer.Serialize(
                transcript with
                {
                    ProofBytes = JsonSerializer.Serialize(tamperedManifest, VerificationJson.Options),
                    ProofHash = HashHex(JsonSerializer.Serialize(tamperedManifest, VerificationJson.Options)),
                },
                VerificationJson.Options));

        var result = await new HushVotingPackageVerifier(new FakeSp07PackagePublicProofVerifier(passed: true))
            .VerifyAsync(new(package.PackagePath, VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(1);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Fail);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "VFY-SP07-073" &&
            x.ResultCode == VerificationResultCodes.PublicationProofTranscriptHashMismatch &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("remove")]
    [InlineData("duplicate")]
    [InlineData("replace")]
    public async Task Verify_HighAssurancePackageWithManifestSp07Evidence_WhenPublishedStreamIsTampered_ShouldFailAtSp07Boundary(
        string tamperKind)
    {
        using var package = CreateHighAssuranceTrusteePackageWithSp07Evidence(
            includeManifestProofVerifierInput: true);
        await TamperPublishedStreamAndRefreshPackageAsync(package.PackagePath, tamperKind);

        var result = await new HushVotingPackageVerifier(new FakeSp07PackagePublicProofVerifier(passed: true))
            .VerifyAsync(new(package.PackagePath, VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(1, tamperKind);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Fail, tamperKind);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "VFY-SP07-073" &&
            x.ResultCode == VerificationResultCodes.PublicationProofTranscriptHashMismatch &&
            x.Status == VerificationCheckStatus.Fail,
            tamperKind);
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("remove")]
    [InlineData("duplicate")]
    [InlineData("replace")]
    public async Task Verify_HighAssurancePackageWithRealManifestSp07Evidence_WhenPublishedStreamIsTampered_ShouldFailAtSp07Boundary(
        string tamperKind)
    {
        var workerPath = ResolveAvailableWorkerPath();
        if (string.IsNullOrWhiteSpace(workerPath))
        {
            return;
        }

        using var package = await CreateHighAssuranceTrusteePackageWithRealSp07ManifestEvidenceAsync(workerPath);
        await TamperPublishedStreamAndRefreshPackageAsync(package.PackagePath, tamperKind);

        var result = await new HushVotingPackageVerifier(new WorkerBackedSp07PackagePublicProofVerifier(workerPath))
            .VerifyAsync(new(package.PackagePath, VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(1, tamperKind);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Fail, tamperKind);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "VFY-SP07-073" &&
            x.ResultCode == VerificationResultCodes.PublicationProofTranscriptHashMismatch &&
            x.Status == VerificationCheckStatus.Fail,
            tamperKind);
    }

    [Fact]
    public async Task Verify_HighAssurancePackageWithRealManifestSp07Evidence_ShouldInvokeRustVerifierAndPassSp07()
    {
        var workerPath = ResolveAvailableWorkerPath();
        if (string.IsNullOrWhiteSpace(workerPath))
        {
            return;
        }

        using var package = await CreateHighAssuranceTrusteePackageWithRealSp07ManifestEvidenceAsync(workerPath);
        var publicProofVerifier = new WorkerBackedSp07PackagePublicProofVerifier(workerPath);

        var result = await new HushVotingPackageVerifier(publicProofVerifier)
            .VerifyAsync(new(package.PackagePath, VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(0);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Warn);
        publicProofVerifier.Requests.Should().HaveCount(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "VFY-SP07-070" &&
            x.ResultCode == VerificationResultCodes.PublicationProofEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
    }

    [Fact]
    public async Task Verify_HighAssurancePackageWithCanonicalSp07Evidence_WhenPublicProofVerifierRejects_ShouldFail()
    {
        using var package = CreateHighAssuranceTrusteePackageWithSp07Evidence(
            includeCanonicalProofVerifierInput: true);
        var publicProofVerifier = new FakeSp07PackagePublicProofVerifier(passed: false);

        var result = await new HushVotingPackageVerifier(publicProofVerifier).VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(1);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Fail);
        publicProofVerifier.Requests.Should().HaveCount(1);
        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "VFY-SP07-071" &&
            x.ResultCode == VerificationResultCodes.PublicationProofVerificationFailed &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_HighAssurancePackageWithSp07AcceptedSetMismatch_ShouldFail()
    {
        using var package = CreateHighAssuranceTrusteePackageWithSp07Evidence();
        var transcriptPath = ResolvePackagePath(package.PackagePath, VerificationPackageFileNames.Sp07PublicationProofTranscript);
        var transcript = JsonSerializer.Deserialize<ElectionSp07PublicationProofTranscriptArtifactRecord>(
            await File.ReadAllTextAsync(transcriptPath),
            VerificationJson.Options)!;
        await File.WriteAllTextAsync(
            transcriptPath,
            JsonSerializer.Serialize(
                transcript with { AcceptedBallotSetHash = new string('0', 64) },
                VerificationJson.Options));

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(1);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Fail);
        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PublicationProofAcceptedSetMismatch &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_HighAssurancePackageWithSp07TranscriptButNoDeletionReceipt_ShouldFail()
    {
        using var package = CreateHighAssuranceTrusteePackageWithSp07Evidence(includeDeletionReceipt: false);

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.ExitCode.Should().Be(1);
        result.Output.OverallStatus.Should().Be(VerificationOverallStatus.Fail);
        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PublicationProofWitnessDeletionMissing &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public async Task Verify_HighAssuranceTrusteePackage_ShouldPassSp06CtrlChecks()
    {
        using var package = CreateHighAssuranceTrusteePackage();

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.HighAssuranceV1));

        result.Output.Results.Should().Contain(x =>
            x.CheckCode == "CTRL-000" &&
            x.ResultCode == VerificationResultCodes.TrusteeControlDomainEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
    }

    [Fact]
    public async Task Verify_MissingArtifact_ShouldFailWithManifestMissingArtifact()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        File.Delete(Path.Combine(package.PackagePath, VerificationPackageFileNames.AcceptedBallotSet.Replace('/', Path.DirectorySeparatorChar)));

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PackageManifestMissingArtifact);
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task Verify_TamperedAcceptedSetHash_ShouldFailWithAcceptedHashCode()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var path = Path.Combine(package.PackagePath, VerificationPackageFileNames.AcceptedBallotSet.Replace('/', Path.DirectorySeparatorChar));
        var accepted = JsonSerializer.Deserialize<AcceptedBallotSetArtifactRecord>(
            await File.ReadAllTextAsync(path),
            VerificationJson.Options)!;
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                accepted with { AcceptedBallotInventoryHash = new string('0', 64) },
                VerificationJson.Options));

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.AcceptedBallotInventoryHashMismatch);
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task Verify_NamedVoterInPublicArtifact_ShouldFailPrivacyBoundary()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var path = Path.Combine(package.PackagePath, VerificationPackageFileNames.ElectionRecord.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(path, "{\"electionId\":\"x\",\"organizationVoterId\":\"voter-1\"}");

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PublicRestrictedFieldLeak);
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task Verify_PublishedSequenceGap_ShouldFailWithPublishedSequenceCode()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        var path = Path.Combine(package.PackagePath, VerificationPackageFileNames.PublishedBallotStream.Replace('/', Path.DirectorySeparatorChar));
        var published = JsonSerializer.Deserialize<PublishedBallotStreamArtifactRecord>(
            await File.ReadAllTextAsync(path),
            VerificationJson.Options)!;
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                published with
                {
                    PublishedBallots =
                    [
                        published.PublishedBallots[0] with
                        {
                            PublicationSequence = 2,
                        },
                    ],
                },
                VerificationJson.Options));

        var result = await new HushVotingPackageVerifier().VerifyAsync(new(
            package.PackagePath,
            VerificationProfileIds.DevelopmentCurrentV1));

        result.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PublishedBallotSequenceInvalid);
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task CommandLine_BackendUrl_ShouldBeRejectedAsLiveDependency()
    {
        using var output = new TemporaryPackageDirectory();

        var exitCode = await HushVotingVerifierCommandLine.RunAsync(
            [
                "--package",
                "https://hush.example/api",
                "--profile",
                VerificationProfileIds.DevelopmentCurrentV1,
                "--output",
                output.PackagePath,
            ]);

        exitCode.Should().Be(1);
        var verifierOutput = JsonSerializer.Deserialize<VerifierOutputRecord>(
            await File.ReadAllTextAsync(Path.Combine(output.PackagePath, "VerifierOutput.json")),
            VerificationJson.Options)!;
        verifierOutput.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.UnsupportedLiveDependency);
    }

    [Fact]
    public async Task CommandLine_ShouldWriteSp08ResultsToJsonAndSummary()
    {
        using var package = CreatePackage(VerificationProfileIds.DevelopmentCurrentV1);
        using var output = new TemporaryPackageDirectory();

        var exitCode = await HushVotingVerifierCommandLine.RunAsync(
            [
                "--package",
                package.PackagePath,
                "--profile",
                VerificationProfileIds.DevelopmentCurrentV1,
                "--output",
                output.PackagePath,
            ]);

        exitCode.Should().Be(0);
        var verifierOutput = JsonSerializer.Deserialize<VerifierOutputRecord>(
            await File.ReadAllTextAsync(Path.Combine(output.PackagePath, "VerifierOutput.json")),
            VerificationJson.Options)!;
        verifierOutput.Results.Should().Contain(x =>
            x.CheckCode == ElectionSp08ProfileIds.EvidenceModeAllowedCheckCode &&
            x.ResultCode == VerificationResultCodes.ReleaseIntegrityEvidencePending &&
            x.Status == VerificationCheckStatus.Warn);

        var summary = await File.ReadAllTextAsync(Path.Combine(output.PackagePath, "VerifierSummary.md"));
        summary.Should().Contain(ElectionSp08ProfileIds.EvidenceModeAllowedCheckCode);
        summary.Should().Contain(VerificationResultCodes.ReleaseIntegrityEvidencePending);
    }

    private static TemporaryPackageDirectory CreatePackage(string profileId)
    {
        var directory = new TemporaryPackageDirectory();
        var export = new ElectionVerificationPackageExportService().Export(
            ElectionVerificationPackageExportServiceTests.CreateRequest(
                VerificationPackageView.PublicAnonymous,
                profileId: profileId));
        ElectionVerificationPackageExportService.WritePackageToDirectory(export, directory.PackagePath);
        return directory;
    }

    private static TemporaryPackageDirectory CreatePackageWithRegulatoryClaim(
        ElectionSp11RegulatoryClaimStateArtifactRecord claim)
    {
        var directory = new TemporaryPackageDirectory();
        var export = new ElectionVerificationPackageExportService().Export(
            ElectionVerificationPackageExportServiceTests.CreateRequest(
                VerificationPackageView.PublicAnonymous,
                profileId: VerificationProfileIds.DevelopmentCurrentV1) with
            {
                Sp11RegulatoryClaimState = claim,
            });
        ElectionVerificationPackageExportService.WritePackageToDirectory(export, directory.PackagePath);
        return directory;
    }

    private static TemporaryPackageDirectory CreateRestrictedPackageWithAnomalyManifest()
    {
        var directory = new TemporaryPackageDirectory();
        var request = ElectionVerificationPackageExportServiceTests.CreateRequest(
            VerificationPackageView.RestrictedOwnerAuditor,
            restrictedAccessAuthorized: true,
            profileId: VerificationProfileIds.RestrictedOwnerAuditorV1);
        request = request with
        {
            ReportArtifacts = [.. request.ReportArtifacts, .. CreateAnomalyReportArtifacts(request)],
        };

        var export = new ElectionVerificationPackageExportService().Export(request);
        export.Success.Should().BeTrue();
        ElectionVerificationPackageExportService.WritePackageToDirectory(export, directory.PackagePath);
        return directory;
    }

    private static IReadOnlyList<ElectionReportArtifactRecord> CreateAnomalyReportArtifacts(
        ElectionVerificationPackageExportRequest request)
    {
        var anomalyArtifactId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var evidenceGraphArtifactId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var manifest = CreateRestrictedAnomalyIntakeManifest(request.Election.ElectionId);
        var manifestHash = ElectionAnomalyIntakeManifestHasher.ComputeHash(manifest);
        var graphContent = JsonSerializer.Serialize(
            new
            {
                restrictedAnomalyIntakeManifest = new
                {
                    nodeType = "anomaly_intake_manifest",
                    artifactId = anomalyArtifactId,
                    canonicalizationId = manifest.CanonicalizationId,
                    manifestHash,
                    scopeId = manifest.ScopeId,
                    packageReadinessStatusId = manifest.PackageReadinessStatusId,
                    packageReadinessBlockerIds = manifest.PackageReadinessBlockerIds
                        .OrderBy(x => x, StringComparer.Ordinal)
                        .ToArray(),
                    threadCount = manifest.Threads.Count,
                    attachmentManifestCount = manifest.Threads.Sum(x => x.Attachments.Count),
                    redactionCount = manifest.Threads.Sum(x => x.Redactions.Count),
                    recipientStatusCount = manifest.Threads.Sum(x => x.RecipientStatuses.Count),
                    anomalyThreadIds = manifest.Threads
                        .Select(x => x.AnomalyThreadId)
                        .OrderBy(x => x)
                        .ToArray(),
                    attachmentManifestIds = manifest.Threads
                        .SelectMany(x => x.Attachments.Select(attachment => attachment.AttachmentManifestId))
                        .OrderBy(x => x)
                        .ToArray(),
                    redactionEventIds = manifest.Threads
                        .SelectMany(x => x.Redactions.Select(redaction => redaction.RedactionEventId))
                        .OrderBy(x => x)
                        .ToArray(),
                    sourceEventIds = manifest.Threads
                        .SelectMany(x => x.Attachments.Select(attachment => attachment.EventId)
                            .Concat(x.Redactions.Select(redaction => redaction.EventId)))
                        .OrderBy(x => x)
                        .ToArray(),
                },
            },
            VerificationJson.Options);
        var artifactContent = JsonSerializer.Serialize(
            new
            {
                artifactSchemaId = "restricted-anomaly-intake-manifest-artifact-v1",
                manifestHash,
                canonicalizationId = manifest.CanonicalizationId,
                scopeId = manifest.ScopeId,
                packageReadinessStatusId = manifest.PackageReadinessStatusId,
                packageReadinessBlockerIds = manifest.PackageReadinessBlockerIds
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray(),
                threadCount = manifest.Threads.Count,
                attachmentManifestCount = manifest.Threads.Sum(x => x.Attachments.Count),
                redactionCount = manifest.Threads.Sum(x => x.Redactions.Count),
                recipientStatusCount = manifest.Threads.Sum(x => x.RecipientStatuses.Count),
                manifest,
            },
            VerificationJson.Options);

        return
        [
            ElectionModelFactory.CreateReportArtifact(
                request.ReportPackage!.Id,
                request.Election.ElectionId,
                ElectionReportArtifactKind.MachineEvidenceGraph,
                ElectionReportArtifactFormat.Json,
                ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                sortOrder: 8,
                title: "Evidence graph",
                fileName: "evidence-graph.json",
                mediaType: "application/json",
                contentHash: SHA256.HashData(Encoding.UTF8.GetBytes(graphContent)),
                content: graphContent,
                preassignedArtifactId: evidenceGraphArtifactId),
            ElectionModelFactory.CreateReportArtifact(
                request.ReportPackage!.Id,
                request.Election.ElectionId,
                ElectionReportArtifactKind.MachineRestrictedAnomalyIntakeManifest,
                ElectionReportArtifactFormat.Json,
                ElectionReportArtifactAccessScope.OwnerAuditorOnly,
                sortOrder: 14,
                title: "Restricted anomaly intake manifest",
                fileName: "restricted-anomaly-intake-manifest.json",
                mediaType: "application/json",
                contentHash: SHA256.HashData(Encoding.UTF8.GetBytes(artifactContent)),
                content: artifactContent,
                preassignedArtifactId: anomalyArtifactId),
        ];
    }

    private static AnomalyIntakeManifest CreateRestrictedAnomalyIntakeManifest(ElectionId electionId)
    {
        var threadId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var attachmentManifestId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var recordedAt = new DateTime(2026, 5, 4, 12, 8, 0, DateTimeKind.Utc);

        return new AnomalyIntakeManifest(
            ElectionAnomalyManifestCanonicalizationIds.Current,
            electionId.ToString(),
            ElectionAnomalyEvidenceManifestScopeIds.Package,
            ElectionAnomalyPackageReadinessStatusIds.Blocked,
            [ElectionAnomalyEvidenceScannerStatusIds.Pending],
            [
                new AnomalyIntakeManifestThread(
                    threadId,
                    ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern,
                    ElectionAnomalyCaseStateIds.UnderReview,
                    "sha256:thread",
                    GovernedDecisionRef: "proposal-1",
                    HasOpenClarificationRequest: true,
                    OpenClarificationRequestId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    recordedAt.AddMinutes(-20),
                    recordedAt,
                    Attachments:
                    [
                        new AnomalyIntakeManifestAttachment(
                            attachmentManifestId,
                            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                            $"sha256:{RepeatHash('e')}",
                            ElectionAnomalyAttachmentKindIds.SubmitterEvidence,
                            $"{ElectionAnomalyRestrictedPayloadReference.Prefix}eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
                            $"sha256:{RepeatHash('a')}",
                            $"sha256:{RepeatHash('b')}",
                            2048,
                            ElectionAnomalyEvidenceMimeTypes.ApplicationPdf,
                            ElectionAnomalyAttachmentValidationStatusIds.PendingScan,
                            ElectionAnomalyEvidenceScannerStatusIds.Pending,
                            ElectionAnomalyPayloadAvailabilityStatusIds.Available,
                            ClarificationRequestId: null,
                            recordedAt,
                            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")),
                    ],
                    Redactions:
                    [
                        new AnomalyIntakeManifestRedaction(
                            Guid.Parse("11111111-1111-1111-1111-111111111111"),
                            Guid.Parse("22222222-2222-2222-2222-222222222222"),
                            $"sha256:{RepeatHash('f')}",
                            ElectionAnomalyRedactionTargetKindIds.AttachmentManifest,
                            attachmentManifestId.ToString(),
                            ElectionAnomalyRedactionReasonIds.PersonalData,
                            $"sha256:{RepeatHash('b')}",
                            ReplacementManifestHash: null,
                            TombstoneStatusId: "redacted",
                            recordedAt.AddMinutes(1),
                            Guid.Parse("33333333-3333-3333-3333-333333333333")),
                    ],
                    RecipientStatuses:
                    [
                        new AnomalyIntakeManifestRecipientStatus(
                            ElectionAnomalyRecipientRoleIds.ElectionOwner,
                            ElectionAnomalyRecipientWrapStatusIds.Available),
                    ]),
            ]);
    }

    private static TemporaryPackageDirectory CreateHighAssuranceTrusteePackage()
    {
        var directory = new TemporaryPackageDirectory();
        var export = new ElectionVerificationPackageExportService().Export(
            ElectionVerificationPackageExportServiceTests.CreateHighAssuranceTrusteeRequest());
        ElectionVerificationPackageExportService.WritePackageToDirectory(export, directory.PackagePath);
        return directory;
    }

    private static TemporaryPackageDirectory CreateHighAssuranceTrusteePackageWithSp07Evidence(
        bool includeDeletionReceipt = true,
        bool includeCanonicalProofVerifierInput = false,
        bool includeManifestProofVerifierInput = false,
        string packageProfileId = VerificationProfileIds.HighAssuranceV1)
    {
        var directory = new TemporaryPackageDirectory();
        var request = ElectionVerificationPackageExportServiceTests.CreateHighAssuranceTrusteeRequest() with
        {
            VerifierProfileId = packageProfileId,
        };
        var witnessSetId = Guid.NewGuid();
        var proofBytes = "synthetic-proof-bytes";
        var canonicalProofBytes = Encoding.UTF8.GetBytes("canonical-sp07-proof-fixture");
        var canonicalProofBytesHex = Convert.ToHexString(canonicalProofBytes).ToLowerInvariant();
        var canonicalProofHashSha512 = Convert.ToHexString(SHA512.HashData(canonicalProofBytes)).ToLowerInvariant();
        var statementHashSha512 = new string('b', 128);
        var fiatShamirTranscriptHashSha512 = new string('c', 128);
        if (includeManifestProofVerifierInput)
        {
            request = WithSp07PublicCiphertextPackages(request);
            var acceptedHash = VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(request.AcceptedBallots));
            var publishedHash = VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputePublishedBallotStreamHash(request.PublishedBallots));
            var manifest = new ElectionSp07PublicationProofManifestArtifactRecord(
                ElectionSp07PublicationProofManifestArtifactRecord.SchemaVersion,
                request.Election.ElectionId.ToString(),
                ProofSessionId: "manifest-session",
                PlanId: "sp07-plan-test",
                ElectionSp07ProfileIds.PublicationProofMode,
                ElectionSp07ProfileIds.ProofConstruction,
                ElectionSp07ProfileIds.StatementId,
                VerificationProfileIds.HighAssuranceV1,
                acceptedHash,
                publishedHash,
                request.AcceptedBallots.Count,
                request.PublishedBallots.Count,
                request.Election.Options.Count,
                ChunkCount: 2,
                CompletedChunkCount: 2,
                FailedChunkCount: 0,
                SlowestChunkMilliseconds: 8.5,
                [
                    CreateManifestChunk(request, "chunk-0001", 0, 0, 1, "manifest-proof-1", acceptedHash, publishedHash),
                    CreateManifestChunk(request, "chunk-0002", 1, 1, 1, "manifest-proof-2", acceptedHash, publishedHash),
                ],
                PublicPrivacyBoundary:
                [
                    "no_hidden_permutation",
                    "no_shuffle_map",
                    "no_rerandomization_randomness",
                    "no_raw_witness",
                ]);
            proofBytes = JsonSerializer.Serialize(manifest, VerificationJson.Options);
        }

        var proofHash = HashHex(proofBytes);
        var session = new ElectionPublicationProofSessionRecord(
            Guid.NewGuid(),
            request.Election.ElectionId,
            witnessSetId,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            includeDeletionReceipt
                ? ElectionPublicationProofSessionStatus.WitnessDeleted
                : ElectionPublicationProofSessionStatus.Verified,
            DateTime.UnixEpoch.AddHours(3),
            DateTime.UnixEpoch.AddHours(3).AddMinutes(2),
            request.AcceptedBallots.Count,
            request.PublishedBallots.Count,
            ChunkCount: 1,
            RetryCount: 0,
            FailureCode: null,
            FailureReason: null,
            VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(request.AcceptedBallots)),
            VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputePublishedBallotStreamHash(request.PublishedBallots)),
            TranscriptHash: "sp07-transcript-hash",
            ProofHash: proofHash,
            ServerVerifierOutputHash: "sp07-server-verifier-output-hash",
            DeletionReceiptId: null);
        var transcript = new ElectionPublicationProofTranscriptRecord(
            Guid.NewGuid(),
            request.Election.ElectionId,
            session.Id,
            session.WitnessSetId,
            ElectionSp07ProfileIds.TranscriptVersion,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            VerificationProfileIds.HighAssuranceV1,
            VerificationCanonicalHash.ToLowerHex(request.Election.BallotDefinitionHash),
            BallotEncryptionSchemeVersion: "babyjubjub-elgamal-vector-ballot-v1",
            ElectionPublicKeyId: "election-public-key-id",
            session.AcceptedBallotSetHash!,
            session.PublishedBallotStreamHash!,
            request.AcceptedBallots.Count,
            request.PublishedBallots.Count,
            CiphertextSlotCount: request.Election.Options.Count,
            ElectionSp07ProfileIds.ProofSystemVersion,
            proofBytes,
            proofHash,
            session.TranscriptHash!,
            ElectionSp07ProfileIds.ExternalReviewStatus,
            DateTime.UnixEpoch.AddHours(3).AddMinutes(1),
            GeneratorReleaseHash: "generator-release-hash",
            VerifierReleaseHash: "verifier-release-hash",
            PublicPrivacyBoundary:
            [
                "no_hidden_permutation",
                "no_shuffle_map",
                "no_rerandomization_randomness",
                "no_raw_witness",
            ],
            StatementHashSha512: includeCanonicalProofVerifierInput ? statementHashSha512 : null,
            FiatShamirTranscriptHashSha512: includeCanonicalProofVerifierInput ? fiatShamirTranscriptHashSha512 : null,
            CanonicalProofBytesHex: includeCanonicalProofVerifierInput ? canonicalProofBytesHex : null,
            CanonicalProofHashSha512: includeCanonicalProofVerifierInput ? canonicalProofHashSha512 : null,
            CanonicalProofByteLength: includeCanonicalProofVerifierInput ? canonicalProofBytes.Length : null);
        var receipts = includeDeletionReceipt
            ? new[]
            {
                new ElectionPublicationWitnessDeletionReceiptRecord(
                    Guid.NewGuid(),
                    request.Election.ElectionId,
                    session.Id,
                    session.WitnessSetId,
                    WitnessSetHash: "witness-set-hash",
                    WitnessCount: request.AcceptedBallots.Count,
                    transcript.TranscriptHash,
                    transcript.ProofHash,
                    ElectionPublicationWitnessDeletionStatus.Completed,
                    DateTime.UnixEpoch.AddHours(3).AddMinutes(3),
                    DeletionActorRef: "proof-worker",
                    FailureCode: null,
                    FailureReason: null),
            }
            : [];
        request = request with
        {
            PublicationProofSessions = [session],
            PublicationProofTranscripts = [transcript],
            PublicationWitnessDeletionReceipts = receipts,
        };
        request = ElectionVerificationPackageExportServiceTests.WithCompleteSp10OperationalSecurityStatus(
            ElectionVerificationPackageExportServiceTests.WithOfficialSp08ReleaseManifest(request));

        var export = new ElectionVerificationPackageExportService().Export(request);
        ElectionVerificationPackageExportService.WritePackageToDirectory(export, directory.PackagePath);
        return directory;
    }

    private static async Task<TemporaryPackageDirectory> CreateHighAssuranceTrusteePackageWithRealSp07ManifestEvidenceAsync(
        string workerPath)
    {
        var directory = new TemporaryPackageDirectory();
        try
        {
            var request = ElectionVerificationPackageExportServiceTests.CreateHighAssuranceTrusteeRequest();
            var fixture = await GenerateRustProductionFixtureAsync(
                workerPath,
                directory.PackagePath,
                request.PublishedBallots.Count,
                request.Election.Options.Count);
            request = WithSp07ProductionFixturePackages(request, fixture.ProductionProofInput);
            var acceptedHash = VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(request.AcceptedBallots));
            var publishedHash = VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputePublishedBallotStreamHash(request.PublishedBallots));
            var proofSessionId = "real-manifest-session";
            var chunkId = "chunk-0001";
            var proof = await GenerateRustWorkerProofAsync(
                workerPath,
                request,
                proofSessionId,
                chunkId,
                acceptedHash,
                publishedHash,
                fixture.ProductionProofInput);
            var manifest = new ElectionSp07PublicationProofManifestArtifactRecord(
                ElectionSp07PublicationProofManifestArtifactRecord.SchemaVersion,
                request.Election.ElectionId.ToString(),
                proofSessionId,
                PlanId: "sp07-plan-real-test",
                ElectionSp07ProfileIds.PublicationProofMode,
                ElectionSp07ProfileIds.ProofConstruction,
                ElectionSp07ProfileIds.StatementId,
                VerificationProfileIds.HighAssuranceV1,
                acceptedHash,
                publishedHash,
                request.AcceptedBallots.Count,
                request.PublishedBallots.Count,
                request.Election.Options.Count,
                ChunkCount: 1,
                CompletedChunkCount: 1,
                FailedChunkCount: 0,
                SlowestChunkMilliseconds: proof.ElapsedMilliseconds,
                [
                    CreateManifestChunkFromWorkerProof(request, chunkId, proof, publishedHash),
                ],
                PublicPrivacyBoundary:
                [
                    "no_hidden_permutation",
                    "no_shuffle_map",
                    "no_rerandomization_randomness",
                    "no_raw_witness",
                ]);
            DeleteRustWorkerProofTemp(proof);
            var proofBytes = JsonSerializer.Serialize(manifest, VerificationJson.Options);
            var proofHash = HashHex(proofBytes);
            var witnessSetId = Guid.NewGuid();
            var session = new ElectionPublicationProofSessionRecord(
                Guid.NewGuid(),
                request.Election.ElectionId,
                witnessSetId,
                ElectionSp07ProfileIds.PublicationProofMode,
                ElectionSp07ProfileIds.ProofConstruction,
                ElectionSp07ProfileIds.StatementId,
                ElectionPublicationProofSessionStatus.WitnessDeleted,
                DateTime.UnixEpoch.AddHours(3),
                DateTime.UnixEpoch.AddHours(3).AddMinutes(2),
                request.AcceptedBallots.Count,
                request.PublishedBallots.Count,
                ChunkCount: 1,
                RetryCount: 0,
                FailureCode: null,
                FailureReason: null,
                acceptedHash,
                publishedHash,
                TranscriptHash: "sp07-transcript-hash",
                ProofHash: proofHash,
                ServerVerifierOutputHash: "sp07-server-verifier-output-hash",
                DeletionReceiptId: null);
            var transcript = new ElectionPublicationProofTranscriptRecord(
                Guid.NewGuid(),
                request.Election.ElectionId,
                session.Id,
                session.WitnessSetId,
                ElectionSp07ProfileIds.TranscriptVersion,
                ElectionSp07ProfileIds.PublicationProofMode,
                ElectionSp07ProfileIds.ProofConstruction,
                ElectionSp07ProfileIds.StatementId,
                VerificationProfileIds.HighAssuranceV1,
                VerificationCanonicalHash.ToLowerHex(request.Election.BallotDefinitionHash),
                BallotEncryptionSchemeVersion: "babyjubjub-elgamal-vector-ballot-v1",
                ElectionPublicKeyId: "election-public-key-id",
                acceptedHash,
                publishedHash,
                request.AcceptedBallots.Count,
                request.PublishedBallots.Count,
                CiphertextSlotCount: request.Election.Options.Count,
                ElectionSp07ProfileIds.ProofSystemVersion,
                proofBytes,
                proofHash,
                session.TranscriptHash!,
                ElectionSp07ProfileIds.ExternalReviewStatus,
                DateTime.UnixEpoch.AddHours(3).AddMinutes(1),
                GeneratorReleaseHash: "generator-release-hash",
                VerifierReleaseHash: "verifier-release-hash",
                PublicPrivacyBoundary:
                [
                    "no_hidden_permutation",
                    "no_shuffle_map",
                    "no_rerandomization_randomness",
                    "no_raw_witness",
                ]);
            var receipt = new ElectionPublicationWitnessDeletionReceiptRecord(
                Guid.NewGuid(),
                request.Election.ElectionId,
                session.Id,
                session.WitnessSetId,
                WitnessSetHash: "witness-set-hash",
                WitnessCount: request.AcceptedBallots.Count,
                transcript.TranscriptHash,
                transcript.ProofHash,
                ElectionPublicationWitnessDeletionStatus.Completed,
                DateTime.UnixEpoch.AddHours(3).AddMinutes(3),
                DeletionActorRef: "proof-worker",
                FailureCode: null,
                FailureReason: null);
            request = request with
            {
                PublicationProofSessions = [session],
                PublicationProofTranscripts = [transcript],
                PublicationWitnessDeletionReceipts = [receipt],
            };
            request = ElectionVerificationPackageExportServiceTests.WithCompleteSp10OperationalSecurityStatus(
                ElectionVerificationPackageExportServiceTests.WithOfficialSp08ReleaseManifest(request));

            var export = new ElectionVerificationPackageExportService().Export(request);
            ElectionVerificationPackageExportService.WritePackageToDirectory(export, directory.PackagePath);
            return directory;
        }
        catch
        {
            directory.Dispose();
            throw;
        }
    }

    private static string ResolvePackagePath(string packagePath, string relativePath) =>
        Path.Combine(packagePath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string HashHex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string RepeatHash(char value) =>
        new(char.ToLowerInvariant(value), 64);

    private static async Task<ElectionSp09ExternalReviewStatusArtifactRecord> CreateReviewedSp09StatusAsync(
        string packagePath,
        string detailedStatus = ElectionSp09ProfileIds.StatusReviewedForDeclaredScope,
        string? claimState = null,
        bool reviewScopeMatchesElection = true,
        string? customerSafeSummaryHash = "sha256:customer-safe-summary",
        string? reportHashOrRestrictedRef = "sha256:review-report",
        IReadOnlyList<ElectionSp09FindingSeverityCountRecord>? findingSummary = null,
        IReadOnlyList<string>? publicPrivacyBoundary = null)
    {
        var status = await ReadPackageArtifactAsync<ElectionSp09ExternalReviewStatusArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp09ExternalReviewStatus);
        var electionRecord = await ReadPackageArtifactAsync<ElectionRecordReferenceRecord>(
            packagePath,
            VerificationPackageFileNames.ElectionRecord);

        return status with
        {
            DetailedStatus = detailedStatus,
            Availability = ElectionSp09ExternalReviewRules.ProjectAvailability(
                detailedStatus,
                reviewScopeMatchesElection),
            ClaimState = claimState ??
                ElectionSp09ExternalReviewRules.GetDefaultClaimState(detailedStatus, reviewScopeMatchesElection),
            ReviewScopeMatchesElection = reviewScopeMatchesElection,
            PrimaryResultCode = VerificationResultCodes.ExternalReviewStatusValid,
            PrimaryIssue = null,
            ReviewerEvidenceRef = "reviewer:engagement-42",
            ReportHashOrRestrictedRef = reportHashOrRestrictedRef,
            CustomerSafeSummaryHash = customerSafeSummaryHash,
            ReviewedArtifacts = BuildSp09ReviewedArtifacts(electionRecord),
            FindingSummary = findingSummary ?? status.FindingSummary,
            PublicPrivacyBoundary = publicPrivacyBoundary ?? status.PublicPrivacyBoundary,
        };
    }

    private static IReadOnlyList<ElectionSp09ReviewedArtifactRecord> BuildSp09ReviewedArtifacts(
        ElectionRecordReferenceRecord electionRecord) =>
    [
        new(
            "protocol-specification-package",
            "protocol_specification_package",
            "Protocol specification package",
            BuildSp09Sha256Ref(electionRecord.ProtocolSpecificationHash),
            electionRecord.ProtocolPackageVersion,
            ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1),
        new(
            "protocol-proof-package",
            "protocol_proof_package",
            "Protocol proof package",
            BuildSp09Sha256Ref(electionRecord.ProtocolProofPackageHash),
            electionRecord.ProtocolPackageVersion,
            ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1),
        new(
            "protocol-release-manifest",
            "protocol_release_manifest",
            "Protocol release manifest",
            BuildSp09Sha256Ref(electionRecord.ProtocolReleaseManifestHash),
            electionRecord.ProtocolPackageVersion,
            ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1),
    ];

    private static string BuildSp09Sha256Ref(string hash) =>
        hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? hash
            : $"sha256:{hash}";

    private static async Task<string> ReadPackageAcceptedBallotSetHashAsync(string packagePath)
    {
        var accepted = JsonSerializer.Deserialize<AcceptedBallotSetArtifactRecord>(
            await File.ReadAllTextAsync(ResolvePackagePath(packagePath, VerificationPackageFileNames.AcceptedBallotSet)),
            VerificationJson.Options)!;
        return accepted.AcceptedBallotInventoryHash;
    }

    private static async Task TamperPublishedStreamAndRefreshPackageAsync(string packagePath, string tamperKind)
    {
        var published = await ReadPackageArtifactAsync<PublishedBallotStreamArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.PublishedBallotStream);
        var transcript = await ReadPackageArtifactAsync<ElectionSp07PublicationProofTranscriptArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp07PublicationProofTranscript);
        var ballots = published.PublishedBallots.ToList();
        var publicKey = ReadSp07PackagePublicKey(ballots[0].EncryptedBallotPackage);
        switch (tamperKind)
        {
            case "insert":
                ballots.Insert(1, ballots[0] with
                {
                    PublicationSequence = 2,
                    EncryptedBallotPackage = CreateSp07CiphertextPackage(
                        999,
                        transcript.CiphertextSlotCount,
                        publicKey),
                });
                break;

            case "remove":
                ballots.RemoveAt(1);
                break;

            case "duplicate":
                ballots[1] = ballots[0] with
                {
                    PublicationSequence = ballots[1].PublicationSequence,
                };
                break;

            case "replace":
                ballots[1] = ballots[1] with
                {
                    EncryptedBallotPackage = CreateSp07CiphertextPackage(
                        998,
                        transcript.CiphertextSlotCount,
                        publicKey),
                };
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(tamperKind), tamperKind, "Unsupported tamper kind.");
        }

        var normalized = ballots
            .Select((ballot, index) => ballot with
            {
                PublicationSequence = index + 1,
                EncryptedBallotPackageHash = VerificationCanonicalHash.ComputeSha256UpperHex(ballot.EncryptedBallotPackage),
                ProofBundleHash = VerificationCanonicalHash.ComputeSha256UpperHex(ballot.ProofBundle),
            })
            .ToArray();
        var republished = published with
        {
            PublishedBallotCount = normalized.Length,
            PublishedBallots = normalized,
            PublishedBallotStreamHash = ComputePublishedArtifactHash(published.ElectionId, normalized),
        };
        await WritePackageArtifactAsync(packagePath, VerificationPackageFileNames.PublishedBallotStream, republished);

        var manifest = JsonSerializer.Deserialize<ElectionSp07PublicationProofManifestArtifactRecord>(
            transcript.ProofBytes,
            VerificationJson.Options)!;
        var remappedManifest = manifest with
        {
            ChunkCount = 1,
            CompletedChunkCount = 1,
            PublishedBallotCount = normalized.Length,
            PublishedBallotStreamHash = republished.PublishedBallotStreamHash,
            Chunks =
            [
                manifest.Chunks[0] with
                {
                    Count = normalized.Length,
                    PublishedBallotStreamHash = republished.PublishedBallotStreamHash,
                },
            ],
        };
        var manifestBytes = JsonSerializer.Serialize(remappedManifest, VerificationJson.Options);
        var proofHash = HashHex(manifestBytes);
        var updatedTranscript = transcript with
        {
            PublishedBallotCount = normalized.Length,
            PublishedBallotStreamHash = republished.PublishedBallotStreamHash,
            ProofBytes = manifestBytes,
            ProofHash = proofHash,
            TranscriptHash = ComputeTamperedTranscriptHash(transcript, proofHash),
        };
        await WritePackageArtifactAsync(
            packagePath,
            VerificationPackageFileNames.Sp07PublicationProofTranscript,
            updatedTranscript);

        var tallyReplay = await ReadPackageArtifactAsync<TallyReplayArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.TallyReplay);
        await WritePackageArtifactAsync(
            packagePath,
            VerificationPackageFileNames.TallyReplay,
            tallyReplay with
            {
                PublishedBallotStreamHash = republished.PublishedBallotStreamHash,
                PublicationProofTranscriptHash = updatedTranscript.TranscriptHash,
                PublicationProofHash = updatedTranscript.ProofHash,
            });

        var deletionReceipt = await ReadPackageArtifactAsync<ElectionSp07WitnessDeletionReceiptArtifactRecord>(
            packagePath,
            VerificationPackageFileNames.Sp07WitnessDeletionReceipt);
        await WritePackageArtifactAsync(
            packagePath,
            VerificationPackageFileNames.Sp07WitnessDeletionReceipt,
            deletionReceipt with
            {
                WitnessCount = updatedTranscript.AcceptedBallotCount,
                TranscriptHash = updatedTranscript.TranscriptHash,
                ProofHash = updatedTranscript.ProofHash,
            });

        await RefreshAuditManifestAsync(packagePath);
    }

    private static string ComputePublishedArtifactHash(
        string electionId,
        IReadOnlyList<PublishedBallotArtifactRecord> ballots)
    {
        var records = ballots
            .Select(x => new ElectionPublishedBallotRecord(
                Guid.NewGuid(),
                new ElectionId(Guid.Parse(electionId)),
                x.PublicationSequence,
                x.EncryptedBallotPackage,
                x.ProofBundle,
                DateTime.UnixEpoch,
                SourceBlockHeight: null,
                SourceBlockId: null))
            .ToArray();

        return VerificationCanonicalHash.ToLowerHex(VerificationCanonicalHash.ComputePublishedBallotStreamHash(records));
    }

    private static string ComputeTamperedTranscriptHash(
        ElectionSp07PublicationProofTranscriptArtifactRecord transcript,
        string proofHash) =>
        VerificationCanonicalHash.ComputeSha256LowerHex(string.Join(
            '\n',
            "HUSH_SP07_PUBLICATION_PROOF_TRANSCRIPT_V1",
            transcript.ElectionId,
            "unknown-package-proof-session",
            "unknown-witness-set",
            transcript.ProfileId,
            transcript.BallotDefinitionHash,
            transcript.AcceptedBallotSetHash,
            transcript.PublishedBallotStreamHash,
            proofHash));

    private static async Task<T> ReadPackageArtifactAsync<T>(string packagePath, string relativePath) =>
        JsonSerializer.Deserialize<T>(
            await File.ReadAllTextAsync(ResolvePackagePath(packagePath, relativePath)),
            VerificationJson.Options)!;

    private static async Task WritePackageArtifactAsync<T>(string packagePath, string relativePath, T value) =>
        await File.WriteAllTextAsync(
            ResolvePackagePath(packagePath, relativePath),
            JsonSerializer.Serialize(value, VerificationJson.Options));

    private static async Task MutateJsonArtifactAndRefreshPackageAsync(
        string packagePath,
        string relativePath,
        Action<JsonObject> mutate)
    {
        var path = ResolvePackagePath(packagePath, relativePath);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))?.AsObject() ??
            throw new InvalidOperationException($"Package artifact '{relativePath}' is not a JSON object.");
        mutate(root);
        await File.WriteAllTextAsync(path, root.ToJsonString(VerificationJson.Options));
        await RefreshAuditManifestAsync(packagePath);
    }

    private static void MutateFirstAnomalyAttachment(JsonObject root, Action<JsonObject> mutate) =>
        mutate(GetFirstAnomalyManifestRow(root, "attachments"));

    private static void MutateFirstAnomalyRedaction(JsonObject root, Action<JsonObject> mutate) =>
        mutate(GetFirstAnomalyManifestRow(root, "redactions"));

    private static void MutateFirstAnomalyRecipientStatus(JsonObject root, Action<JsonObject> mutate) =>
        mutate(GetFirstAnomalyManifestRow(root, "recipientStatuses"));

    private static JsonObject GetFirstAnomalyManifestRow(JsonObject root, string collectionName) =>
        root["manifest"]!.AsObject()["threads"]!.AsArray()[0]!
            .AsObject()[collectionName]!.AsArray()[0]!
            .AsObject();

    private static async Task RefreshAuditManifestAsync(string packagePath)
    {
        var manifest = await ReadPackageArtifactAsync<AuditPackageManifestRecord>(
            packagePath,
            VerificationPackageFileNames.AuditPackageManifest);
        var entries = new List<AuditPackageManifestEntryRecord>();
        foreach (var entry in manifest.Entries)
        {
            var bytes = await File.ReadAllBytesAsync(ResolvePackagePath(packagePath, entry.Path));
            entries.Add(entry with
            {
                Sha256Hash = VerificationCanonicalHash.ComputeManifestFileSha256(bytes),
                SizeBytes = bytes.Length,
            });
        }

        await WritePackageArtifactAsync(
            packagePath,
            VerificationPackageFileNames.AuditPackageManifest,
            manifest with { Entries = entries });
    }

    private static ElectionSp07PublicationProofManifestChunkArtifactRecord CreateManifestChunk(
        ElectionVerificationPackageExportRequest request,
        string chunkId,
        int chunkIndex,
        int offset,
        int count,
        string proofText,
        string acceptedBallotSetHash,
        string publishedBallotStreamHash)
    {
        var proofBytes = Encoding.UTF8.GetBytes(proofText);
        var publicKey = new Sp07PackagePublicPoint("111", "222");
        var statementHash = Sp07PackagePublicStatementHasher.ComputeStatementHashSha512(
            new Sp07PackagePublicStatementHashInput(
                request.Election.ElectionId.ToString(),
                chunkId,
                request.ProtocolPackageBinding!.ReleaseManifestHash,
                VerificationCanonicalHash.ToLowerHex(request.Election.BallotDefinitionHash),
                publicKey,
                count,
                request.Election.Options.Count,
                acceptedBallotSetHash,
                publishedBallotStreamHash));
        return new ElectionSp07PublicationProofManifestChunkArtifactRecord(
            chunkId,
            chunkIndex,
            offset,
            count,
            Passed: true,
            "PUB-005",
            "matrix_m_1_publication_proof_v1",
            "rust_arkworks_m1_process_worker",
            "0.1.0",
            WorkerThreadCount: 2,
            StatementHashSha512: statementHash,
            FiatShamirTranscriptHashSha512: new string((char)('c' + chunkIndex), 128),
            CanonicalProofHashSha512: Convert.ToHexString(SHA512.HashData(proofBytes)).ToLowerInvariant(),
            CanonicalProofByteLength: proofBytes.Length,
            CanonicalProofBytesHex: Convert.ToHexString(proofBytes).ToLowerInvariant(),
            publishedBallotStreamHash,
            ElapsedMilliseconds: 4.5 + chunkIndex);
    }

    private static ElectionSp07PublicationProofManifestChunkArtifactRecord CreateManifestChunkFromWorkerProof(
        ElectionVerificationPackageExportRequest request,
        string chunkId,
        TestRustWorkerProofResult proof,
        string publishedBallotStreamHash) =>
        new(
            chunkId,
            ChunkIndex: 0,
            Offset: 0,
            Count: request.PublishedBallots.Count,
            Passed: proof.Passed,
            proof.ResultCode,
            proof.ProofProfileId,
            proof.WorkerKind,
            proof.WorkerVersion,
            proof.WorkerThreadCount,
            proof.StatementHashSha512,
            proof.TranscriptHashSha512,
            proof.ProofHashSha512,
            proof.CanonicalProofByteLength,
            proof.CanonicalProofBytesHex!,
            publishedBallotStreamHash,
            proof.ElapsedMilliseconds);

    private static ElectionVerificationPackageExportRequest WithSp07PublicCiphertextPackages(
        ElectionVerificationPackageExportRequest request,
        Sp07PackagePublicPoint? publicKey = null)
    {
        var accepted = request.AcceptedBallots
            .Select((ballot, index) => ballot with
            {
                EncryptedBallotPackage = CreateSp07CiphertextPackage(
                    index + 1,
                    request.Election.Options.Count,
                    publicKey),
            })
            .ToArray();
        var published = request.PublishedBallots
            .Select((ballot, index) => ballot with
            {
                EncryptedBallotPackage = CreateSp07CiphertextPackage(
                    index + 101,
                    request.Election.Options.Count,
                    publicKey),
            })
            .ToArray();

        return request with
        {
            AcceptedBallots = accepted,
            PublishedBallots = published,
        };
    }

    private static ElectionVerificationPackageExportRequest WithSp07ProductionFixturePackages(
        ElectionVerificationPackageExportRequest request,
        Sp07RustWorkerProductionProofInput fixture)
    {
        var accepted = request.AcceptedBallots
            .Select((ballot, index) => ballot with
            {
                EncryptedBallotPackage = CreateSp07CiphertextPackage(
                    fixture.PublicKey,
                    fixture.AcceptedBallots[index]),
            })
            .ToArray();
        var published = request.PublishedBallots
            .Select((ballot, index) => ballot with
            {
                EncryptedBallotPackage = CreateSp07CiphertextPackage(
                    fixture.PublicKey,
                    fixture.PublishedBallots[index]),
            })
            .ToArray();

        return request with
        {
            AcceptedBallots = accepted,
            PublishedBallots = published,
        };
    }

    private static string CreateSp07CiphertextPackage(
        int seed,
        int selectionCount,
        Sp07PackagePublicPoint? publicKey = null)
    {
        var points = Enumerable.Range(0, selectionCount)
            .Select(index => new
            {
                x = (seed * 1000 + index * 10 + 1).ToString(),
                y = (seed * 1000 + index * 10 + 2).ToString(),
            })
            .ToArray();
        var points2 = Enumerable.Range(0, selectionCount)
            .Select(index => new
            {
                x = (seed * 1000 + index * 10 + 3).ToString(),
                y = (seed * 1000 + index * 10 + 4).ToString(),
            })
            .ToArray();

        return JsonSerializer.Serialize(
            new
            {
                version = "babyjubjub-elgamal-vector-ballot-v1",
                publicKey = new { x = publicKey?.X ?? "111", y = publicKey?.Y ?? "222" },
                selectionCount,
                ciphertext = new
                {
                    c1 = points,
                    c2 = points2,
                },
            },
            VerificationJson.Options);
    }

    private static string CreateSp07CiphertextPackage(
        Sp07PointPayload publicKey,
        Sp07CipherBallotPayload ballot) =>
        JsonSerializer.Serialize(
            new
            {
                version = "babyjubjub-elgamal-vector-ballot-v1",
                publicKey = new { x = publicKey.X, y = publicKey.Y },
                selectionCount = ballot.Slots.Count,
                ciphertext = new
                {
                    c1 = ballot.Slots.Select(slot => new { x = slot.C1.X, y = slot.C1.Y }).ToArray(),
                    c2 = ballot.Slots.Select(slot => new { x = slot.C2.X, y = slot.C2.Y }).ToArray(),
                },
            },
            VerificationJson.Options);

    private static Sp07PackagePublicPoint ReadSp07PackagePublicKey(string encryptedBallotPackage)
    {
        using var document = JsonDocument.Parse(encryptedBallotPackage);
        var publicKey = document.RootElement.GetProperty("publicKey");
        return new Sp07PackagePublicPoint(
            publicKey.GetProperty("x").GetString()!,
            publicKey.GetProperty("y").GetString()!);
    }

    private static async Task<RustProductionFixture> GenerateRustProductionFixtureAsync(
        string workerPath,
        string temp,
        int ballots,
        int slots)
    {
        var output = Path.Combine(temp, "sp07-production-fixture.json");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = workerPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            }
        };
        process.StartInfo.ArgumentList.Add("fixture");
        process.StartInfo.ArgumentList.Add("--ballots");
        process.StartInfo.ArgumentList.Add(ballots.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("--slots");
        process.StartInfo.ArgumentList.Add(slots.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("--output");
        process.StartInfo.ArgumentList.Add(output);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0, $"Rust fixture command failed. stdout: {stdout}; stderr: {stderr}");
        var fixture = JsonSerializer.Deserialize<RustProductionFixture>(
            await File.ReadAllTextAsync(output),
            RustWorkerJsonOptions);
        fixture.Should().NotBeNull();
        fixture!.Schema.Should().Be("HushSp07ProductionProofInputFixtureV1");
        fixture.Ballots.Should().Be(ballots);
        fixture.Slots.Should().Be(slots);
        return fixture;
    }

    private static async Task<Sp07PackagePublicPoint> GenerateRustWorkerPublicKeyAsync(string workerPath)
    {
        var proof = await GenerateRustWorkerProofAsync(
            workerPath,
            ElectionVerificationPackageExportServiceTests.CreateHighAssuranceTrusteeRequest(),
            proofSessionId: "bootstrap-session",
            chunkId: "chunk-bootstrap",
            acceptedBallotSetHash: "bootstrap-accepted-hash",
            publishedBallotStreamHash: "bootstrap-published-hash");
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(proof.ProofExampleResultPath!));
        var publicKey = document.RootElement
            .GetProperty("proof_example")
            .GetProperty("statement")
            .GetProperty("public_key");
        var result = new Sp07PackagePublicPoint(
            publicKey.GetProperty("x").GetString()!,
            publicKey.GetProperty("y").GetString()!);
        DeleteRustWorkerProofTemp(proof);
        return result;
    }

    private static async Task<TestRustWorkerProofResult> GenerateRustWorkerProofAsync(
        string workerPath,
        ElectionVerificationPackageExportRequest request,
        string proofSessionId,
        string chunkId,
        string acceptedBallotSetHash,
        string publishedBallotStreamHash,
        Sp07RustWorkerProductionProofInput? productionProofInput = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), "hush-sp07-package-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var job = new Sp07RustWorkerProofJob(
            request.Election.ElectionId.ToString(),
            proofSessionId,
            chunkId,
            request.PublishedBallots.Count,
            request.Election.Options.Count,
            WorkDirectory: Path.Combine(temp, "work"),
            RequestPath: Path.Combine(temp, "work", "proof-request.json"),
            ResultPath: Path.Combine(temp, "work", "proof-result.json"),
            IncludeTamperVectors: true,
            IncludeLegacyPhaseArtifacts: false,
            ProtocolPackageHash: request.ProtocolPackageBinding!.ReleaseManifestHash,
            BallotDefinitionHash: VerificationCanonicalHash.ToLowerHex(request.Election.BallotDefinitionHash),
            AcceptedBallotSetHash: acceptedBallotSetHash,
            PublishedBallotStreamHash: publishedBallotStreamHash,
            ProductionProofInput: productionProofInput);
        var client = new Sp07RustWorkerProcessClient(new Sp07RustWorkerProcessOptions(
            workerPath,
            TimeSpan.FromSeconds(60),
            WorkingDirectory: temp,
            Threads: 2));
        var proof = await client.ProveAsync(job);
        return new TestRustWorkerProofResult(proof, job.ResultPath);
    }

    private static void DeleteRustWorkerProofTemp(TestRustWorkerProofResult proof)
    {
        if (string.IsNullOrWhiteSpace(proof.ProofExampleResultPath))
        {
            return;
        }

        var workDirectory = Directory.GetParent(proof.ProofExampleResultPath);
        var tempDirectory = workDirectory?.Parent;
        if (tempDirectory is not null && Directory.Exists(tempDirectory.FullName))
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    private static string? ResolveAvailableWorkerPath()
    {
        var configured = Environment.GetEnvironmentVariable(
            Sp07RustWorkerProcessOptions.WorkerPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return null;
        }

        var localDebugWorker = Path.Combine(
            repositoryRoot,
            "Tools",
            "HushSp07RustWorker",
            "target",
            "debug",
            OperatingSystem.IsWindows()
                ? "hush-sp07-rust-worker.exe"
                : "hush-sp07-rust-worker");
        return File.Exists(localDebugWorker) ? localDebugWorker : null;
    }

    private static string? FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Tools", "HushSp07RustWorker", "Cargo.toml")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private sealed record RustProductionFixture(
        string Schema,
        int Ballots,
        int Slots,
        Sp07RustWorkerProductionProofInput ProductionProofInput);

    private sealed record TestRustWorkerProofResult(
        string Schema,
        string WorkerKind,
        string Command,
        string Status,
        bool Passed,
        string ResultCode,
        string Message,
        string ElectionId,
        string ProofSessionId,
        string ChunkId,
        string ProofProfileId,
        string WorkerVersion,
        int WorkerThreadCount,
        string StatementHashSha512,
        string TranscriptHashSha512,
        string ProofHashSha512,
        string AcceptedBallotSetHash,
        string PublishedBallotStreamHash,
        int CanonicalProofByteLength,
        string? CanonicalProofBytesHex,
        string ProofExampleHashSha512,
        double ElapsedMilliseconds,
        Sp07RustWorkerTelemetry? Telemetry,
        string? ProofExampleResultPath)
    {
        public TestRustWorkerProofResult(Sp07RustWorkerCommandResult result, string proofExampleResultPath)
            : this(
                result.Schema,
                result.WorkerKind,
                result.Command,
                result.Status,
                result.Passed,
                result.ResultCode,
                result.Message,
                result.ElectionId,
                result.ProofSessionId,
                result.ChunkId,
                result.ProofProfileId,
                result.WorkerVersion,
                result.WorkerThreadCount,
                result.StatementHashSha512,
                result.TranscriptHashSha512,
                result.ProofHashSha512,
                result.AcceptedBallotSetHash,
                result.PublishedBallotStreamHash,
                result.CanonicalProofByteLength,
                result.CanonicalProofBytesHex,
                result.ProofExampleHashSha512,
                result.ElapsedMilliseconds,
                result.Telemetry,
                proofExampleResultPath)
        {
        }
    }

    private sealed class WorkerBackedSp07PackagePublicProofVerifier(string workerPath) : ISp07PackagePublicProofVerifier
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
        };

        public List<Sp07PackagePublicProofVerificationRequest> Requests { get; } = [];

        public async Task<Sp07PackagePublicProofVerificationResult> VerifyAsync(
            Sp07PackagePublicProofVerificationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var temp = Path.Combine(
                Path.GetTempPath(),
                "hush-sp07-package-public-verifier-tests",
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var inputPath = Path.Combine(temp, "verify-input.json");
                var outputPath = Path.Combine(temp, "verify-output.json");
                await File.WriteAllTextAsync(
                    inputPath,
                    JsonSerializer.Serialize(BuildWorkerVerifyInput(request), JsonOptions),
                    cancellationToken);
                var client = new Sp07RustWorkerProcessClient(new Sp07RustWorkerProcessOptions(
                    workerPath,
                    TimeSpan.FromSeconds(60),
                    WorkingDirectory: temp,
                    Threads: 2));
                var result = await client.VerifyAsync(
                    new Sp07RustWorkerVerifyJob(
                        request.ElectionId,
                        request.ProofSessionId,
                        request.ChunkId,
                        inputPath,
                        outputPath),
                    cancellationToken);

                return new Sp07PackagePublicProofVerificationResult(
                    result.Passed,
                    result.ResultCode,
                    result.Message,
                    new Dictionary<string, string>
                    {
                        ["worker_kind"] = result.WorkerKind,
                        ["worker_version"] = result.WorkerVersion,
                        ["statement_hash_sha512"] = result.StatementHashSha512,
                        ["proof_hash_sha512"] = result.ProofHashSha512,
                    });
            }
            finally
            {
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, recursive: true);
                }
            }
        }

        private static object BuildWorkerVerifyInput(Sp07PackagePublicProofVerificationRequest request) =>
            new
            {
                Passed = true,
                ResultCode = "PUB-005",
                ElectionId = request.ElectionId,
                ProofSessionId = request.ProofSessionId,
                ChunkId = request.ChunkId,
                ProofProfileId = "matrix_m_1_publication_proof_v1",
                StatementHashSha512 = request.StatementHashSha512,
                TranscriptHashSha512 = request.FiatShamirTranscriptHashSha512,
                ProofHashSha512 = request.CanonicalProofHashSha512,
                PublishedBallotStreamHash = request.PublishedBallotStreamHash,
                AcceptedBallotSetHash = request.AcceptedBallotSetHash,
                CanonicalProofByteLength = request.CanonicalProofByteLength,
                CanonicalProofBytesHex = request.CanonicalProofBytesHex,
            };
    }

    private sealed class FakeSp07PackagePublicProofVerifier(bool passed) : ISp07PackagePublicProofVerifier
    {
        public List<Sp07PackagePublicProofVerificationRequest> Requests { get; } = [];

        public Task<Sp07PackagePublicProofVerificationResult> VerifyAsync(
            Sp07PackagePublicProofVerificationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new Sp07PackagePublicProofVerificationResult(
                passed,
                passed ? "PUB-005" : "PUB-015",
                passed
                    ? "fake public verifier accepted canonical proof bytes"
                    : "fake public verifier rejected canonical proof bytes",
                new Dictionary<string, string>
                {
                    ["test_public_verifier"] = "fake",
                }));
        }
    }

    private sealed class TemporaryPackageDirectory : IDisposable
    {
        public string PackagePath { get; } = Path.Combine(
            Path.GetTempPath(),
            $"hush-verifier-{Guid.NewGuid():N}");

        public TemporaryPackageDirectory()
        {
            Directory.CreateDirectory(PackagePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(PackagePath))
            {
                Directory.Delete(PackagePath, recursive: true);
            }
        }
    }
}
