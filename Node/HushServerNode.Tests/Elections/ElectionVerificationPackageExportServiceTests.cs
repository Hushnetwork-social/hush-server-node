using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionVerificationPackageExportServiceTests
{
    [Fact]
    public void Export_PublicPackage_ShouldWriteRootFilesAndBindManifestEntries()
    {
        var result = Export(CreateRequest(VerificationPackageView.PublicAnonymous));

        result.Success.Should().BeTrue();
        result.Files.Select(x => x.RelativePath).Should().Contain(VerificationPackageFileNames.RootFiles);
        result.Files.Select(x => x.RelativePath).Should().Contain([
            VerificationPackageFileNames.Sp04Evidence,
            VerificationPackageFileNames.Sp04ReceiptCommitments,
            VerificationPackageFileNames.Sp05EligibilityPolicy,
            VerificationPackageFileNames.Sp05EligibilitySummary,
            VerificationPackageFileNames.Sp05EligibilityVerifierOutput,
            VerificationPackageFileNames.Sp06TrusteeControlProfile,
            VerificationPackageFileNames.Sp06TrusteeControlSummary,
            VerificationPackageFileNames.Sp06TrusteeVerifierOutput,
            VerificationPackageFileNames.Sp08ReleaseManifest,
            VerificationPackageFileNames.Sp08ReleaseIntegrity,
            VerificationPackageFileNames.Sp08ReleaseIntegrityVerifierOutput,
            VerificationPackageFileNames.Sp09ExternalReviewStatus,
            VerificationPackageFileNames.Sp09ExternalReviewClaimTable,
            VerificationPackageFileNames.Sp09ExternalReviewVerifierOutput,
            VerificationPackageFileNames.Sp10OperationalSecuritySummary,
            VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
            VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
            VerificationPackageFileNames.Sp10OperationalVerifierOutput,
        ]);
        result.Files.Should().NotContain(x => x.RelativePath.StartsWith("artifacts/restricted/", StringComparison.OrdinalIgnoreCase));
        var publicPayload = string.Join(
            '\n',
            result.Files
                .Where(x => x.Visibility == VerificationArtifactVisibility.Public)
                .Select(x => x.ContentText));
        publicPayload.Should().NotContain("voter-1");
        publicPayload.Should().NotContain("actor-voter-1");
        publicPayload.Should().NotContain("voter-1@example.test");
        publicPayload.Should().NotContain("fullReportBody");
        publicPayload.Should().NotContain("findingBody");
        publicPayload.Should().NotContain("reviewerWorkpaper");
        publicPayload.Should().NotContain("retestEvidenceBody");

        var manifest = ReadFile<AuditPackageManifestRecord>(result, VerificationPackageFileNames.AuditPackageManifest);
        foreach (var entry in manifest.Entries)
        {
            var file = result.Files.Single(x => x.RelativePath == entry.Path);
            VerificationCanonicalHash.ComputeManifestFileSha256(file.Content).Should().Be(entry.Sha256Hash);
        }
    }

    [Fact]
    public void Export_PublicPackageWithRestrictedAnomalyArtifact_ShouldExcludeRestrictedAnomalyContent()
    {
        var request = CreateRequest(VerificationPackageView.PublicAnonymous);
        var content = """
            {
              "artifactSchemaId": "restricted-anomaly-intake-manifest-artifact-v1",
              "submitterActorPublicAddress": "private-address"
            }
            """;
        var artifact = ElectionModelFactory.CreateReportArtifact(
            request.ReportPackage!.Id,
            request.Election.ElectionId,
            ElectionReportArtifactKind.MachineRestrictedAnomalyIntakeManifest,
            ElectionReportArtifactFormat.Json,
            ElectionReportArtifactAccessScope.OwnerAuditorOnly,
            sortOrder: 14,
            title: "Restricted anomaly intake manifest",
            fileName: "restricted-anomaly-intake-manifest.json",
            mediaType: "application/json",
            contentHash: SHA256.HashData(Encoding.UTF8.GetBytes(content)),
            content: content);

        var result = Export(request with
        {
            ReportArtifacts = [.. request.ReportArtifacts, artifact],
        });

        result.Success.Should().BeTrue();
        result.Files.Should().NotContain(x =>
            x.RelativePath == VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest);
        var publicPayload = string.Join(
            '\n',
            result.Files
                .Where(x => x.Visibility == VerificationArtifactVisibility.Public)
                .Select(x => x.ContentText));
        publicPayload.Should().NotContain("restricted-anomaly-intake-manifest-artifact-v1");
        publicPayload.Should().NotContain("submitterActorPublicAddress");
        publicPayload.Should().NotContain("private-address");
    }

    [Fact]
    public void Export_PublicPackage_ShouldIncludePlannedSp09ExternalReviewArtifacts()
    {
        var result = Export(CreateRequest(VerificationPackageView.PublicAnonymous));

        result.Success.Should().BeTrue();
        var status = ReadFile<ElectionSp09ExternalReviewStatusArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp09ExternalReviewStatus);
        status.ProgramVersion.Should().Be(ElectionSp09ProfileIds.ExternalExaminationProgramVersion);
        status.ReviewScope.Should().Be(ElectionSp09ProfileIds.ReviewScopeProtocolOmegaV1);
        status.ReviewType.Should().Be(ElectionSp09ProfileIds.ReviewTypeCryptographicSecurity);
        status.DetailedStatus.Should().Be(ElectionSp09ProfileIds.StatusNotStarted);
        status.Availability.Should().Be(ElectionSp09ProfileIds.AvailabilityPlanned);
        status.ClaimState.Should().Be(ElectionSp09ProfileIds.ClaimStateProgramDefined);
        status.PrimaryResultCode.Should().Be(VerificationResultCodes.ExternalReviewNotComplete);
        status.ReviewerEvidenceRef.Should().BeNull();
        status.ReportHashOrRestrictedRef.Should().BeNull();
        status.RestrictedEvidenceFiles.Should().BeEmpty();
        ElectionSp09ExternalReviewRules.Validate(status).Should().BeEmpty();

        var claimTable = ReadFile<ElectionSp09ExternalReviewClaimTableArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp09ExternalReviewClaimTable);
        claimTable.Claims.Should().Contain(x => x.ClaimId == "CLM-SP09-EXAMINATION-PROGRAM");
        claimTable.Claims.Should().AllSatisfy(x =>
            ElectionSp09ExternalReviewRules.ContainsForbiddenClaimPhrase(x.AllowedWording).Should().BeFalse());

        var verifierOutput = ReadFile<ElectionSp09VerifierOutputArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp09ExternalReviewVerifierOutput);
        verifierOutput.Results.Should().ContainSingle(x =>
            x.CheckCode == ElectionSp09ProfileIds.ReviewStatusValidCheckCode &&
            x.Status == VerificationCheckStatus.Pass &&
            x.ResultCode == VerificationResultCodes.ExternalReviewStatusValid);
        verifierOutput.Results.Should().ContainSingle(x =>
            x.CheckCode == ElectionSp09ProfileIds.ReviewNotCompleteCheckCode &&
            x.Status == VerificationCheckStatus.Warn &&
            x.ResultCode == VerificationResultCodes.ExternalReviewNotComplete);
    }

    [Fact]
    public void Export_PublicPackage_ShouldIncludeDevelopmentPlaceholderSp10OperationalArtifacts()
    {
        var result = Export(CreateRequest(VerificationPackageView.PublicAnonymous));

        result.Success.Should().BeTrue();
        var manifest = ReadFile<AuditPackageManifestRecord>(result, VerificationPackageFileNames.AuditPackageManifest);
        manifest.Entries.Select(x => x.Path).Should().Contain([
            VerificationPackageFileNames.Sp10OperationalSecuritySummary,
            VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
            VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
            VerificationPackageFileNames.Sp10OperationalVerifierOutput,
        ]);

        var summary = ReadFile<ElectionSp10OperationalSecurityStatusArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp10OperationalSecuritySummary);
        summary.Schema.Should().Be(ElectionSp10ProfileIds.OperationalSecuritySummarySchema);
        summary.ProgramVersion.Should().Be(ElectionSp10ProfileIds.OperationalSecurityProgramVersion);
        summary.DeploymentProfileId.Should().Be(ElectionSp10ProfileIds.DeploymentProfileManagedAwsContainerV1);
        summary.EvidenceState.Should().Be(ElectionSp10ProfileIds.EvidenceStateDevelopmentPlaceholder);
        summary.DoesNotCompleteFeat106Readiness.Should().BeTrue();
        summary.BlocksHighAssurance.Should().BeTrue();
        summary.PrimaryResultCode.Should().Be(VerificationResultCodes.OperationalSecurityDevelopmentPlaceholder);
        summary.RestrictedEvidenceFiles.Should().BeEmpty();
        ElectionSp10OperationalSecurityRules.Validate(summary).Should().BeEmpty();

        var deployment = ReadFile<ElectionSp10OperationalDeploymentEvidenceArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp10OperationalDeploymentEvidence);
        deployment.Schema.Should().Be(ElectionSp10ProfileIds.OperationalDeploymentEvidenceSchema);
        deployment.ReleaseEvidenceMode.Should().Be(ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder);
        deployment.ReleaseManifestHash.Should().NotBeNullOrWhiteSpace();
        deployment.ImmutableDeploymentRef.Should().Contain("development-placeholder");

        var custody = ReadFile<ElectionSp10OperationalCustodyEvidenceArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp10OperationalCustodyEvidence);
        custody.Schema.Should().Be(ElectionSp10ProfileIds.OperationalCustodyEvidenceSchema);
        custody.CustodyMode.Should().Be(ElectionSp10ProfileIds.CustodyModeAwsKmsPerElectionEnvelopeV1);
        custody.ExecutorKeyLifecycle.Should().Be(ElectionSp10ProfileIds.ExecutorKeyLifecycleEphemeralMemoryV1);

        var verifierOutput = ReadFile<ElectionSp10OperationalVerifierOutputArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp10OperationalVerifierOutput);
        verifierOutput.Results.Should().ContainSingle(x =>
            x.CheckCode == ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode &&
            x.Status == VerificationCheckStatus.Pass &&
            x.ResultCode == VerificationResultCodes.OperationalSecurityProfileDeclared);
        verifierOutput.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.OperationalSecurityDevelopmentPlaceholder &&
            x.Status == VerificationCheckStatus.Warn);

        var publicPayload = string.Join(
            '\n',
            result.Files
                .Where(x => x.Visibility == VerificationArtifactVisibility.Public)
                .Select(x => x.ContentText));
        publicPayload.Should().NotContain("rawLogLine");
        publicPayload.Should().NotContain("kmsPlaintextKey");
        publicPayload.Should().NotContain("executorPrivateKey");
        publicPayload.Should().NotContain("incidentWorkpaper");
        publicPayload.Should().NotContain("regulatoryWorkpaper");
    }

    [Fact]
    public void Export_PublicPackageWithRegulatoryClaim_ShouldIncludeOptionalSp11ClaimState()
    {
        var request = CreateRequest(VerificationPackageView.PublicAnonymous) with
        {
            Sp11RegulatoryClaimState = CreateRegulatoryClaimState(),
        };

        var result = Export(request);

        result.Success.Should().BeTrue();
        var claim = ReadFile<ElectionSp11RegulatoryClaimStateArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp11RegulatoryClaimState);
        claim.Schema.Should().Be(ElectionSp11ProfileIds.RegulatoryClaimStateSchema);
        claim.ClaimState.Should().Be(ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation);
        claim.IsLegalAdvice.Should().BeFalse();
        claim.RestrictedWorkpaperRef.Should().BeNull();
        ElectionSp11RegulatoryRules.Validate(claim).Should().BeEmpty();
    }

    [Fact]
    public void Export_PublicPackage_ShouldIncludeDevelopmentPlaceholderSp08Artifacts()
    {
        var result = Export(CreateRequest(VerificationPackageView.PublicAnonymous));

        result.Success.Should().BeTrue();
        var manifest = ReadFile<AuditPackageManifestRecord>(result, VerificationPackageFileNames.AuditPackageManifest);
        manifest.Entries.Select(x => x.Path).Should().Contain([
            VerificationPackageFileNames.Sp08ReleaseManifest,
            VerificationPackageFileNames.Sp08ReleaseIntegrity,
            VerificationPackageFileNames.Sp08ReleaseIntegrityVerifierOutput,
        ]);

        var releaseManifest = ReadFile<ElectionSp08ReleaseManifestArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp08ReleaseManifest);
        releaseManifest.EvidenceMode.Should().Be(ElectionSp08ProfileIds.EvidenceModeDevelopmentPlaceholder);
        releaseManifest.NotForReleaseIntegrityClaims.Should().BeTrue();
        releaseManifest.Components.Select(x => x.ComponentId).Should().Contain(
            ElectionSp08ProfileIds.RequiredHighAssuranceComponentIds);

        var integrity = ReadFile<ElectionSp08ReleaseIntegrityArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp08ReleaseIntegrity);
        integrity.BlocksHighAssurance.Should().BeTrue();
        integrity.PrimaryResultCode.Should().Be(VerificationResultCodes.ReleaseIntegrityEvidencePending);
        integrity.ReleaseManifestHash.Should().Be(
            ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(releaseManifest));

        var verifierOutput = ReadFile<ElectionSp08VerifierOutputArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp08ReleaseIntegrityVerifierOutput);
        verifierOutput.Results.Should().ContainSingle(x =>
            x.CheckCode == ElectionSp08ProfileIds.EvidenceModeAllowedCheckCode &&
            x.Status == VerificationCheckStatus.Warn &&
            x.ResultCode == VerificationResultCodes.ReleaseIntegrityEvidencePending);
    }

    [Fact]
    public void Export_TrusteeThresholdWithoutSp06Profile_ShouldTreatControlEvidenceAsNotApplicable()
    {
        var request = CreateRequest(VerificationPackageView.PublicAnonymous);
        request = request with
        {
            Election = request.Election with
            {
                GovernanceMode = ElectionGovernanceMode.TrusteeThreshold,
                SelectedProfileId = ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
                RequiredApprovalCount = 3,
            },
        };

        var result = Export(request);

        result.Success.Should().BeTrue();
        var profile = ReadFile<ElectionSp06ControlProfileArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp06TrusteeControlProfile);
        profile.HighAssuranceClaimed.Should().BeFalse();
        profile.ControlDomainProfileId.Should().Be("not_applicable");

        var verifierOutput = ReadFile<ElectionSp06VerifierOutputArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp06TrusteeVerifierOutput);
        verifierOutput.Results.Should().Contain(x =>
            x.CheckCode == "CTRL-000" &&
            x.Status == VerificationCheckStatus.NotApplicable);
        verifierOutput.Results.Should().NotContain(x =>
            x.ResultCode == VerificationResultCodes.TrusteeAcceptanceIncomplete &&
            x.Status == VerificationCheckStatus.Fail);
    }

    [Fact]
    public void Export_RestrictedPackageWithoutAuthorization_ShouldFailDeterministically()
    {
        var result = Export(CreateRequest(
            VerificationPackageView.RestrictedOwnerAuditor,
            restrictedAccessAuthorized: false));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(VerificationResultCodes.RestrictedExportUnauthorized);
        result.Files.Should().BeEmpty();
    }

    [Fact]
    public void Export_RestrictedPackageWithAuthorization_ShouldIsolateRestrictedEvidence()
    {
        var result = Export(CreateRequest(
            VerificationPackageView.RestrictedOwnerAuditor,
            restrictedAccessAuthorized: true,
            profileId: VerificationProfileIds.RestrictedOwnerAuditorV1));

        result.Success.Should().BeTrue();
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedRosterCheckoff &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp04CeremonyRecords &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp04PreparedBallotCommitments &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp04SpoilMarkers &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedRosterImportEvidence &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedRoster &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedLinkingEvidence &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedActivationEvents &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedCheckoffLedger &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedDisputes &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp06TrusteeControlDomains &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp06TrusteeReleaseArtifacts &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp07PublicationProofSession &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp07WitnessDeletionLog &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp09FindingTracker &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp09RetestEvidence &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp09ReportReference &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp10LoggingEvidence &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp10BackupRestoreEvidence &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp10IncidentEvidence &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp10AuditorRoomAccessLog &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
    }

    [Fact]
    public void Export_RestrictedPackageWithOwnerOnlyReportArtifact_ShouldIncludeRestrictedReportArtifact()
    {
        var request = CreateRequest(
            VerificationPackageView.RestrictedOwnerAuditor,
            restrictedAccessAuthorized: true,
            profileId: VerificationProfileIds.RestrictedOwnerAuditorV1);
        var content = "{\"artifactSchemaId\":\"restricted-anomaly-intake-manifest-artifact-v1\"}";
        var artifact = ElectionModelFactory.CreateReportArtifact(
            request.ReportPackage!.Id,
            request.Election.ElectionId,
            ElectionReportArtifactKind.MachineRestrictedAnomalyIntakeManifest,
            ElectionReportArtifactFormat.Json,
            ElectionReportArtifactAccessScope.OwnerAuditorOnly,
            sortOrder: 14,
            title: "Restricted anomaly intake manifest",
            fileName: "restricted-anomaly-intake-manifest.json",
            mediaType: "application/json",
            contentHash: SHA256.HashData(Encoding.UTF8.GetBytes(content)),
            content: content);

        var result = Export(request with
        {
            ReportArtifacts = [.. request.ReportArtifacts, artifact],
        });

        result.Success.Should().BeTrue();
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
        var manifest = ReadFile<AuditPackageManifestRecord>(result, VerificationPackageFileNames.AuditPackageManifest);
        manifest.Entries.Should().Contain(x =>
            x.Path == VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest &&
            x.Visibility == VerificationArtifactVisibility.Restricted);
    }

    [Fact]
    public void Export_RestrictedPackageWithRegulatoryClaim_ShouldIncludeRestrictedWorkpaper()
    {
        var result = Export(CreateRequest(
            VerificationPackageView.RestrictedOwnerAuditor,
            restrictedAccessAuthorized: true,
            profileId: VerificationProfileIds.RestrictedOwnerAuditorV1) with
        {
            Sp11RegulatoryClaimState = CreateRegulatoryClaimState(restrictedWorkpaperRef: "restricted-workpaper-hash"),
        });

        result.Success.Should().BeTrue();
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.Sp11RegulatoryClaimState &&
            x.Visibility == VerificationArtifactVisibility.Public);
        result.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp11RegulatoryJurisdictionWorkpaper &&
            x.Visibility == VerificationArtifactVisibility.Restricted);

        var workpaper = ReadFile<ElectionSp11RestrictedJurisdictionWorkpaperArtifactRecord>(
            result,
            VerificationPackageFileNames.RestrictedSp11RegulatoryJurisdictionWorkpaper);
        workpaper.WorkpaperHash.Should().Be("restricted-workpaper-hash");
        workpaper.ReviewState.Should().Be(ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation);
    }

    [Fact]
    public void Export_PublicPackageWithSp07Evidence_ShouldIncludePublicationProofArtifacts()
    {
        var request = CreateHighAssuranceTrusteeRequest();
        var witnessSetId = Guid.NewGuid();
        var session = CreatePublicationProofSession(request, witnessSetId);
        var transcript = CreatePublicationProofTranscript(request, session);
        var deletionReceipt = CreatePublicationWitnessDeletionReceipt(request, session, transcript);
        request = request with
        {
            PublicationProofSessions = [session],
            PublicationProofTranscripts = [transcript],
            PublicationWitnessDeletionReceipts = [deletionReceipt],
        };

        var result = Export(request);

        result.Success.Should().BeTrue();
        result.Files.Select(x => x.RelativePath).Should().Contain([
            VerificationPackageFileNames.Sp07PublicationProofTranscript,
            VerificationPackageFileNames.Sp07PublicationProofVerifierOutput,
            VerificationPackageFileNames.Sp07WitnessDeletionReceipt,
        ]);

        var tallyReplay = ReadFile<TallyReplayArtifactRecord>(result, VerificationPackageFileNames.TallyReplay);
        tallyReplay.EvidenceStatus.Should().Be(VerificationCheckStatus.Pass);
        tallyReplay.ResultCode.Should().Be(VerificationResultCodes.PublicationProofEvidenceValid);
        tallyReplay.PublicationProofTranscriptHash.Should().Be(transcript.TranscriptHash);

        var verifierOutput = ReadFile<ElectionSp07VerifierOutputArtifactRecord>(
            result,
            VerificationPackageFileNames.Sp07PublicationProofVerifierOutput);
        verifierOutput.Results.Should().ContainSingle(x =>
            x.CheckCode == "VFY-SP07-000" &&
            x.Status == VerificationCheckStatus.Pass &&
            x.ResultCode == VerificationResultCodes.PublicationProofEvidenceValid);

        var publicPayload = string.Join(
            '\n',
            result.Files
                .Where(x => x.Visibility == VerificationArtifactVisibility.Public)
                .Select(x => x.ContentText));
        publicPayload.Should().NotContain("hiddenPermutation");
        publicPayload.Should().NotContain("shuffleMap");
        publicPayload.Should().NotContain("rerandomizationRandomness");
        publicPayload.Should().NotContain("sealedWitnessMaterial");
        publicPayload.Should().NotContain("plaintextVote");
    }

    [Fact]
    public void Export_BeforeFinalization_ShouldFailWithoutMutatingPackageFiles()
    {
        var request = CreateRequest(VerificationPackageView.PublicAnonymous);
        request = request with
        {
            Election = request.Election with
            {
                LifecycleState = ElectionLifecycleState.Closed,
            },
        };

        var result = Export(request);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(VerificationResultCodes.ElectionNotFinalized);
        result.Files.Should().BeEmpty();
    }

    private static ElectionVerificationPackageExportResult Export(
        ElectionVerificationPackageExportRequest request) =>
        new ElectionVerificationPackageExportService().Export(request);

    private static T ReadFile<T>(ElectionVerificationPackageExportResult result, string path)
    {
        var file = result.Files.Single(x => x.RelativePath == path);
        return JsonSerializer.Deserialize<T>(file.Content, VerificationJson.Options)!;
    }

    internal static ElectionVerificationPackageExportRequest CreateRequest(
        VerificationPackageView view,
        bool restrictedAccessAuthorized = false,
        string profileId = VerificationProfileIds.DevelopmentCurrentV1)
    {
        var electionId = ElectionId.NewElectionId;
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "Verifier package election",
            shortDescription: "FEAT-113 test",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-113",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: CreatePassFailRule(),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushvoting", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.1.1",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject", 2, false),
            ],
            officialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

        var openedAt = DateTime.UtcNow.AddMinutes(-10);
        var ballotDefinitionSeal = ElectionModelFactory.CreateBallotDefinitionSeal(
            ElectionBallotDefinitionCanonicalizer.CurrentVersion,
            ElectionBallotDefinitionCanonicalizer.ComputeHash(draftElection),
            openedAt);
        var sealedElection = draftElection with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            BallotDefinitionVersion = ballotDefinitionSeal.BallotDefinitionVersion,
            BallotDefinitionHash = ballotDefinitionSeal.BallotDefinitionHash,
            BallotDefinitionSealedAt = ballotDefinitionSeal.SealedAt,
            BallotDefinitionMutationPolicy = ballotDefinitionSeal.MutationPolicy,
        };

        var voter1FinalPreparedId = Guid.NewGuid();
        var voter2FinalPreparedId = Guid.NewGuid();
        var acceptedBallots = new[]
        {
            ElectionModelFactory.CreateAcceptedBallotRecord(
                electionId,
                "ballot-a",
                "proof-a",
                "nullifier-a",
                preparedBallotId: voter1FinalPreparedId,
                preparedBallotHash: "prepared-final-a",
                receiptCommitment: "receipt-a",
                receiptCommitmentScheme: "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)",
                ballotDefinitionVersion: ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionHash: ballotDefinitionSeal.BallotDefinitionHash),
            ElectionModelFactory.CreateAcceptedBallotRecord(
                electionId,
                "ballot-b",
                "proof-b",
                "nullifier-b",
                preparedBallotId: voter2FinalPreparedId,
                preparedBallotHash: "prepared-final-b",
                receiptCommitment: "receipt-b",
                receiptCommitmentScheme: "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)",
                ballotDefinitionVersion: ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionHash: ballotDefinitionSeal.BallotDefinitionHash),
        };
        var voter1SpoiledPreparedId = Guid.NewGuid();
        var voter2SpoiledPreparedId = Guid.NewGuid();
        var spoiledPreparedBallots = new[]
        {
            ElectionModelFactory.CreateSpoiledPreparedBallotRecord(
                electionId,
                voter1SpoiledPreparedId,
                "prepared-spoiled-a",
                "spoiled-transcript-a",
                "spoil-record-a",
                "local-verifier-v1",
                openedAt.AddMinutes(2)),
            ElectionModelFactory.CreateSpoiledPreparedBallotRecord(
                electionId,
                voter2SpoiledPreparedId,
                "prepared-spoiled-b",
                "spoiled-transcript-b",
                "spoil-record-b",
                "local-verifier-v1",
                openedAt.AddMinutes(2)),
        };
        var preparedBallots = new[]
        {
            ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
                electionId,
                "voter-1",
                "actor-voter-1",
                "prepared-spoiled-a",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                "sp04-proof",
                openedAt.AddMinutes(1),
                preparedBallotId: voter1SpoiledPreparedId) with
            {
                State = ElectionPreparedBallotState.Spoiled,
                SpoilMarkerId = spoiledPreparedBallots[0].Id,
                SpoiledAt = spoiledPreparedBallots[0].SpoiledAt,
            },
            ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
                electionId,
                "voter-1",
                "actor-voter-1",
                "prepared-final-a",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                "sp04-proof",
                openedAt.AddMinutes(3),
                preparedBallotId: voter1FinalPreparedId) with
            {
                State = ElectionPreparedBallotState.Cast,
                AcceptedBallotId = acceptedBallots[0].Id,
                CastAt = openedAt.AddMinutes(4),
            },
            ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
                electionId,
                "voter-2",
                "actor-voter-2",
                "prepared-spoiled-b",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                "sp04-proof",
                openedAt.AddMinutes(1),
                preparedBallotId: voter2SpoiledPreparedId) with
            {
                State = ElectionPreparedBallotState.Spoiled,
                SpoilMarkerId = spoiledPreparedBallots[1].Id,
                SpoiledAt = spoiledPreparedBallots[1].SpoiledAt,
            },
            ElectionModelFactory.CreatePreparedBallotCommitmentRecord(
                electionId,
                "voter-2",
                "actor-voter-2",
                "prepared-final-b",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                "sp04-proof",
                openedAt.AddMinutes(3),
                preparedBallotId: voter2FinalPreparedId) with
            {
                State = ElectionPreparedBallotState.Cast,
                AcceptedBallotId = acceptedBallots[1].Id,
                CastAt = openedAt.AddMinutes(4),
            },
        };
        var ceremonies = new[]
        {
            ElectionModelFactory.CreateVoterCeremonyRecord(
                electionId,
                "voter-1",
                "actor-voter-1",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                createdAt: openedAt.AddMinutes(1)) with
            {
                PreparedPackageCount = 2,
                SpoiledPackageCount = 1,
                FinalState = ElectionVoterCeremonyFinalState.FinalCastAccepted,
                FinalAcceptedBallotId = acceptedBallots[0].Id,
                LastUpdatedAt = openedAt.AddMinutes(4),
            },
            ElectionModelFactory.CreateVoterCeremonyRecord(
                electionId,
                "voter-2",
                "actor-voter-2",
                ballotDefinitionSeal.BallotDefinitionVersion,
                ballotDefinitionSeal.BallotDefinitionHash,
                createdAt: openedAt.AddMinutes(1)) with
            {
                PreparedPackageCount = 2,
                SpoiledPackageCount = 1,
                FinalState = ElectionVoterCeremonyFinalState.FinalCastAccepted,
                FinalAcceptedBallotId = acceptedBallots[1].Id,
                LastUpdatedAt = openedAt.AddMinutes(4),
            },
        };
        var publishedBallots = new[]
        {
            ElectionModelFactory.CreatePublishedBallotRecord(electionId, 1, "published-a", "proof-a"),
            ElectionModelFactory.CreatePublishedBallotRecord(electionId, 2, "published-b", "proof-b"),
        };
        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            sealedElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: acceptedBallots.Length,
            acceptedBallotSetHash: VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(acceptedBallots),
            publishedBallotCount: publishedBallots.Length,
            publishedBallotStreamHash: VerificationCanonicalHash.ComputePublishedBallotStreamHash(publishedBallots),
            finalEncryptedTallyHash: HashBytes("tally"));
        var tallyReadyArtifactId = Guid.NewGuid();
        var officialResultArtifactId = Guid.NewGuid();
        var unofficialResultArtifactId = Guid.NewGuid();
        var finalizeArtifactId = Guid.NewGuid();
        var finalizedElection = sealedElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = DateTime.UtcNow.AddMinutes(-5),
            FinalizedAt = DateTime.UtcNow,
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = tallyReadyArtifactId,
            OfficialResultArtifactId = officialResultArtifactId,
            UnofficialResultArtifactId = unofficialResultArtifactId,
            FinalizeArtifactId = finalizeArtifactId,
        };
        var binding = CreateSealedProtocolBinding(electionId, profileId);
        var reportPackage = ElectionModelFactory.CreateSealedReportPackage(
            electionId,
            attemptNumber: 1,
            tallyReadyArtifactId,
            unofficialResultArtifactId,
            officialResultArtifactId,
            finalizeArtifactId,
            frozenEvidenceHash: HashBytes("frozen"),
            frozenEvidenceFingerprint: "sha256:frozen",
            packageHash: HashBytes("report-package"),
            artifactCount: 1,
            attemptedByPublicAddress: "owner-address",
            closeBoundaryArtifactId: closeArtifact.Id);
        var reportArtifact = ElectionModelFactory.CreateReportArtifact(
            reportPackage.Id,
            electionId,
            ElectionReportArtifactKind.MachineManifest,
            ElectionReportArtifactFormat.Json,
            ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
            sortOrder: 1,
            title: "Machine manifest",
            fileName: "canonical-manifest.json",
            mediaType: "application/json",
            contentHash: HashBytes("{\"ok\":true}"),
            content: "{\"ok\":true}");
        var rosterEntries = new[]
        {
            CreateRosterEntry(electionId, "voter-1", "actor-voter-1"),
            CreateRosterEntry(electionId, "voter-2", "actor-voter-2"),
        };
        var participationRecords = new[]
        {
            ElectionModelFactory.CreateParticipationRecord(
                electionId,
                "voter-1",
                ElectionParticipationStatus.CountedAsVoted,
                recordedAt: DateTime.UtcNow),
            ElectionModelFactory.CreateParticipationRecord(
                electionId,
                "voter-2",
                ElectionParticipationStatus.CountedAsVoted,
                recordedAt: DateTime.UtcNow),
        };
        var commitmentRegistrations = new[]
        {
            ElectionModelFactory.CreateCommitmentRegistrationRecord(
                electionId,
                "voter-1",
                "actor-voter-1",
                "commitment-a",
                registeredAt: openedAt.AddMinutes(3)),
            ElectionModelFactory.CreateCommitmentRegistrationRecord(
                electionId,
                "voter-2",
                "actor-voter-2",
                "commitment-b",
                registeredAt: openedAt.AddMinutes(3)),
        };
        var checkoffConsumptions = new[]
        {
            ElectionModelFactory.CreateCheckoffConsumptionRecord(
                electionId,
                "voter-1",
                consumedAt: openedAt.AddMinutes(4)),
            ElectionModelFactory.CreateCheckoffConsumptionRecord(
                electionId,
                "voter-2",
                consumedAt: openedAt.AddMinutes(4)),
        };
        var rosterCanonicalHash = ElectionEligibilityContracts.ComputeRosterCanonicalHash(rosterEntries);

        return new ElectionVerificationPackageExportRequest(
            finalizedElection,
            binding,
            reportPackage,
            [reportArtifact],
            [closeArtifact],
            acceptedBallots,
            publishedBallots,
            FinalizationSessions: [],
            FinalizationShares: [],
            ReleaseEvidenceRecords: [],
            RosterEntries: rosterEntries,
            ParticipationRecords: participationRecords,
            view,
            profileId,
            restrictedAccessAuthorized,
            ExportedAt: DateTime.UnixEpoch,
            VoterCeremonyRecords: ceremonies,
            PreparedBallotCommitments: preparedBallots,
            SpoiledPreparedBallots: spoiledPreparedBallots,
            RosterImportEvidences:
            [
                ElectionModelFactory.CreateRosterImportEvidence(
                    electionId,
                    rosterImportVersion: 1,
                    rosterSourceFileHash: HashHex("source-roster"),
                    rosterCanonicalHash,
                    ElectionSp05ProfileIds.RosterCanonicalizationV1,
                    ElectionEligibilityContracts.RosterCanonicalizationVersionHash,
                    acceptedRowCount: 2,
                    rejectedRowCount: 0,
                    invalidRowRejectionCount: 0,
                    duplicateIdRejectionCount: 0,
                    duplicateContactWarningCount: 0,
                    importedByActor: "owner-address",
                    importedAt: openedAt)
            ],
            EligibilityPolicyEvidences:
            [
                ElectionModelFactory.CreateEligibilityPolicyEvidence(
                    electionId,
                    eligibilityPolicyVersion: "1.0.0",
                    EligibilityMutationPolicy.FrozenAtOpen,
                    ElectionIdentityLinkPolicy.ContactCodeV1,
                    ElectionCheckoffVisibilityPolicy.RestrictedOwnerAuditor,
                    ElectionActorLinkMultiplicityPolicy.SingleRosterEntryPerActor,
                    ElectionContactCodeProviderReadiness.Ready,
                    ElectionEligibilityContracts.EligibilityPolicyCanonicalizationVersionHash,
                    declaredByActor: "owner-address",
                    declaredAt: openedAt)
            ],
            CommitmentSchemeEvidences:
            [
                ElectionModelFactory.CreateCommitmentSchemeEvidence(
                    electionId,
                    ElectionEligibilityContracts.CommitmentSchemeVersionHash,
                    ElectionEligibilityContracts.NullifierSchemeVersionHash,
                    ElectionEligibilityContracts.RosterCanonicalizationVersionHash,
                    ElectionEligibilityContracts.EligibilityPolicyCanonicalizationVersionHash,
                    declaredByActor: "owner-address",
                    declaredAt: openedAt)
            ],
            CommitmentRegistrations: commitmentRegistrations,
            CheckoffConsumptions: checkoffConsumptions,
            EligibilityActivationEvents: []);
    }

    internal static ElectionVerificationPackageExportRequest CreateHighAssuranceTrusteeRequest()
    {
        var request = CreateRequest(
            VerificationPackageView.PublicAnonymous,
            profileId: VerificationProfileIds.HighAssuranceV1);
        var trusteeElection = request.Election with
        {
            GovernanceMode = ElectionGovernanceMode.TrusteeThreshold,
            SelectedProfileId = ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            SelectedProfileDevOnly = false,
            RequiredApprovalCount = 3,
        };
        var trustees = Enumerable.Range(1, 5)
            .Select(x => new ElectionTrusteeReference($"trustee-{x}@hush.test", $"Trustee {x}"))
            .ToArray();
        var ceremonySnapshot = ElectionModelFactory.CreateCeremonyBindingSnapshot(
            Guid.NewGuid(),
            ceremonyVersionNumber: 1,
            ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
            boundTrusteeCount: 5,
            requiredApprovalCount: 3,
            trustees,
            tallyPublicKeyFingerprint: "tally-public-key-fingerprint",
            tallyPublicKey: [1, 2, 3, 4]);
        var closeArtifact = request.BoundaryArtifacts.Single(x => x.ArtifactType == ElectionBoundaryArtifactType.Close);
        var finalizationSession = ElectionModelFactory.CreateFinalizationSession(
            trusteeElection,
            closeArtifact.Id,
            closeArtifact.AcceptedBallotSetHash!,
            closeArtifact.FinalEncryptedTallyHash!,
            ElectionFinalizationSessionPurpose.CloseCounting,
            ceremonySnapshot,
            requiredShareCount: 3,
            trustees,
            createdByPublicAddress: "owner-address",
            createdAt: DateTime.UnixEpoch.AddHours(1));
        var shares = trustees.Take(3)
            .Select((trustee, index) => ElectionModelFactory.CreateAcceptedFinalizationShare(
                finalizationSession.Id,
                trusteeElection.ElectionId,
                trustee.TrusteeUserAddress,
                trustee.TrusteeDisplayName,
                trustee.TrusteeUserAddress,
                shareIndex: index + 1,
                shareVersion: "share-v1",
                ElectionFinalizationTargetType.AggregateTally,
                finalizationSession.CloseArtifactId,
                finalizationSession.AcceptedBallotSetHash,
                finalizationSession.FinalEncryptedTallyHash,
                finalizationSession.TargetTallyId,
                ceremonySnapshot.CeremonyVersionId,
                ceremonySnapshot.TallyPublicKeyFingerprint,
                shareMaterial: $"executor-encrypted-share-{index + 1}",
                executorKeyAlgorithm: "ecies-secp256k1-v1",
                submittedAt: DateTime.UnixEpoch.AddHours(2).AddMinutes(index)))
            .ToArray();
        var controlDomains = trustees
            .Select((trustee, index) => new ElectionTrusteeControlDomainRecord(
                Guid.NewGuid(),
                trusteeElection.ElectionId,
                ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1,
                ElectionSp06ProfileIds.HighAssuranceIndependentTrusteesV1Version,
                ElectionSelectableProfileCatalog.TrusteeProductionProfileId,
                ceremonySnapshot.CeremonyVersionId,
                TrusteeId: $"trustee-{index + 1:00}",
                TrusteeAccountId: trustee.TrusteeUserAddress,
                TrusteePersonRef: $"person-ref-{index + 1}",
                ElectionTrusteeRole.ExternalTrustee,
                CustodyMode: ElectionSp06ProfileIds.TrusteeLocalSecureVaultV1,
                CustodyDomainRefHash: $"custody-domain-hash-{index + 1}",
                AdminDomainRefHash: $"admin-domain-hash-{index + 1}",
                LegalEntityRefHash: null,
                PublicKeyCommitmentHash: $"public-key-commitment-{index + 1}",
                AcceptedAt: DateTime.UnixEpoch.AddMinutes(index),
                AcceptedBeforeOpen: true,
                ElectionTrusteeBackupStatus.Registered,
                ElectionTrusteeExceptionStatus.None,
                ElectionTrusteeControlDomainEvidenceStatus.Accepted,
                EvidenceFailureCode: null,
                EvidenceFailureReason: null,
                RecordedAt: DateTime.UnixEpoch.AddMinutes(index),
                RecordedByPublicAddress: "owner-address",
                SourceTransactionId: null,
                SourceBlockHeight: null,
                SourceBlockId: null))
            .ToArray();

        return request with
        {
            Election = trusteeElection,
            FinalizationSessions = [finalizationSession],
            FinalizationShares = shares,
            TrusteeControlDomainRecords = controlDomains,
        };
    }

    internal static ElectionVerificationPackageExportRequest WithOfficialSp08ReleaseManifest(
        ElectionVerificationPackageExportRequest request) =>
        request with
        {
            Sp08ReleaseManifest = CreateOfficialSp08ReleaseManifest(request),
        };

    internal static ElectionVerificationPackageExportRequest WithCompleteSp10OperationalSecurityStatus(
        ElectionVerificationPackageExportRequest request)
    {
        var requestWithRelease = request.Sp08ReleaseManifest is null
            ? WithOfficialSp08ReleaseManifest(request)
            : request;
        var releaseManifest = requestWithRelease.Sp08ReleaseManifest!;
        var releaseManifestHash = ElectionSp08ReleaseManifestHasher.ComputeReleaseManifestHash(releaseManifest);
        var serverComponent = releaseManifest.Components.Single(x =>
            string.Equals(x.ComponentId, ElectionSp08ProfileIds.ServerComponent, StringComparison.Ordinal));
        var evidenceState = ElectionSp10ProfileIds.EvidenceStateManagedProfileEvidenceAvailable;

        return requestWithRelease with
        {
            Sp10OperationalSecurityStatus = new ElectionSp10OperationalSecurityStatusArtifactRecord(
                Schema: ElectionSp10ProfileIds.OperationalSecuritySummarySchema,
                requestWithRelease.Election.ElectionId.ToString(),
                ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
                ElectionSp10ProfileIds.DeploymentProfileManagedAwsContainerV1,
                evidenceState,
                DoesNotCompleteFeat106Readiness: true,
                Feat106ReadinessCaveat: ElectionSp10OperationalSecurityRules.GetAllowedWordingForEvidenceState(evidenceState),
                ReleaseEvidenceMode: releaseManifest.EvidenceMode,
                ReleaseManifestHash: releaseManifestHash,
                ImmutableDeploymentRef: serverComponent.ImmutableReference,
                CustodyMode: requestWithRelease.Election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold
                    ? ElectionSp10ProfileIds.CustodyModeTrusteeLocalSecureVaultV1
                    : ElectionSp10ProfileIds.CustodyModeAwsKmsPerElectionEnvelopeV1,
                ExecutorKeyLifecycle: ElectionSp10ProfileIds.ExecutorKeyLifecycleEphemeralMemoryV1,
                AccessSnapshotHashOrRestrictedRef: "sha256:access-snapshot",
                BackupRestoreHashOrRestrictedRef: "sha256:backup-restore",
                IncidentStatus: ElectionSp10ProfileIds.IncidentStatusNoIncidentDeclared,
                AuditorRoomAccessLogHashOrRestrictedRef: "sha256:auditor-room-access-log",
                BlocksHighAssurance: false,
                PrimaryResultCode: VerificationResultCodes.OperationalSecurityEvidenceValid,
                PrimaryIssue: null,
                PublicEvidenceFiles:
                [
                    VerificationPackageFileNames.Sp10OperationalSecuritySummary,
                    VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
                    VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
                    VerificationPackageFileNames.Sp10OperationalVerifierOutput,
                ],
                RestrictedEvidenceFiles: [],
                PublicPrivacyBoundary:
                [
                    "no_raw_log_line",
                    "no_raw_audit_log",
                    "no_ip_address",
                    "no_device_id",
                    "no_kms_plaintext_key",
                    "no_kms_unwrapped_key",
                    "no_executor_private_key",
                    "no_iam_policy_document",
                    "no_security_group_rule_dump",
                    "no_raw_backup_archive",
                    "no_incident_workpaper",
                    "no_regulatory_workpaper",
                    "no_authority_private_correspondence",
                    "no_voter_detail",
                    "no_plaintext_vote",
                    "no_raw_trustee_share",
                    "no_proof_witness",
                ]),
        };
    }

    internal static ElectionSp11RegulatoryClaimStateArtifactRecord CreateSp11RegulatoryClaimState(
        string claimState = ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation,
        DateTimeOffset? sourceCheckedAt = null,
        DateTimeOffset? nextReviewAt = null,
        bool requiresAuthorityEvidence = false,
        string? authorityEvidenceRef = null,
        string? restrictedWorkpaperRef = null,
        string? allowedWording = null) =>
        new(
            Schema: ElectionSp11ProfileIds.RegulatoryClaimStateSchema,
            JurisdictionId: "CH",
            ClaimId: "organizational_remote_voting_market_intelligence",
            TrackerVersion: ElectionSp11ProfileIds.RegulatoryTrackerVersion,
            ClaimState: claimState,
            SourceCheckedAt: sourceCheckedAt ?? DateTimeOffset.UtcNow.AddDays(-1),
            NextReviewAt: nextReviewAt ?? DateTimeOffset.UtcNow.AddDays(30),
            SourceRef: "https://www.bk.admin.ch/bk/en/home/politische-rechte/e-voting.html",
            Owner: "protocol-omega-regulatory-tracker",
            IsLegalAdvice: false,
            RequiresAuthorityEvidence: requiresAuthorityEvidence,
            AuthorityEvidenceRef: authorityEvidenceRef,
            RestrictedWorkpaperRef: restrictedWorkpaperRef,
            AllowedWording: allowedWording ?? ElectionSp11RegulatoryRules.GetAllowedWordingForClaimState(claimState),
            PublicEvidenceFiles:
            [
                VerificationPackageFileNames.Sp11RegulatoryClaimState,
            ],
            RestrictedEvidenceFiles: [],
            PublicPrivacyBoundary:
            [
                "no_legal_advice",
                "no_authority_private_correspondence",
                "no_jurisdiction_workpaper_body",
            ]);

    internal static ElectionSp08ReleaseManifestArtifactRecord CreateOfficialSp08ReleaseManifest(
        ElectionVerificationPackageExportRequest request)
    {
        var releaseId = "release-2026.05.11";
        var sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var sourceTag = "hush-voting-2026.05.11";
        var serverDigest = Sp08Digest("server");
        var webDigest = Sp08Digest("web-client");
        var verifierDigest = Sp08Digest("standalone-verifier");
        var sp07Digest = Sp08Digest("sp07-worker");
        var protocolDigest = $"sha256:{request.ProtocolPackageBinding!.ReleaseManifestHash}";
        var exporterDigest = Sp08Digest("audit-package-exporter");

        return new ElectionSp08ReleaseManifestArtifactRecord(
            Schema: ElectionSp08ProfileIds.ReleaseManifestSchema,
            ManifestId: "release-manifest-2026-05-11",
            releaseId,
            ElectionSp08ProfileIds.EvidenceModeOfficial,
            NotForReleaseIntegrityClaims: false,
            GeneratedAt: DateTime.UnixEpoch,
            SourceAuthority: "github-actions",
            sourceCommit,
            sourceTag,
            Components:
            [
                CreateOfficialSp08Component(ElectionSp08ProfileIds.ServerComponent, serverDigest, sourceCommit, sourceTag),
                CreateOfficialSp08Component(ElectionSp08ProfileIds.WebClientComponent, webDigest, sourceCommit, sourceTag),
                CreateOfficialSp08Component(ElectionSp08ProfileIds.StandaloneVerifierComponent, verifierDigest, sourceCommit, sourceTag),
                CreateOfficialSp08Component(ElectionSp08ProfileIds.Sp07ProofWorkerComponent, sp07Digest, sourceCommit, sourceTag),
                CreateOfficialSp08Component(ElectionSp08ProfileIds.ProtocolPackageComponent, protocolDigest, sourceCommit, sourceTag),
                CreateOfficialSp08Component(ElectionSp08ProfileIds.AuditPackageExporterComponent, exporterDigest, sourceCommit, sourceTag),
            ],
            CircuitAndKeys:
            [
                new ElectionSp08CircuitKeyArtifactRecord(
                    CircuitId: "protocol-omega-publication-proof-v1",
                    CircuitHash: Sp08Digest("circuit"),
                    ProvingKeyHash: Sp08Digest("proving-key"),
                    VerifyingKeyHash: Sp08Digest("verifying-key"),
                    ProtocolPackageManifestHash: request.ProtocolPackageBinding.ReleaseManifestHash),
            ],
            LifecycleBindings:
            [
                CreateOfficialSp08Lifecycle(ElectionSp08ProfileIds.OpenLifecycleStage, releaseId, serverDigest),
                CreateOfficialSp08Lifecycle(ElectionSp08ProfileIds.CloseLifecycleStage, releaseId, serverDigest),
                CreateOfficialSp08Lifecycle(ElectionSp08ProfileIds.ProofWorkerLifecycleStage, releaseId, sp07Digest),
                CreateOfficialSp08Lifecycle(ElectionSp08ProfileIds.ExporterLifecycleStage, releaseId, exporterDigest),
                CreateOfficialSp08Lifecycle(ElectionSp08ProfileIds.ClientReleaseSetLifecycleStage, releaseId, webDigest),
            ],
            PublicPrivacyBoundary:
            [
                "no_private_host_state",
                "no_per_voter_device_identifier",
                "no_raw_attestation_token",
                "no_ip_address",
            ]);
    }

    private static ElectionSp08ReleaseComponentArtifactRecord CreateOfficialSp08Component(
        string componentId,
        string digest,
        string sourceCommit,
        string sourceTag) =>
        new(
            componentId,
            componentId,
            ElectionSp08ProfileIds.EvidenceModeOfficial,
            $"{componentId}.artifact",
            digest,
            sourceCommit,
            sourceTag,
            $"{componentId}@{digest}",
            BuildWorkflowRunId: "1234567890",
            DistributionReference: null,
            SigningFingerprint: null,
            IsPlaceholder: false);

    private static ElectionSp08LifecycleReleaseBindingRecord CreateOfficialSp08Lifecycle(
        string lifecycleStage,
        string releaseId,
        string digest) =>
        new(
            lifecycleStage,
            releaseId,
            releaseId,
            digest,
            digest,
            MatchesSealedPolicy: true);

    private static ElectionSp11RegulatoryClaimStateArtifactRecord CreateRegulatoryClaimState(
        string? restrictedWorkpaperRef = null) =>
        new(
            ElectionSp11ProfileIds.RegulatoryClaimStateSchema,
            JurisdictionId: "CH",
            ClaimId: "organizational-remote-voting-fit",
            TrackerVersion: ElectionSp11ProfileIds.RegulatoryTrackerVersion,
            ClaimState: ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation,
            SourceCheckedAt: DateTimeOffset.UnixEpoch.AddDays(10),
            NextReviewAt: DateTimeOffset.UtcNow.AddDays(30),
            SourceRef: "https://www.bk.admin.ch/bk/en/home/politische-rechte/e-voting.html",
            Owner: "regulatory-tracker-owner",
            IsLegalAdvice: false,
            RequiresAuthorityEvidence: false,
            AuthorityEvidenceRef: null,
            RestrictedWorkpaperRef: restrictedWorkpaperRef,
            AllowedWording: ElectionSp11RegulatoryRules.GetAllowedWordingForClaimState(
                ElectionSp11ProfileIds.ClaimStateAllowedWithLimitation),
            PublicEvidenceFiles:
            [
                VerificationPackageFileNames.Sp11RegulatoryClaimState,
            ],
            RestrictedEvidenceFiles: restrictedWorkpaperRef is null
                ? []
                : [VerificationPackageFileNames.RestrictedSp11RegulatoryJurisdictionWorkpaper],
            PublicPrivacyBoundary:
            [
                "no_legal_advice",
                "no_authority_private_correspondence",
                "no_jurisdiction_workpaper_body",
            ]);

    private static ElectionPublicationProofSessionRecord CreatePublicationProofSession(
        ElectionVerificationPackageExportRequest request,
        Guid witnessSetId) =>
        new(
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
            VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(request.AcceptedBallots)),
            VerificationCanonicalHash.ToLowerHex(
                VerificationCanonicalHash.ComputePublishedBallotStreamHash(request.PublishedBallots)),
            TranscriptHash: "sp07-transcript-hash",
            ProofHash: HashHex("synthetic-proof-bytes"),
            ServerVerifierOutputHash: "sp07-server-verifier-output-hash",
            DeletionReceiptId: null);

    private static ElectionPublicationProofTranscriptRecord CreatePublicationProofTranscript(
        ElectionVerificationPackageExportRequest request,
        ElectionPublicationProofSessionRecord session) =>
        new(
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
            ProofBytes: "synthetic-proof-bytes",
            session.ProofHash!,
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

    private static ElectionPublicationWitnessDeletionReceiptRecord CreatePublicationWitnessDeletionReceipt(
        ElectionVerificationPackageExportRequest request,
        ElectionPublicationProofSessionRecord session,
        ElectionPublicationProofTranscriptRecord transcript) =>
        new(
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

    private static ProtocolPackageBindingRecord CreateSealedProtocolBinding(
        ElectionId electionId,
        string profileId)
    {
        var accessLocation = ElectionModelFactory.CreateProtocolPackageAccessLocation(
            ProtocolPackageAccessLocationKind.Repository,
            "Repository",
            "https://example.test/protocol",
            HashHex("access"));
        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            "omega-hushvoting-v1",
            "v1.1.1",
            HashHex("spec"),
            HashHex("proof"),
            HashHex("release"),
            [profileId],
            ProtocolPackageApprovalStatus.DraftPrivate,
            isLatestForCompatibleProfiles: true,
            [accessLocation],
            [accessLocation]);

        return ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
                electionId,
                catalogEntry,
                profileId,
                draftRevision: 1,
                boundByPublicAddress: "owner-address")
            .SealAtOpen(DateTime.UtcNow, "owner-address");
    }

    private static ElectionRosterEntryRecord CreateRosterEntry(
        ElectionId electionId,
        string organizationVoterId,
        string actorPublicAddress) =>
        new(
            electionId,
            organizationVoterId,
            ElectionRosterContactType.Email,
            $"{organizationVoterId}@example.test",
            ElectionVoterLinkStatus.Linked,
            actorPublicAddress,
            DateTime.UtcNow,
            ElectionVotingRightStatus.Active,
            DateTime.UtcNow,
            WasPresentAtOpen: true,
            WasActiveAtOpen: true,
            LastActivatedAt: DateTime.UtcNow,
            LastActivatedByPublicAddress: "owner-address",
            LastUpdatedAt: DateTime.UtcNow,
            LatestTransactionId: null,
            LatestBlockHeight: null,
            LatestBlockId: null);

    private static OutcomeRuleDefinition CreatePassFailRule() =>
        new(
            OutcomeRuleKind.PassFail,
            TemplateKey: "pass-fail-simple-majority",
            SeatCount: 1,
            BlankVoteCountsForTurnout: true,
            BlankVoteExcludedFromWinnerSelection: true,
            BlankVoteExcludedFromThresholdDenominator: true,
            TieResolutionRule: "reject-on-tie",
            CalculationBasis: "counted-votes");

    private static byte[] HashBytes(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string HashHex(string value) =>
        Convert.ToHexString(HashBytes(value)).ToLowerInvariant();

    private static string Sp08Digest(string value) =>
        $"sha256:{HashHex($"sp08:{value}")}";
}
