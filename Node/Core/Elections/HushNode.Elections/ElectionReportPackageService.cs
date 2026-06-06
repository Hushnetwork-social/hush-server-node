using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public sealed class ElectionReportPackageService : IElectionReportPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public ElectionReportPackageBuildResult Build(ElectionReportPackageBuildRequest request)
    {
        var frozenEvidenceJson = SerializeJson(BuildFrozenEvidenceProjection(request));
        var frozenEvidenceHash = ComputeHashBytes(frozenEvidenceJson);
        var frozenEvidenceFingerprint = BuildHashHex(frozenEvidenceHash);

        try
        {
            var consistencyFailure = ValidateConsistency(request);
            if (consistencyFailure is not null)
            {
                return ElectionReportPackageBuildResult.Failure(CreateFailedAttempt(
                    request,
                    frozenEvidenceHash,
                    frozenEvidenceFingerprint,
                    consistencyFailure.Value.Code,
                    consistencyFailure.Value.Reason));
            }

            var packageId = request.PreassignedPackageId ?? Guid.NewGuid();
            var abnormalFinalizationEvidence = BuildAbnormalFinalizationEvidence(request, packageId);
            var trustees = request.TrusteeInvitations
                .Where(x => x.Status == ElectionTrusteeInvitationStatus.Accepted)
                .OrderBy(x => x.TrusteeDisplayName ?? x.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase)
                .Select(x => new TrusteeProjection(
                    x.TrusteeUserAddress,
                    x.TrusteeDisplayName,
                    x.Status.ToString(),
                    x.SentAt,
                    x.RespondedAt))
                .ToArray();
            var participationLookup = request.ParticipationRecords.ToDictionary(
                x => x.OrganizationVoterId,
                StringComparer.OrdinalIgnoreCase);
            var rosterEntries = request.RosterEntries
                .OrderBy(x => x.OrganizationVoterId, StringComparer.OrdinalIgnoreCase)
                .Select(x => BuildRosterEntryProjection(x, participationLookup.GetValueOrDefault(x.OrganizationVoterId)))
                .ToArray();
            var warningEvidence = BuildWarningEvidenceProjections(request);
            var governedApprovalProjections = request.FinalizationGovernedApprovals
                .OrderBy(x => x.ApprovedAt)
                .ThenBy(x => x.TrusteeDisplayName ?? x.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase)
                .Select(BuildGovernedApprovalProjection)
                .ToArray();
            var finalizationShareProjections = request.FinalizationShares
                .OrderBy(x => x.ShareIndex)
                .ThenBy(x => x.SubmittedAt)
                .Select(BuildFinalizationShareProjection)
                .ToArray();
            var officialResultProjection = BuildResultArtifactProjection(request.OfficialResult);
            var officialResultHash = ComputeHashBytes(SerializeJson(officialResultProjection));
            var ceremonyPublicKey = ResolveCeremonyPublicKeyProjection(request);
            var deploymentProofLedgerArtifactId = request.DeploymentProofBindingLedger is null
                ? (Guid?)null
                : Guid.NewGuid();
            var deploymentProofLedgerHash = request.DeploymentProofBindingLedger is null
                ? null
                : ComputeHashBytes(SerializeJson(request.DeploymentProofBindingLedger));
            var deploymentProofBindingProjection = BuildDeploymentProofBindingProjection(
                request.DeploymentProofBindingLedger,
                deploymentProofLedgerArtifactId,
                deploymentProofLedgerHash);
            var machineRestrictedAnomalyIntakeManifestId = request.RestrictedAnomalyIntakeManifest is null
                ? (Guid?)null
                : Guid.NewGuid();
            var publicAnomalySummary = ElectionAnomalyPublicSummaryBuilder.Build(new(
                request.Election.ElectionId.ToString(),
                request.RestrictedAnomalyIntakeManifest,
                machineRestrictedAnomalyIntakeManifestId,
                request.AttemptedAt));
            var anomalyReportReadiness = ElectionAnomalyReportReadinessProjectionBuilder.Build(new(
                publicAnomalySummary,
                request.RestrictedAnomalyIntakeManifest,
                ElectionAnomalyPublicArtifactScanStatusIds.Passed));
            var outcomeProjection = BuildOutcomeProjection(
                request.Election,
                request.OfficialResult,
                request.CloseEligibilitySnapshot);
            var resultReportProjection = BuildResultReportProjection(
                request.Election,
                request.OfficialResult,
                outcomeProjection,
                ceremonyPublicKey,
                publicAnomalySummary,
                anomalyReportReadiness);
            var protocolPackageBindingProjection = BuildProtocolPackageBindingProjection(request.ProtocolPackageBinding);
            var operationalSecurityProjection = BuildOperationalSecurityProjection(request);
            var regulatoryClaimProjection = BuildRegulatoryClaimProjection(request);
            var auditProjection = BuildAuditProjection(
                request,
                protocolPackageBindingProjection,
                operationalSecurityProjection,
                regulatoryClaimProjection,
                deploymentProofBindingProjection,
                frozenEvidenceFingerprint,
                trustees,
                warningEvidence,
                governedApprovalProjections,
                finalizationShareProjections,
                BuildHashHex(officialResultHash));
            var manifestProjection = BuildManifestProjection(
                request,
                packageId,
                frozenEvidenceFingerprint,
                trustees.Length,
                rosterEntries.Length,
                protocolPackageBindingProjection,
                operationalSecurityProjection,
                regulatoryClaimProjection,
                deploymentProofBindingProjection,
                outcomeProjection,
                warningEvidence,
                governedApprovalProjections,
                finalizationShareProjections,
                BuildHashHex(officialResultHash));
            var evidenceGraphProjection = BuildEvidenceGraphProjection(
                request,
                trustees,
                rosterEntries.Length,
                warningEvidence.Length,
                governedApprovalProjections.Length,
                finalizationShareProjections.Length,
                protocolPackageBindingProjection,
                operationalSecurityProjection,
                regulatoryClaimProjection,
                deploymentProofBindingProjection);

            var machineManifestId = Guid.NewGuid();
            var humanManifestId = Guid.NewGuid();
            var machineEvidenceGraphId = Guid.NewGuid();
            var machineResultId = Guid.NewGuid();
            var humanResultId = Guid.NewGuid();
            var machineRosterId = Guid.NewGuid();
            var humanRosterId = Guid.NewGuid();
            var machineAuditId = Guid.NewGuid();
            var humanAuditId = Guid.NewGuid();
            var machineOutcomeId = Guid.NewGuid();
            var humanOutcomeId = Guid.NewGuid();
            var machineDisputeId = Guid.NewGuid();
            var humanDisputeId = Guid.NewGuid();
            var artifacts = new List<ElectionReportArtifactRecord>
            {
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineManifestId,
                    humanManifestId,
                    ElectionReportArtifactKind.MachineManifest,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    7,
                    "Canonical manifest",
                    "canonical-manifest.json",
                    manifestProjection with
                    {
                        MachineArtifactId = machineManifestId,
                        HumanArtifactId = humanManifestId,
                        EvidenceGraphArtifactId = machineEvidenceGraphId,
                    }),
                CreateMarkdownArtifact(
                    request,
                    packageId,
                    humanManifestId,
                    machineManifestId,
                    ElectionReportArtifactKind.HumanManifest,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    1,
                    "Final manifest",
                    "final-manifest.md",
                    BuildHumanManifestContent(
                        manifestProjection,
                        packageId,
                        frozenEvidenceFingerprint,
                        machineManifestId,
                        humanManifestId,
                        machineEvidenceGraphId)),
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineEvidenceGraphId,
                    null,
                    ElectionReportArtifactKind.MachineEvidenceGraph,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    8,
                    "Evidence graph",
                    "evidence-graph.json",
                    evidenceGraphProjection with
                    {
                        ArtifactId = machineEvidenceGraphId,
                        ManifestArtifactId = machineManifestId,
                        RestrictedAnomalyIntakeManifest = BuildRestrictedAnomalyIntakeManifestEvidenceGraphNode(
                            request.RestrictedAnomalyIntakeManifest,
                            machineRestrictedAnomalyIntakeManifestId),
                    }),
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineResultId,
                    humanResultId,
                    ElectionReportArtifactKind.MachineResultReportProjection,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    9,
                    "Final result report projection",
                    "result-report.json",
                    resultReportProjection with
                    {
                        MachineArtifactId = machineResultId,
                        HumanArtifactId = humanResultId,
                    }),
                CreateMarkdownArtifact(
                    request,
                    packageId,
                    humanResultId,
                    machineResultId,
                    ElectionReportArtifactKind.HumanResultReport,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    2,
                    "Final result report",
                    "final-result-report.md",
                    BuildHumanResultReportContent(resultReportProjection)),
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineRosterId,
                    humanRosterId,
                    ElectionReportArtifactKind.MachineNamedParticipationRosterProjection,
                    ElectionReportArtifactAccessScope.OwnerAuditorOnly,
                    10,
                    "Named participation roster projection",
                    "named-participation-roster.json",
                    new RosterProjection(
                        machineRosterId,
                        humanRosterId,
                        request.Election.ElectionId.ToString(),
                        request.Election.BindingStatus.ToString(),
                        request.Election.BindingStatus == ElectionBindingStatus.NonBinding,
                        request.Election.GovernanceMode.ToString(),
                        request.Election.SelectedProfileId,
                        GetCircuitClassificationLabel(request.Election),
                        GetModeProfileFamilyLabel(request.Election),
                        rosterEntries.Length,
                        rosterEntries)),
                CreateMarkdownArtifact(
                    request,
                    packageId,
                    humanRosterId,
                    machineRosterId,
                    ElectionReportArtifactKind.HumanNamedParticipationRoster,
                    ElectionReportArtifactAccessScope.OwnerAuditorOnly,
                    3,
                    "Named participation roster",
                    "named-participation-roster.md",
                    BuildHumanRosterContent(request.Election, rosterEntries)),
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineAuditId,
                    humanAuditId,
                    ElectionReportArtifactKind.MachineAuditProvenanceReportProjection,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    11,
                    "Audit and provenance projection",
                    "audit-provenance-report.json",
                    auditProjection with
                    {
                        MachineArtifactId = machineAuditId,
                        HumanArtifactId = humanAuditId,
                    }),
                CreateMarkdownArtifact(
                    request,
                    packageId,
                    humanAuditId,
                    machineAuditId,
                    ElectionReportArtifactKind.HumanAuditProvenanceReport,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    4,
                    "Audit and provenance report",
                    "audit-provenance-report.md",
                    BuildHumanAuditContent(auditProjection)),
                CreateJsonArtifact(
                    request,
                    packageId,
                    machineOutcomeId,
                    humanOutcomeId,
                    ElectionReportArtifactKind.MachineOutcomeDeterminationProjection,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    12,
                    "Outcome determination projection",
                    "outcome-determination.json",
                    outcomeProjection with
                    {
                        MachineArtifactId = machineOutcomeId,
                        HumanArtifactId = humanOutcomeId,
                    }),
                CreateMarkdownArtifact(
                    request,
                    packageId,
                    humanOutcomeId,
                    machineOutcomeId,
                    ElectionReportArtifactKind.HumanOutcomeDetermination,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    5,
                    "Outcome determination",
                    "outcome-determination.md",
                    BuildHumanOutcomeContent(request.Election, outcomeProjection)),
            };

            if (request.RestrictedAnomalyIntakeManifest is not null &&
                machineRestrictedAnomalyIntakeManifestId.HasValue)
            {
                artifacts.Add(CreateJsonArtifact(
                    request,
                    packageId,
                    machineRestrictedAnomalyIntakeManifestId.Value,
                    null,
                    ElectionReportArtifactKind.MachineRestrictedAnomalyIntakeManifest,
                    ElectionReportArtifactAccessScope.OwnerAuditorOnly,
                    14,
                    "Restricted anomaly intake manifest",
                    "restricted-anomaly-intake-manifest.json",
                    BuildRestrictedAnomalyIntakeManifestArtifact(request.RestrictedAnomalyIntakeManifest)));
            }

            if (abnormalFinalizationEvidence is not null)
            {
                artifacts.Add(CreateJsonArtifact(
                    request,
                    packageId,
                    Guid.NewGuid(),
                    null,
                    ElectionReportArtifactKind.MachineAbnormalFinalizationEvidence,
                    ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                    15,
                    "Abnormal finalization evidence",
                    "abnormal-finalization-evidence.json",
                    abnormalFinalizationEvidence));
            }

            if (request.DeploymentProofBindingLedger is not null && deploymentProofLedgerArtifactId.HasValue)
            {
                var deploymentProofLedgerContent = SerializeJson(request.DeploymentProofBindingLedger);
                ValidateDeploymentProofPublicLedgerContent(deploymentProofLedgerContent);
                artifacts.Add(CreateJsonArtifact(
                    request,
                    packageId,
                    deploymentProofLedgerArtifactId.Value,
                    null,
                    ElectionReportArtifactKind.MachineDeploymentProofBindingLedger,
                    ElectionReportArtifactAccessScope.Public,
                    16,
                    "Deployment proof binding ledger",
                    ElectionDeploymentProofConstants.PublicLedgerArtifactFileName,
                    request.DeploymentProofBindingLedger));
            }

            var disputeCatalogEntries = artifacts
                .Select(x => new DisputeArtifactCatalogEntryProjection(
                    x.Id,
                    x.ArtifactKind.ToString(),
                    x.Format.ToString(),
                    x.AccessScope.ToString(),
                    x.Title,
                    x.FileName,
                    BuildHashHex(x.ContentHash),
                    x.PairedArtifactId))
                .OrderBy(x => x.ArtifactKind, StringComparer.Ordinal)
                .ThenBy(x => x.Title, StringComparer.Ordinal)
                .ToArray();

            artifacts.Add(CreateJsonArtifact(
                request,
                packageId,
                machineDisputeId,
                humanDisputeId,
                ElectionReportArtifactKind.MachineDisputeReviewIndexProjection,
                ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                13,
                "Dispute review index projection",
                "dispute-review-index.json",
                new DisputeReviewIndexProjection(
                    machineDisputeId,
                    humanDisputeId,
                    request.Election.ElectionId.ToString(),
                    packageId,
                    request.Election.BindingStatus.ToString(),
                    request.Election.BindingStatus == ElectionBindingStatus.NonBinding,
                    request.Election.GovernanceMode.ToString(),
                    request.Election.SelectedProfileId,
                    GetCircuitClassificationLabel(request.Election),
                    GetModeProfileFamilyLabel(request.Election),
                    disputeCatalogEntries)));
            artifacts.Add(CreateMarkdownArtifact(
                request,
                packageId,
                humanDisputeId,
                machineDisputeId,
                ElectionReportArtifactKind.HumanDisputeReviewIndex,
                ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
                6,
                "Dispute review index",
                "dispute-review-index.md",
                BuildHumanDisputeIndexContent(
                    request.Election,
                    packageId,
                    disputeCatalogEntries)));

            var packageHash = ComputeHashBytes(string.Join(
                "\n",
                artifacts
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.ArtifactKind)
                    .Select(x => $"{x.SortOrder}|{x.ArtifactKind}|{x.Format}|{BuildHashHex(x.ContentHash)}")));

            var package = ElectionModelFactory.CreateSealedReportPackage(
                request.Election.ElectionId,
                request.AttemptNumber,
                request.TallyReadyArtifact.Id,
                request.UnofficialResult.Id,
                request.OfficialResult.Id,
                request.FinalizeArtifact.Id,
                frozenEvidenceHash,
                frozenEvidenceFingerprint,
                packageHash,
                artifacts.Count,
                request.AttemptedByPublicAddress,
                previousAttemptId: request.PreviousAttemptId,
                finalizationSessionId: request.FinalizationSession?.Id,
                closeBoundaryArtifactId: request.CloseArtifact.Id,
                closeEligibilitySnapshotId: request.CloseEligibilitySnapshot?.Id,
                finalizationReleaseEvidenceId: request.FinalizationReleaseEvidence?.Id,
                attemptedAt: request.AttemptedAt,
                sealedAt: request.AttemptedAt,
                preassignedPackageId: packageId);

            return ElectionReportPackageBuildResult.Success(package, artifacts);
        }
        catch (Exception ex)
        {
            return ElectionReportPackageBuildResult.Failure(CreateFailedAttempt(
                request,
                frozenEvidenceHash,
                frozenEvidenceFingerprint,
                "PACKAGE_BUILD_FAILED",
                ex.Message));
        }
    }

    public ElectionVoidReportPackageBuildResult BuildVoid(ElectionVoidReportPackageBuildRequest request)
    {
        try
        {
            ValidateVoidRequest(request);

            var packageId = Guid.NewGuid();
            var publicSupersededArtifacts = request.SupersededArtifacts
                .OrderBy(x => x.ArtifactKind)
                .ThenBy(x => x.ArtifactRef, StringComparer.Ordinal)
                .Select(x => new ElectionVoidSupersededPublicArtifactReference(
                    x.ArtifactKind,
                    x.ArtifactRef,
                    x.ArtifactHash))
                .ToArray();
            var historicalUnofficialHash = request.HistoricalUnofficialResult is null
                ? null
                : CreateSha256Fingerprint(ComputeResultArtifactHash(request.HistoricalUnofficialResult));
            var packageArtifactRef = BuildVoidArtifactRef(packageId, VerificationPackageFileNames.VoidPackageArchive);
            var publicStatusArtifactRef = BuildVoidArtifactRef(packageId, VerificationPackageFileNames.VoidPublicStatus);
            var contentPackageHash = ComputeVoidContentPackageHash(
                request,
                publicSupersededArtifacts,
                historicalUnofficialHash);
            var contentPackageFingerprint = CreateSha256Fingerprint(contentPackageHash);
            var publicStatus = new ElectionVoidPublicStatusRecord(
                request.Election.ElectionId,
                request.Decision.Id,
                request.PublicationAttempt.Id,
                "VOID",
                request.Decision.PublicJustification,
                VerificationResultCodes.ElectionVoided,
                packageArtifactRef,
                contentPackageFingerprint,
                request.AttemptedAt,
                publicSupersededArtifacts);
            var restrictedEvidenceIndex = new ElectionVoidRestrictedEvidenceIndexRecord(
                request.Election.ElectionId,
                request.Decision.Id,
                request.PublicationAttempt.Id,
                request.Decision.EvidenceReferences,
                request.HistoricalUnofficialResult?.Id,
                historicalUnofficialHash,
                request.AttemptedAt);
            var verifierOutput = BuildVoidVerifierOutput(
                request,
                packageId,
                contentPackageFingerprint);

            var artifactIds = Enumerable.Range(0, 9)
                .Select(_ => Guid.NewGuid())
                .ToArray();
            var artifacts = new List<ElectionReportArtifactRecord>
            {
                CreateVoidJsonArtifact(
                    request,
                    packageId,
                    artifactIds[0],
                    ElectionReportArtifactKind.MachineVoidDecision,
                    ElectionReportArtifactAccessScope.Public,
                    1,
                    "VOID decision",
                    VerificationPackageFileNames.VoidDecision,
                    BuildPublicVoidDecisionProjection(request.Decision)),
                CreateVoidMarkdownArtifact(
                    request,
                    packageId,
                    artifactIds[1],
                    ElectionReportArtifactKind.HumanVoidSummary,
                    ElectionReportArtifactAccessScope.Public,
                    2,
                    "Public VOID summary",
                    VerificationPackageFileNames.PublicVoidSummary,
                    BuildPublicVoidSummaryContent(
                        request,
                        packageId,
                        contentPackageFingerprint,
                        publicSupersededArtifacts,
                        historicalUnofficialHash)),
                CreateVoidJsonArtifact(
                    request,
                    packageId,
                    artifactIds[2],
                    ElectionReportArtifactKind.MachineVoidPublicStatus,
                    ElectionReportArtifactAccessScope.Public,
                    3,
                    "Public VOID status",
                    VerificationPackageFileNames.VoidPublicStatus,
                    publicStatus),
                CreateVoidJsonArtifact(
                    request,
                    packageId,
                    artifactIds[3],
                    ElectionReportArtifactKind.MachineVoidSupersededArtifacts,
                    ElectionReportArtifactAccessScope.Public,
                    4,
                    "Superseded artifacts",
                    VerificationPackageFileNames.VoidSupersededArtifacts,
                    new VoidSupersededArtifactsProjection(
                        request.Election.ElectionId.ToString(),
                        request.Decision.Id,
                        request.PublicationAttempt.Id,
                        publicSupersededArtifacts)),
                CreateVoidJsonArtifact(
                    request,
                    packageId,
                    artifactIds[4],
                    ElectionReportArtifactKind.MachineVoidVerifierResult,
                    ElectionReportArtifactAccessScope.Public,
                    5,
                    "VOID verifier result",
                    VerificationPackageFileNames.VoidVerifierResult,
                    verifierOutput),
                CreateVoidMarkdownArtifact(
                    request,
                    packageId,
                    artifactIds[5],
                    ElectionReportArtifactKind.HumanRestrictedVoidEvidenceIndex,
                    ElectionReportArtifactAccessScope.OwnerAuditorOnly,
                    6,
                    "Restricted VOID evidence index",
                    VerificationPackageFileNames.RestrictedVoidEvidenceIndex,
                    BuildRestrictedVoidEvidenceIndexContent(restrictedEvidenceIndex)),
            };

            if (request.HistoricalUnofficialResult is not null)
            {
                artifacts.Add(CreateVoidJsonArtifact(
                    request,
                    packageId,
                    artifactIds[6],
                    ElectionReportArtifactKind.MachineRestrictedHistoricalUnofficialResult,
                    ElectionReportArtifactAccessScope.OwnerAuditorOnly,
                    7,
                    "Historical unofficial result",
                    VerificationPackageFileNames.RestrictedHistoricalUnofficialResult,
                    BuildHistoricalUnofficialResultProjection(request.HistoricalUnofficialResult)));
            }

            var manifest = BuildVoidPackageManifest(
                request,
                packageId,
                contentPackageFingerprint,
                artifacts
                    .Where(x => x.AccessScope == ElectionReportArtifactAccessScope.Public)
                    .ToArray());
            var manifestArtifact = CreateVoidJsonArtifact(
                request,
                packageId,
                artifactIds[7],
                ElectionReportArtifactKind.MachineVoidPackageManifest,
                ElectionReportArtifactAccessScope.Public,
                8,
                "VOID package manifest",
                VerificationPackageFileNames.VoidPackageManifest,
                manifest);
            artifacts.Add(manifestArtifact);
            var archiveBytes = BuildVoidPackageArchiveBytes(
                artifacts
                    .Where(x => x.AccessScope == ElectionReportArtifactAccessScope.Public)
                    .ToArray());
            artifacts.Add(CreateVoidBinaryArtifact(
                request,
                packageId,
                artifactIds[8],
                ElectionReportArtifactKind.MachineVoidPackageArchive,
                ElectionReportArtifactAccessScope.Public,
                9,
                "VOID package ZIP",
                VerificationPackageFileNames.VoidPackageArchive,
                "application/zip",
                archiveBytes));

            var sealedAttempt = ElectionModelFactory.CreateSealedVoidPublicationAttempt(
                request.Election.ElectionId,
                request.Decision.Id,
                request.PublicationAttempt.AttemptNumber,
                request.PublicationAttempt.FrozenEvidenceHash,
                request.PublicationAttempt.FrozenEvidenceFingerprint,
                contentPackageHash,
                artifacts.Count,
                request.AttemptedByPublicAddress,
                reportPackageId: packageId,
                previousAttemptId: request.PublicationAttempt.PreviousAttemptId,
                publicStatusArtifactRef: publicStatusArtifactRef,
                voidPackageArtifactRef: packageArtifactRef,
                attemptedAt: request.AttemptedAt,
                sealedAt: request.AttemptedAt,
                preassignedAttemptId: request.PublicationAttempt.Id);
            var package = ElectionModelFactory.CreateSealedVoidReportPackage(
                request.Election.ElectionId,
                request.AttemptNumber,
                request.Decision.Id,
                request.PublicationAttempt.Id,
                request.PublicationAttempt.FrozenEvidenceHash,
                request.PublicationAttempt.FrozenEvidenceFingerprint,
                contentPackageHash,
                artifacts.Count,
                request.AttemptedByPublicAddress,
                previousAttemptId: request.PreviousReportPackageId,
                attemptedAt: request.AttemptedAt,
                sealedAt: request.AttemptedAt,
                preassignedPackageId: packageId);

            return ElectionVoidReportPackageBuildResult.Success(
                package,
                sealedAttempt,
                publicStatus,
                restrictedEvidenceIndex,
                artifacts);
        }
        catch (Exception ex)
        {
            var failedAttempt = ElectionModelFactory.CreateFailedVoidPublicationAttempt(
                request.Election.ElectionId,
                request.Decision.Id,
                request.PublicationAttempt.AttemptNumber,
                request.PublicationAttempt.FrozenEvidenceHash,
                request.PublicationAttempt.FrozenEvidenceFingerprint,
                request.AttemptedByPublicAddress,
                "VOID_PACKAGE_BUILD_FAILED",
                ex.Message,
                previousAttemptId: request.PublicationAttempt.PreviousAttemptId,
                attemptedAt: request.AttemptedAt,
                preassignedAttemptId: request.PublicationAttempt.Id);
            var package = ElectionModelFactory.CreateFailedVoidReportPackageAttempt(
                request.Election.ElectionId,
                request.AttemptNumber,
                request.Decision.Id,
                request.PublicationAttempt.Id,
                request.PublicationAttempt.FrozenEvidenceHash,
                request.PublicationAttempt.FrozenEvidenceFingerprint,
                request.AttemptedByPublicAddress,
                "VOID_PACKAGE_BUILD_FAILED",
                ex.Message,
                previousAttemptId: request.PreviousReportPackageId,
                attemptedAt: request.AttemptedAt);

            return ElectionVoidReportPackageBuildResult.Failure(package, failedAttempt);
        }
    }

    private static ElectionReportPackageRecord CreateFailedAttempt(
        ElectionReportPackageBuildRequest request,
        byte[] frozenEvidenceHash,
        string frozenEvidenceFingerprint,
        string failureCode,
        string failureReason) =>
        ElectionModelFactory.CreateFailedReportPackageAttempt(
            request.Election.ElectionId,
            request.AttemptNumber,
            request.TallyReadyArtifact.Id,
            request.UnofficialResult.Id,
            frozenEvidenceHash,
            frozenEvidenceFingerprint,
            request.AttemptedByPublicAddress,
            failureCode,
            failureReason,
            previousAttemptId: request.PreviousAttemptId,
            finalizationSessionId: request.FinalizationSession?.Id,
            closeBoundaryArtifactId: request.CloseArtifact.Id,
            closeEligibilitySnapshotId: request.CloseEligibilitySnapshot?.Id,
            finalizationReleaseEvidenceId: request.FinalizationReleaseEvidence?.Id,
            attemptedAt: request.AttemptedAt,
            preassignedPackageId: request.PreassignedPackageId);

    private static (string Code, string Reason)? ValidateConsistency(ElectionReportPackageBuildRequest request)
    {
        if (request.CloseArtifact.ArtifactType != ElectionBoundaryArtifactType.Close)
        {
            return ("CONSISTENCY_MISMATCH", "Package generation requires the exact close boundary artifact.");
        }

        if (request.TallyReadyArtifact.ArtifactType != ElectionBoundaryArtifactType.TallyReady)
        {
            return ("CONSISTENCY_MISMATCH", "Package generation requires the exact tally-ready boundary artifact.");
        }

        if (request.FinalizeArtifact.ArtifactType != ElectionBoundaryArtifactType.Finalize)
        {
            return ("CONSISTENCY_MISMATCH", "Package generation requires the exact finalize boundary artifact.");
        }

        if (request.UnofficialResult.ArtifactKind != ElectionResultArtifactKind.Unofficial)
        {
            return ("CONSISTENCY_MISMATCH", "Package generation requires an unofficial result source artifact.");
        }

        if (request.OfficialResult.ArtifactKind != ElectionResultArtifactKind.Official)
        {
            return ("CONSISTENCY_MISMATCH", "Package generation requires an official result artifact.");
        }

        if (request.OfficialResult.SourceResultArtifactId != request.UnofficialResult.Id)
        {
            return ("CONSISTENCY_MISMATCH", "Official result lineage must point to the sealed unofficial result.");
        }

        if (!ByteArrayEquals(
                request.TallyReadyArtifact.AcceptedBallotSetHash,
                request.FinalizeArtifact.AcceptedBallotSetHash))
        {
            return ("CONSISTENCY_MISMATCH", "Finalize boundary accepted-ballot hash must match tally-ready evidence.");
        }

        if (!ByteArrayEquals(
                request.TallyReadyArtifact.FinalEncryptedTallyHash,
                request.FinalizeArtifact.FinalEncryptedTallyHash))
        {
            return ("CONSISTENCY_MISMATCH", "Finalize boundary tally hash must match tally-ready evidence.");
        }

        if (request.CloseEligibilitySnapshot is not null &&
            request.CloseEligibilitySnapshot.BoundaryArtifactId != request.CloseArtifact.Id)
        {
            return ("CONSISTENCY_MISMATCH", "Close eligibility snapshot must bind to the exact close artifact.");
        }

        if (request.FinalizationSession?.GovernedProposalId is Guid governedProposalId &&
            (request.FinalizationGovernedProposal is null ||
             request.FinalizationGovernedProposal.Id != governedProposalId))
        {
            return ("CONSISTENCY_MISMATCH", "Finalization governed proposal evidence must match the session-bound proposal id.");
        }

        if (request.FinalizationGovernedProposal is not null &&
            request.FinalizationGovernedProposal.ActionType != ElectionGovernedActionType.Finalize)
        {
            return ("CONSISTENCY_MISMATCH", "Report-package governance evidence must reference the finalize proposal.");
        }

        if (request.FinalizationGovernedApprovals.Any(x =>
                request.FinalizationGovernedProposal is null ||
                x.ProposalId != request.FinalizationGovernedProposal.Id))
        {
            return ("CONSISTENCY_MISMATCH", "Finalization governed approvals must bind to the selected finalize proposal.");
        }

        if (request.FinalizationShares.Any(x =>
                request.FinalizationSession is null ||
                x.FinalizationSessionId != request.FinalizationSession.Id))
        {
            return ("CONSISTENCY_MISMATCH", "Finalization share evidence must bind to the selected finalization session.");
        }

        return null;
    }

    private static FrozenEvidenceProjection BuildFrozenEvidenceProjection(ElectionReportPackageBuildRequest request) =>
        new(
            request.Election.ElectionId.ToString(),
            BuildSetupProjection(request),
            BuildBoundaryEvidenceProjection(request.CloseArtifact),
            request.CloseEligibilitySnapshot is null
                ? null
                : BuildEligibilitySnapshotProjection(request.CloseEligibilitySnapshot),
            BuildBoundaryEvidenceProjection(request.TallyReadyArtifact),
            BuildResultArtifactProjection(request.UnofficialResult),
            BuildResultArtifactProjection(request.OfficialResult),
            BuildProtocolPackageBindingProjection(request.ProtocolPackageBinding),
            BuildOperationalSecurityProjection(request),
            BuildRegulatoryClaimProjection(request),
            request.RestrictedAnomalyIntakeManifest,
            request.FinalizationSession is null
                ? null
                : new FinalizationSessionProjection(
                    request.FinalizationSession.Id,
                    request.FinalizationSession.SessionPurpose.ToString(),
                    request.FinalizationSession.Status.ToString(),
                    request.FinalizationSession.CloseArtifactId,
                    BuildHashHex(request.FinalizationSession.AcceptedBallotSetHash),
                    BuildHashHex(request.FinalizationSession.FinalEncryptedTallyHash),
                    request.FinalizationSession.TargetTallyId,
                    request.FinalizationSession.RequiredShareCount,
                    request.FinalizationSession.EligibleTrustees
                        .Select(x => new TrusteeProjection(
                            x.TrusteeUserAddress,
                            x.TrusteeDisplayName,
                            "Accepted",
                            null,
                            null))
                        .ToArray(),
                    request.FinalizationSession.CreatedAt,
                    request.FinalizationSession.CompletedAt,
                    request.FinalizationSession.ReleaseEvidenceId,
                    request.FinalizationSession.GovernedProposalId,
                    request.FinalizationSession.CreatedByPublicAddress),
            request.FinalizationReleaseEvidence is null
                ? null
                : new FinalizationReleaseProjection(
                    request.FinalizationReleaseEvidence.Id,
                    request.FinalizationReleaseEvidence.FinalizationSessionId,
                    request.FinalizationReleaseEvidence.ReleaseMode.ToString(),
                    request.FinalizationReleaseEvidence.CloseArtifactId,
                    BuildHashHex(request.FinalizationReleaseEvidence.AcceptedBallotSetHash),
                    BuildHashHex(request.FinalizationReleaseEvidence.FinalEncryptedTallyHash),
                    request.FinalizationReleaseEvidence.TargetTallyId,
                    request.FinalizationReleaseEvidence.AcceptedShareCount,
                    request.FinalizationReleaseEvidence.CompletedAt,
                    request.FinalizationReleaseEvidence.AcceptedTrustees
                        .Select(x => new TrusteeProjection(
                            x.TrusteeUserAddress,
                            x.TrusteeDisplayName,
                            "Accepted",
                            null,
                            null))
                        .ToArray()),
            BuildWarningEvidenceProjections(request),
            request.FinalizationGovernedProposal is null
                ? null
                : BuildGovernedProposalProjection(request.FinalizationGovernedProposal),
            request.FinalizationGovernedApprovals
                .OrderBy(x => x.ApprovedAt)
                .ThenBy(x => x.TrusteeDisplayName ?? x.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase)
                .Select(BuildGovernedApprovalProjection)
                .ToArray(),
            request.FinalizationShares
                .OrderBy(x => x.ShareIndex)
                .ThenBy(x => x.SubmittedAt)
                .Select(BuildFinalizationShareProjection)
                .ToArray());

    private static SetupProjection BuildSetupProjection(ElectionReportPackageBuildRequest request) =>
        new(
            request.CloseArtifact.Metadata.Title,
            request.CloseArtifact.Metadata.ShortDescription,
            request.CloseArtifact.Metadata.OwnerPublicAddress,
            request.CloseArtifact.Metadata.ExternalReferenceCode,
            request.CloseArtifact.SourceDraftRevision,
            request.CloseArtifact.Policy.BindingStatus.ToString(),
            request.CloseArtifact.Policy.BindingStatus == ElectionBindingStatus.NonBinding,
            request.CloseArtifact.Policy.GovernanceMode.ToString(),
            request.Election.SelectedProfileId,
            GetCircuitClassificationLabel(request.Election),
            GetModeProfileFamilyLabel(request.Election),
            request.CloseArtifact.Policy.ParticipationPrivacyMode.ToString(),
            request.CloseArtifact.Policy.ReportingPolicy.ToString(),
            request.CloseArtifact.Policy.ReviewWindowPolicy.ToString(),
            request.CloseArtifact.Policy.OfficialResultVisibilityPolicy.ToString(),
            GetSecrecyBoundarySummary(request.Election),
            GetGovernanceCustodySummary(request.Election),
            request.CloseArtifact.Policy.RequiredApprovalCount,
            request.CloseArtifact.Policy.ApprovedClientApplications
                .Select(x => new ApprovedClientProjection(x.ApplicationId, x.Version))
                .ToArray(),
            request.CloseArtifact.Options
                .OrderBy(x => x.BallotOrder)
                .Select(x => new ElectionOptionProjection(
                    x.OptionId,
                    x.DisplayLabel,
                    x.ShortDescription,
                    x.BallotOrder,
                    x.IsBlankOption))
                .ToArray(),
            request.CloseArtifact.TrusteeSnapshot is null
                ? null
                : BuildTrusteeThresholdProjection(request.CloseArtifact.TrusteeSnapshot),
            ResolveCeremonyPublicKeyProjection(request));

    private static BoundaryEvidenceProjection BuildBoundaryEvidenceProjection(ElectionBoundaryArtifactRecord artifact) =>
        new(
            artifact.Id,
            artifact.ArtifactType.ToString(),
            artifact.RecordedAt,
            BuildHashHex(artifact.FrozenEligibleVoterSetHash),
            BuildHashHex(artifact.AcceptedBallotSetHash),
            BuildHashHex(artifact.PublishedBallotStreamHash),
            BuildHashHex(artifact.FinalEncryptedTallyHash),
            artifact.SourceTransactionId,
            artifact.SourceBlockHeight,
            artifact.SourceBlockId);

    private static EligibilitySnapshotProjection BuildEligibilitySnapshotProjection(
        ElectionEligibilitySnapshotRecord snapshot) =>
        new(
            snapshot.Id,
            snapshot.SnapshotType.ToString(),
            snapshot.RecordedAt,
            snapshot.RosteredCount,
            snapshot.LinkedCount,
            snapshot.ActiveDenominatorCount,
            snapshot.CountedParticipationCount,
            snapshot.BlankCount,
            snapshot.DidNotVoteCount,
            BuildHashHex(snapshot.RosteredVoterSetHash),
            BuildHashHex(snapshot.ActiveDenominatorSetHash),
            BuildHashHex(snapshot.CountedParticipationSetHash));

    private static ResultArtifactProjection BuildResultArtifactProjection(ElectionResultArtifactRecord artifact) =>
        new(
            artifact.ArtifactKind.ToString(),
            artifact.Visibility.ToString(),
            artifact.NamedOptionResults.Select(x => new ResultOptionProjection(
                x.OptionId,
                x.DisplayLabel,
                x.ShortDescription,
                x.BallotOrder,
                x.Rank,
                x.VoteCount)).ToArray(),
            artifact.BlankCount,
            artifact.TotalVotedCount,
            artifact.EligibleToVoteCount,
            artifact.DidNotVoteCount,
            artifact.DenominatorEvidence.EligibilitySnapshotId,
            artifact.DenominatorEvidence.BoundaryArtifactId,
            BuildHashHex(artifact.DenominatorEvidence.ActiveDenominatorSetHash),
            artifact.SourceResultArtifactId);

    private static ProtocolPackageBindingProjection? BuildProtocolPackageBindingProjection(
        ProtocolPackageBindingRecord? binding)
    {
        if (binding is null)
        {
            return null;
        }

        var externalReviewSummary = ElectionSp09ExternalReviewRules.BuildCustomerSafeSummary(binding.ExternalReviewStatus);
        return new ProtocolPackageBindingProjection(
            binding.Id,
            binding.PackageId,
            binding.PackageVersion,
            binding.SelectedProfileId,
            binding.SpecPackageHash,
            binding.ProofPackageHash,
            binding.ReleaseManifestHash,
            binding.PackageApprovalStatus.ToString(),
            binding.ExternalReviewStatus.ToString(),
            externalReviewSummary.Availability,
            externalReviewSummary.ClaimState,
            externalReviewSummary.Wording,
            binding.Status.ToString(),
            binding.Source.ToString(),
            binding.DraftRevision,
            binding.BoundAt,
            binding.SealedAt,
            binding.BoundByPublicAddress,
            binding.SourceTransactionId,
            binding.SourceBlockHeight,
            binding.SourceBlockId,
            binding.SpecAccessLocations
                .OrderBy(x => x.LocationKind)
                .ThenBy(x => x.Label, StringComparer.Ordinal)
                .ThenBy(x => x.Location, StringComparer.Ordinal)
                .Select(BuildProtocolPackageAccessLocationProjection)
                .ToArray(),
            binding.ProofAccessLocations
                .OrderBy(x => x.LocationKind)
                .ThenBy(x => x.Label, StringComparer.Ordinal)
                .ThenBy(x => x.Location, StringComparer.Ordinal)
                .Select(BuildProtocolPackageAccessLocationProjection)
                .ToArray(),
            "Protocol package archives are referenced by immutable hashes and access locations; the final election report package does not embed the full protocol packages by default. Temporary access-location outage is operational and does not change the sealed election refs.");
    }

    private static ProtocolPackageAccessLocationProjection BuildProtocolPackageAccessLocationProjection(
        ProtocolPackageAccessLocationRecord accessLocation) =>
        new(
            accessLocation.LocationKind.ToString(),
            accessLocation.Label,
            accessLocation.Location,
            accessLocation.ContentHash);

    private static OperationalSecurityProjection BuildOperationalSecurityProjection(
        ElectionReportPackageBuildRequest request)
    {
        if (request.Sp10OperationalSecurityStatus is null)
        {
            var evidenceState = ElectionSp10ProfileIds.EvidenceStateDevelopmentPlaceholder;
            return new OperationalSecurityProjection(
                ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
                ElectionSp10ProfileIds.DeploymentProfileManagedAwsContainerV1,
                evidenceState,
                DoesNotCompleteFeat106Readiness: true,
                ElectionSp10OperationalSecurityRules.GetAllowedWordingForEvidenceState(evidenceState),
                request.FinalizationReleaseEvidence?.ReleaseMode.ToString(),
                ReleaseManifestHash: null,
                ImmutableDeploymentRef: request.DeploymentProofBindingLedger?.PublicCatalogRef,
                CustodyMode: ResolveSp10CustodyMode(request.Election),
                ExecutorKeyLifecycle: ElectionSp10ProfileIds.ExecutorKeyLifecycleEphemeralMemoryV1,
                IncidentStatus: ElectionSp10ProfileIds.IncidentStatusNoIncidentDeclared,
                ElectionSp10OperationalSecurityRules.BlocksHighAssurance(evidenceState),
                ElectionSp10OperationalSecurityRules.GetPrimaryResultCode(evidenceState),
                "Development-only SP-10 operational evidence is attached for this local rehearsal. Rollout readiness, legal validation, public-election approval, and certification remain out of scope.",
                PublicEvidenceFiles:
                [
                    VerificationPackageFileNames.Sp10OperationalSecuritySummary,
                    VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
                    VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
                    VerificationPackageFileNames.Sp10OperationalVerifierOutput,
                ],
                RestrictedEvidenceFiles: []);
        }

        var status = request.Sp10OperationalSecurityStatus;
        var errors = ElectionSp10OperationalSecurityRules.Validate(
            status,
            VerificationPackageView.RestrictedOwnerAuditor);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Report package SP-10 operational status is invalid: {string.Join("; ", errors)}");
        }

        return new OperationalSecurityProjection(
            status.ProgramVersion,
            status.DeploymentProfileId,
            status.EvidenceState,
            status.DoesNotCompleteFeat106Readiness,
            status.Feat106ReadinessCaveat,
            status.ReleaseEvidenceMode,
            status.ReleaseManifestHash,
            status.ImmutableDeploymentRef,
            status.CustodyMode,
            status.ExecutorKeyLifecycle,
            status.IncidentStatus,
            status.BlocksHighAssurance,
            status.PrimaryResultCode,
            status.PrimaryIssue,
            status.PublicEvidenceFiles,
            status.RestrictedEvidenceFiles);
    }

    private static RegulatoryClaimProjection? BuildRegulatoryClaimProjection(
        ElectionReportPackageBuildRequest request)
    {
        if (request.Sp11RegulatoryClaimState is null)
        {
            return null;
        }

        var claim = request.Sp11RegulatoryClaimState;
        if (!string.Equals(claim.Schema, ElectionSp11ProfileIds.RegulatoryClaimStateSchema, StringComparison.Ordinal) ||
            !ElectionSp11RegulatoryRules.IsSupportedClaimState(claim.ClaimState) ||
            claim.IsLegalAdvice ||
            ElectionSp11RegulatoryRules.ContainsForbiddenClaimPhrase(claim.AllowedWording))
        {
            throw new InvalidOperationException("Report package SP-11 regulatory claim state is invalid.");
        }

        return new RegulatoryClaimProjection(
            claim.TrackerVersion,
            claim.JurisdictionId,
            claim.ClaimId,
            claim.ClaimState,
            claim.SourceCheckedAt,
            claim.NextReviewAt,
            claim.SourceRef,
            claim.Owner,
            claim.RequiresAuthorityEvidence,
            claim.AuthorityEvidenceRef,
            IsTrackerStale: ElectionSp11RegulatoryRules.IsTrackerStale(claim, DateTimeOffset.UtcNow),
            claim.AllowedWording,
            claim.PublicEvidenceFiles,
            claim.RestrictedEvidenceFiles);
    }

    private static string ResolveSp10CustodyMode(ElectionRecord election) =>
        election.GovernanceMode == ElectionGovernanceMode.TrusteeThreshold
            ? ElectionSp10ProfileIds.CustodyModeTrusteeLocalSecureVaultV1
            : ElectionSp10ProfileIds.CustodyModeAwsKmsPerElectionEnvelopeV1;

    private static WarningEvidenceProjection[] BuildWarningEvidenceProjections(ElectionReportPackageBuildRequest request)
    {
        var warningsByCode = request.WarningAcknowledgements
            .GroupBy(x => x.WarningCode)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.AcknowledgedAt).First());

        var warningCodes = request.CloseArtifact.AcknowledgedWarningCodes
            .Concat(request.WarningAcknowledgements.Select(x => x.WarningCode))
            .Distinct()
            .OrderBy(x => x.ToString(), StringComparer.Ordinal)
            .ToArray();

        return warningCodes
            .Select(code =>
            {
                warningsByCode.TryGetValue(code, out var acknowledgement);
                return new WarningEvidenceProjection(
                    code.ToString(),
                    acknowledgement?.DraftRevision ?? request.CloseArtifact.SourceDraftRevision,
                    acknowledgement?.AcknowledgedByPublicAddress,
                    acknowledgement?.AcknowledgedAt,
                    acknowledgement?.SourceTransactionId,
                    acknowledgement?.SourceBlockHeight,
                    acknowledgement?.SourceBlockId);
            })
            .ToArray();
    }

    private static GovernedProposalProjection BuildGovernedProposalProjection(
        ElectionGovernedProposalRecord proposal) =>
        new(
            proposal.Id,
            proposal.ActionType.ToString(),
            proposal.LifecycleStateAtCreation.ToString(),
            proposal.ProposedByPublicAddress,
            proposal.CreatedAt);

    private static GovernedApprovalProjection BuildGovernedApprovalProjection(
        ElectionGovernedProposalApprovalRecord approval) =>
        new(
            approval.Id,
            approval.ProposalId,
            approval.ActionType.ToString(),
            approval.TrusteeUserAddress,
            approval.TrusteeDisplayName,
            approval.ApprovalNote,
            approval.ApprovedAt,
            approval.SourceTransactionId,
            approval.SourceBlockHeight,
            approval.SourceBlockId);

    private static FinalizationShareProjection BuildFinalizationShareProjection(
        ElectionFinalizationShareRecord share) =>
        new(
            share.Id,
            share.FinalizationSessionId,
            share.TrusteeUserAddress,
            share.TrusteeDisplayName,
            share.ShareIndex,
            share.TargetType.ToString(),
            share.Status.ToString(),
            BuildHashHex(share.ClaimedAcceptedBallotSetHash),
            BuildHashHex(share.ClaimedFinalEncryptedTallyHash),
            share.ClaimedTargetTallyId,
            share.ClaimedCeremonyVersionId,
            share.ClaimedTallyPublicKeyFingerprint,
            string.IsNullOrWhiteSpace(share.ShareMaterialHash)
                ? BuildHashHex(ComputeHashBytes(share.ShareMaterial))
                : share.ShareMaterialHash,
            share.FailureCode,
            share.FailureReason,
            share.SubmittedAt,
            share.SourceTransactionId,
            share.SourceBlockHeight,
            share.SourceBlockId);

    private static TrusteeThresholdProjection BuildTrusteeThresholdProjection(
        ElectionTrusteeBoundarySnapshot snapshot) =>
        new(
            snapshot.RequiredApprovalCount,
            snapshot.EveryAcceptedTrusteeMustApprove,
            snapshot.AcceptedTrustees
                .OrderBy(x => x.TrusteeDisplayName ?? x.TrusteeUserAddress, StringComparer.OrdinalIgnoreCase)
                .Select(x => new TrusteeProjection(
                    x.TrusteeUserAddress,
                    x.TrusteeDisplayName,
                    "Accepted",
                    null,
                    null))
                .ToArray());

    private static CeremonyPublicKeyProjection BuildCeremonyPublicKeyProjection(
        ElectionCeremonyBindingSnapshot snapshot) =>
        new(
            snapshot.CeremonyVersionId,
            snapshot.CeremonyVersionNumber,
            snapshot.ProfileId,
            snapshot.BoundTrusteeCount,
            snapshot.RequiredApprovalCount,
            snapshot.EveryActiveTrusteeMustApprove,
            snapshot.TallyPublicKeyFingerprint);

    private static CeremonyPublicKeyProjection? ResolveCeremonyPublicKeyProjection(
        ElectionReportPackageBuildRequest request)
    {
        var ceremonySnapshot = request.FinalizationSession?.CeremonySnapshot
            ?? ElectionProtectedTallyBinding.ResolveBoundaryBinding(request.Election, request.CloseArtifact);
        return ceremonySnapshot is null ? null : BuildCeremonyPublicKeyProjection(ceremonySnapshot);
    }

    private static ManifestProjection BuildManifestProjection(
        ElectionReportPackageBuildRequest request,
        Guid packageId,
        string frozenEvidenceFingerprint,
        int acceptedTrusteeCount,
        int rosterEntryCount,
        ProtocolPackageBindingProjection? protocolPackageBinding,
        OperationalSecurityProjection operationalSecurity,
        RegulatoryClaimProjection? regulatoryClaim,
        DeploymentProofBindingProjection? deploymentProofBinding,
        OutcomeDeterminationProjection outcomeProjection,
        IReadOnlyList<WarningEvidenceProjection> warningEvidence,
        IReadOnlyList<GovernedApprovalProjection> governedApprovals,
        IReadOnlyList<FinalizationShareProjection> finalizationShares,
        string officialResultHash) =>
        new(
            PackageId: packageId,
            MachineArtifactId: Guid.Empty,
            HumanArtifactId: Guid.Empty,
            EvidenceGraphArtifactId: Guid.Empty,
            ElectionId: request.Election.ElectionId.ToString(),
            ElectionTitle: request.Election.Title,
            AttemptNumber: request.AttemptNumber,
            PreviousAttemptId: request.PreviousAttemptId,
            AttemptedAt: request.AttemptedAt,
            AttemptedBy: request.AttemptedByPublicAddress,
            FrozenEvidenceFingerprint: frozenEvidenceFingerprint,
            BindingStatus: request.Election.BindingStatus.ToString(),
            IsNonBindingElection: request.Election.BindingStatus == ElectionBindingStatus.NonBinding,
            GovernanceMode: request.Election.GovernanceMode.ToString(),
            SelectedProfileId: request.Election.SelectedProfileId,
            CircuitClassification: GetCircuitClassificationLabel(request.Election),
            ModeProfileFamily: GetModeProfileFamilyLabel(request.Election),
            BoundCeremonyProfileId: ResolveCeremonyPublicKeyProjection(request)?.ProfileId,
            TallyPublicKeyFingerprint: ResolveCeremonyPublicKeyProjection(request)?.TallyPublicKeyFingerprint,
            OfficialVisibility: request.Election.OfficialResultVisibilityPolicy.ToString(),
            SecrecyBoundarySummary: GetSecrecyBoundarySummary(request.Election),
            CustodyBoundarySummary: GetGovernanceCustodySummary(request.Election),
            AcceptedTrusteeCount: acceptedTrusteeCount,
            RosterEntryCount: rosterEntryCount,
            ProtocolPackageBinding: protocolPackageBinding,
            OperationalSecurity: operationalSecurity,
            RegulatoryClaim: regulatoryClaim,
            DeploymentProofBinding: deploymentProofBinding,
            FinalizeArtifactId: request.FinalizeArtifact.Id,
            OfficialResultArtifactId: request.OfficialResult.Id,
            OfficialResultHash: officialResultHash,
            WarningCount: warningEvidence.Count,
            GovernedApprovalCount: governedApprovals.Count,
            FinalizationShareCount: finalizationShares.Count,
            OutcomeLabel: outcomeProjection.ConclusionLabel,
            OutcomeSummary: outcomeProjection.ConclusionSummary);

    private static EvidenceGraphProjection BuildEvidenceGraphProjection(
        ElectionReportPackageBuildRequest request,
        IReadOnlyList<TrusteeProjection> trustees,
        int rosterEntryCount,
        int warningCount,
        int governedApprovalCount,
        int finalizationShareCount,
        ProtocolPackageBindingProjection? protocolPackageBinding,
        OperationalSecurityProjection operationalSecurity,
        RegulatoryClaimProjection? regulatoryClaim,
        DeploymentProofBindingProjection? deploymentProofBinding) =>
        new(
            ArtifactId: Guid.Empty,
            ManifestArtifactId: Guid.Empty,
            ElectionId: request.Election.ElectionId.ToString(),
            BindingStatus: request.Election.BindingStatus.ToString(),
            IsNonBindingElection: request.Election.BindingStatus == ElectionBindingStatus.NonBinding,
            GovernanceMode: request.Election.GovernanceMode.ToString(),
            SelectedProfileId: request.Election.SelectedProfileId,
            CircuitClassification: GetCircuitClassificationLabel(request.Election),
            ModeProfileFamily: GetModeProfileFamilyLabel(request.Election),
            CloseArtifactId: request.CloseArtifact.Id,
            CloseEligibilitySnapshotId: request.CloseEligibilitySnapshot?.Id,
            TallyReadyArtifactId: request.TallyReadyArtifact.Id,
            UnofficialResultArtifactId: request.UnofficialResult.Id,
            OfficialResultArtifactId: request.OfficialResult.Id,
            FinalizeArtifactId: request.FinalizeArtifact.Id,
            FinalizationSessionId: request.FinalizationSession?.Id,
            FinalizationReleaseEvidenceId: request.FinalizationReleaseEvidence?.Id,
            AcceptedBallotSetHash: BuildHashHex(request.TallyReadyArtifact.AcceptedBallotSetHash),
            PublishedBallotStreamHash: BuildHashHex(request.TallyReadyArtifact.PublishedBallotStreamHash),
            FinalEncryptedTallyHash: BuildHashHex(request.TallyReadyArtifact.FinalEncryptedTallyHash),
            ActiveDenominatorSetHash: BuildHashHex(request.CloseEligibilitySnapshot?.ActiveDenominatorSetHash),
            RosterEntryCount: rosterEntryCount,
            WarningCount: warningCount,
            GovernedApprovalCount: governedApprovalCount,
            FinalizationShareCount: finalizationShareCount,
            ProtocolPackageBinding: protocolPackageBinding,
            OperationalSecurity: operationalSecurity,
            RegulatoryClaim: regulatoryClaim,
            DeploymentProofBinding: deploymentProofBinding,
            RestrictedAnomalyIntakeManifest: null,
            Trustees: trustees);

    private static EvidenceGraphAnomalyIntakeManifestProjection? BuildRestrictedAnomalyIntakeManifestEvidenceGraphNode(
        AnomalyIntakeManifest? manifest,
        Guid? artifactId)
    {
        if (manifest is null || !artifactId.HasValue)
        {
            return null;
        }

        return new EvidenceGraphAnomalyIntakeManifestProjection(
            NodeType: "anomaly_intake_manifest",
            ArtifactId: artifactId.Value,
            CanonicalizationId: manifest.CanonicalizationId,
            ManifestHash: ElectionAnomalyIntakeManifestHasher.ComputeHash(manifest),
            ScopeId: manifest.ScopeId,
            PackageReadinessStatusId: manifest.PackageReadinessStatusId,
            PackageReadinessBlockerIds: manifest.PackageReadinessBlockerIds
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            ThreadCount: manifest.Threads.Count,
            AttachmentManifestCount: manifest.Threads.Sum(x => x.Attachments.Count),
            RedactionCount: manifest.Threads.Sum(x => x.Redactions.Count),
            RecipientStatusCount: manifest.Threads.Sum(x => x.RecipientStatuses.Count),
            AnomalyThreadIds: manifest.Threads
                .Select(x => x.AnomalyThreadId)
                .OrderBy(x => x)
                .ToArray(),
            AttachmentManifestIds: manifest.Threads
                .SelectMany(x => x.Attachments.Select(attachment => attachment.AttachmentManifestId))
                .OrderBy(x => x)
                .ToArray(),
            RedactionEventIds: manifest.Threads
                .SelectMany(x => x.Redactions.Select(redaction => redaction.RedactionEventId))
                .OrderBy(x => x)
                .ToArray(),
            SourceEventIds: manifest.Threads
                .SelectMany(x => x.Attachments.Select(attachment => attachment.EventId)
                    .Concat(x.Redactions.Select(redaction => redaction.EventId)))
                .OrderBy(x => x)
                .ToArray());
    }

    private static RestrictedAnomalyIntakeManifestArtifactProjection BuildRestrictedAnomalyIntakeManifestArtifact(
        AnomalyIntakeManifest manifest) =>
        new(
            ArtifactSchemaId: "restricted-anomaly-intake-manifest-artifact-v1",
            ManifestHash: ElectionAnomalyIntakeManifestHasher.ComputeHash(manifest),
            CanonicalizationId: manifest.CanonicalizationId,
            ScopeId: manifest.ScopeId,
            PackageReadinessStatusId: manifest.PackageReadinessStatusId,
            PackageReadinessBlockerIds: manifest.PackageReadinessBlockerIds
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            ThreadCount: manifest.Threads.Count,
            AttachmentManifestCount: manifest.Threads.Sum(x => x.Attachments.Count),
            RedactionCount: manifest.Threads.Sum(x => x.Redactions.Count),
            RecipientStatusCount: manifest.Threads.Sum(x => x.RecipientStatuses.Count),
            Manifest: manifest);

    private static DeploymentProofBindingProjection? BuildDeploymentProofBindingProjection(
        ElectionDeploymentProofPublicLedgerArtifactRecord? ledger,
        Guid? artifactId,
        byte[]? artifactHash)
    {
        if (ledger is null)
        {
            return null;
        }

        var latestWebClientObservation = ledger.ComponentObservations
            .Where(x => x.ComponentId == ElectionDeploymentProofComponentId.HushWebClient.ToString())
            .OrderByDescending(x => x.ObservedAtUtc)
            .ThenByDescending(x => x.ObservationId)
            .FirstOrDefault();
        var latestPrivacyProof = ledger.ProofFamilies
            .Where(x => string.Equals(
                x.ProofFamilyId,
                ElectionDeploymentProofConstants.RetentionLogPrivacyProofFamilyId,
                StringComparison.Ordinal))
            .OrderByDescending(x => x.ObservedAtUtc)
            .ThenByDescending(x => x.ProofFamilyBindingStatusId)
            .FirstOrDefault();

        return new DeploymentProofBindingProjection(
            ledger.LedgerId,
            ledger.LedgerPublicId,
            ledger.Status,
            ledger.FinalStatus,
            ledger.ClaimEffect,
            ledger.BlocksDeploymentProofClaims,
            ledger.ClaimSummary,
            ledger.DeploymentProfile,
            ledger.DeploymentProtocolVersion,
            ledger.LatestCheckpointId,
            artifactId,
            artifactId.HasValue
                ? ElectionDeploymentProofConstants.PublicLedgerArtifactFileName
                : null,
            artifactHash is null ? null : BuildHashHex(artifactHash),
            ledger.Checkpoints.Count,
            ledger.ComponentObservations.Count,
            ledger.DeploymentEvents.Count,
            ledger.ProofFamilies.Count,
            latestWebClientObservation?.EvidenceStatus,
            latestWebClientObservation?.MismatchCode,
            latestPrivacyProof?.EvidenceStatus,
            latestPrivacyProof?.ClaimEffect,
            latestPrivacyProof?.MismatchCode,
            ledger.ClaimLimitations);
    }

    private static ResultReportProjection BuildResultReportProjection(
        ElectionRecord election,
        ElectionResultArtifactRecord officialResult,
        OutcomeDeterminationProjection outcomeProjection,
        CeremonyPublicKeyProjection? ceremonyPublicKey,
        PublicAnomalySummary publicAnomalySummary,
        AnomalyReportReadinessProjection anomalyReportReadiness)
    {
        var eligibleCount = Math.Max(officialResult.EligibleToVoteCount, 0);
        var turnoutPercent = eligibleCount == 0
            ? 0m
            : decimal.Round((officialResult.TotalVotedCount * 100m) / eligibleCount, 2, MidpointRounding.AwayFromZero);

        var optionResults = officialResult.NamedOptionResults
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.BallotOrder)
            .Select(x =>
            {
                var voteShare = officialResult.TotalVotedCount == 0
                    ? 0m
                    : decimal.Round((x.VoteCount * 100m) / officialResult.TotalVotedCount, 2, MidpointRounding.AwayFromZero);
                return new ResultOptionShareProjection(
                    x.OptionId,
                    x.DisplayLabel,
                    x.ShortDescription,
                    x.Rank,
                    x.VoteCount,
                    voteShare);
            })
            .ToArray();

        return new ResultReportProjection(
            MachineArtifactId: Guid.Empty,
            HumanArtifactId: Guid.Empty,
            ElectionId: election.ElectionId.ToString(),
            ElectionTitle: election.Title,
            OfficialResultArtifactId: officialResult.Id,
            BindingStatus: election.BindingStatus.ToString(),
            IsNonBindingElection: election.BindingStatus == ElectionBindingStatus.NonBinding,
            GovernanceMode: election.GovernanceMode.ToString(),
            SelectedProfileId: election.SelectedProfileId,
            CircuitClassification: GetCircuitClassificationLabel(election),
            ModeProfileFamily: GetModeProfileFamilyLabel(election),
            BoundCeremonyProfileId: ceremonyPublicKey?.ProfileId,
            TallyPublicKeyFingerprint: ceremonyPublicKey?.TallyPublicKeyFingerprint,
            Visibility: officialResult.Visibility.ToString(),
            SecrecyBoundarySummary: GetSecrecyBoundarySummary(election),
            CustodyBoundarySummary: GetGovernanceCustodySummary(election),
            TotalVotedCount: officialResult.TotalVotedCount,
            EligibleToVoteCount: officialResult.EligibleToVoteCount,
            DidNotVoteCount: officialResult.DidNotVoteCount,
            BlankCount: officialResult.BlankCount,
            TurnoutPercent: turnoutPercent,
            DenominatorSnapshotId: officialResult.DenominatorEvidence.EligibilitySnapshotId,
            DenominatorBoundaryArtifactId: officialResult.DenominatorEvidence.BoundaryArtifactId,
            DenominatorHash: BuildHashHex(officialResult.DenominatorEvidence.ActiveDenominatorSetHash),
            OutcomeLabel: outcomeProjection.ConclusionLabel,
            OutcomeSummary: outcomeProjection.ConclusionSummary,
            PublicAnomalySummary: publicAnomalySummary,
            AnomalyReportReadiness: anomalyReportReadiness,
            OptionResults: optionResults);
    }

    private static AuditProvenanceProjection BuildAuditProjection(
        ElectionReportPackageBuildRequest request,
        ProtocolPackageBindingProjection? protocolPackageBinding,
        OperationalSecurityProjection operationalSecurity,
        RegulatoryClaimProjection? regulatoryClaim,
        DeploymentProofBindingProjection? deploymentProofBinding,
        string frozenEvidenceFingerprint,
        IReadOnlyList<TrusteeProjection> trustees,
        IReadOnlyList<WarningEvidenceProjection> warningEvidence,
        IReadOnlyList<GovernedApprovalProjection> governedApprovals,
        IReadOnlyList<FinalizationShareProjection> finalizationShares,
        string officialResultHash) =>
        new(
            MachineArtifactId: Guid.Empty,
            HumanArtifactId: Guid.Empty,
            ElectionId: request.Election.ElectionId.ToString(),
            FrozenEvidenceFingerprint: frozenEvidenceFingerprint,
            Setup: BuildSetupProjection(request),
            CloseArtifactId: request.CloseArtifact.Id,
            TallyReadyArtifactId: request.TallyReadyArtifact.Id,
            UnofficialResultArtifactId: request.UnofficialResult.Id,
            OfficialResultArtifactId: request.OfficialResult.Id,
            OfficialResultHash: officialResultHash,
            FinalizeArtifactId: request.FinalizeArtifact.Id,
            FinalizationSessionId: request.FinalizationSession?.Id,
            FinalizationReleaseEvidenceId: request.FinalizationReleaseEvidence?.Id,
            AcceptedBallotSetHash: BuildHashHex(request.TallyReadyArtifact.AcceptedBallotSetHash),
            PublishedBallotStreamHash: BuildHashHex(request.TallyReadyArtifact.PublishedBallotStreamHash),
            FinalEncryptedTallyHash: BuildHashHex(request.TallyReadyArtifact.FinalEncryptedTallyHash),
            DenominatorHash: BuildHashHex(request.CloseEligibilitySnapshot?.ActiveDenominatorSetHash),
            Trustees: trustees,
            CeremonyPublicKey: ResolveCeremonyPublicKeyProjection(request),
            TrusteeThreshold: request.CloseArtifact.TrusteeSnapshot is null
                ? null
                : BuildTrusteeThresholdProjection(request.CloseArtifact.TrusteeSnapshot),
            ProtocolPackageBinding: protocolPackageBinding,
            OperationalSecurity: operationalSecurity,
            RegulatoryClaim: regulatoryClaim,
            DeploymentProofBinding: deploymentProofBinding,
            FinalizationGovernedProposal: request.FinalizationGovernedProposal is null
                ? null
                : BuildGovernedProposalProjection(request.FinalizationGovernedProposal),
            FinalizationApprovals: governedApprovals,
            FinalizationShares: finalizationShares,
            WarningEvidence: warningEvidence,
            SourceTransactionId: request.FinalizeArtifact.SourceTransactionId,
            SourceBlockHeight: request.FinalizeArtifact.SourceBlockHeight,
            SourceBlockId: request.FinalizeArtifact.SourceBlockId);

    private static OutcomeDeterminationProjection BuildOutcomeProjection(
        ElectionRecord election,
        ElectionResultArtifactRecord officialResult,
        ElectionEligibilitySnapshotRecord? closeEligibilitySnapshot)
    {
        var topResults = officialResult.NamedOptionResults
            .OrderByDescending(x => x.VoteCount)
            .ThenBy(x => x.BallotOrder)
            .ToArray();
        var leader = topResults.FirstOrDefault();
        var tie = leader is not null &&
            topResults.Count(x => x.VoteCount == leader.VoteCount) > 1;
        var eligibleCount = closeEligibilitySnapshot?.ActiveDenominatorCount ?? officialResult.EligibleToVoteCount;
        var turnoutPercent = eligibleCount <= 0
            ? 0m
            : decimal.Round((officialResult.TotalVotedCount * 100m) / eligibleCount, 2, MidpointRounding.AwayFromZero);

        string conclusionLabel;
        string conclusionSummary;
        string? decisiveOptionId = null;
        string? decisiveOptionLabel = null;

        if (tie || leader is null)
        {
            conclusionLabel = election.OutcomeRule.Kind == OutcomeRuleKind.PassFail
                ? "Tie / unresolved"
                : "Winner unresolved";
            conclusionSummary = "The platform outcome is unresolved because the top counted options are tied.";
        }
        else if (election.OutcomeRule.Kind == OutcomeRuleKind.PassFail)
        {
            decisiveOptionId = leader.OptionId;
            decisiveOptionLabel = leader.DisplayLabel;
            var firstNamedBallotOrder = topResults.Min(x => x.BallotOrder);
            var isPass = string.Equals(election.OutcomeRule.TemplateKey, "pass_fail_yes_no", StringComparison.OrdinalIgnoreCase)
                ? leader.BallotOrder == firstNamedBallotOrder
                : StartsWithAny(leader.DisplayLabel, "yes", "approve", "approved", "pass");
            conclusionLabel = isPass ? "Pass" : "Fail";
            conclusionSummary = $"{conclusionLabel} based on {election.OutcomeRule.CalculationBasis} with decisive option '{leader.DisplayLabel}'.";
        }
        else
        {
            decisiveOptionId = leader.OptionId;
            decisiveOptionLabel = leader.DisplayLabel;
            conclusionLabel = "Winner";
            conclusionSummary = $"Winner '{leader.DisplayLabel}' based on {election.OutcomeRule.CalculationBasis}.";
        }

        return new OutcomeDeterminationProjection(
            MachineArtifactId: Guid.Empty,
            HumanArtifactId: Guid.Empty,
            ElectionId: election.ElectionId.ToString(),
            BindingStatus: election.BindingStatus.ToString(),
            IsNonBindingElection: election.BindingStatus == ElectionBindingStatus.NonBinding,
            GovernanceMode: election.GovernanceMode.ToString(),
            SelectedProfileId: election.SelectedProfileId,
            CircuitClassification: GetCircuitClassificationLabel(election),
            ModeProfileFamily: GetModeProfileFamilyLabel(election),
            OutcomeRuleKind: election.OutcomeRule.Kind.ToString(),
            OutcomeTemplateKey: election.OutcomeRule.TemplateKey,
            CalculationBasis: election.OutcomeRule.CalculationBasis,
            TieResolutionRule: election.OutcomeRule.TieResolutionRule,
            ConclusionLabel: conclusionLabel,
            ConclusionSummary: conclusionSummary,
            DecisiveOptionId: decisiveOptionId,
            DecisiveOptionLabel: decisiveOptionLabel,
            TotalVotedCount: officialResult.TotalVotedCount,
            EligibleToVoteCount: officialResult.EligibleToVoteCount,
            TurnoutPercent: turnoutPercent,
            BlankCount: officialResult.BlankCount,
            DidNotVoteCount: officialResult.DidNotVoteCount);
    }

    private static AbnormalFinalizationEvidenceArtifactRecord? BuildAbnormalFinalizationEvidence(
        ElectionReportPackageBuildRequest request,
        Guid packageId)
    {
        var input = request.AbnormalFinalizationEvidence;
        if (input is null)
        {
            return null;
        }

        return new AbnormalFinalizationEvidenceArtifactRecord(
            AbnormalFinalizationVerificationIds.ArtifactSchemaId,
            request.Election.ElectionId.ToString(),
            packageId.ToString(),
            AbnormalFinalizationVerificationIds.OutcomeStatusFinalizedWithAnomaly,
            CleanFinalization: false,
            AbnormalFinalizationVerificationIds.FinalizationModeAbnormal,
            input.AuthorityDecisionRef,
            input.AuthorityDecisionHash,
            input.GovernanceRuleRef,
            AbnormalFinalizationVerificationIds.OfficialResultSourceCopiedFromFixedUnofficial,
            request.CloseArtifact.Id.ToString(),
            request.TallyReadyArtifact.Id.ToString(),
            request.UnofficialResult.Id.ToString(),
            request.OfficialResult.Id.ToString(),
            (request.OfficialResult.SourceResultArtifactId ?? request.UnofficialResult.Id).ToString(),
            request.FinalizeArtifact.Id.ToString(),
            input.MissingFinalizeEvidence,
            input.ContinuityIncidentEvidenceRefs,
            input.AvailableTrusteeAcknowledgementRefs,
            input.PublicSummary,
            input.DecidedAtUtc);
    }

    private static RosterEntryProjection BuildRosterEntryProjection(
        ElectionRosterEntryRecord rosterEntry,
        ElectionParticipationRecord? participationRecord) =>
        new(
            rosterEntry.OrganizationVoterId,
            rosterEntry.LinkStatus.ToString(),
            rosterEntry.VotingRightStatus.ToString(),
            rosterEntry.LinkedActorPublicAddress,
            rosterEntry.WasPresentAtOpen,
            rosterEntry.WasActiveAtOpen,
            (participationRecord?.ParticipationStatus ?? ElectionParticipationStatus.DidNotVote).ToString(),
            participationRecord?.CountsAsParticipation ?? false);

    private static void ValidateVoidRequest(ElectionVoidReportPackageBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Election.LifecycleState != ElectionLifecycleState.Voided)
        {
            throw new InvalidOperationException("VOID packages require a voided election record.");
        }

        if (request.Decision.ElectionId != request.Election.ElectionId ||
            request.PublicationAttempt.ElectionId != request.Election.ElectionId ||
            request.PublicationAttempt.VoidDecisionId != request.Decision.Id)
        {
            throw new InvalidOperationException("VOID package inputs must refer to the same election and decision.");
        }

        if (request.PublicationAttempt.Status != ElectionVoidPublicationAttemptStatus.Pending)
        {
            throw new InvalidOperationException("VOID package generation requires a pending publication attempt.");
        }
    }

    private static ElectionReportArtifactRecord CreateVoidJsonArtifact(
        ElectionVoidReportPackageBuildRequest request,
        Guid packageId,
        Guid artifactId,
        ElectionReportArtifactKind artifactKind,
        ElectionReportArtifactAccessScope accessScope,
        int sortOrder,
        string title,
        string fileName,
        object payload)
    {
        var content = SerializeJson(payload);
        return ElectionModelFactory.CreateReportArtifact(
            packageId,
            request.Election.ElectionId,
            artifactKind,
            ElectionReportArtifactFormat.Json,
            accessScope,
            sortOrder,
            title,
            fileName,
            "application/json",
            ComputeHashBytes(content),
            content,
            recordedAt: request.AttemptedAt,
            preassignedArtifactId: artifactId);
    }

    private static ElectionReportArtifactRecord CreateVoidMarkdownArtifact(
        ElectionVoidReportPackageBuildRequest request,
        Guid packageId,
        Guid artifactId,
        ElectionReportArtifactKind artifactKind,
        ElectionReportArtifactAccessScope accessScope,
        int sortOrder,
        string title,
        string fileName,
        string content) =>
        ElectionModelFactory.CreateReportArtifact(
            packageId,
            request.Election.ElectionId,
            artifactKind,
            ElectionReportArtifactFormat.Markdown,
            accessScope,
            sortOrder,
            title,
            fileName,
            "text/markdown",
            ComputeHashBytes(content),
            content,
            recordedAt: request.AttemptedAt,
            preassignedArtifactId: artifactId);

    private static ElectionReportArtifactRecord CreateVoidBinaryArtifact(
        ElectionVoidReportPackageBuildRequest request,
        Guid packageId,
        Guid artifactId,
        ElectionReportArtifactKind artifactKind,
        ElectionReportArtifactAccessScope accessScope,
        int sortOrder,
        string title,
        string fileName,
        string mediaType,
        byte[] content) =>
        ElectionModelFactory.CreateReportArtifact(
            packageId,
            request.Election.ElectionId,
            artifactKind,
            ElectionReportArtifactFormat.Binary,
            accessScope,
            sortOrder,
            title,
            fileName,
            mediaType,
            ComputeHashBytes(content),
            Convert.ToBase64String(content),
            recordedAt: request.AttemptedAt,
            preassignedArtifactId: artifactId);

    private static object BuildPublicVoidDecisionProjection(ElectionVoidDecisionRecord decision) =>
        new
        {
            schemaId = "hushvoting-void-decision-public-v1",
            voidDecisionId = decision.Id,
            electionId = decision.ElectionId.ToString(),
            actorPublicAddress = decision.ActorPublicAddress,
            actorRole = decision.ActorRole,
            sourceTransactionId = decision.SourceTransactionId,
            sourceBlockHeight = decision.SourceBlockHeight,
            sourceBlockId = decision.SourceBlockId,
            decidedAt = decision.DecidedAt,
            previousLifecycleState = decision.PreviousLifecycleState.ToString(),
            resultingLifecycleState = "Voided",
            publicStatus = "VOID",
            publicJustification = decision.PublicJustification,
            publicJustificationHash = CreateSha256Fingerprint(decision.PublicJustificationHash),
            evidenceReferences = decision.EvidenceReferences
                .OrderBy(x => x.ReferenceKind)
                .ThenBy(x => x.ReferenceId, StringComparer.Ordinal)
                .Select(x => new
                {
                    referenceKind = x.ReferenceKind.ToString(),
                    x.ReferenceId,
                    x.ReferenceHash,
                    visibility = x.Visibility.ToString(),
                })
                .ToArray(),
        };

    private static string BuildPublicVoidSummaryContent(
        ElectionVoidReportPackageBuildRequest request,
        Guid packageId,
        string contentPackageHash,
        IReadOnlyList<ElectionVoidSupersededPublicArtifactReference> supersededArtifacts,
        string? historicalUnofficialHash)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# VOID Election Summary");
        builder.AppendLine();
        builder.AppendLine($"Election: `{request.Election.Title}`");
        builder.AppendLine($"Election id: `{request.Election.ElectionId}`");
        builder.AppendLine($"Status: `VOID`");
        builder.AppendLine($"Void decision id: `{request.Decision.Id}`");
        builder.AppendLine($"VOID package id: `{packageId}`");
        builder.AppendLine($"VOID package content hash: `{contentPackageHash}`");
        builder.AppendLine($"Previous lifecycle state: `{request.Decision.PreviousLifecycleState}`");
        builder.AppendLine("Resulting lifecycle state: `Voided`");
        builder.AppendLine($"Decided at: `{request.Decision.DecidedAt:O}`");
        builder.AppendLine($"Published at: `{request.AttemptedAt:O}`");
        builder.AppendLine();
        builder.AppendLine("## Public Justification");
        builder.AppendLine();
        builder.AppendLine(request.Decision.PublicJustification);
        builder.AppendLine();
        builder.AppendLine("## Result Claim Impact");
        builder.AppendLine();
        builder.AppendLine("This election is voided. No current final-result claim is available.");
        builder.AppendLine("Historical packages, reports, verifier summaries, and public status references are superseded by this VOID decision.");
        builder.AppendLine();
        builder.AppendLine("## Superseded Artifacts");
        builder.AppendLine();
        if (supersededArtifacts.Count == 0)
        {
            builder.AppendLine("No previous current publication artifacts were recorded before the VOID decision.");
        }
        else
        {
            foreach (var artifact in supersededArtifacts)
            {
                builder.AppendLine(
                    $"- `{artifact.ArtifactKind}` `{artifact.ArtifactRef}` hash `{artifact.ArtifactHash ?? "not-recorded"}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Historical Unofficial Result");
        builder.AppendLine();
        builder.AppendLine(historicalUnofficialHash is null
            ? "No unofficial result was available before void."
            : $"A historical unofficial result existed before void. Restricted hash/id: `{historicalUnofficialHash}`.");
        builder.AppendLine();
        builder.AppendLine("No participation counts, vote counts, accepted ballot sets, tally material, voter identities, vote choices, anomaly bodies, or support logs are included in this public summary.");
        return builder.ToString();
    }

    private static string BuildRestrictedVoidEvidenceIndexContent(
        ElectionVoidRestrictedEvidenceIndexRecord restrictedEvidenceIndex)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Restricted VOID Evidence Index");
        builder.AppendLine();
        builder.AppendLine($"Election id: `{restrictedEvidenceIndex.ElectionId}`");
        builder.AppendLine($"Void decision id: `{restrictedEvidenceIndex.VoidDecisionId}`");
        builder.AppendLine($"Publication attempt id: `{restrictedEvidenceIndex.PublicationAttemptId}`");
        builder.AppendLine($"Recorded at: `{restrictedEvidenceIndex.RecordedAt:O}`");
        builder.AppendLine();
        builder.AppendLine("## Evidence References");
        builder.AppendLine();
        if (restrictedEvidenceIndex.EvidenceReferences.Count == 0)
        {
            builder.AppendLine("No optional evidence references were provided.");
        }
        else
        {
            foreach (var reference in restrictedEvidenceIndex.EvidenceReferences
                         .OrderBy(x => x.ReferenceKind)
                         .ThenBy(x => x.ReferenceId, StringComparer.Ordinal))
            {
                builder.AppendLine($"- Kind: `{reference.ReferenceKind}`");
                builder.AppendLine($"  Reference id: `{reference.ReferenceId}`");
                builder.AppendLine($"  Internal record id: `{reference.InternalRecordId?.ToString() ?? "not-applicable"}`");
                builder.AppendLine($"  External reference: `{reference.ExternalReference ?? "not-provided"}`");
                builder.AppendLine($"  Reference hash: `{reference.ReferenceHash ?? "not-provided"}`");
                builder.AppendLine($"  Visibility: `{reference.Visibility}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Historical Unofficial Result");
        builder.AppendLine();
        builder.AppendLine(restrictedEvidenceIndex.HistoricalUnofficialResultArtifactId.HasValue
            ? $"Historical unofficial result artifact id: `{restrictedEvidenceIndex.HistoricalUnofficialResultArtifactId}` hash `{restrictedEvidenceIndex.HistoricalUnofficialResultHash}`."
            : "No historical unofficial result artifact is attached to this VOID package.");
        return builder.ToString();
    }

    private static object BuildHistoricalUnofficialResultProjection(ElectionResultArtifactRecord resultArtifact) =>
        new
        {
            schemaId = "hushvoting-restricted-historical-unofficial-result-v1",
            artifactId = resultArtifact.Id,
            electionId = resultArtifact.ElectionId.ToString(),
            resultArtifact.Title,
            artifactKind = resultArtifact.ArtifactKind.ToString(),
            visibility = resultArtifact.Visibility.ToString(),
            resultArtifact.NamedOptionResults,
            resultArtifact.BlankCount,
            resultArtifact.TotalVotedCount,
            resultArtifact.EligibleToVoteCount,
            resultArtifact.DidNotVoteCount,
            resultArtifact.DenominatorEvidence,
            resultArtifact.PublicPayload,
            contentHash = CreateSha256Fingerprint(ComputeResultArtifactHash(resultArtifact)),
            resultArtifact.RecordedAt,
            resultArtifact.RecordedByPublicAddress,
        };

    private static VerifierOutputRecord BuildVoidVerifierOutput(
        ElectionVoidReportPackageBuildRequest request,
        Guid packageId,
        string contentPackageHash) =>
        new(
            OutputVersion: "1.0",
            PackageId: packageId.ToString(),
            ElectionId: request.Election.ElectionId.ToString(),
            VerifierProfileId: VerificationProfileIds.PublicAnonymousV1,
            OverallStatus: VerificationOverallStatus.Warn,
            ExitCode: VerificationExitCodes.FromOverallStatus(VerificationOverallStatus.Warn),
            VerifiedAt: request.AttemptedAt,
            Results:
            [
                new VerifierCheckResultRecord(
                    "VFY-VOID-000",
                    VerificationCheckStatus.Pass,
                    VerificationResultCodes.PackageStructureValid,
                    "VOID package structure is authentic and internally consistent.",
                    new Dictionary<string, string>
                    {
                        ["package_id"] = packageId.ToString(),
                        ["content_package_hash"] = contentPackageHash,
                    }),
                new VerifierCheckResultRecord(
                    "VFY-VOID-001",
                    VerificationCheckStatus.Warn,
                    VerificationResultCodes.ElectionVoided,
                    "This election is voided. No current final-result or final-inclusion claim is available.",
                    new Dictionary<string, string>
                    {
                        ["void_decision_id"] = request.Decision.Id.ToString(),
                        ["previous_lifecycle_state"] = request.Decision.PreviousLifecycleState.ToString(),
                        ["resulting_lifecycle_state"] = request.Decision.ResultingLifecycleState.ToString(),
                    }),
            ]);

    private static VoidPackageManifestProjection BuildVoidPackageManifest(
        ElectionVoidReportPackageBuildRequest request,
        Guid packageId,
        string contentPackageHash,
        IReadOnlyList<ElectionReportArtifactRecord> artifacts) =>
        new(
            SchemaId: "hushvoting-void-package-manifest-v1",
            PackageId: packageId,
            ElectionId: request.Election.ElectionId.ToString(),
            VoidDecisionId: request.Decision.Id,
            PublicationAttemptId: request.PublicationAttempt.Id,
            Status: "VOID",
            VerifierResultCode: VerificationResultCodes.ElectionVoided,
            PackageHashCanonicalization: "sha256 over immutable void decision/publication inputs, public supersession refs, historical result hash when present, and frozen evidence hash; self-referential status, manifest, and archive files are excluded.",
            PackageHash: contentPackageHash,
            CreatedAt: request.AttemptedAt,
            Entries: artifacts
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.FileName, StringComparer.Ordinal)
                .Select(x => new VoidPackageManifestEntryProjection(
                    x.FileName,
                    CreateSha256Fingerprint(x.ContentHash),
                    x.MediaType,
                    x.AccessScope == ElectionReportArtifactAccessScope.OwnerAuditorOnly
                        ? "restricted-owner-auditor"
                        : "public",
                    x.ArtifactKind.ToString(),
                    x.Format.ToString()))
                .ToArray());

    private static byte[] ComputeVoidContentPackageHash(
        ElectionVoidReportPackageBuildRequest request,
        IReadOnlyList<ElectionVoidSupersededPublicArtifactReference> supersededArtifacts,
        string? historicalUnofficialHash)
    {
        var payload = SerializeJson(new
        {
            schemaId = "hushvoting-void-package-content-hash-v1",
            electionId = request.Election.ElectionId.ToString(),
            decisionId = request.Decision.Id,
            publicationAttemptId = request.PublicationAttempt.Id,
            request.Decision.PreviousLifecycleState,
            request.Decision.ResultingLifecycleState,
            publicJustificationHash = CreateSha256Fingerprint(request.Decision.PublicJustificationHash),
            supersededArtifacts,
            historicalUnofficialHash,
            frozenEvidenceHash = CreateSha256Fingerprint(request.PublicationAttempt.FrozenEvidenceHash),
        });
        return ComputeHashBytes(payload);
    }

    private static byte[] ComputeResultArtifactHash(ElectionResultArtifactRecord artifact) =>
        ComputeHashBytes(SerializeJson(new
        {
            artifact.Id,
            ElectionId = artifact.ElectionId.ToString(),
            artifact.ArtifactKind,
            artifact.Visibility,
            artifact.Title,
            artifact.NamedOptionResults,
            artifact.BlankCount,
            artifact.TotalVotedCount,
            artifact.EligibleToVoteCount,
            artifact.DidNotVoteCount,
            artifact.DenominatorEvidence,
            artifact.TallyReadyArtifactId,
            artifact.SourceResultArtifactId,
            artifact.PublicPayload,
            artifact.RecordedAt,
            artifact.RecordedByPublicAddress,
        }));

    private static byte[] BuildVoidPackageArchiveBytes(IReadOnlyList<ElectionReportArtifactRecord> artifacts)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var artifact in artifacts
                         .OrderBy(x => x.SortOrder)
                         .ThenBy(x => x.FileName, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(artifact.FileName.Replace('\\', '/'));
                using var entryStream = entry.Open();
                var bytes = artifact.Format == ElectionReportArtifactFormat.Binary
                    ? Convert.FromBase64String(artifact.Content)
                    : Encoding.UTF8.GetBytes(artifact.Content);
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }

        return stream.ToArray();
    }

    private static string BuildVoidArtifactRef(Guid packageId, string artifactName) =>
        $"report-package:{packageId:N}/{artifactName}";

    private static ElectionReportArtifactRecord CreateJsonArtifact(
        ElectionReportPackageBuildRequest request,
        Guid packageId,
        Guid artifactId,
        Guid? pairedArtifactId,
        ElectionReportArtifactKind artifactKind,
        ElectionReportArtifactAccessScope accessScope,
        int sortOrder,
        string title,
        string fileName,
        object payload)
    {
        var content = SerializeJson(payload);
        return ElectionModelFactory.CreateReportArtifact(
            packageId,
            request.Election.ElectionId,
            artifactKind,
            ElectionReportArtifactFormat.Json,
            accessScope,
            sortOrder,
            title,
            fileName,
            "application/json",
            ComputeHashBytes(content),
            content,
            pairedArtifactId,
            request.AttemptedAt,
            artifactId);
    }

    private static void ValidateDeploymentProofPublicLedgerContent(string content)
    {
        string[] forbiddenFragments =
        [
            "private key",
            "begin private key",
            "kms:",
            "aws:kms",
            "kms alias",
            "password",
            "secret access key",
            "connection string",
            "raw log",
            "support log",
            "voter identity",
            "vote choice",
            "trustee share",
            "receipt secret",
        ];

        if (forbiddenFragments.Any(fragment => content.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Deployment proof public ledger artifact contains restricted material.");
        }
    }

    private static ElectionReportArtifactRecord CreateMarkdownArtifact(
        ElectionReportPackageBuildRequest request,
        Guid packageId,
        Guid artifactId,
        Guid? pairedArtifactId,
        ElectionReportArtifactKind artifactKind,
        ElectionReportArtifactAccessScope accessScope,
        int sortOrder,
        string title,
        string fileName,
        string content) =>
        ElectionModelFactory.CreateReportArtifact(
            packageId,
            request.Election.ElectionId,
            artifactKind,
            ElectionReportArtifactFormat.Markdown,
            accessScope,
            sortOrder,
            title,
            fileName,
            "text/markdown",
            ComputeHashBytes(content),
            content,
            pairedArtifactId,
            request.AttemptedAt,
            artifactId);

    private static string BuildHumanManifestContent(
        ManifestProjection manifest,
        Guid packageId,
        string frozenEvidenceFingerprint,
        Guid machineManifestId,
        Guid humanManifestId,
        Guid evidenceGraphArtifactId) =>
        $"""
        # Final Manifest

        - Package id: `{packageId}`
        - Attempt number: `{manifest.AttemptNumber}`
        - Previous attempt id: `{manifest.PreviousAttemptId?.ToString() ?? "none"}`
        - Election id: `{manifest.ElectionId}`
        - Election title: {manifest.ElectionTitle}
        - Attempted at: `{manifest.AttemptedAt:O}`
        - Attempted by: `{manifest.AttemptedBy}`
        - Frozen evidence fingerprint: `{frozenEvidenceFingerprint}`
        - Binding status: `{manifest.BindingStatus}`
        - Non-binding election: `{(manifest.IsNonBindingElection ? "yes" : "no")}`
        - Governance mode: `{manifest.GovernanceMode}`
        - Selected circuit/profile: `{manifest.SelectedProfileId}`
        - Circuit class: `{manifest.CircuitClassification}`
        - Profile family: `{manifest.ModeProfileFamily}`
        - Bound ceremony profile: `{manifest.BoundCeremonyProfileId ?? "not recorded"}`
        - Tally public key fingerprint: `{manifest.TallyPublicKeyFingerprint ?? "not recorded"}`
        - Official visibility: `{manifest.OfficialVisibility}`
        - Secrecy boundary: {manifest.SecrecyBoundarySummary}
        - Custody boundary: {manifest.CustodyBoundarySummary}
        {BuildHumanProtocolPackageBindingContent(manifest.ProtocolPackageBinding)}
        {BuildHumanOperationalRegulatoryContent(manifest.OperationalSecurity, manifest.RegulatoryClaim)}
        {BuildHumanDeploymentProofBindingContent(manifest.DeploymentProofBinding)}
        - Accepted trustee count: `{manifest.AcceptedTrusteeCount}`
        - Roster entry count: `{manifest.RosterEntryCount}`
        - Warning count: `{manifest.WarningCount}`
        - Governed approval count: `{manifest.GovernedApprovalCount}`
        - Finalization share count: `{manifest.FinalizationShareCount}`
        - Outcome label: `{manifest.OutcomeLabel}`
        - Outcome summary: {manifest.OutcomeSummary}
        - Machine manifest artifact id: `{machineManifestId}`
        - Human manifest artifact id: `{humanManifestId}`
        - Evidence graph artifact id: `{evidenceGraphArtifactId}`
        - Finalize artifact id: `{manifest.FinalizeArtifactId}`
        - Official result artifact id: `{manifest.OfficialResultArtifactId}`
        - Official result hash: `{manifest.OfficialResultHash}`
        """;

    private static string BuildHumanResultReportContent(ResultReportProjection projection)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Final Result Report");
        builder.AppendLine();
        builder.AppendLine($"- Election id: `{projection.ElectionId}`");
        builder.AppendLine($"- Election title: {projection.ElectionTitle}");
        builder.AppendLine($"- Official result artifact id: `{projection.OfficialResultArtifactId}`");
        builder.AppendLine($"- Binding status: `{projection.BindingStatus}`");
        builder.AppendLine($"- Non-binding election: `{(projection.IsNonBindingElection ? "yes" : "no")}`");
        builder.AppendLine($"- Governance mode: `{projection.GovernanceMode}`");
        builder.AppendLine($"- Selected circuit/profile: `{projection.SelectedProfileId}`");
        builder.AppendLine($"- Circuit class: `{projection.CircuitClassification}`");
        builder.AppendLine($"- Profile family: `{projection.ModeProfileFamily}`");
        builder.AppendLine($"- Bound ceremony profile: `{projection.BoundCeremonyProfileId ?? "not recorded"}`");
        builder.AppendLine($"- Tally public key fingerprint: `{projection.TallyPublicKeyFingerprint ?? "not recorded"}`");
        builder.AppendLine($"- Visibility: `{projection.Visibility}`");
        builder.AppendLine($"- Secrecy boundary: {projection.SecrecyBoundarySummary}");
        builder.AppendLine($"- Custody boundary: {projection.CustodyBoundarySummary}");
        builder.AppendLine($"- Total voted: `{projection.TotalVotedCount}`");
        builder.AppendLine($"- Eligible to vote: `{projection.EligibleToVoteCount}`");
        builder.AppendLine($"- Did not vote: `{projection.DidNotVoteCount}`");
        builder.AppendLine($"- Blank votes: `{projection.BlankCount}`");
        builder.AppendLine($"- Turnout percent: `{projection.TurnoutPercent:F2}`");
        builder.AppendLine($"- Outcome label: `{projection.OutcomeLabel}`");
        builder.AppendLine($"- Outcome summary: {projection.OutcomeSummary}");
        builder.AppendLine();
        AppendHumanAnomalySummaryContent(builder, projection.PublicAnomalySummary, projection.AnomalyReportReadiness);
        builder.AppendLine();
        builder.AppendLine("| Rank | Option | Votes | Share |");
        builder.AppendLine("|------|--------|-------|-------|");
        foreach (var option in projection.OptionResults)
        {
            builder.AppendLine($"| {option.Rank} | {option.DisplayLabel} | {option.VoteCount} | {option.VoteSharePercent:F2}% |");
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendHumanAnomalySummaryContent(
        StringBuilder builder,
        PublicAnomalySummary summary,
        AnomalyReportReadinessProjection readiness)
    {
        builder.AppendLine("## Anomaly Reporting");
        builder.AppendLine();
        builder.AppendLine($"- Public summary schema: `{summary.SchemaId}`");
        builder.AppendLine($"- Suppression policy: `{summary.SuppressionPolicyId}`");
        builder.AppendLine($"- Source manifest hash: `{summary.SourceManifestHash ?? "not available"}`");
        builder.AppendLine($"- Restricted manifest artifact id: `{summary.RestrictedManifestArtifactId?.ToString() ?? "not available"}`");
        builder.AppendLine($"- Restricted manifest hash: `{summary.RestrictedManifestHash ?? "not available"}`");
        if (summary.TotalThreadCountMode == ElectionAnomalyPublicSummaryCountModeIds.Exact)
        {
            builder.AppendLine($"- Public anomaly thread count: `{summary.TotalThreadCount}`");
        }
        else
        {
            builder.AppendLine("- Public anomaly thread count: suppressed by privacy policy.");
        }

        builder.AppendLine($"- Suppressed thread count: `{summary.SuppressedThreadCount}`");
        builder.AppendLine($"- Aggregated category count: `{summary.AggregatedBucketCount}`");
        builder.AppendLine($"- Package readiness status: `{readiness.PackageReadinessStatusId}`");
        builder.AppendLine($"- Package readiness blockers: `{(readiness.PackageReadinessBlockerIds.Count == 0 ? "none" : string.Join("`, `", readiness.PackageReadinessBlockerIds))}`");
        builder.AppendLine($"- Retention evidence status: `{readiness.RetentionEvidenceStatusId}`");
        builder.AppendLine($"- Open anomaly case count: `{readiness.OpenCaseCount}`");
        builder.AppendLine($"- Escalated anomaly case count: `{readiness.EscalatedCaseCount}`");
        builder.AppendLine($"- Readiness blocks validation claims: `{(readiness.RetentionEvidenceStatus.ReadinessBlocksValidationClaims ? "yes" : "no")}`");
        builder.AppendLine($"- Report generation read-only status: `{readiness.ReportGenerationReadOnlyStatusId}`");
        builder.AppendLine(
            summary.RestrictedManifestArtifactId.HasValue
                ? "- Restricted anomaly evidence is available only in the owner/auditor report package artifact."
                : "- No restricted anomaly evidence artifact is linked to this report package.");

        if (summary.SuppressionReasonIds.Count > 0)
        {
            builder.AppendLine($"- Public suppression reasons: `{string.Join("`, `", summary.SuppressionReasonIds)}`");
        }
        else
        {
            builder.AppendLine("- Public suppression reasons: none");
        }

        builder.AppendLine();
        if (summary.VisibleBuckets.Count == 0)
        {
            builder.AppendLine("No public anomaly categories are present in this report.");
            return;
        }

        builder.AppendLine("| Category | Mode | Public count | Reasons | Source categories |");
        builder.AppendLine("|----------|------|--------------|---------|-------------------|");
        foreach (var bucket in summary.VisibleBuckets)
        {
            var publicCount = bucket.PublicCount?.ToString() ?? "suppressed";
            var reasons = bucket.SuppressionReasonIds.Count == 0
                ? "none"
                : string.Join(", ", bucket.SuppressionReasonIds);
            var sources = bucket.SourceCategoryIds.Count == 0
                ? bucket.CategoryId
                : string.Join(", ", bucket.SourceCategoryIds);
            builder.AppendLine($"| `{bucket.CategoryId}` | `{bucket.CountMode}` | `{publicCount}` | {reasons} | {sources} |");
        }
    }

    private static string BuildHumanRosterContent(
        ElectionRecord election,
        IReadOnlyList<RosterEntryProjection> rosterEntries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Named Participation Roster");
        builder.AppendLine();
        builder.AppendLine($"- Election id: `{election.ElectionId}`");
        builder.AppendLine($"- Election title: {election.Title}");
        builder.AppendLine($"- Roster entries: `{rosterEntries.Count}`");
        builder.AppendLine($"- Binding status: `{election.BindingStatus}`");
        builder.AppendLine($"- Non-binding election: `{(election.BindingStatus == ElectionBindingStatus.NonBinding ? "yes" : "no")}`");
        builder.AppendLine($"- Governance mode: `{election.GovernanceMode}`");
        builder.AppendLine($"- Selected circuit/profile: `{election.SelectedProfileId}`");
        builder.AppendLine($"- Circuit class: `{GetCircuitClassificationLabel(election)}`");
        builder.AppendLine($"- Profile family: `{GetModeProfileFamilyLabel(election)}`");
        builder.AppendLine();
        builder.AppendLine("| Organization voter id | Link status | Voting right | Linked actor | Participation | Counts as participation |");
        builder.AppendLine("|-----------------------|-------------|--------------|--------------|---------------|-------------------------|");
        foreach (var entry in rosterEntries)
        {
            builder.AppendLine(
                $"| {entry.OrganizationVoterId} | {entry.LinkStatus} | {entry.VotingRightStatus} | {(entry.LinkedActorPublicAddress ?? "unlinked")} | {entry.ParticipationStatus} | {(entry.CountsAsParticipation ? "yes" : "no")} |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildHumanAuditContent(AuditProvenanceProjection projection)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Audit And Provenance Report");
        builder.AppendLine();
        builder.AppendLine($"- Election id: `{projection.ElectionId}`");
        builder.AppendLine($"- Frozen evidence fingerprint: `{projection.FrozenEvidenceFingerprint}`");
        builder.AppendLine($"- Close artifact id: `{projection.CloseArtifactId}`");
        builder.AppendLine($"- Tally-ready artifact id: `{projection.TallyReadyArtifactId}`");
        builder.AppendLine($"- Unofficial result artifact id: `{projection.UnofficialResultArtifactId}`");
        builder.AppendLine($"- Official result artifact id: `{projection.OfficialResultArtifactId}`");
        builder.AppendLine($"- Official result hash: `{projection.OfficialResultHash}`");
        builder.AppendLine($"- Finalize artifact id: `{projection.FinalizeArtifactId}`");
        builder.AppendLine($"- Finalization session id: `{projection.FinalizationSessionId?.ToString() ?? "none"}`");
        builder.AppendLine($"- Finalization release evidence id: `{projection.FinalizationReleaseEvidenceId?.ToString() ?? "none"}`");
        builder.AppendLine($"- Accepted ballot set hash: `{projection.AcceptedBallotSetHash}`");
        builder.AppendLine($"- Published ballot stream hash: `{projection.PublishedBallotStreamHash}`");
        builder.AppendLine($"- Final encrypted tally hash: `{projection.FinalEncryptedTallyHash}`");
        builder.AppendLine($"- Denominator hash: `{projection.DenominatorHash}`");
        builder.AppendLine($"- Source transaction id: `{projection.SourceTransactionId?.ToString() ?? "none"}`");
        builder.AppendLine($"- Source block height: `{projection.SourceBlockHeight?.ToString() ?? "none"}`");
        builder.AppendLine($"- Source block id: `{projection.SourceBlockId?.ToString() ?? "none"}`");
        builder.AppendLine();
        builder.AppendLine("## Setup Metadata");
        builder.AppendLine();
        builder.AppendLine($"- Title: {projection.Setup.Title}");
        builder.AppendLine($"- Short description: {projection.Setup.ShortDescription ?? "none"}");
        builder.AppendLine($"- Owner: `{projection.Setup.OwnerPublicAddress}`");
        builder.AppendLine($"- External reference: `{projection.Setup.ExternalReferenceCode ?? "none"}`");
        builder.AppendLine($"- Draft revision: `{projection.Setup.SourceDraftRevision}`");
        builder.AppendLine($"- Governance mode: `{projection.Setup.GovernanceMode}`");
        builder.AppendLine($"- Binding status: `{projection.Setup.BindingStatus}`");
        builder.AppendLine($"- Non-binding election: `{(projection.Setup.IsNonBindingElection ? "yes" : "no")}`");
        builder.AppendLine($"- Selected circuit/profile: `{projection.Setup.SelectedProfileId}`");
        builder.AppendLine($"- Circuit class: `{projection.Setup.CircuitClassification}`");
        builder.AppendLine($"- Profile family: `{projection.Setup.ModeProfileFamily}`");
        builder.AppendLine($"- Participation privacy mode: `{projection.Setup.ParticipationPrivacyMode}`");
        builder.AppendLine($"- Reporting policy: `{projection.Setup.ReportingPolicy}`");
        builder.AppendLine($"- Review window policy: `{projection.Setup.ReviewWindowPolicy}`");
        builder.AppendLine($"- Official visibility: `{projection.Setup.OfficialVisibility}`");
        builder.AppendLine($"- Required approval count: `{projection.Setup.RequiredApprovalCount?.ToString() ?? "none"}`");
        builder.AppendLine($"- Secrecy boundary: {projection.Setup.SecrecyBoundarySummary}");
        builder.AppendLine($"- Custody boundary: {projection.Setup.CustodyBoundarySummary}");
        builder.AppendLine();
        builder.AppendLine("## Protocol Omega Package Binding");
        builder.AppendLine();
        AppendHumanProtocolPackageBindingContent(builder, projection.ProtocolPackageBinding);
        builder.AppendLine();
        builder.AppendLine("## Operational Security And Regulatory Boundaries");
        builder.AppendLine();
        AppendHumanOperationalRegulatoryContent(builder, projection.OperationalSecurity, projection.RegulatoryClaim);
        builder.AppendLine();
        builder.AppendLine("## Deployment Proof Binding");
        builder.AppendLine();
        AppendHumanDeploymentProofBindingContent(builder, projection.DeploymentProofBinding);
        builder.AppendLine();
        builder.AppendLine("### Approved Clients");
        builder.AppendLine();
        foreach (var client in projection.Setup.ApprovedClients)
        {
            builder.AppendLine($"- `{client.ApplicationId}` version `{client.Version}`");
        }

        builder.AppendLine();
        builder.AppendLine("### Election Options");
        builder.AppendLine();
        foreach (var option in projection.Setup.Options)
        {
            builder.AppendLine($"- `{option.OptionId}` {option.DisplayLabel} (order `{option.BallotOrder}`, blank `{option.IsBlankOption}`)");
        }

        builder.AppendLine();
        builder.AppendLine("## Accepted Trustees");
        builder.AppendLine();
        foreach (var trustee in projection.Trustees)
        {
            builder.AppendLine($"- `{trustee.TrusteeUserAddress}` ({trustee.TrusteeDisplayName ?? "unnamed"})");
        }

        builder.AppendLine();
        builder.AppendLine("## Trustee Threshold Rule");
        builder.AppendLine();
        if (projection.TrusteeThreshold is null)
        {
            builder.AppendLine("- No trustee-threshold rule applies to this package.");
        }
        else
        {
            builder.AppendLine($"- Required approval count: `{projection.TrusteeThreshold.RequiredApprovalCount}`");
            builder.AppendLine($"- Every accepted trustee must approve: `{projection.TrusteeThreshold.EveryAcceptedTrusteeMustApprove}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Public Key Record");
        builder.AppendLine();
        if (projection.CeremonyPublicKey is null)
        {
            builder.AppendLine("- No ceremony-bound public key record is attached.");
        }
        else
        {
            builder.AppendLine($"- Ceremony version id: `{projection.CeremonyPublicKey.CeremonyVersionId}`");
            builder.AppendLine($"- Ceremony version number: `{projection.CeremonyPublicKey.CeremonyVersionNumber}`");
            builder.AppendLine($"- Ceremony profile id: `{projection.CeremonyPublicKey.ProfileId}`");
            builder.AppendLine($"- Bound trustee count: `{projection.CeremonyPublicKey.BoundTrusteeCount}`");
            builder.AppendLine($"- Required approval count: `{projection.CeremonyPublicKey.RequiredApprovalCount}`");
            builder.AppendLine($"- Every active trustee must approve: `{projection.CeremonyPublicKey.EveryActiveTrusteeMustApprove}`");
            builder.AppendLine($"- Tally public key fingerprint: `{projection.CeremonyPublicKey.TallyPublicKeyFingerprint}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Warning Evidence");
        builder.AppendLine();
        if (projection.WarningEvidence.Count == 0)
        {
            builder.AppendLine("- No warning evidence was recorded.");
        }
        else
        {
            foreach (var warning in projection.WarningEvidence)
            {
                builder.AppendLine($"- `{warning.WarningCode}` acknowledged by `{warning.AcknowledgedByPublicAddress ?? "not recorded"}` at `{warning.AcknowledgedAt?.ToString("O") ?? "not recorded"}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Governed Finalization Approvals");
        builder.AppendLine();
        if (projection.FinalizationGovernedProposal is null)
        {
            builder.AppendLine("- No finalize proposal record is attached.");
        }
        else
        {
            builder.AppendLine($"- Proposal id: `{projection.FinalizationGovernedProposal.Id}`");
            builder.AppendLine($"- Action: `{projection.FinalizationGovernedProposal.ActionType}`");
            builder.AppendLine($"- Lifecycle state at creation: `{projection.FinalizationGovernedProposal.LifecycleStateAtCreation}`");
            builder.AppendLine($"- Proposed by: `{projection.FinalizationGovernedProposal.ProposedByPublicAddress}`");
            builder.AppendLine($"- Created at: `{projection.FinalizationGovernedProposal.CreatedAt:O}`");
        }

        if (projection.FinalizationApprovals.Count == 0)
        {
            builder.AppendLine("- No trustee approvals were recorded for this package.");
        }
        else
        {
            foreach (var approval in projection.FinalizationApprovals)
            {
                builder.AppendLine($"- `{approval.TrusteeUserAddress}` approved at `{approval.ApprovedAt:O}` note: {approval.ApprovalNote ?? "none"}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Finalization Share Evidence");
        builder.AppendLine();
        if (projection.FinalizationShares.Count == 0)
        {
            builder.AppendLine("- No finalization share evidence is attached.");
        }
        else
        {
            foreach (var share in projection.FinalizationShares)
            {
                builder.AppendLine($"- Share `{share.Id}` trustee `{share.TrusteeUserAddress}` index `{share.ShareIndex}` status `{share.Status}` material hash `{share.ShareMaterialHash}`");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildHumanProtocolPackageBindingContent(
        ProtocolPackageBindingProjection? binding)
    {
        var builder = new StringBuilder();
        AppendHumanProtocolPackageBindingContent(builder, binding);
        return builder.ToString().TrimEnd();
    }

    private static string BuildHumanOperationalRegulatoryContent(
        OperationalSecurityProjection operationalSecurity,
        RegulatoryClaimProjection? regulatoryClaim)
    {
        var builder = new StringBuilder();
        AppendHumanOperationalRegulatoryContent(builder, operationalSecurity, regulatoryClaim);
        return builder.ToString().TrimEnd();
    }

    private static void AppendHumanProtocolPackageBindingContent(
        StringBuilder builder,
        ProtocolPackageBindingProjection? binding)
    {
        if (binding is null)
        {
            builder.AppendLine("- Protocol package binding: `not recorded`");
            return;
        }

        builder.AppendLine($"- Protocol package binding id: `{binding.BindingId}`");
        builder.AppendLine($"- Package id: `{binding.PackageId}`");
        builder.AppendLine($"- Package version: `{binding.PackageVersion}`");
        builder.AppendLine($"- Selected profile: `{binding.SelectedProfileId}`");
        builder.AppendLine($"- Status: `{binding.Status}`");
        builder.AppendLine($"- Source: `{binding.Source}`");
        builder.AppendLine($"- Approval status: `{binding.ApprovalStatus}`");
        builder.AppendLine($"- SP-09 external review status: `{binding.ExternalReviewStatus}`");
        builder.AppendLine($"- SP-09 external review availability: `{binding.ExternalReviewAvailability}`");
        builder.AppendLine($"- SP-09 external review claim state: `{binding.ExternalReviewClaimState}`");
        builder.AppendLine($"- SP-09 external review summary: {binding.ExternalReviewCustomerSafeSummary}");
        builder.AppendLine($"- Spec package hash: `{binding.SpecPackageHash}`");
        builder.AppendLine($"- Proof package hash: `{binding.ProofPackageHash}`");
        builder.AppendLine($"- SP-08 release integrity manifest hash: `{binding.ReleaseManifestHash}`");
        builder.AppendLine($"- Draft revision: `{binding.DraftRevision}`");
        builder.AppendLine($"- Bound at: `{binding.BoundAt:O}`");
        builder.AppendLine($"- Sealed at: `{binding.SealedAt?.ToString("O") ?? "not sealed"}`");
        builder.AppendLine($"- Bound by: `{binding.BoundByPublicAddress}`");
        builder.AppendLine($"- Source transaction id: `{binding.SourceTransactionId?.ToString() ?? "none"}`");
        builder.AppendLine($"- Source block height: `{binding.SourceBlockHeight?.ToString() ?? "none"}`");
        builder.AppendLine($"- Source block id: `{binding.SourceBlockId?.ToString() ?? "none"}`");
        builder.AppendLine($"- Access-location note: {binding.AccessLocationOperationalNote}");
        AppendProtocolPackageAccessLocations(builder, "Spec access locations", binding.SpecAccessLocations);
        AppendProtocolPackageAccessLocations(builder, "Proof access locations", binding.ProofAccessLocations);
    }

    private static void AppendHumanOperationalRegulatoryContent(
        StringBuilder builder,
        OperationalSecurityProjection operationalSecurity,
        RegulatoryClaimProjection? regulatoryClaim)
    {
        var sectionStart = builder.Length;
        builder.AppendLine("- SP-10 operational security boundary: operational evidence state only; no rollout readiness, legal validation, public-election approval, or certification is asserted.");
        builder.AppendLine($"- SP-10 program version: `{operationalSecurity.ProgramVersion}`");
        builder.AppendLine($"- SP-10 deployment profile: `{operationalSecurity.DeploymentProfileId}`");
        builder.AppendLine($"- SP-10 evidence state: `{operationalSecurity.EvidenceState}`");
        builder.AppendLine($"- SP-10 primary result: `{operationalSecurity.PrimaryResultCode}`");
        builder.AppendLine($"- SP-10 blocks high assurance: `{operationalSecurity.BlocksHighAssurance}`");
        builder.AppendLine($"- SP-10 operational readiness caveat: {operationalSecurity.Feat106ReadinessCaveat}");
        builder.AppendLine($"- SP-10 release evidence mode: `{operationalSecurity.ReleaseEvidenceMode ?? "not recorded"}`");
        builder.AppendLine($"- SP-10 release manifest hash: `{operationalSecurity.ReleaseManifestHash ?? "not recorded"}`");
        builder.AppendLine($"- SP-10 immutable deployment ref: `{operationalSecurity.ImmutableDeploymentRef ?? "not recorded"}`");
        builder.AppendLine($"- SP-10 custody mode: `{operationalSecurity.CustodyMode ?? "not recorded"}`");
        builder.AppendLine($"- SP-10 executor key lifecycle: `{operationalSecurity.ExecutorKeyLifecycle ?? "not recorded"}`");
        builder.AppendLine($"- SP-10 incident status: `{operationalSecurity.IncidentStatus ?? "not recorded"}`");
        builder.AppendLine($"- SP-10 primary issue: {operationalSecurity.PrimaryIssue ?? "none"}");
        builder.AppendLine($"- SP-10 public evidence files: `{operationalSecurity.PublicEvidenceFiles.Count}`");
        builder.AppendLine($"- SP-10 restricted evidence files: `{operationalSecurity.RestrictedEvidenceFiles.Count}`");

        if (regulatoryClaim is null)
        {
            builder.AppendLine("- SP-11 regulatory tracker claim: `not exported`");
            builder.AppendLine("- SP-11 legal validation boundary: no legal advice, authority approval, public-election parity, or certification is asserted.");
        }
        else
        {
            builder.AppendLine($"- SP-11 tracker version: `{regulatoryClaim.TrackerVersion}`");
            builder.AppendLine($"- SP-11 jurisdiction id: `{regulatoryClaim.JurisdictionId}`");
            builder.AppendLine($"- SP-11 claim id: `{regulatoryClaim.ClaimId}`");
            builder.AppendLine($"- SP-11 claim state: `{regulatoryClaim.ClaimState}`");
            builder.AppendLine($"- SP-11 source checked at: `{regulatoryClaim.SourceCheckedAt:O}`");
            builder.AppendLine($"- SP-11 next review at: `{regulatoryClaim.NextReviewAt:O}`");
            builder.AppendLine($"- SP-11 tracker stale: `{regulatoryClaim.IsTrackerStale}`");
            builder.AppendLine($"- SP-11 source ref: `{regulatoryClaim.SourceRef}`");
            builder.AppendLine($"- SP-11 owner: `{regulatoryClaim.Owner}`");
            builder.AppendLine($"- SP-11 requires authority evidence: `{regulatoryClaim.RequiresAuthorityEvidence}`");
            builder.AppendLine($"- SP-11 authority evidence ref: `{regulatoryClaim.AuthorityEvidenceRef ?? "not recorded"}`");
            builder.AppendLine($"- SP-11 allowed wording: {regulatoryClaim.AllowedWording}");
            builder.AppendLine($"- SP-11 public evidence files: `{regulatoryClaim.PublicEvidenceFiles.Count}`");
            builder.AppendLine($"- SP-11 restricted evidence files: `{regulatoryClaim.RestrictedEvidenceFiles.Count}`");
            builder.AppendLine("- SP-11 legal validation boundary: tracker content is market intelligence, not legal advice; no certification or public-election parity is asserted.");
        }

        var sectionText = builder.ToString(sectionStart, builder.Length - sectionStart);
        if (ElectionSp10OperationalSecurityRules.ContainsForbiddenClaimPhrase(sectionText) ||
            ElectionSp11RegulatoryRules.ContainsForbiddenClaimPhrase(sectionText))
        {
            throw new InvalidOperationException("Generated operational/regulatory report wording contains a forbidden claim phrase.");
        }
    }

    private static string BuildHumanDeploymentProofBindingContent(
        DeploymentProofBindingProjection? deploymentProofBinding)
    {
        var builder = new StringBuilder();
        AppendHumanDeploymentProofBindingContent(builder, deploymentProofBinding);
        return builder.ToString().TrimEnd();
    }

    private static void AppendHumanDeploymentProofBindingContent(
        StringBuilder builder,
        DeploymentProofBindingProjection? deploymentProofBinding)
    {
        if (deploymentProofBinding is null)
        {
            builder.AppendLine("- Deployment proof binding ledger: `not exported`");
            builder.AppendLine("- Deployment proof boundary: no deployment-readiness claim is made in this report package.");
            return;
        }

        builder.AppendLine($"- Deployment proof ledger id: `{deploymentProofBinding.LedgerPublicId}`");
        builder.AppendLine($"- Deployment proof status: `{deploymentProofBinding.FinalStatus}`");
        builder.AppendLine($"- Deployment proof claim effect: `{deploymentProofBinding.ClaimEffect}`");
        builder.AppendLine($"- Deployment proof blocks validation claims: `{(deploymentProofBinding.BlocksDeploymentProofClaims ? "yes" : "no")}`");
        builder.AppendLine($"- Deployment proof summary: {deploymentProofBinding.ClaimSummary}");
        builder.AppendLine($"- Deployment profile: `{deploymentProofBinding.DeploymentProfile}`");
        builder.AppendLine($"- Deployment protocol version: `{deploymentProofBinding.DeploymentProtocolVersion}`");
        builder.AppendLine($"- Latest checkpoint id: `{deploymentProofBinding.LatestCheckpointId?.ToString() ?? "not recorded"}`");
        builder.AppendLine($"- Public ledger artifact id: `{deploymentProofBinding.LedgerArtifactId?.ToString() ?? "not exported"}`");
        builder.AppendLine($"- Public ledger file: `{deploymentProofBinding.LedgerArtifactFileName ?? "not exported"}`");
        builder.AppendLine($"- Public ledger hash: `{deploymentProofBinding.LedgerArtifactHash ?? "not exported"}`");
        builder.AppendLine($"- Deployment proof checkpoints: `{deploymentProofBinding.CheckpointCount}`");
        builder.AppendLine($"- Component observations: `{deploymentProofBinding.ComponentObservationCount}`");
        builder.AppendLine($"- Deployment events: `{deploymentProofBinding.EventCount}`");
        builder.AppendLine($"- Proof-family status records: `{deploymentProofBinding.ProofFamilyCount}`");
        builder.AppendLine(
            $"- WebClient proof status: `{deploymentProofBinding.WebClientProofStatus ?? "not recorded"}`");
        builder.AppendLine(
            $"- FEAT-137 retention/log privacy proof status: `{deploymentProofBinding.RetentionLogPrivacyProofStatus ?? "not recorded"}`");
        builder.AppendLine(
            $"- FEAT-137 retention/log privacy claim effect: `{deploymentProofBinding.RetentionLogPrivacyClaimEffect ?? "not recorded"}`");
        if (!string.IsNullOrWhiteSpace(deploymentProofBinding.WebClientProofMismatchCode))
        {
            builder.AppendLine(
                $"- WebClient proof mismatch code: `{deploymentProofBinding.WebClientProofMismatchCode}`");
        }

        if (!string.IsNullOrWhiteSpace(deploymentProofBinding.RetentionLogPrivacyMismatchCode))
        {
            builder.AppendLine(
                $"- FEAT-137 retention/log privacy mismatch code: `{deploymentProofBinding.RetentionLogPrivacyMismatchCode}`");
        }

        foreach (var limitation in deploymentProofBinding.ClaimLimitations)
        {
            builder.AppendLine($"- Deployment proof claim limitation: {limitation}");
        }

        builder.AppendLine("- Deployment proof status is separate from election outcome authority and does not decide the vote outcome.");
    }

    private static void AppendProtocolPackageAccessLocations(
        StringBuilder builder,
        string title,
        IReadOnlyList<ProtocolPackageAccessLocationProjection> accessLocations)
    {
        builder.AppendLine($"- {title}: `{accessLocations.Count}`");
        foreach (var accessLocation in accessLocations)
        {
            builder.AppendLine(
                $"  - `{accessLocation.LocationKind}` {accessLocation.Label}: {accessLocation.Location} (content hash `{accessLocation.ContentHash ?? "not recorded"}`)");
        }
    }

    private static string BuildHumanOutcomeContent(
        ElectionRecord election,
        OutcomeDeterminationProjection projection) =>
        $"""
        # Outcome Determination

        - Election id: `{projection.ElectionId}`
        - Binding status: `{projection.BindingStatus}`
        - Non-binding election: `{(projection.IsNonBindingElection ? "yes" : "no")}`
        - Governance mode: `{projection.GovernanceMode}`
        - Selected circuit/profile: `{projection.SelectedProfileId}`
        - Circuit class: `{projection.CircuitClassification}`
        - Profile family: `{projection.ModeProfileFamily}`
        - Outcome rule kind: `{projection.OutcomeRuleKind}`
        - Template key: `{projection.OutcomeTemplateKey}`
        - Calculation basis: `{projection.CalculationBasis}`
        - Tie resolution rule: `{projection.TieResolutionRule}`
        - Platform conclusion: `{projection.ConclusionLabel}`
        - Conclusion summary: {projection.ConclusionSummary}
        - Decisive option id: `{projection.DecisiveOptionId ?? "none"}`
        - Decisive option label: `{projection.DecisiveOptionLabel ?? "none"}`
        - Total voted: `{projection.TotalVotedCount}`
        - Eligible to vote: `{projection.EligibleToVoteCount}`
        - Blank votes: `{projection.BlankCount}`
        - Did not vote: `{projection.DidNotVoteCount}`
        - Turnout percent: `{projection.TurnoutPercent:F2}`

        The platform conclusion above is derived from frozen official counts and the frozen outcome
        rule for `{election.Title}`.
        """;

    private static string BuildHumanDisputeIndexContent(
        ElectionRecord election,
        Guid packageId,
        IReadOnlyList<DisputeArtifactCatalogEntryProjection> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Dispute Review Index");
        builder.AppendLine();
        builder.AppendLine($"- Election id: `{election.ElectionId}`");
        builder.AppendLine($"- Package id: `{packageId}`");
        builder.AppendLine($"- Catalog entries: `{entries.Count}`");
        builder.AppendLine($"- Binding status: `{election.BindingStatus}`");
        builder.AppendLine($"- Non-binding election: `{(election.BindingStatus == ElectionBindingStatus.NonBinding ? "yes" : "no")}`");
        builder.AppendLine($"- Governance mode: `{election.GovernanceMode}`");
        builder.AppendLine($"- Selected circuit/profile: `{election.SelectedProfileId}`");
        builder.AppendLine($"- Circuit class: `{GetCircuitClassificationLabel(election)}`");
        builder.AppendLine($"- Profile family: `{GetModeProfileFamilyLabel(election)}`");
        builder.AppendLine();
        builder.AppendLine("| Title | Kind | Format | Scope | Artifact id | Hash | Paired artifact id |");
        builder.AppendLine("|-------|------|--------|-------|-------------|------|--------------------|");
        foreach (var entry in entries)
        {
            builder.AppendLine(
                $"| {entry.Title} | {entry.ArtifactKind} | {entry.Format} | {entry.AccessScope} | `{entry.ArtifactId}` | `{entry.ContentHash}` | `{entry.PairedArtifactId?.ToString() ?? "none"}` |");
        }

        return builder.ToString().TrimEnd();
    }

    private static byte[] ComputeHashBytes(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static byte[] ComputeHashBytes(byte[] value) =>
        SHA256.HashData(value);

    private static string GetModeProfileFamilyLabel(ElectionRecord election) =>
        election.SelectedProfileDevOnly
            ? "dev/open ceremony profiles"
            : "production-like ceremony profiles";

    private static string GetCircuitClassificationLabel(ElectionRecord election) =>
        election.SelectedProfileDevOnly
            ? "Development"
            : "Production";

    private static string GetSecrecyBoundarySummary(ElectionRecord election) =>
        election.SelectedProfileDevOnly
            ? "This election runs the explicit non-binding open-audit path. Readable ballot content may appear where artifact visibility allows, so this mode is excluded from secret-ballot claims."
            : "This election runs the protected-ballot path. Result and report artifacts should expose aggregate outcomes and circuit metadata, not readable ballot choices.";

    private static string GetGovernanceCustodySummary(ElectionRecord election) =>
        election.GovernanceMode == ElectionGovernanceMode.AdminOnly
            ? election.SelectedProfileDevOnly
                ? "Admin-only open-audit custody keeps the owner-admin workflow while intentionally allowing readable ballot artifacts for audit review. This mode still does not expose reusable protected tally private keys or hidden single-ballot inspection authority beyond the explicit open-audit contract."
                : "Admin-only protected custody keeps tally release bound to the owner-admin protected custody profile. This path does not expose trustee shares, reusable tally private keys, or single-ballot inspection authority."
            : "Trustee-threshold custody requires exact-target aggregate tally release with executor-bound trustee submissions. This path does not expose arbitrary ballot inspection, raw trustee shares on persisted surfaces, or reusable tally private keys.";

    private static string SerializeJson(object payload) =>
        JsonSerializer.Serialize(payload, JsonOptions);

    private static string BuildHashHex(byte[]? value) =>
        value is { Length: > 0 }
            ? Convert.ToHexString(value).ToLowerInvariant()
            : string.Empty;

    private static string CreateSha256Fingerprint(byte[] value) =>
        $"sha256:{BuildHashHex(value)}";

    private static bool ByteArrayEquals(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWithAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));

    private sealed record VoidSupersededArtifactsProjection(
        string ElectionId,
        Guid VoidDecisionId,
        Guid PublicationAttemptId,
        IReadOnlyList<ElectionVoidSupersededPublicArtifactReference> Artifacts);

    private sealed record VoidPackageManifestProjection(
        string SchemaId,
        Guid PackageId,
        string ElectionId,
        Guid VoidDecisionId,
        Guid PublicationAttemptId,
        string Status,
        string VerifierResultCode,
        string PackageHashCanonicalization,
        string PackageHash,
        DateTime CreatedAt,
        IReadOnlyList<VoidPackageManifestEntryProjection> Entries);

    private sealed record VoidPackageManifestEntryProjection(
        string Path,
        string Sha256Hash,
        string MediaType,
        string AccessScope,
        string ArtifactKind,
        string Format);

    private sealed record FrozenEvidenceProjection(
        string ElectionId,
        SetupProjection Setup,
        BoundaryEvidenceProjection CloseBoundary,
        EligibilitySnapshotProjection? CloseEligibilitySnapshot,
        BoundaryEvidenceProjection TallyReadyBoundary,
        ResultArtifactProjection UnofficialResult,
        ResultArtifactProjection OfficialResult,
        ProtocolPackageBindingProjection? ProtocolPackageBinding,
        OperationalSecurityProjection OperationalSecurity,
        RegulatoryClaimProjection? RegulatoryClaim,
        AnomalyIntakeManifest? RestrictedAnomalyIntakeManifest,
        FinalizationSessionProjection? FinalizationSession,
        FinalizationReleaseProjection? FinalizationReleaseEvidence,
        IReadOnlyList<WarningEvidenceProjection> WarningEvidence,
        GovernedProposalProjection? FinalizationGovernedProposal,
        IReadOnlyList<GovernedApprovalProjection> FinalizationGovernedApprovals,
        IReadOnlyList<FinalizationShareProjection> FinalizationShares);

    private sealed record SetupProjection(
        string Title,
        string? ShortDescription,
        string OwnerPublicAddress,
        string? ExternalReferenceCode,
        int SourceDraftRevision,
        string BindingStatus,
        bool IsNonBindingElection,
        string GovernanceMode,
        string SelectedProfileId,
        string CircuitClassification,
        string ModeProfileFamily,
        string ParticipationPrivacyMode,
        string ReportingPolicy,
        string ReviewWindowPolicy,
        string OfficialVisibility,
        string SecrecyBoundarySummary,
        string CustodyBoundarySummary,
        int? RequiredApprovalCount,
        IReadOnlyList<ApprovedClientProjection> ApprovedClients,
        IReadOnlyList<ElectionOptionProjection> Options,
        TrusteeThresholdProjection? TrusteeThreshold,
        CeremonyPublicKeyProjection? CeremonyPublicKey);

    private sealed record ApprovedClientProjection(
        string ApplicationId,
        string Version);

    private sealed record ElectionOptionProjection(
        string OptionId,
        string DisplayLabel,
        string? ShortDescription,
        int BallotOrder,
        bool IsBlankOption);

    private sealed record TrusteeThresholdProjection(
        int RequiredApprovalCount,
        bool EveryAcceptedTrusteeMustApprove,
        IReadOnlyList<TrusteeProjection> AcceptedTrustees);

    private sealed record CeremonyPublicKeyProjection(
        Guid CeremonyVersionId,
        int CeremonyVersionNumber,
        string ProfileId,
        int BoundTrusteeCount,
        int RequiredApprovalCount,
        bool EveryActiveTrusteeMustApprove,
        string TallyPublicKeyFingerprint);

    private sealed record BoundaryEvidenceProjection(
        Guid Id,
        string ArtifactType,
        DateTime RecordedAt,
        string FrozenEligibleVoterSetHash,
        string AcceptedBallotSetHash,
        string PublishedBallotStreamHash,
        string FinalEncryptedTallyHash,
        Guid? SourceTransactionId,
        long? SourceBlockHeight,
        Guid? SourceBlockId);

    private sealed record EligibilitySnapshotProjection(
        Guid Id,
        string SnapshotType,
        DateTime RecordedAt,
        int RosteredCount,
        int LinkedCount,
        int ActiveDenominatorCount,
        int CountedParticipationCount,
        int BlankCount,
        int DidNotVoteCount,
        string RosteredSetHash,
        string ActiveDenominatorSetHash,
        string CountedParticipationSetHash);

    private sealed record ResultArtifactProjection(
        string ArtifactKind,
        string Visibility,
        IReadOnlyList<ResultOptionProjection> NamedOptionResults,
        int BlankCount,
        int TotalVotedCount,
        int EligibleToVoteCount,
        int DidNotVoteCount,
        Guid? DenominatorSnapshotId,
        Guid? DenominatorBoundaryArtifactId,
        string DenominatorHash,
        Guid? SourceResultArtifactId);

    private sealed record ResultOptionProjection(
        string OptionId,
        string DisplayLabel,
        string? ShortDescription,
        int BallotOrder,
        int Rank,
        int VoteCount);

    private sealed record FinalizationSessionProjection(
        Guid Id,
        string SessionPurpose,
        string Status,
        Guid CloseArtifactId,
        string AcceptedBallotSetHash,
        string FinalEncryptedTallyHash,
        string TargetTallyId,
        int RequiredShareCount,
        IReadOnlyList<TrusteeProjection> EligibleTrustees,
        DateTime CreatedAt,
        DateTime? CompletedAt,
        Guid? ReleaseEvidenceId,
        Guid? GovernedProposalId,
        string CreatedByPublicAddress);

    private sealed record FinalizationReleaseProjection(
        Guid Id,
        Guid FinalizationSessionId,
        string ReleaseMode,
        Guid CloseArtifactId,
        string AcceptedBallotSetHash,
        string FinalEncryptedTallyHash,
        string TargetTallyId,
        int AcceptedShareCount,
        DateTime CompletedAt,
        IReadOnlyList<TrusteeProjection> AcceptedTrustees);

    private sealed record TrusteeProjection(
        string TrusteeUserAddress,
        string? TrusteeDisplayName,
        string Status,
        DateTime? SentAt,
        DateTime? RespondedAt);

    private sealed record WarningEvidenceProjection(
        string WarningCode,
        int DraftRevision,
        string? AcknowledgedByPublicAddress,
        DateTime? AcknowledgedAt,
        Guid? SourceTransactionId,
        long? SourceBlockHeight,
        Guid? SourceBlockId);

    private sealed record GovernedProposalProjection(
        Guid Id,
        string ActionType,
        string LifecycleStateAtCreation,
        string ProposedByPublicAddress,
        DateTime CreatedAt);

    private sealed record GovernedApprovalProjection(
        Guid Id,
        Guid ProposalId,
        string ActionType,
        string TrusteeUserAddress,
        string? TrusteeDisplayName,
        string? ApprovalNote,
        DateTime ApprovedAt,
        Guid? SourceTransactionId,
        long? SourceBlockHeight,
        Guid? SourceBlockId);

    private sealed record FinalizationShareProjection(
        Guid Id,
        Guid FinalizationSessionId,
        string TrusteeUserAddress,
        string? TrusteeDisplayName,
        int ShareIndex,
        string TargetType,
        string Status,
        string ClaimedAcceptedBallotSetHash,
        string ClaimedFinalEncryptedTallyHash,
        string ClaimedTargetTallyId,
        Guid? ClaimedCeremonyVersionId,
        string? ClaimedTallyPublicKeyFingerprint,
        string ShareMaterialHash,
        string? FailureCode,
        string? FailureReason,
        DateTime SubmittedAt,
        Guid? SourceTransactionId,
        long? SourceBlockHeight,
        Guid? SourceBlockId);

    private sealed record ManifestProjection(
        Guid PackageId,
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        Guid EvidenceGraphArtifactId,
        string ElectionId,
        string ElectionTitle,
        int AttemptNumber,
        Guid? PreviousAttemptId,
        DateTime AttemptedAt,
        string AttemptedBy,
        string FrozenEvidenceFingerprint,
        string BindingStatus,
        bool IsNonBindingElection,
        string GovernanceMode,
        string SelectedProfileId,
        string CircuitClassification,
        string ModeProfileFamily,
        string? BoundCeremonyProfileId,
        string? TallyPublicKeyFingerprint,
        string OfficialVisibility,
        string SecrecyBoundarySummary,
        string CustodyBoundarySummary,
        int AcceptedTrusteeCount,
        int RosterEntryCount,
        ProtocolPackageBindingProjection? ProtocolPackageBinding,
        OperationalSecurityProjection OperationalSecurity,
        RegulatoryClaimProjection? RegulatoryClaim,
        DeploymentProofBindingProjection? DeploymentProofBinding,
        Guid FinalizeArtifactId,
        Guid OfficialResultArtifactId,
        string OfficialResultHash,
        int WarningCount,
        int GovernedApprovalCount,
        int FinalizationShareCount,
        string OutcomeLabel,
        string OutcomeSummary);

    private sealed record EvidenceGraphProjection(
        Guid ArtifactId,
        Guid ManifestArtifactId,
        string ElectionId,
        string BindingStatus,
        bool IsNonBindingElection,
        string GovernanceMode,
        string SelectedProfileId,
        string CircuitClassification,
        string ModeProfileFamily,
        Guid CloseArtifactId,
        Guid? CloseEligibilitySnapshotId,
        Guid TallyReadyArtifactId,
        Guid UnofficialResultArtifactId,
        Guid OfficialResultArtifactId,
        Guid FinalizeArtifactId,
        Guid? FinalizationSessionId,
        Guid? FinalizationReleaseEvidenceId,
        string AcceptedBallotSetHash,
        string PublishedBallotStreamHash,
        string FinalEncryptedTallyHash,
        string ActiveDenominatorSetHash,
        int RosterEntryCount,
        int WarningCount,
        int GovernedApprovalCount,
        int FinalizationShareCount,
        ProtocolPackageBindingProjection? ProtocolPackageBinding,
        OperationalSecurityProjection OperationalSecurity,
        RegulatoryClaimProjection? RegulatoryClaim,
        DeploymentProofBindingProjection? DeploymentProofBinding,
        EvidenceGraphAnomalyIntakeManifestProjection? RestrictedAnomalyIntakeManifest,
        IReadOnlyList<TrusteeProjection> Trustees);

    private sealed record EvidenceGraphAnomalyIntakeManifestProjection(
        string NodeType,
        Guid ArtifactId,
        string CanonicalizationId,
        string ManifestHash,
        string ScopeId,
        string PackageReadinessStatusId,
        IReadOnlyList<string> PackageReadinessBlockerIds,
        int ThreadCount,
        int AttachmentManifestCount,
        int RedactionCount,
        int RecipientStatusCount,
        IReadOnlyList<Guid> AnomalyThreadIds,
        IReadOnlyList<Guid> AttachmentManifestIds,
        IReadOnlyList<Guid> RedactionEventIds,
        IReadOnlyList<Guid> SourceEventIds);

    private sealed record RestrictedAnomalyIntakeManifestArtifactProjection(
        string ArtifactSchemaId,
        string ManifestHash,
        string CanonicalizationId,
        string ScopeId,
        string PackageReadinessStatusId,
        IReadOnlyList<string> PackageReadinessBlockerIds,
        int ThreadCount,
        int AttachmentManifestCount,
        int RedactionCount,
        int RecipientStatusCount,
        AnomalyIntakeManifest Manifest);

    private sealed record ResultReportProjection(
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        string ElectionId,
        string ElectionTitle,
        Guid OfficialResultArtifactId,
        string BindingStatus,
        bool IsNonBindingElection,
        string GovernanceMode,
        string SelectedProfileId,
        string CircuitClassification,
        string ModeProfileFamily,
        string? BoundCeremonyProfileId,
        string? TallyPublicKeyFingerprint,
        string Visibility,
        string SecrecyBoundarySummary,
        string CustodyBoundarySummary,
        int TotalVotedCount,
        int EligibleToVoteCount,
        int DidNotVoteCount,
        int BlankCount,
        decimal TurnoutPercent,
        Guid? DenominatorSnapshotId,
        Guid? DenominatorBoundaryArtifactId,
        string DenominatorHash,
        string OutcomeLabel,
        string OutcomeSummary,
        PublicAnomalySummary PublicAnomalySummary,
        AnomalyReportReadinessProjection AnomalyReportReadiness,
        IReadOnlyList<ResultOptionShareProjection> OptionResults);

    private sealed record ResultOptionShareProjection(
        string OptionId,
        string DisplayLabel,
        string? ShortDescription,
        int Rank,
        int VoteCount,
        decimal VoteSharePercent);

    private sealed record RosterProjection(
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        string ElectionId,
        string BindingStatus,
        bool IsNonBindingElection,
        string GovernanceMode,
        string SelectedProfileId,
        string CircuitClassification,
        string ModeProfileFamily,
        int EntryCount,
        IReadOnlyList<RosterEntryProjection> Entries);

    private sealed record RosterEntryProjection(
        string OrganizationVoterId,
        string LinkStatus,
        string VotingRightStatus,
        string? LinkedActorPublicAddress,
        bool WasPresentAtOpen,
        bool WasActiveAtOpen,
        string ParticipationStatus,
        bool CountsAsParticipation);

    private sealed record AuditProvenanceProjection(
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        string ElectionId,
        string FrozenEvidenceFingerprint,
        SetupProjection Setup,
        Guid CloseArtifactId,
        Guid TallyReadyArtifactId,
        Guid UnofficialResultArtifactId,
        Guid OfficialResultArtifactId,
        string OfficialResultHash,
        Guid FinalizeArtifactId,
        Guid? FinalizationSessionId,
        Guid? FinalizationReleaseEvidenceId,
        string AcceptedBallotSetHash,
        string PublishedBallotStreamHash,
        string FinalEncryptedTallyHash,
        string DenominatorHash,
        IReadOnlyList<TrusteeProjection> Trustees,
        CeremonyPublicKeyProjection? CeremonyPublicKey,
        TrusteeThresholdProjection? TrusteeThreshold,
        ProtocolPackageBindingProjection? ProtocolPackageBinding,
        OperationalSecurityProjection OperationalSecurity,
        RegulatoryClaimProjection? RegulatoryClaim,
        DeploymentProofBindingProjection? DeploymentProofBinding,
        GovernedProposalProjection? FinalizationGovernedProposal,
        IReadOnlyList<GovernedApprovalProjection> FinalizationApprovals,
        IReadOnlyList<FinalizationShareProjection> FinalizationShares,
        IReadOnlyList<WarningEvidenceProjection> WarningEvidence,
        Guid? SourceTransactionId,
        long? SourceBlockHeight,
        Guid? SourceBlockId);

    private sealed record OutcomeDeterminationProjection(
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        string ElectionId,
        string BindingStatus,
        bool IsNonBindingElection,
        string GovernanceMode,
        string SelectedProfileId,
        string CircuitClassification,
        string ModeProfileFamily,
        string OutcomeRuleKind,
        string OutcomeTemplateKey,
        string CalculationBasis,
        string TieResolutionRule,
        string ConclusionLabel,
        string ConclusionSummary,
        string? DecisiveOptionId,
        string? DecisiveOptionLabel,
        int TotalVotedCount,
        int EligibleToVoteCount,
        decimal TurnoutPercent,
        int BlankCount,
        int DidNotVoteCount);

    private sealed record ProtocolPackageBindingProjection(
        Guid BindingId,
        string PackageId,
        string PackageVersion,
        string SelectedProfileId,
        string SpecPackageHash,
        string ProofPackageHash,
        string ReleaseManifestHash,
        string ApprovalStatus,
        string ExternalReviewStatus,
        string ExternalReviewAvailability,
        string ExternalReviewClaimState,
        string ExternalReviewCustomerSafeSummary,
        string Status,
        string Source,
        int DraftRevision,
        DateTime BoundAt,
        DateTime? SealedAt,
        string BoundByPublicAddress,
        Guid? SourceTransactionId,
        long? SourceBlockHeight,
        Guid? SourceBlockId,
        IReadOnlyList<ProtocolPackageAccessLocationProjection> SpecAccessLocations,
        IReadOnlyList<ProtocolPackageAccessLocationProjection> ProofAccessLocations,
        string AccessLocationOperationalNote);

    private sealed record ProtocolPackageAccessLocationProjection(
        string LocationKind,
        string Label,
        string Location,
        string? ContentHash);

    private sealed record OperationalSecurityProjection(
        string ProgramVersion,
        string DeploymentProfileId,
        string EvidenceState,
        bool DoesNotCompleteFeat106Readiness,
        string Feat106ReadinessCaveat,
        string? ReleaseEvidenceMode,
        string? ReleaseManifestHash,
        string? ImmutableDeploymentRef,
        string? CustodyMode,
        string? ExecutorKeyLifecycle,
        string? IncidentStatus,
        bool BlocksHighAssurance,
        string PrimaryResultCode,
        string? PrimaryIssue,
        IReadOnlyList<string> PublicEvidenceFiles,
        IReadOnlyList<string> RestrictedEvidenceFiles);

    private sealed record RegulatoryClaimProjection(
        string TrackerVersion,
        string JurisdictionId,
        string ClaimId,
        string ClaimState,
        DateTimeOffset SourceCheckedAt,
        DateTimeOffset NextReviewAt,
        string SourceRef,
        string Owner,
        bool RequiresAuthorityEvidence,
        string? AuthorityEvidenceRef,
        bool IsTrackerStale,
        string AllowedWording,
        IReadOnlyList<string> PublicEvidenceFiles,
        IReadOnlyList<string> RestrictedEvidenceFiles);

    private sealed record DeploymentProofBindingProjection(
        Guid LedgerId,
        string LedgerPublicId,
        string Status,
        string FinalStatus,
        string ClaimEffect,
        bool BlocksDeploymentProofClaims,
        string ClaimSummary,
        string DeploymentProfile,
        string DeploymentProtocolVersion,
        Guid? LatestCheckpointId,
        Guid? LedgerArtifactId,
        string? LedgerArtifactFileName,
        string? LedgerArtifactHash,
        int CheckpointCount,
        int ComponentObservationCount,
        int EventCount,
        int ProofFamilyCount,
        string? WebClientProofStatus,
        string? WebClientProofMismatchCode,
        string? RetentionLogPrivacyProofStatus,
        string? RetentionLogPrivacyClaimEffect,
        string? RetentionLogPrivacyMismatchCode,
        IReadOnlyList<string> ClaimLimitations);

    private sealed record DisputeReviewIndexProjection(
        Guid MachineArtifactId,
        Guid HumanArtifactId,
        string ElectionId,
        Guid PackageId,
        string BindingStatus,
        bool IsNonBindingElection,
        string GovernanceMode,
        string SelectedProfileId,
        string CircuitClassification,
        string ModeProfileFamily,
        IReadOnlyList<DisputeArtifactCatalogEntryProjection> Entries);

    private sealed record DisputeArtifactCatalogEntryProjection(
        Guid ArtifactId,
        string ArtifactKind,
        string Format,
        string AccessScope,
        string Title,
        string FileName,
        string ContentHash,
        Guid? PairedArtifactId);
}
