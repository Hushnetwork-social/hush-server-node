using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushNode.Reactions.Crypto;
using HushServerNode;
using HushServerNode.Testing;
using HushServerNode.Testing.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Olimpo;
using ReactionECPoint = HushShared.Reactions.Model.ECPoint;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("Integration Tests")]
[Trait("Category", "FEAT-102")]
public sealed class ElectionReportPackageIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly TestIdentity Delta = TestIdentities.GenerateFromSeed("TEST_DELTA_V1", "Delta");
    private static readonly TestIdentity Echo = TestIdentities.GenerateFromSeed("TEST_ECHO_V1", "Echo");
    private static readonly TestIdentity Foxtrot = TestIdentities.GenerateFromSeed("TEST_FOXTROT_V1", "Foxtrot");
    private static readonly TestIdentity Guest = TestIdentities.GenerateFromSeed("TEST_GUEST_V1", "Guest");
    private const string Sp04ReceiptCommitmentScheme = "hushvoting-sp04-receipt-commitment-sha256-v1";
    private const string Sp04ProofStatementId = "hushvoting-sp04-prepared-ballot-proof-v1";
    private const string Sp04LocalVerifierVersion = "hushvoting-local-sp04-verifier-v1";
    private static readonly IReadOnlyList<TestIdentity> RolloutTrustees =
    [
        TestIdentities.Bob,
        TestIdentities.Charlie,
        Delta,
        Echo,
        Foxtrot,
    ];
    private static readonly string[] BindingBallotLeakMarkers =
    [
        "election-dev-mode-v1",
        "dev-protected-ballot",
        "plaintext-choice-projection",
        "selectionFingerprint",
        "omega-binding-ballot-v1",
        "omega-binding-proof-v1",
        "binding-circuit-envelope",
    ];

    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private BlockProductionControl? _blockControl;
    private GrpcClientFactory? _grpcFactory;

    public async Task InitializeAsync()
    {
        _fixture = new HushTestFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await DisposeNodeAsync();

        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task FinalizeElection_WithSealedPackage_ExposesRoleScopedArtifactsAcrossResultViews()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-102 Sealed Review Package");

        var finalizeProposalId = await StartGovernedProposalAsync(
            client,
            context.ElectionId,
            ElectionGovernedActionType.Finalize);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, Delta);

        var finalizedElection = await ReloadElectionAsync(client, context.ElectionId, TestIdentities.Alice);
        finalizedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);
        finalizedElection.Election.FinalizeArtifactId.Should().NotBeNullOrWhiteSpace();
        finalizedElection.Election.OfficialResultArtifactId.Should().NotBeNullOrWhiteSpace();

        var ownerResult = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            waitForOfficialResult: true);
        ownerResult.CanViewReportPackage.Should().BeTrue();
        ownerResult.CanRetryFailedPackageFinalization.Should().BeFalse();
        ownerResult.LatestReportPackage.Should().NotBeNull();
        ownerResult.LatestReportPackage!.Status.Should().Be(ElectionReportPackageStatusProto.ReportPackageSealed);
        ownerResult.LatestReportPackage.ArtifactCount.Should().Be(13);
        ownerResult.VisibleReportArtifacts.Should().HaveCount(13);
        ownerResult.VisibleReportArtifacts.Should().Contain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanNamedParticipationRoster);
        ownerResult.VisibleReportArtifacts.Should().Contain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactMachineNamedParticipationRosterProjection);
        var ownerManifestArtifact = ownerResult.VisibleReportArtifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanManifest);
        ownerManifestArtifact.Content.Should().Contain("Binding status");
        ownerManifestArtifact.Content.Should().Contain("Non-binding election: `no`");
        ownerManifestArtifact.Content.Should().Contain("Circuit class: `Production`");
        ownerManifestArtifact.Content.Should().Contain("Profile family");
        ownerManifestArtifact.Content.Should().Contain("Secrecy boundary");
        ownerManifestArtifact.Content.Should().Contain("Custody boundary");
        ownerManifestArtifact.Content.Should().Contain("dkg-prod-3of5");
        var ownerResultReportArtifact = ownerResult.VisibleReportArtifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanResultReport);
        ownerResultReportArtifact.Content.Should().Contain("Binding status");
        ownerResultReportArtifact.Content.Should().Contain("Non-binding election: `no`");
        ownerResultReportArtifact.Content.Should().Contain("Circuit class: `Production`");
        ownerResultReportArtifact.Content.Should().Contain("TrusteeThreshold");
        ownerResultReportArtifact.Content.Should().Contain("production-like ceremony profiles");
        ownerResultReportArtifact.Content.Should().Contain("protected-ballot path");
        var ownerRosterArtifact = ownerResult.VisibleReportArtifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanNamedParticipationRoster);
        ownerRosterArtifact.Content.Should().Contain("Binding status: `Binding`");
        ownerRosterArtifact.Content.Should().Contain("Non-binding election: `no`");
        ownerRosterArtifact.Content.Should().Contain("Circuit class: `Production`");
        var ownerAuditArtifact = ownerResult.VisibleReportArtifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanAuditProvenanceReport);
        ownerAuditArtifact.Content.Should().Contain("AllTrusteesRequiredFragility");
        ownerAuditArtifact.Content.Should().Contain("Tally public key fingerprint");
        ownerAuditArtifact.Content.Should().Contain("Binding status");
        ownerAuditArtifact.Content.Should().Contain("Non-binding election: `no`");
        ownerAuditArtifact.Content.Should().Contain("Circuit class: `Production`");
        ownerAuditArtifact.Content.Should().Contain("Profile family");
        ownerAuditArtifact.Content.Should().Contain("Secrecy boundary");
        ownerAuditArtifact.Content.Should().Contain("Custody boundary");
        ownerAuditArtifact.Content.Should().Contain("Governed Finalization Approvals");
        ownerAuditArtifact.Content.Should().Contain("Finalization Share Evidence");
        ownerAuditArtifact.Content.Should().Contain("Official result hash");
        var ownerOutcomeArtifact = ownerResult.VisibleReportArtifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanOutcomeDetermination);
        ownerOutcomeArtifact.Content.Should().Contain("Binding status: `Binding`");
        ownerOutcomeArtifact.Content.Should().Contain("Non-binding election: `no`");
        ownerOutcomeArtifact.Content.Should().Contain("Circuit class: `Production`");
        var ownerDisputeArtifact = ownerResult.VisibleReportArtifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanDisputeReviewIndex);
        ownerDisputeArtifact.Content.Should().Contain("Binding status: `Binding`");
        ownerDisputeArtifact.Content.Should().Contain("Non-binding election: `no`");
        ownerDisputeArtifact.Content.Should().Contain("Circuit class: `Production`");

        var trusteeResult = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            TestIdentities.Bob,
            waitForOfficialResult: true);
        trusteeResult.CanViewReportPackage.Should().BeTrue();
        trusteeResult.VisibleReportArtifacts.Should().HaveCount(11);
        trusteeResult.VisibleReportArtifacts.Should().NotContain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanNamedParticipationRoster);
        trusteeResult.VisibleReportArtifacts.Should().NotContain(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactMachineNamedParticipationRosterProjection);
        trusteeResult.VisibleReportArtifacts.Should().OnlyContain(x =>
            x.AccessScope == ElectionReportArtifactAccessScopeProto.ReportArtifactOwnerAuditorTrustee);

        var participantResult = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            Guest,
            waitForOfficialResult: true);
        participantResult.CanViewReportPackage.Should().BeFalse();
        participantResult.CanRetryFailedPackageFinalization.Should().BeFalse();
        participantResult.VisibleReportArtifacts.Should().BeEmpty();
        participantResult.OfficialResult.Should().NotBeNull();

        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var packages = await dbContext.Set<ElectionReportPackageRecord>()
            .Where(x => x.ElectionId == new ElectionId(Guid.Parse(context.ElectionId)))
            .OrderBy(x => x.AttemptNumber)
            .ToListAsync();
        packages.Should().ContainSingle();
        packages.Single().Status.Should().Be(ElectionReportPackageStatus.Sealed);
        packages.Single().ArtifactCount.Should().Be(13);

        var artifacts = await dbContext.Set<ElectionReportArtifactRecord>()
            .Where(x => x.ReportPackageId == packages.Single().Id)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();
        artifacts.Should().HaveCount(13);
        artifacts.Should().Contain(x => x.ArtifactKind == ElectionReportArtifactKind.HumanManifest);
        artifacts.Should().Contain(x => x.ArtifactKind == ElectionReportArtifactKind.MachineEvidenceGraph);
        artifacts.Should().Contain(x => x.ArtifactKind == ElectionReportArtifactKind.HumanNamedParticipationRoster);
    }

    [Fact]
    [Trait("Category", "FEAT-128")]
    [Trait("Category", "TwinTest")]
    [Trait("Category", "NON_E2E")]
    public async Task FinalizeElection_WithSignedAnomalyEvidence_IncludesRestrictedManifestArtifact()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-128 Report Package Handoff",
            castSubmissionIdempotencyKey: "feat128-package-handoff-cast");
        var electionId = new ElectionId(Guid.Parse(context.ElectionId));
        var (_, attachmentManifestId, encryptedPayloadReference) = await RecordReadyAnomalyEvidenceAsync(electionId);

        var finalizeProposalId = await StartGovernedProposalAsync(
            client,
            context.ElectionId,
            ElectionGovernedActionType.Finalize);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, Delta);

        var finalizedElection = await ReloadElectionAsync(client, context.ElectionId, TestIdentities.Alice);
        finalizedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);

        var ownerResult = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            waitForOfficialResult: true);
        ownerResult.LatestReportPackage.Should().NotBeNull();
        ownerResult.LatestReportPackage!.Status.Should().Be(ElectionReportPackageStatusProto.ReportPackageSealed);
        ownerResult.LatestReportPackage.ArtifactCount.Should().Be(14);

        var anomalyArtifact = ownerResult.VisibleReportArtifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactMachineRestrictedAnomalyIntakeManifest);
        anomalyArtifact.AccessScope.Should().Be(ElectionReportArtifactAccessScopeProto.ReportArtifactOwnerAuditorOnly);
        anomalyArtifact.Content.Should().Contain("\"artifactSchemaId\": \"restricted-anomaly-intake-manifest-artifact-v1\"");
        anomalyArtifact.Content.Should().Contain("\"scopeId\": \"package\"");
        anomalyArtifact.Content.Should().Contain("\"packageReadinessStatusId\": \"ready\"");
        anomalyArtifact.Content.Should().Contain($"\"attachmentManifestId\": \"{attachmentManifestId}\"");
        anomalyArtifact.Content.Should().Contain($"\"encryptedPayloadReference\": \"{encryptedPayloadReference}\"");
        anomalyArtifact.Content.Should().Contain("\"redactionCount\": 1");

        var publicSummary = ownerResult.PublicAnomalySummary;
        publicSummary.Should().NotBeNull();
        publicSummary!.SchemaId.Should().Be(ElectionAnomalyPublicSummarySchemaIds.Current);
        publicSummary.SuppressionPolicyId.Should().Be(ElectionAnomalyPublicSummarySuppressionPolicyIds.Current);
        publicSummary.TotalThreadCountMode.Should().Be(ElectionAnomalyPublicSummaryCountModeIds.Suppressed);
        publicSummary.HasTotalThreadCount.Should().BeFalse();
        publicSummary.SuppressedThreadCount.Should().Be(1);
        publicSummary.SuppressionReasonIds.Should().Contain([
            ElectionAnomalyPublicSummarySuppressionReasonIds.AggregationNotSafe,
            ElectionAnomalyPublicSummarySuppressionReasonIds.LowCountCategory,
            ElectionAnomalyPublicSummarySuppressionReasonIds.SmallElectionIdentifiability,
        ]);
        publicSummary.RestrictedManifestArtifactId.Should().Be(anomalyArtifact.Id);
        publicSummary.HasRestrictedManifestHash.Should().BeTrue();
        var summaryBucket = publicSummary.VisibleBuckets.Should().ContainSingle().Subject;
        summaryBucket.CountMode.Should().Be(ElectionAnomalyPublicSummaryCountModeIds.Suppressed);
        summaryBucket.HasPublicCount.Should().BeFalse();
        summaryBucket.SuppressionReasonIds.Should().Contain(ElectionAnomalyPublicSummarySuppressionReasonIds.LowCountCategory);

        var readiness = ownerResult.AnomalyReportReadiness;
        readiness.Should().NotBeNull();
        readiness!.ForbiddenFieldScanStatusId.Should().Be(ElectionAnomalyPublicArtifactScanStatusIds.Passed);
        readiness.PackageReadinessStatusId.Should().Be(ElectionAnomalyPackageReadinessStatusIds.Ready);
        readiness.PackageReadinessBlockerIds.Should().BeEmpty();
        readiness.RetentionEvidenceStatusId.Should().Be(ElectionAnomalyRetentionEvidenceStatusIds.OpenCaseRequiresPolicyReview);
        readiness.RetentionEvidenceStatus.StatusId.Should().Be(ElectionAnomalyRetentionEvidenceStatusIds.OpenCaseRequiresPolicyReview);
        readiness.RetentionEvidenceStatus.OpenCaseCount.Should().Be(1);
        readiness.RetentionEvidenceStatus.ReadinessBlocksValidationClaims.Should().BeTrue();
        readiness.ReportGenerationReadOnlyStatusId.Should().Be(ElectionAnomalyReportGenerationReadOnlyStatusIds.Validated);

        var resultReportJson = ownerResult.VisibleReportArtifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactMachineResultReportProjection);
        resultReportJson.Content.Should().Contain("\"publicAnomalySummary\"");
        resultReportJson.Content.Should().Contain("\"anomalyReportReadiness\"");
        resultReportJson.Content.Should().Contain("\"totalThreadCountMode\": \"suppressed\"");
        resultReportJson.Content.Should().NotContain(encryptedPayloadReference);
        resultReportJson.Content.Should().NotContain("submitterActorPublicAddress");

        var finalResultReport = ownerResult.VisibleReportArtifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactHumanResultReport);
        finalResultReport.Content.Should().Contain("## Anomaly Reporting");
        finalResultReport.Content.Should().Contain("- Public anomaly thread count: suppressed by privacy policy.");
        finalResultReport.Content.Should().Contain($"- Restricted manifest artifact id: `{anomalyArtifact.Id}`");
        finalResultReport.Content.Should().NotContain(encryptedPayloadReference);
        finalResultReport.Content.Should().NotContain("submitterActorPublicAddress");

        var evidenceGraph = ownerResult.VisibleReportArtifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKindProto.ReportArtifactMachineEvidenceGraph);
        evidenceGraph.Content.Should().Contain("\"restrictedAnomalyIntakeManifest\"");
        evidenceGraph.Content.Should().Contain("\"nodeType\": \"anomaly_intake_manifest\"");
        evidenceGraph.Content.Should().Contain($"\"artifactId\": \"{anomalyArtifact.Id}\"");
        evidenceGraph.Content.Should().Contain("\"attachmentManifestCount\": 1");
        evidenceGraph.Content.Should().Contain("\"redactionCount\": 1");

        var participantResult = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            Guest,
            waitForOfficialResult: true);
        participantResult.VisibleReportArtifacts.Should().BeEmpty();
        participantResult.PublicAnomalySummary.Should().BeNull();
        participantResult.AnomalyReportReadiness.Should().BeNull();

        var publicExport = await ExportElectionVerificationPackageAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            ElectionVerificationPackageViewProto.VerificationPackagePublicAnonymous);
        publicExport.Success.Should().BeTrue(publicExport.ErrorMessage);
        publicExport.Files.Should().NotContain(x =>
            x.RelativePath == VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest);
        publicExport.Files.Should().NotContain(x =>
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);
        var publicPackageText = string.Join(
            '\n',
            publicExport.Files.Select(x => Encoding.UTF8.GetString(x.Content.ToByteArray())));
        publicPackageText.Should().NotContain("restricted-anomaly-intake-manifest-artifact-v1");
        publicPackageText.Should().NotContain(encryptedPayloadReference);

        var restrictedExport = await ExportElectionVerificationPackageAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            ElectionVerificationPackageViewProto.VerificationPackageRestrictedOwnerAuditor);
        restrictedExport.Success.Should().BeTrue(restrictedExport.ErrorMessage);
        restrictedExport.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.ReportPackageRestrictedAnomalyIntakeManifest &&
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);
    }

    [Fact]
    [Trait("Category", "FEAT-113")]
    public async Task FinalizeElection_WithSealedPackage_ExportsVerificationPackageAndVerifierReplaysLocalFiles()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-113 Verification Package Replay",
            castSubmissionIdempotencyKey: "feat113-cast-001");

        var finalizeProposalId = await StartGovernedProposalAsync(
            client,
            context.ElectionId,
            ElectionGovernedActionType.Finalize);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, Delta);

        var finalizedElection = await ReloadElectionAsync(client, context.ElectionId, TestIdentities.Alice);
        finalizedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);

        var ownerStatus = await GetElectionVerificationPackageStatusAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice);
        ownerStatus.Success.Should().BeTrue(ownerStatus.ErrorMessage);
        ownerStatus.Status.Should().NotBeNull();
        ownerStatus.Status!.IsVisible.Should().BeTrue();
        ownerStatus.Status.Status.Should().Be(ElectionVerificationPackageStatusProto.VerificationPackageReady);
        ownerStatus.Status.PublicPackage.IsAvailable.Should().BeTrue();
        ownerStatus.Status.PublicPackage.PackageHash.Should().NotBeNullOrWhiteSpace();
        ownerStatus.Status.RestrictedPackage.IsAvailable.Should().BeTrue();
        ownerStatus.Status.RestrictedPackage.PackageHash.Should().NotBeNullOrWhiteSpace();
        ownerStatus.Status.ProtocolPackageBinding.Should().NotBeNull();
        ownerStatus.Status.ProtocolPackageBinding!.Status.Should().Be(
            ProtocolPackageBindingStatusProto.ProtocolPackageBindingSealed);

        var publicExport = await ExportElectionVerificationPackageAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            ElectionVerificationPackageViewProto.VerificationPackagePublicAnonymous);
        publicExport.Success.Should().BeTrue(publicExport.ErrorMessage);
        publicExport.PackageHash.Should().Be(ownerStatus.Status.PublicPackage.PackageHash);
        publicExport.Files.Should().NotBeEmpty();
        publicExport.Files.Should().Contain(x => x.RelativePath == VerificationPackageFileNames.AuditPackageManifest);
        publicExport.Files.Should().Contain(x => x.RelativePath == VerificationPackageFileNames.AcceptedBallotSet);
        publicExport.Files.Should().Contain(x => x.RelativePath == VerificationPackageFileNames.PublishedBallotStream);
        publicExport.Files.Should().NotContain(x =>
            x.RelativePath.StartsWith("artifacts/restricted/", StringComparison.OrdinalIgnoreCase) ||
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);

        using var packageDirectory = new TemporaryPackageDirectory();
        WriteVerificationPackageToDirectory(publicExport, packageDirectory.PackagePath);
        var verifierOutputPath = Path.Combine(packageDirectory.PackagePath, "verifier-output-local");
        var verification = await new HushVotingPackageVerifier().VerifyAsync(new(
            packageDirectory.PackagePath,
            VerificationProfileIds.PublicAnonymousV1,
            verifierOutputPath));
        verification.ExitCode.Should().Be(VerificationExitCodes.Pass);
        verification.Output.PackageId.Should().Be(publicExport.PackageId);
        verification.Output.ElectionId.Should().Be(context.ElectionId);
        verification.Output.VerifierProfileId.Should().Be(VerificationProfileIds.PublicAnonymousV1);
        verification.Output.OverallStatus.Should().Be(VerificationOverallStatus.Warn);
        verification.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PackageManifestValid &&
            x.Status == VerificationCheckStatus.Pass);
        verification.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PublicationProofEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);
        verification.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.PublicationProofExternalReviewPending &&
            x.Status == VerificationCheckStatus.Warn);
        File.Exists(Path.Combine(verifierOutputPath, "VerifierOutput.json")).Should().BeTrue();
        File.Exists(Path.Combine(verifierOutputPath, "VerifierSummary.md")).Should().BeTrue();

        var restrictedAsTrustee = await ExportElectionVerificationPackageAsync(
            client,
            context.ElectionId,
            TestIdentities.Bob,
            ElectionVerificationPackageViewProto.VerificationPackageRestrictedOwnerAuditor);
        restrictedAsTrustee.Success.Should().BeFalse();
        restrictedAsTrustee.Blocker.Should().Be(
            ElectionVerificationPackageBlockerProto.VerificationPackageBlockerUnauthorized);
        restrictedAsTrustee.ResultCode.Should().Be(VerificationResultCodes.RestrictedExportUnauthorized);
        restrictedAsTrustee.Files.Should().BeEmpty();

        var restrictedAsOwner = await ExportElectionVerificationPackageAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            ElectionVerificationPackageViewProto.VerificationPackageRestrictedOwnerAuditor);
        restrictedAsOwner.Success.Should().BeTrue(restrictedAsOwner.ErrorMessage);
        restrictedAsOwner.PackageHash.Should().Be(ownerStatus.Status.RestrictedPackage.PackageHash);
        restrictedAsOwner.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedRosterCheckoff &&
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);
    }

    [Fact]
    [Trait("Category", "FEAT-114")]
    public async Task ChallengeSpoilCeremony_WithBoundReceipt_ExportsPublicAndRestrictedSp04Evidence()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-114 Challenge Spoil Ceremony",
            castSubmissionIdempotencyKey: "feat114-cast-001");

        var finalizeProposalId = await StartGovernedProposalAsync(
            client,
            context.ElectionId,
            ElectionGovernedActionType.Finalize);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, Delta);

        var finalizedElection = await ReloadElectionAsync(client, context.ElectionId, TestIdentities.Alice);
        finalizedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);

        var voterView = await GetElectionVotingViewAsync(client, context.ElectionId, TestIdentities.Alice);
        voterView.Sp04Required.Should().BeTrue();
        voterView.ChallengeSatisfied.Should().BeTrue();
        voterView.SpoiledPackageCount.Should().BeGreaterThanOrEqualTo(1);
        voterView.PreparedBallotState.Should().Be(PreparedBallotStateProto.PreparedBallotCast);
        voterView.PreparedBallotId.Should().NotBeNullOrWhiteSpace();
        voterView.PreparedBallotHash.Should().NotBeNullOrWhiteSpace();
        voterView.ReceiptCommitment.Should().NotBeNullOrWhiteSpace();
        voterView.ReceiptCommitmentScheme.Should().Be(Sp04ReceiptCommitmentScheme);

        var receiptVerification = await VerifyElectionReceiptAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            voterView.ReceiptCommitment,
            voterView.PreparedBallotId);
        receiptVerification.Success.Should().BeTrue(receiptVerification.ErrorMessage);
        receiptVerification.HasBoundReceipt.Should().BeTrue();
        receiptVerification.ReceiptCommitmentInAcceptedSet.Should().BeTrue();
        receiptVerification.ParticipationCountedAsVoted.Should().BeTrue();
        receiptVerification.VerifiedPreparedBallotId.Should().Be(voterView.PreparedBallotId);

        var ownerStatus = await GetElectionVerificationPackageStatusAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice);
        ownerStatus.Success.Should().BeTrue(ownerStatus.ErrorMessage);
        ownerStatus.Status.Should().NotBeNull();
        ownerStatus.Status!.Sp04Evidence.PreparedPackageCount.Should().BeGreaterThanOrEqualTo(2);
        ownerStatus.Status.Sp04Evidence.SpoiledPackageCount.Should().BeGreaterThanOrEqualTo(1);
        ownerStatus.Status.Sp04Evidence.AcceptedBoundReceiptCount.Should().Be(2);
        ownerStatus.Status.Sp04Evidence.ReceiptCommitmentSetHash.Should().NotBeNullOrWhiteSpace();

        var publicExport = await ExportElectionVerificationPackageAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            ElectionVerificationPackageViewProto.VerificationPackagePublicAnonymous);
        publicExport.Success.Should().BeTrue(publicExport.ErrorMessage);
        publicExport.Files.Should().Contain(x => x.RelativePath == VerificationPackageFileNames.Sp04Evidence);
        publicExport.Files.Should().Contain(x => x.RelativePath == VerificationPackageFileNames.Sp04ReceiptCommitments);
        publicExport.Files.Should().NotContain(x =>
            x.RelativePath.StartsWith("artifacts/restricted/", StringComparison.OrdinalIgnoreCase) ||
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);

        var publicSp04Evidence = ParsePackageJson(publicExport, VerificationPackageFileNames.Sp04Evidence);
        publicSp04Evidence.RootElement.GetProperty("preparedPackageCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        publicSp04Evidence.RootElement.GetProperty("spoiledPackageCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        publicSp04Evidence.RootElement.GetProperty("acceptedBoundReceiptCount").GetInt32().Should().Be(2);
        publicSp04Evidence.Dispose();

        var publicReceiptCommitments = ParsePackageJson(publicExport, VerificationPackageFileNames.Sp04ReceiptCommitments);
        publicReceiptCommitments.RootElement.GetArrayLength().Should().Be(2);
        publicReceiptCommitments.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("preparedBallotId").GetString())
            .Should()
            .Contain(voterView.PreparedBallotId);
        publicReceiptCommitments.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("receiptCommitment").GetString())
            .Should()
            .Contain(voterView.ReceiptCommitment);
        publicReceiptCommitments.Dispose();

        var publicPayload = string.Join(
            '\n',
            publicExport.Files.Select(x => Encoding.UTF8.GetString(x.Content.ToByteArray())));
        publicPayload.Should().NotContain("voter-alice");
        publicPayload.Should().NotContain("spoiled-transcript-hash");
        publicPayload.Should().NotContain("spoil-record-hash");

        using var packageDirectory = new TemporaryPackageDirectory();
        WriteVerificationPackageToDirectory(publicExport, packageDirectory.PackagePath);
        var verifierOutputPath = Path.Combine(packageDirectory.PackagePath, "verifier-output-local");
        var verification = await new HushVotingPackageVerifier().VerifyAsync(new(
            packageDirectory.PackagePath,
            VerificationProfileIds.PublicAnonymousV1,
            verifierOutputPath));
        verification.ExitCode.Should().Be(VerificationExitCodes.Pass);
        verification.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.ChallengeSpoilEvidenceValid &&
            x.Status == VerificationCheckStatus.Pass);

        var restrictedExport = await ExportElectionVerificationPackageAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            ElectionVerificationPackageViewProto.VerificationPackageRestrictedOwnerAuditor);
        restrictedExport.Success.Should().BeTrue(restrictedExport.ErrorMessage);
        restrictedExport.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp04CeremonyRecords &&
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);
        restrictedExport.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp04PreparedBallotCommitments &&
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);
        restrictedExport.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedSp04SpoilMarkers &&
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);

        var restrictedPrepared = GetPackageFileText(
            restrictedExport,
            VerificationPackageFileNames.RestrictedSp04PreparedBallotCommitments);
        restrictedPrepared.Should().Contain("\"state\": \"spoiled\"");
        restrictedPrepared.Should().Contain("\"state\": \"cast\"");
        restrictedPrepared.Should().Contain(voterView.PreparedBallotId);
        restrictedPrepared.Should().NotContain("plaintext-choice");
        restrictedPrepared.Should().NotContain("final-randomness");
    }

    [Fact]
    [Trait("Category", "FEAT-115")]
    [Trait("Category", "TwinTest")]
    [Trait("Category", "NON_E2E")]
    public async Task EligibilityCheckoffSeparation_ExportsSp05EvidenceAndKeepsPublicPackageAnonymous()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-115 Eligibility Checkoff Separation",
            castSubmissionIdempotencyKey: "feat115-cast-001");

        var finalizeProposalId = await StartGovernedProposalAsync(
            client,
            context.ElectionId,
            ElectionGovernedActionType.Finalize);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, Delta);

        var finalizedElection = await ReloadElectionAsync(client, context.ElectionId, TestIdentities.Alice);
        finalizedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);

        var duplicateStrictLink = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.ClaimElectionRosterEntry(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(context.ElectionId)),
                "voter-charlie",
                "1111"));
        duplicateStrictLink.Successfull.Should().BeFalse();

        var ownerStatus = await GetElectionVerificationPackageStatusAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice);
        ownerStatus.Success.Should().BeTrue(ownerStatus.ErrorMessage);
        ownerStatus.Status.Should().NotBeNull();
        ownerStatus.Status!.Sp05Evidence.EvidenceExpected.Should().BeTrue();
        ownerStatus.Status.Sp05Evidence.PublicEvidenceAvailable.Should().BeTrue();
        ownerStatus.Status.Sp05Evidence.RestrictedEvidenceAvailable.Should().BeTrue();
        ownerStatus.Status.Sp05Evidence.RosteredCount.Should().Be(4);
        ownerStatus.Status.Sp05Evidence.LinkedCount.Should().Be(3);
        ownerStatus.Status.Sp05Evidence.ActiveDenominatorCount.Should().Be(3);
        ownerStatus.Status.Sp05Evidence.CommitmentCount.Should().Be(2);
        ownerStatus.Status.Sp05Evidence.CountedParticipationCount.Should().Be(2);
        ownerStatus.Status.Sp05Evidence.RosterCanonicalHash.Should().NotBeNullOrWhiteSpace();
        ownerStatus.Status.Sp05Evidence.CommitmentTreeRoot.Should().NotBeNullOrWhiteSpace();
        ownerStatus.Status.Sp05Evidence.LatestEliResultCode.Should().Be(
            VerificationResultCodes.EligibilityDevOnlyVerificationBlocked);

        var publicExport = await ExportElectionVerificationPackageAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            ElectionVerificationPackageViewProto.VerificationPackagePublicAnonymous);
        publicExport.Success.Should().BeTrue(publicExport.ErrorMessage);
        publicExport.Files.Should().Contain(x => x.RelativePath == VerificationPackageFileNames.Sp05EligibilityPolicy);
        publicExport.Files.Should().Contain(x => x.RelativePath == VerificationPackageFileNames.Sp05EligibilitySummary);
        publicExport.Files.Should().Contain(x => x.RelativePath == VerificationPackageFileNames.Sp05EligibilityVerifierOutput);
        publicExport.Files.Should().NotContain(x =>
            x.RelativePath.StartsWith("artifacts/restricted/", StringComparison.OrdinalIgnoreCase) ||
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);

        using (var publicSummary = ParsePackageJson(publicExport, VerificationPackageFileNames.Sp05EligibilitySummary))
        {
            publicSummary.RootElement.GetProperty("rosteredCount").GetInt32().Should().Be(4);
            publicSummary.RootElement.GetProperty("linkedCount").GetInt32().Should().Be(3);
            publicSummary.RootElement.GetProperty("activeDenominatorCount").GetInt32().Should().Be(3);
            publicSummary.RootElement.GetProperty("commitmentCount").GetInt32().Should().Be(2);
            publicSummary.RootElement.GetProperty("countedParticipationCount").GetInt32().Should().Be(2);
            publicSummary.RootElement.GetProperty("publicPrivacyBoundary").EnumerateArray()
                .Select(x => x.GetString())
                .Should()
                .Contain("no_organization_voter_id");
        }

        var publicPayload = string.Join(
            '\n',
            publicExport.Files.Select(x => Encoding.UTF8.GetString(x.Content.ToByteArray())));
        publicPayload.Should().NotContain("voter-alice");
        publicPayload.Should().NotContain("voter-charlie");
        publicPayload.Should().NotContain("voter-guest");
        publicPayload.Should().NotContain("alice.eligibility@hush.test");
        publicPayload.Should().NotContain("charlie.eligibility@hush.test");
        publicPayload.Should().NotContain("guest.eligibility@hush.test");

        using var packageDirectory = new TemporaryPackageDirectory();
        WriteVerificationPackageToDirectory(publicExport, packageDirectory.PackagePath);
        var verifierOutputPath = Path.Combine(packageDirectory.PackagePath, "verifier-output-local");
        var verification = await new HushVotingPackageVerifier().VerifyAsync(new(
            packageDirectory.PackagePath,
            VerificationProfileIds.PublicAnonymousV1,
            verifierOutputPath));
        verification.ExitCode.Should().Be(
            VerificationExitCodes.Pass,
            string.Join(
                " | ",
                verification.Output.Results.Select(x =>
                    $"{x.ResultCode}:{x.Status}:{x.Message}:{string.Join(",", x.Evidence.Select(pair => $"{pair.Key}={pair.Value}"))}")));
        verification.Output.Results.Should().Contain(x =>
            x.ResultCode == VerificationResultCodes.EligibilityDevOnlyVerificationBlocked &&
            x.Status == VerificationCheckStatus.Warn);
        verification.Output.Results.Should().NotContain(x =>
            x.ResultCode == VerificationResultCodes.EligibilityPublicPrivacyBoundaryViolation);

        var restrictedExport = await ExportElectionVerificationPackageAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            ElectionVerificationPackageViewProto.VerificationPackageRestrictedOwnerAuditor);
        restrictedExport.Success.Should().BeTrue(restrictedExport.ErrorMessage);
        restrictedExport.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedRosterImportEvidence &&
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);
        restrictedExport.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedRoster &&
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);
        restrictedExport.Files.Should().Contain(x =>
            x.RelativePath == VerificationPackageFileNames.RestrictedCheckoffLedger &&
            x.Visibility == ElectionVerificationArtifactVisibilityProto.VerificationArtifactRestricted);

        var restrictedCheckoff = GetPackageFileText(
            restrictedExport,
            VerificationPackageFileNames.RestrictedCheckoffLedger);
        restrictedCheckoff.Should().Contain("voter-alice");
        restrictedCheckoff.Should().Contain("voter-charlie");
        restrictedCheckoff.Should().Contain("countedAsVoted");
    }

    [Fact]
    public async Task FinalizeElection_WhenPackageGenerationFails_PersistsFailedAttemptAndRetrySealsANewAttempt()
    {
        var client = await StartClientAsync();
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-102 Failed Attempt Retry");

        await CorruptCloseEligibilitySnapshotBoundaryAsync(context.ElectionId);

        var finalizeProposalId = await StartGovernedProposalAsync(
            client,
            context.ElectionId,
            ElectionGovernedActionType.Finalize);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, Delta);

        var failedElection = await ReloadElectionAsync(client, context.ElectionId, TestIdentities.Alice);
        failedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        failedElection.Election.FinalizeArtifactId.Should().BeNullOrWhiteSpace();
        failedElection.GovernedProposals.Should().ContainSingle(x =>
            x.Id == finalizeProposalId.ToString() &&
            x.ActionType == ElectionGovernedActionTypeProto.GovernedActionFinalize &&
            x.ExecutionStatus == ElectionGovernedProposalExecutionStatusProto.ExecutionFailed);

        var failedOwnerResult = await GetElectionResultViewAsync(client, context.ElectionId, TestIdentities.Alice);
        failedOwnerResult.CanViewReportPackage.Should().BeTrue();
        failedOwnerResult.CanRetryFailedPackageFinalization.Should().BeTrue();
        failedOwnerResult.LatestReportPackage.Should().NotBeNull();
        failedOwnerResult.LatestReportPackage!.Status.Should().Be(
            ElectionReportPackageStatusProto.ReportPackageGenerationFailed);
        failedOwnerResult.VisibleReportArtifacts.Should().BeEmpty();

        await using (var scope = _node!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
            var failedPackages = await dbContext.Set<ElectionReportPackageRecord>()
                .Where(x => x.ElectionId == new ElectionId(Guid.Parse(context.ElectionId)))
                .OrderBy(x => x.AttemptNumber)
                .ToListAsync();
            failedPackages.Should().ContainSingle();
            failedPackages.Single().Status.Should().Be(ElectionReportPackageStatus.GenerationFailed);
            failedPackages.Single().FailureReason.Should().Contain("Close eligibility snapshot");
        }

        var electionAfterFailure = await ReloadElectionAsync(client, context.ElectionId, TestIdentities.Alice);
        await RestoreCloseEligibilitySnapshotBoundaryAsync(
            context.ElectionId,
            Guid.Parse(electionAfterFailure.Election.CloseArtifactId));

        await RetryProposalExecutionAsync(client, context.ElectionId, finalizeProposalId);

        var finalizedElection = await ReloadElectionAsync(client, context.ElectionId, TestIdentities.Alice);
        finalizedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Finalized);
        finalizedElection.Election.FinalizeArtifactId.Should().NotBeNullOrWhiteSpace();

        var retriedOwnerResult = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            waitForOfficialResult: true);
        retriedOwnerResult.CanRetryFailedPackageFinalization.Should().BeFalse();
        retriedOwnerResult.LatestReportPackage.Should().NotBeNull();
        retriedOwnerResult.LatestReportPackage!.Status.Should().Be(
            ElectionReportPackageStatusProto.ReportPackageSealed);
        retriedOwnerResult.VisibleReportArtifacts.Should().HaveCount(13);

        await using (var scope = _node!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
            var packages = await dbContext.Set<ElectionReportPackageRecord>()
                .Where(x => x.ElectionId == new ElectionId(Guid.Parse(context.ElectionId)))
                .OrderBy(x => x.AttemptNumber)
                .ToListAsync();
            packages.Should().HaveCount(2);
            packages[0].Status.Should().Be(ElectionReportPackageStatus.GenerationFailed);
            packages[1].Status.Should().Be(ElectionReportPackageStatus.Sealed);
            packages[1].AttemptNumber.Should().Be(2);
            packages[1].PreviousAttemptId.Should().Be(packages[0].Id);
            packages[1].FrozenEvidenceHash.Should().Equal(packages[0].FrozenEvidenceHash);
        }
    }

    [Fact]
    [Trait("Category", "FEAT-105")]
    public async Task FinalizeElection_WithBindingBallotPath_DoesNotPersistBallotPayloadLeakMarkersInReportArtifactsOrNormalLogs()
    {
        var diagnostics = new DiagnosticCapture();
        var client = await StartClientAsync(diagnostics);
        const string castSubmissionIdempotencyKey = "feat105-phase4-report-log-audit";
        var context = await CreateClosedElectionReadyForFinalizeAsync(
            client,
            "FEAT-105 Binding Report and Log Audit",
            castSubmissionIdempotencyKey);

        var finalizeProposalId = await StartGovernedProposalAsync(
            client,
            context.ElectionId,
            ElectionGovernedActionType.Finalize);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(context.ElectionId, finalizeProposalId, Delta);

        var ownerResult = await GetElectionResultViewAsync(
            client,
            context.ElectionId,
            TestIdentities.Alice,
            waitForOfficialResult: true);
        ownerResult.LatestReportPackage.Should().NotBeNull();
        ownerResult.LatestReportPackage!.Status.Should().Be(ElectionReportPackageStatusProto.ReportPackageSealed);

        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var reportPackageId = Guid.Parse(ownerResult.LatestReportPackage.Id);
        var persistedArtifacts = await dbContext.Set<ElectionReportArtifactRecord>()
            .Where(x => x.ReportPackageId == reportPackageId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        persistedArtifacts.Should().NotBeEmpty();

        var persistedArtifactContent = string.Join(
            "\n---artifact---\n",
            persistedArtifacts.Select(x => x.Content));
        var capturedLogs = diagnostics.GetCapturedLogs();

        foreach (var leakMarker in BindingBallotLeakMarkers.Append(castSubmissionIdempotencyKey))
        {
            persistedArtifactContent.Should().NotContain(leakMarker);
            capturedLogs.Should().NotContain(leakMarker);
        }
    }

    private async Task<HushElections.HushElectionsClient> StartClientAsync(
        DiagnosticCapture? diagnosticCapture = null)
    {
        await DisposeNodeAsync();
        await _fixture!.ResetAllAsync();
        (_node, _blockControl, _grpcFactory) = await _fixture.StartNodeAsync(diagnosticCapture);
        return _grpcFactory.CreateClient<HushElections.HushElectionsClient>();
    }

    private async Task DisposeNodeAsync()
    {
        _grpcFactory?.Dispose();
        _grpcFactory = null;
        _blockControl = null;

        if (_node is not null)
        {
            await _node.DisposeAsync();
            _node = null;
        }
    }

    private async Task<ClosedElectionReadyContext> CreateClosedElectionReadyForFinalizeAsync(
        HushElections.HushElectionsClient client,
        string title,
        string castSubmissionIdempotencyKey = "feat102-cast-001")
    {
        var createResponse = await CreateTrusteeThresholdDraftAsync(client, title);
        var electionId = createResponse.Election.ElectionId;

        await InviteAndAcceptRolloutTrusteesAsync(electionId);
        var ceremonyVersionId = await StartCeremonyAsync(client, electionId, "dkg-prod-3of5");
        await CompleteReadyThresholdAsync(electionId, ceremonyVersionId, requiredCompletionCount: RolloutTrustees.Count);

        var readiness = await client.GetElectionOpenReadinessAsync(new GetElectionOpenReadinessRequest
        {
            ElectionId = electionId,
        });
        readiness.IsReadyToOpen.Should().BeTrue(string.Join(" | ", readiness.ValidationErrors));

        var openProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Open);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, openProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, openProposalId, Delta);

        var openElection = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        openElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Open);

        await ClaimRosterEntryAsync(electionId, TestIdentities.Alice, "voter-alice");
        await ClaimRosterEntryAsync(electionId, Guest, "voter-guest");
        await RegisterVotingCommitmentAsync(client, electionId, TestIdentities.Alice, "feat102-commitment-001");
        var castSubmitResponse = await SubmitAcceptedBallotCastViaBlockchainAsync(
            client,
            electionId,
            TestIdentities.Alice,
            castSubmissionIdempotencyKey);
        castSubmitResponse.Successfull.Should().BeTrue(castSubmitResponse.Message);

        await ClaimRosterEntryAsync(electionId, TestIdentities.Charlie, "voter-charlie");
        await RegisterVotingCommitmentAsync(client, electionId, TestIdentities.Charlie, "feat102-charlie-commitment-001");
        var charlieCastSubmitResponse = await SubmitAcceptedBallotCastViaBlockchainAsync(
            client,
            electionId,
            TestIdentities.Charlie,
            $"{castSubmissionIdempotencyKey}-charlie");
        charlieCastSubmitResponse.Successfull.Should().BeTrue(charlieCastSubmitResponse.Message);

        var closeProposalId = await StartGovernedProposalAsync(
            client,
            electionId,
            ElectionGovernedActionType.Close);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Bob);
        await ApproveProposalAsync(electionId, closeProposalId, TestIdentities.Charlie);
        await ApproveProposalAsync(electionId, closeProposalId, Delta);

        var closedElection = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        closedElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        closedElection.Election.TallyReadyArtifactId.Should().BeNullOrWhiteSpace();
        closedElection.FinalizationSessions.Should().ContainSingle(x =>
            x.SessionPurpose == ElectionFinalizationSessionPurposeProto.FinalizationSessionPurposeCloseCounting &&
            x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares);

        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, TestIdentities.Bob);
        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, TestIdentities.Charlie);
        await SubmitFinalizationShareViaBlockchainAsync(client, electionId, Delta);

        var tallyReadyElection = await WaitForTallyReadyElectionAsync(client, electionId, TestIdentities.Alice);
        tallyReadyElection.Election.LifecycleState.Should().Be(ElectionLifecycleStateProto.Closed);
        tallyReadyElection.Election.TallyReadyArtifactId.Should().NotBeNullOrWhiteSpace();
        tallyReadyElection.Election.UnofficialResultArtifactId.Should().NotBeNullOrWhiteSpace();

        return new ClosedElectionReadyContext(electionId);
    }

    private async Task<(Guid AnomalyThreadId, Guid AttachmentManifestId, string EncryptedPayloadReference)> RecordReadyAnomalyEvidenceAsync(
        ElectionId electionId)
    {
        var (submissionTransaction, anomalyThreadId) = TestTransactionFactory.SubmitElectionAnomalyThread(
            TestIdentities.Alice,
            TestIdentities.Alice,
            electionId);
        var submissionResponse = await SubmitBlockchainTransactionAsync(submissionTransaction);
        submissionResponse.Successfull.Should().BeTrue(submissionResponse.Message);

        var (
            attachmentTransaction,
            attachmentManifestId,
            encryptedPayloadReference,
            contentHash) = TestTransactionFactory.RecordElectionAnomalyAuthorityAttachmentManifest(
            TestIdentities.Alice,
            electionId,
            anomalyThreadId);
        var attachmentResponse = await SubmitBlockchainTransactionAsync(attachmentTransaction);
        attachmentResponse.Successfull.Should().BeTrue(attachmentResponse.Message);

        var (redactionTransaction, _) = TestTransactionFactory.RecordElectionAnomalyEvidenceRedaction(
            TestIdentities.Alice,
            electionId,
            anomalyThreadId,
            attachmentManifestId,
            contentHash);
        var redactionResponse = await SubmitBlockchainTransactionAsync(redactionTransaction);
        redactionResponse.Successfull.Should().BeTrue(redactionResponse.Message);

        return (anomalyThreadId, attachmentManifestId, encryptedPayloadReference);
    }

    private async Task<ElectionCommandResponse> CreateTrusteeThresholdDraftAsync(
        HushElections.HushElectionsClient client,
        string title)
    {
        var (signedTransaction, electionId) = TestTransactionFactory.CreateElectionDraft(
            TestIdentities.Alice,
            "feat-102 integration draft",
            BuildTrusteeThresholdDraftSpecification(title));
        var submitResponse = await SubmitBlockchainTransactionAsync(signedTransaction);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var importRosterResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.ImportElectionRoster(
                TestIdentities.Alice,
                electionId,
                BuildOpenReadyRosterEntries()));
        importRosterResponse.Successfull.Should().BeTrue(importRosterResponse.Message);

        var response = await ReloadElectionAsync(client, electionId.ToString(), TestIdentities.Alice);
        response.LatestDraftSnapshot.Should().NotBeNull();

        return new ElectionCommandResponse
        {
            Success = true,
            Election = response.Election,
            DraftSnapshot = response.LatestDraftSnapshot,
        };
    }

    private async Task InviteAndAcceptRolloutTrusteesAsync(string electionId)
    {
        foreach (var trustee in RolloutTrustees)
        {
            var identitySubmitResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.CreateIdentityRegistration(trustee));
            identitySubmitResponse.Successfull.Should().BeTrue(identitySubmitResponse.Message);

            var (inviteTransaction, invitationId) = TestTransactionFactory.CreateElectionTrusteeInvitation(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                trustee);
            var inviteSubmitResponse = await SubmitBlockchainTransactionAsync(inviteTransaction);
            inviteSubmitResponse.Successfull.Should().BeTrue(inviteSubmitResponse.Message);

            var acceptSubmitResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.AcceptElectionTrusteeInvitation(
                    trustee,
                    new ElectionId(Guid.Parse(electionId)),
                    invitationId));
            acceptSubmitResponse.Successfull.Should().BeTrue(acceptSubmitResponse.Message);
        }
    }

    private async Task<string> StartCeremonyAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        string profileId)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.StartElectionCeremony(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                profileId));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        response.Success.Should().BeTrue(response.ErrorMessage);
        return response.CeremonyVersions
            .Where(x => x.ProfileId == profileId)
            .OrderByDescending(x => x.VersionNumber)
            .First()
            .Id;
    }

    private async Task CompleteReadyThresholdAsync(
        string electionId,
        string ceremonyVersionId,
        int requiredCompletionCount)
    {
        for (var index = 0; index < requiredCompletionCount; index++)
        {
            var trustee = RolloutTrustees[index];
            await PublishJoinAndSelfTestAsync(electionId, ceremonyVersionId, trustee, index);

            var submitMaterialResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.SubmitElectionCeremonyMaterial(
                    trustee,
                    new ElectionId(Guid.Parse(electionId)),
                    Guid.Parse(ceremonyVersionId),
                    recipientTrusteeUserAddress: null,
                    messageType: "dkg-share-package",
                    payloadVersion: "omega-v1.0.0",
                    encryptedPayload: $"feat102-payload-{index}",
                    payloadFingerprint: $"feat102-payload-fingerprint-{index}"));
            submitMaterialResponse.Successfull.Should().BeTrue(submitMaterialResponse.Message);

            var completeTrusteeResponse = await SubmitBlockchainTransactionAsync(
                TestTransactionFactory.CompleteElectionCeremonyTrustee(
                    TestIdentities.Alice,
                    new ElectionId(Guid.Parse(electionId)),
                    Guid.Parse(ceremonyVersionId),
                    trustee.PublicSigningAddress,
                    $"feat102-share-v1-{index}",
                    tallyPublicKeyFingerprint: null));
            completeTrusteeResponse.Successfull.Should().BeTrue(completeTrusteeResponse.Message);
        }
    }

    private async Task PublishJoinAndSelfTestAsync(
        string electionId,
        string ceremonyVersionId,
        TestIdentity trustee,
        int index)
    {
        var publishResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.PublishElectionCeremonyTransportKey(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId),
                $"feat102-transport-fingerprint-{index}"));
        publishResponse.Successfull.Should().BeTrue(publishResponse.Message);

        var joinResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.JoinElectionCeremony(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));
        joinResponse.Successfull.Should().BeTrue(joinResponse.Message);

        var selfTestResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RecordElectionCeremonySelfTestSuccess(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(ceremonyVersionId)));
        selfTestResponse.Successfull.Should().BeTrue(selfTestResponse.Message);
    }

    private async Task<Guid> StartGovernedProposalAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        ElectionGovernedActionType actionType)
    {
        var (signedTransaction, proposalId) = TestTransactionFactory.StartElectionGovernedProposal(
            TestIdentities.Alice,
            new ElectionId(Guid.Parse(electionId)),
            actionType);
        var submitResponse = await SubmitBlockchainTransactionAsync(signedTransaction);
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        response.GovernedProposals.Should().ContainSingle(x => x.Id == proposalId.ToString());
        return proposalId;
    }

    private async Task ApproveProposalAsync(
        string electionId,
        Guid proposalId,
        TestIdentity trustee)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.ApproveElectionGovernedProposal(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                proposalId,
                approvalNote: null));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
    }

    private async Task RetryProposalExecutionAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        Guid proposalId)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RetryElectionGovernedProposalExecution(
                TestIdentities.Alice,
                new ElectionId(Guid.Parse(electionId)),
                proposalId));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        response.GovernedProposals.Should().ContainSingle(x => x.Id == proposalId.ToString());
    }

    private async Task ClaimRosterEntryAsync(
        string electionId,
        TestIdentity actor,
        string organizationVoterId)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.ClaimElectionRosterEntry(
                actor,
                new ElectionId(Guid.Parse(electionId)),
                organizationVoterId,
                "1111"));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
    }

    private async Task RegisterVotingCommitmentAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string commitmentHash)
    {
        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RegisterElectionVotingCommitment(
                actor,
                new ElectionId(Guid.Parse(electionId)),
                commitmentHash));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);

        var response = await GetElectionVotingViewAsync(client, electionId, actor);
        response.CommitmentRegistered.Should().BeTrue();
    }

    private async Task<SubmitSignedTransactionReply> SubmitAcceptedBallotCastViaBlockchainAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey)
    {
        var signedTransaction = await BuildAcceptedBallotCastTransactionAsync(
            client,
            electionId,
            actor,
            submissionIdempotencyKey);

        return await SubmitBlockchainTransactionAsync(signedTransaction);
    }

    private async Task<string> BuildAcceptedBallotCastTransactionAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey)
    {
        var votingView = await GetElectionVotingViewAsync(client, electionId, actor);

        votingView.CommitmentRegistered.Should().BeTrue();
        votingView.OpenArtifactId.Should().NotBeNullOrWhiteSpace();
        votingView.EligibleSetHash.Should().NotBeNullOrWhiteSpace();
        votingView.CeremonyVersionId.Should().NotBeNullOrWhiteSpace();
        votingView.DkgProfileId.Should().NotBeNullOrWhiteSpace();
        votingView.TallyPublicKeyFingerprint.Should().NotBeNullOrWhiteSpace();
        votingView.TallyPublicKey.Should().NotBeNull();
        votingView.TallyPublicKey.X.Length.Should().Be(32);
        votingView.TallyPublicKey.Y.Length.Should().Be(32);
        votingView.Sp04Required.Should().BeTrue();
        votingView.BallotDefinitionVersion.Should().BeGreaterThan(0);
        votingView.BallotDefinitionHash.Length.Should().BeGreaterThan(0);

        await EnsureSp04ChallengeSatisfiedAsync(
            client,
            electionId,
            actor,
            submissionIdempotencyKey,
            votingView);

        var selectionCount = votingView.Election.Options.Count;
        selectionCount.Should().BeGreaterThan(0);
        var nonBlankChoiceIndexes = votingView.Election.Options
            .Select((option, index) => new { option, index })
            .Where(x => !x.option.IsBlankOption)
            .Select(x => x.index)
            .ToArray();
        nonBlankChoiceIndexes.Should().NotBeEmpty();

        var choiceIndex = ResolveChoiceIndex(actor, submissionIdempotencyKey, nonBlankChoiceIndexes);
        var tallyPublicKey = ReactionECPoint.FromCoordinates(
            votingView.TallyPublicKey.X.ToByteArray(),
            votingView.TallyPublicKey.Y.ToByteArray());

        var encryptedBallotPackage = BuildEncryptedBallotPackage(
            electionId,
            actor,
            submissionIdempotencyKey,
            selectionCount,
            choiceIndex,
            tallyPublicKey);
        var proofBundle = BuildProofBundle(votingView, encryptedBallotPackage);
        var sp04Binding = await RegisterFinalPreparedBallotAsync(
            client,
            electionId,
            actor,
            submissionIdempotencyKey,
            votingView,
            encryptedBallotPackage,
            proofBundle);

        return TestTransactionFactory.AcceptElectionBallotCast(
            actor,
            new ElectionId(Guid.Parse(electionId)),
            submissionIdempotencyKey,
            encryptedBallotPackage,
            proofBundle,
            BuildBallotNullifier(electionId, actor, submissionIdempotencyKey),
            Guid.Parse(votingView.OpenArtifactId),
            Convert.FromBase64String(votingView.EligibleSetHash),
            Guid.Parse(votingView.CeremonyVersionId),
            votingView.DkgProfileId,
            votingView.TallyPublicKeyFingerprint,
            sp04Binding.PreparedBallotId,
            sp04Binding.PreparedBallotHash,
            sp04Binding.ReceiptCommitment,
            sp04Binding.ReceiptCommitmentScheme,
            sp04Binding.BallotDefinitionVersion,
            sp04Binding.BallotDefinitionHash);
    }

    private async Task EnsureSp04ChallengeSatisfiedAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey,
        GetElectionVotingViewResponse votingView)
    {
        if (votingView.ChallengeSatisfied)
        {
            return;
        }

        var challengePreparedBallotId = Guid.NewGuid();
        var challengePreparedHash = ComputeLowerHexSha256(
            JsonSerializer.Serialize(new
            {
                purpose = "challenge",
                electionId,
                actor = actor.PublicSigningAddress,
                submissionIdempotencyKey,
                votingView.BallotDefinitionVersion,
                ballotDefinitionHash = Convert.ToHexString(votingView.BallotDefinitionHash.ToByteArray()),
            }));

        var prepareResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RegisterPreparedBallotCommitment(
                actor,
                new ElectionId(Guid.Parse(electionId)),
                challengePreparedBallotId,
                challengePreparedHash,
                votingView.BallotDefinitionVersion,
                votingView.BallotDefinitionHash.ToByteArray(),
                ElectionSp04ProfileIds.ChallengeSpoilV1,
                Sp04ProofStatementId));
        prepareResponse.Successfull.Should().BeTrue(prepareResponse.Message);

        var preparedView = await GetElectionVotingViewAsync(client, electionId, actor);
        preparedView.PreparedBallotId.Should().Be(challengePreparedBallotId.ToString());
        preparedView.PreparedBallotHash.Should().Be(challengePreparedHash);
        preparedView.PreparedBallotState.Should().Be(PreparedBallotStateProto.PreparedBallotPrepared);
        preparedView.HasPreparedBallotExpiresAt.Should().BeTrue();

        var spoiledTranscriptHash = ComputeLowerHexSha256(
            $"spoiled-transcript|{electionId}|{challengePreparedBallotId:N}|{challengePreparedHash}|{submissionIdempotencyKey}");
        var spoilRecordHash = ComputeLowerHexSha256(
            $"spoil-record|{electionId}|{challengePreparedBallotId:N}|{challengePreparedHash}|{spoiledTranscriptHash}");
        var spoilResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SpoilPreparedBallot(
                actor,
                new ElectionId(Guid.Parse(electionId)),
                challengePreparedBallotId,
                challengePreparedHash,
                spoiledTranscriptHash,
                spoilRecordHash,
                Sp04LocalVerifierVersion));
        spoilResponse.Successfull.Should().BeTrue(spoilResponse.Message);

        var spoiledView = await GetElectionVotingViewAsync(client, electionId, actor);
        spoiledView.ChallengeSatisfied.Should().BeTrue();
        spoiledView.SpoiledPackageCount.Should().BeGreaterThanOrEqualTo(1);
    }

    private async Task<Sp04CastBinding> RegisterFinalPreparedBallotAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey,
        GetElectionVotingViewResponse votingView,
        string encryptedBallotPackage,
        string proofBundle)
    {
        var preparedBallotId = Guid.NewGuid();
        var preparedBallotHash = ComputeLowerHexSha256(
            JsonSerializer.Serialize(new
            {
                purpose = "final",
                electionId,
                actor = actor.PublicSigningAddress,
                submissionIdempotencyKey,
                encryptedBallotPackageHash = ComputeLowerHexSha256(encryptedBallotPackage),
                proofBundleHash = ComputeLowerHexSha256(proofBundle),
                votingView.BallotDefinitionVersion,
                ballotDefinitionHash = Convert.ToHexString(votingView.BallotDefinitionHash.ToByteArray()),
            }));
        var receiptCommitment = ComputeLowerHexSha256(
            JsonSerializer.Serialize(new
            {
                version = "sp04-receipt-commitment-v1",
                electionId,
                preparedBallotId,
                preparedBallotHash,
                ballotDefinitionHash = Convert.ToHexString(votingView.BallotDefinitionHash.ToByteArray()),
                votingView.BallotDefinitionVersion,
                scheme = Sp04ReceiptCommitmentScheme,
            }));

        var prepareResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.RegisterPreparedBallotCommitment(
                actor,
                new ElectionId(Guid.Parse(electionId)),
                preparedBallotId,
                preparedBallotHash,
                votingView.BallotDefinitionVersion,
                votingView.BallotDefinitionHash.ToByteArray(),
                ElectionSp04ProfileIds.ChallengeSpoilV1,
                Sp04ProofStatementId));
        prepareResponse.Successfull.Should().BeTrue(prepareResponse.Message);

        var preparedView = await GetElectionVotingViewAsync(client, electionId, actor);
        preparedView.PreparedBallotId.Should().Be(preparedBallotId.ToString());
        preparedView.PreparedBallotHash.Should().Be(preparedBallotHash);
        preparedView.PreparedBallotState.Should().Be(PreparedBallotStateProto.PreparedBallotPrepared);
        preparedView.ChallengeSatisfied.Should().BeTrue();
        preparedView.Sp04BlockerCode.Should().BeEmpty();

        return new Sp04CastBinding(
            preparedBallotId,
            preparedBallotHash,
            receiptCommitment,
            Sp04ReceiptCommitmentScheme,
            votingView.BallotDefinitionVersion,
            votingView.BallotDefinitionHash.ToByteArray());
    }

    private async Task SubmitFinalizationShareViaBlockchainAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity trustee)
    {
        var currentResponse = await ReloadElectionAsync(client, electionId, TestIdentities.Alice);
        var session = currentResponse.FinalizationSessions
            .Single(x => x.Status == ElectionFinalizationSessionStatusProto.FinalizationSessionAwaitingShares);
        var shareIndex = session.EligibleTrustees
            .Select((reference, index) => new { reference.TrusteeUserAddress, ShareIndex = index + 1 })
            .Single(x => x.TrusteeUserAddress == trustee.PublicSigningAddress)
            .ShareIndex;
        var ceremonyVersionId = string.IsNullOrWhiteSpace(session.CeremonySnapshot?.CeremonyVersionId)
            ? (Guid?)null
            : Guid.Parse(session.CeremonySnapshot.CeremonyVersionId);
        var issuedShare = await ResolveIssuedCloseCountingShareAsync(client, electionId, trustee);

        var submitResponse = await SubmitBlockchainTransactionAsync(
            TestTransactionFactory.SubmitElectionFinalizationShare(
                trustee,
                new ElectionId(Guid.Parse(electionId)),
                Guid.Parse(session.Id),
                shareIndex,
                issuedShare.ShareVersion,
                ElectionFinalizationTargetType.AggregateTally,
                Guid.Parse(session.CloseArtifactId),
                session.AcceptedBallotSetHash.ToByteArray(),
                session.FinalEncryptedTallyHash.ToByteArray(),
                session.TargetTallyId,
                ceremonyVersionId,
                session.CeremonySnapshot?.TallyPublicKeyFingerprint,
                issuedShare.ShareMaterial,
                string.IsNullOrWhiteSpace(session.CloseCountingJobId)
                    ? null
                    : Guid.Parse(session.CloseCountingJobId),
                string.IsNullOrWhiteSpace(session.ExecutorSessionPublicKey)
                    ? null
                    : session.ExecutorSessionPublicKey,
                string.IsNullOrWhiteSpace(session.ExecutorKeyAlgorithm)
                    ? null
                    : session.ExecutorKeyAlgorithm));
        submitResponse.Successfull.Should().BeTrue(submitResponse.Message);
    }

    private async Task<IssuedCloseCountingShare> ResolveIssuedCloseCountingShareAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity trustee)
    {
        var actionView = await client.GetElectionCeremonyActionViewAsync(
            new GetElectionCeremonyActionViewRequest
            {
                ElectionId = electionId,
                ActorPublicAddress = trustee.PublicSigningAddress,
            },
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.GetElectionCeremonyActionView),
                trustee,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = electionId,
                    ["ActorPublicAddress"] = trustee.PublicSigningAddress,
                }));

        actionView.Success.Should().BeTrue(actionView.ErrorMessage);
        var vaultEnvelope = actionView.SelfVaultEnvelopes.Should()
            .ContainSingle(x => x.PayloadVersion == "omega-trustee-release-share-v1")
            .Subject;
        var encryptedPayload = Encoding.UTF8.GetString(vaultEnvelope.EncryptedPayload.ToByteArray());
        var decryptedPayload = EncryptKeys.Decrypt(encryptedPayload, trustee.PrivateEncryptKey);
        var releaseEnvelope = JsonSerializer.Deserialize<TrusteeReleaseEnvelopeDto>(
            decryptedPayload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        releaseEnvelope.Should().NotBeNull();
        releaseEnvelope!.ElectionId.Should().Be(electionId);
        releaseEnvelope.TrusteeUserAddress.Should().Be(trustee.PublicSigningAddress);
        releaseEnvelope.Material.CloseCountingShare.ScalarMaterial.Should().NotBeNullOrWhiteSpace();

        return new IssuedCloseCountingShare(
            releaseEnvelope.ShareVersion,
            releaseEnvelope.Material.CloseCountingShare.ScalarMaterial);
    }

    private async Task<GetElectionVotingViewResponse> GetElectionVotingViewAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey = "")
    {
        async Task<GetElectionVotingViewResponse> QueryAsync()
        {
            var request = new GetElectionVotingViewRequest
            {
                ElectionId = electionId,
                ActorPublicAddress = actor.PublicSigningAddress,
                SubmissionIdempotencyKey = submissionIdempotencyKey,
            };

            return await client.GetElectionVotingViewAsync(
                request,
                headers: CreateSignedElectionQueryHeaders(
                    nameof(HushElections.HushElectionsClient.GetElectionVotingView),
                    actor,
                    new Dictionary<string, object?>
                    {
                        ["ElectionId"] = request.ElectionId,
                        ["ActorPublicAddress"] = request.ActorPublicAddress,
                        ["SubmissionIdempotencyKey"] = request.SubmissionIdempotencyKey,
                    }));
        }

        var response = await QueryAsync();
        for (var attempt = 0; attempt < 20 && !response.Success; attempt++)
        {
            await Task.Delay(100);
            response = await QueryAsync();
        }

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response;
    }

    private async Task<GetElectionResultViewResponse> GetElectionResultViewAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        bool waitForOfficialResult = false)
    {
        async Task<GetElectionResultViewResponse> QueryAsync()
        {
            var request = new GetElectionResultViewRequest
            {
                ElectionId = electionId,
                ActorPublicAddress = actor.PublicSigningAddress,
            };

            return await client.GetElectionResultViewAsync(
                request,
                headers: CreateSignedElectionQueryHeaders(
                    nameof(HushElections.HushElectionsClient.GetElectionResultView),
                    actor,
                    new Dictionary<string, object?>
                    {
                        ["ElectionId"] = request.ElectionId,
                        ["ActorPublicAddress"] = request.ActorPublicAddress,
                    }));
        }

        var response = await QueryAsync();
        for (var attempt = 0;
             attempt < 20 &&
             (waitForOfficialResult && string.IsNullOrWhiteSpace(response.OfficialResult?.Id));
             attempt++)
        {
            await Task.Delay(100);
            response = await QueryAsync();
        }

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response;
    }

    private async Task<GetElectionVerificationPackageStatusResponse> GetElectionVerificationPackageStatusAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor)
    {
        var request = new GetElectionVerificationPackageStatusRequest
        {
            ElectionId = electionId,
            ActorPublicAddress = actor.PublicSigningAddress,
        };

        return await client.GetElectionVerificationPackageStatusAsync(
            request,
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.GetElectionVerificationPackageStatus),
                actor,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = request.ElectionId,
                    ["ActorPublicAddress"] = request.ActorPublicAddress,
                }));
    }

    private async Task<ExportElectionVerificationPackageResponse> ExportElectionVerificationPackageAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        ElectionVerificationPackageViewProto packageView)
    {
        var request = new ExportElectionVerificationPackageRequest
        {
            ElectionId = electionId,
            ActorPublicAddress = actor.PublicSigningAddress,
            PackageView = packageView,
        };

        return await client.ExportElectionVerificationPackageAsync(
            request,
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.ExportElectionVerificationPackage),
                actor,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = request.ElectionId,
                    ["ActorPublicAddress"] = request.ActorPublicAddress,
                    ["PackageView"] = request.PackageView,
                }));
    }

    private async Task<VerifyElectionReceiptResponse> VerifyElectionReceiptAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor,
        string receiptCommitment,
        string preparedBallotId)
    {
        var request = new VerifyElectionReceiptRequest
        {
            ElectionId = electionId,
            ActorPublicAddress = actor.PublicSigningAddress,
            ReceiptCommitment = receiptCommitment,
            PreparedBallotId = preparedBallotId,
        };

        return await client.VerifyElectionReceiptAsync(
            request,
            headers: CreateSignedElectionQueryHeaders(
                nameof(HushElections.HushElectionsClient.VerifyElectionReceipt),
                actor,
                new Dictionary<string, object?>
                {
                    ["ElectionId"] = request.ElectionId,
                    ["ActorPublicAddress"] = request.ActorPublicAddress,
                    ["ReceiptId"] = request.ReceiptId,
                    ["AcceptanceId"] = request.AcceptanceId,
                    ["ServerProof"] = request.ServerProof,
                    ["ReceiptCommitment"] = request.ReceiptCommitment,
                    ["PreparedBallotId"] = request.PreparedBallotId,
                }));
    }

    private static void WriteVerificationPackageToDirectory(
        ExportElectionVerificationPackageResponse response,
        string packagePath)
    {
        response.Success.Should().BeTrue(response.ErrorMessage);
        Directory.CreateDirectory(packagePath);

        foreach (var file in response.Files)
        {
            var fullPath = Path.Combine(
                packagePath,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, file.Content.ToByteArray());
        }
    }

    private static JsonDocument ParsePackageJson(
        ExportElectionVerificationPackageResponse response,
        string relativePath) =>
        JsonDocument.Parse(GetPackageFileText(response, relativePath));

    private static string GetPackageFileText(
        ExportElectionVerificationPackageResponse response,
        string relativePath)
    {
        var file = response.Files.Should()
            .ContainSingle(x => x.RelativePath == relativePath)
            .Subject;
        return Encoding.UTF8.GetString(file.Content.ToByteArray());
    }

    private async Task<GetElectionResponse> ReloadElectionAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity? actor = null)
    {
        var request = new GetElectionRequest
        {
            ElectionId = electionId,
        };
        GetElectionResponse response;
        if (actor is null)
        {
            response = await client.GetElectionAsync(request);
        }
        else
        {
            response = await client.GetElectionAsync(
                request,
                headers: CreateSignedElectionQueryHeaders(
                    nameof(HushElections.HushElectionsClient.GetElection),
                    actor,
                    new Dictionary<string, object?>
                    {
                        ["ElectionId"] = request.ElectionId,
                    }));
        }

        response.Success.Should().BeTrue(response.ErrorMessage);
        return response;
    }

    private async Task<GetElectionResponse> WaitForTallyReadyElectionAsync(
        HushElections.HushElectionsClient client,
        string electionId,
        TestIdentity actor)
    {
        var response = await ReloadElectionAsync(client, electionId, actor);

        for (var attempt = 0;
             attempt < 20 &&
             string.IsNullOrWhiteSpace(response.Election.TallyReadyArtifactId);
             attempt++)
        {
            await Task.Delay(100);
            response = await ReloadElectionAsync(client, electionId, actor);
        }

        return response;
    }

    private static Metadata CreateSignedElectionQueryHeaders(
        string method,
        TestIdentity actor,
        IReadOnlyDictionary<string, object?> request)
    {
        var signedAt = DateTimeOffset.UtcNow.ToString("O");
        var payload = BuildSignedElectionQueryPayload(
            method,
            actor.PublicSigningAddress,
            signedAt,
            request);

        return new Metadata
        {
            { "x-hush-election-query-signatory", actor.PublicSigningAddress },
            { "x-hush-election-query-signed-at", signedAt },
            { "x-hush-election-query-signature", DigitalSignature.SignMessageCompactBase64(payload, actor.PrivateSigningKey) },
        };
    }

    private static string BuildSignedElectionQueryPayload(
        string method,
        string actorAddress,
        string signedAt,
        IReadOnlyDictionary<string, object?> request)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["actorAddress"] = actorAddress,
            ["method"] = method,
            ["request"] = DeepSortElectionQueryValue(request),
            ["signedAt"] = signedAt,
        };

        return JsonSerializer.Serialize(payload);
    }

    private static object? DeepSortElectionQueryValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            var sortedDictionary = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in readOnlyDictionary)
            {
                sortedDictionary[entry.Key] = DeepSortElectionQueryValue(entry.Value);
            }

            return sortedDictionary;
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            var sortedDictionary = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in dictionary)
            {
                sortedDictionary[entry.Key] = DeepSortElectionQueryValue(entry.Value);
            }

            return sortedDictionary;
        }

        if (value is IEnumerable<object?> sequence && value is not string)
        {
            return sequence.Select(DeepSortElectionQueryValue).ToArray();
        }

        return value;
    }

    private async Task<SubmitSignedTransactionReply> SubmitBlockchainTransactionAsync(string signedTransaction)
    {
        var blockchainClient = _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();
        using var waiter = _node!.StartListeningForTransactions(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        var submitResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction,
        });

        if (submitResponse.Successfull)
        {
            await waiter.WaitAsync();
            await _blockControl!.ProduceBlockAsync();
        }

        return submitResponse;
    }

    private async Task CorruptCloseEligibilitySnapshotBoundaryAsync(string electionId)
    {
        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var electionKey = new ElectionId(Guid.Parse(electionId));
        var snapshot = await dbContext.Set<ElectionEligibilitySnapshotRecord>()
            .SingleAsync(x =>
                x.ElectionId == electionKey &&
                x.SnapshotType == ElectionEligibilitySnapshotType.Close);

        var updated = snapshot with
        {
            BoundaryArtifactId = Guid.NewGuid(),
        };

        dbContext.Entry(snapshot).State = EntityState.Detached;
        dbContext.Update(updated);
        await dbContext.SaveChangesAsync();
    }

    private async Task RestoreCloseEligibilitySnapshotBoundaryAsync(string electionId, Guid closeArtifactId)
    {
        await using var scope = _node!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var electionKey = new ElectionId(Guid.Parse(electionId));
        var snapshot = await dbContext.Set<ElectionEligibilitySnapshotRecord>()
            .SingleAsync(x =>
                x.ElectionId == electionKey &&
                x.SnapshotType == ElectionEligibilitySnapshotType.Close);

        var updated = snapshot with
        {
            BoundaryArtifactId = closeArtifactId,
        };

        dbContext.Entry(snapshot).State = EntityState.Detached;
        dbContext.Update(updated);
        await dbContext.SaveChangesAsync();
    }

    private static string BuildEncryptedBallotPackage(
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey,
        int selectionCount,
        int choiceIndex,
        ReactionECPoint tallyPublicKey)
    {
        var curve = new BabyJubJubCurve();
        var nonceSeed = ParseSeedToScalar(
            $"feat100:nonces:{electionId}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}",
            curve.Order);
        var ballot = ControlledElectionHarness.EncryptOneHotBallot(
            ballotId: $"feat100-ballot:{electionId}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}",
            choiceIndex: choiceIndex,
            publicKey: tallyPublicKey,
            nonces: ControlledElectionHarness.CreateDeterministicNonceSequence(
                nonceSeed,
                selectionCount,
                curve),
            selectionCount: selectionCount,
            curve: curve);

        var payload = new PublishedElectionBallotPackage(
            Version: "omega-binding-ballot-v1",
            PublicKey: ToPublishedPoint(tallyPublicKey),
            SelectionCount: selectionCount,
            Ciphertext: new PublishedElectionCiphertext(
                ballot.Slots.Select(slot => ToPublishedPoint(slot.C1)).ToArray(),
                ballot.Slots.Select(slot => ToPublishedPoint(slot.C2)).ToArray()));

        return JsonSerializer.Serialize(payload, CamelCaseJsonOptions);
    }

    private static string BuildProofBundle(
        GetElectionVotingViewResponse votingView,
        string ballotPackage) =>
        JsonSerializer.Serialize(new
        {
            version = "omega-binding-proof-v1",
            proofType = "binding-circuit-envelope",
            proofProfile = "PRODUCTION_LIKE_PROFILE",
            circuitVersion = "omega-v1.0.0",
            artifactShape = "opaque-one-hot-elgamal",
            ballotPackageHash = ComputeLowerHexSha256(ballotPackage),
            openArtifactId = votingView.OpenArtifactId,
            eligibleSetHash = votingView.EligibleSetHash,
            ceremonyVersionId = votingView.CeremonyVersionId,
            dkgProfileId = votingView.DkgProfileId,
            tallyPublicKeyFingerprint = votingView.TallyPublicKeyFingerprint,
        });

    private static string BuildBallotNullifier(
        string electionId,
        TestIdentity actor,
        string submissionIdempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"feat099:ballot-nullifier:{electionId}:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}")));

    private static string ComputeLowerHexSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))
            .ToLowerInvariant();

    private static int ResolveChoiceIndex(
        TestIdentity actor,
        string submissionIdempotencyKey,
        IReadOnlyList<int> availableChoiceIndexes)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"feat100:choice-index:{actor.PublicSigningAddress}:{submissionIdempotencyKey.Trim()}"));
        var scalar = new BigInteger(digest, isUnsigned: true, isBigEndian: true);
        return availableChoiceIndexes[(int)(scalar % availableChoiceIndexes.Count)];
    }

    private static string BuildFinalizationShareMaterial(
        string electionId,
        TestIdentity trustee,
        ElectionFinalizationSession session)
    {
        var curve = new BabyJubJubCurve();
        var publicKeySeed = ParseSeedToScalar($"feat100:public-key:{electionId}", curve.Order);
        var keyPair = ControlledElectionHarness.CreateDeterministicKeyPair(publicKeySeed, curve);
        var thresholdSeed = keyPair.PrivateKey - 7920;
        var trusteeIds = ImmutableArray.CreateRange(session.EligibleTrustees.Select(x => x.TrusteeUserAddress));
        var thresholdDefinition = new ControlledElectionThresholdDefinition(
            electionId,
            trusteeIds,
            session.RequiredShareCount);
        var thresholdSetup = ControlledElectionHarness.CreateControlledThresholdSetup(
            thresholdDefinition,
            session.Id,
            session.TargetTallyId,
            thresholdSeed,
            curve);

        return thresholdSetup.Shares
            .Single(x => string.Equals(x.TrusteeId, trustee.PublicSigningAddress, StringComparison.Ordinal))
            .ShareMaterial;
    }

    private static BigInteger ParseSeedToScalar(string seed, BigInteger order)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var scalar = new BigInteger(digest, isUnsigned: true, isBigEndian: true) % order;
        return scalar == BigInteger.Zero ? BigInteger.One : scalar;
    }

    private static PublishedElectionPointPayload ToPublishedPoint(ReactionECPoint point) =>
        new(
            point.X.ToString(CultureInfo.InvariantCulture),
            point.Y.ToString(CultureInfo.InvariantCulture));

    private static IReadOnlyList<ElectionRosterImportItem> BuildOpenReadyRosterEntries() =>
    [
        new ElectionRosterImportItem("voter-alice", ElectionRosterContactType.Email, "alice.eligibility@hush.test"),
        new ElectionRosterImportItem("voter-bob", ElectionRosterContactType.Phone, "+15550001002", IsInitiallyActive: false),
        new ElectionRosterImportItem("voter-charlie", ElectionRosterContactType.Email, "charlie.eligibility@hush.test"),
        new ElectionRosterImportItem("voter-guest", ElectionRosterContactType.Email, "guest.eligibility@hush.test"),
    ];

    private sealed class TemporaryPackageDirectory : IDisposable
    {
        public string PackagePath { get; } = Path.Combine(
            Path.GetTempPath(),
            $"hush-verification-package-integration-{Guid.NewGuid():N}");

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

    private static ElectionDraftSpecification BuildTrusteeThresholdDraftSpecification(string title) =>
        new(
            Title: title,
            ShortDescription: "Governed report package vote",
            ExternalReferenceCode: "REF-2026-102",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: ElectionBindingStatus.Binding,
            SelectedProfileId: "dkg-prod-3of5",
            GovernanceMode: ElectionGovernanceMode.TrusteeThreshold,
            DisclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            OutcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.PassFail,
                "pass_fail_yes_no",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: true,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "simple_majority_of_non_blank_votes"),
            ApprovedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            ProtocolOmegaVersion: "omega-v1.0.0",
            ReportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            OwnerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve the proposal", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject the proposal", 2, false),
            ],
            AcknowledgedWarningCodes:
            [
                ElectionWarningCode.AllTrusteesRequiredFragility,
            ],
            RequiredApprovalCount: 3,
            OfficialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

    private sealed record ClosedElectionReadyContext(string ElectionId);

    private sealed record Sp04CastBinding(
        Guid PreparedBallotId,
        string PreparedBallotHash,
        string ReceiptCommitment,
        string ReceiptCommitmentScheme,
        int BallotDefinitionVersion,
        byte[] BallotDefinitionHash);

    private sealed record IssuedCloseCountingShare(
        string ShareVersion,
        string ShareMaterial);

    private sealed record TrusteeReleaseEnvelopeDto(
        string PackageVersion,
        string MaterialKind,
        string ElectionId,
        string CeremonyVersionId,
        string TrusteeUserAddress,
        string ShareVersion,
        TrusteeReleaseMaterialDto Material);

    private sealed record TrusteeReleaseMaterialDto(
        string PackageKind,
        string SessionPurpose,
        string ProtocolVersion,
        string ProfileId,
        int VersionNumber,
        TrusteeReleaseCloseCountingShareDto CloseCountingShare);

    private sealed record TrusteeReleaseCloseCountingShareDto(
        string Format,
        string ScalarMaterial,
        string ScalarMaterialHash);

    private sealed record PublishedElectionBallotPackage(
        string Version,
        PublishedElectionPointPayload PublicKey,
        int SelectionCount,
        PublishedElectionCiphertext Ciphertext);

    private sealed record PublishedElectionCiphertext(
        PublishedElectionPointPayload[] C1,
        PublishedElectionPointPayload[] C2);

    private sealed record PublishedElectionPointPayload(
        string X,
        string Y);

    private sealed record PublishedElectionProofBundle(
        string Version,
        string Actor,
        string ElectionId,
        string SubmissionIdempotencyKey);
}
