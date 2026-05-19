using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using HushShared.Elections.Verification.Model;

namespace OperationalEvidencePromoter;

public static class OperationalEvidenceArtifactGenerator
{
    public const string OperationalRunPath = "operational-run.json";
    public const string OperationalCheckResultsPath = "operational-check-results.json";
    public const string OperationalExceptionsPath = "operational-exceptions.json";
    public const string OperationalReadinessFragmentPath = "operational-readiness-fragment.json";
    public const string DownstreamHandoffPath = "downstream-handoff.json";
    public const string PublicSummaryMarkdownPath = "public-safe-operational-summary.md";
    public const string RestrictedIndexMarkdownPath = "restricted-operational-evidence-index.md";

    private static readonly string[] PublicSp10ArtifactPaths =
    [
        VerificationPackageFileNames.Sp10OperationalSecuritySummary,
        VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
        VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
        VerificationPackageFileNames.Sp10OperationalVerifierOutput,
    ];

    private static readonly string[] RestrictedSp10ArtifactPaths =
    [
        VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot,
        VerificationPackageFileNames.RestrictedSp10LoggingEvidence,
        VerificationPackageFileNames.RestrictedSp10BackupRestoreEvidence,
        VerificationPackageFileNames.RestrictedSp10IncidentEvidence,
        VerificationPackageFileNames.RestrictedSp10AuditorRoomAccessLog,
    ];

    public static OperationalEvidenceGeneratedArtifactSet Generate(
        OperationalEvidencePromotionPaths paths,
        DateTimeOffset? generatedAt = null,
        string runRelativePath = OperationalEvidenceContracts.AcceptedRunFixture)
    {
        var run = OperationalEvidenceContracts.ReadJsonObject(
            Path.Combine(paths.ExamplesRoot, runRelativePath),
            runRelativePath);
        return Generate(paths, run, generatedAt);
    }

    public static OperationalEvidenceGeneratedArtifactSet Generate(
        OperationalEvidencePromotionPaths paths,
        JsonObject run,
        DateTimeOffset? generatedAt = null)
    {
        var effectiveGeneratedAt = generatedAt ?? ParseTimestamp(GetString(run, "generatedAt")) ?? DateTimeOffset.UtcNow;
        var checkResult = OperationalEvidenceChecker.Evaluate(paths, run);
        var sources = LoadSources(paths, run);
        var artifacts = new List<OperationalEvidenceGeneratedArtifact>
        {
            JsonArtifact(
                VerificationPackageFileNames.Sp10OperationalSecuritySummary,
                OperationalEvidenceArtifactVisibility.Public,
                BuildOperationalSecuritySummary(run, sources, checkResult, effectiveGeneratedAt)),
            JsonArtifact(
                VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
                OperationalEvidenceArtifactVisibility.Public,
                BuildOperationalDeploymentEvidence(run, checkResult)),
            JsonArtifact(
                VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
                OperationalEvidenceArtifactVisibility.Public,
                BuildOperationalCustodyEvidence(run)),
            JsonArtifact(
                VerificationPackageFileNames.Sp10OperationalVerifierOutput,
                OperationalEvidenceArtifactVisibility.Public,
                BuildOperationalVerifierOutput(run, checkResult, effectiveGeneratedAt)),
            JsonArtifact(
                VerificationPackageFileNames.RestrictedSp10AccessControlSnapshot,
                OperationalEvidenceArtifactVisibility.Restricted,
                BuildRestrictedAccessControl(run, sources)),
            JsonArtifact(
                VerificationPackageFileNames.RestrictedSp10LoggingEvidence,
                OperationalEvidenceArtifactVisibility.Restricted,
                BuildRestrictedLogging(run, sources)),
            JsonArtifact(
                VerificationPackageFileNames.RestrictedSp10BackupRestoreEvidence,
                OperationalEvidenceArtifactVisibility.Restricted,
                BuildRestrictedBackupRestore(run, sources)),
            JsonArtifact(
                VerificationPackageFileNames.RestrictedSp10IncidentEvidence,
                OperationalEvidenceArtifactVisibility.Restricted,
                BuildRestrictedIncident(run, sources)),
            JsonArtifact(
                VerificationPackageFileNames.RestrictedSp10AuditorRoomAccessLog,
                OperationalEvidenceArtifactVisibility.Restricted,
                BuildRestrictedAuditorRoom(run, sources)),
        };

        var scanFindings = OperationalEvidenceMaterialScanner.ScanGeneratedArtifacts(artifacts);
        artifacts.Add(JsonArtifact(
            OperationalRunPath,
            OperationalEvidenceArtifactVisibility.Internal,
            BuildOperationalRun(run, effectiveGeneratedAt)));
        artifacts.Add(JsonArtifact(
            OperationalCheckResultsPath,
            OperationalEvidenceArtifactVisibility.Internal,
            BuildOperationalCheckResults(run, checkResult, scanFindings, effectiveGeneratedAt)));
        artifacts.Add(JsonArtifact(
            OperationalExceptionsPath,
            OperationalEvidenceArtifactVisibility.Internal,
            BuildOperationalExceptions(run, sources, checkResult, scanFindings, effectiveGeneratedAt)));
        artifacts.Add(JsonArtifact(
            OperationalReadinessFragmentPath,
            OperationalEvidenceArtifactVisibility.Internal,
            BuildOperationalReadinessFragment(run, checkResult, scanFindings)));
        artifacts.Add(JsonArtifact(
            DownstreamHandoffPath,
            OperationalEvidenceArtifactVisibility.Internal,
            BuildDownstreamHandoff(run, artifacts, checkResult, scanFindings, effectiveGeneratedAt)));
        artifacts.Add(new OperationalEvidenceGeneratedArtifact(
            PublicSummaryMarkdownPath,
            OperationalEvidenceArtifactVisibility.Public,
            "text/markdown",
            OperationalEvidenceMarkdownRenderer.RenderPublicSummary(run, checkResult, artifacts)));
        artifacts.Add(new OperationalEvidenceGeneratedArtifact(
            RestrictedIndexMarkdownPath,
            OperationalEvidenceArtifactVisibility.Restricted,
            "text/markdown",
            OperationalEvidenceMarkdownRenderer.RenderRestrictedIndex(run, artifacts)));

        scanFindings = OperationalEvidenceMaterialScanner.ScanGeneratedArtifacts(artifacts);
        var generationStatus = checkResult.BlocksAcceptedEvidence || scanFindings.Count > 0
            ? "blocked"
            : checkResult.Status;

        return new OperationalEvidenceGeneratedArtifactSet(
            checkResult.RunId,
            effectiveGeneratedAt,
            generationStatus,
            checkResult,
            artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray(),
            scanFindings);
    }

    private static JsonObject BuildOperationalSecuritySummary(
        JsonObject run,
        IReadOnlyDictionary<string, JsonObject> sources,
        OperationalEvidenceCheckSetResult checkResult,
        DateTimeOffset generatedAt)
    {
        var access = GetSource(sources, "accessControlSource");
        var backup = GetSource(sources, "backupRestoreSource");
        var incident = GetSource(sources, "incidentSource");
        var auditorRoom = GetSource(sources, "auditorRoomSource");
        var summary = ToJsonObject(new ElectionSp10OperationalSecurityStatusArtifactRecord(
            Schema: ElectionSp10ProfileIds.OperationalSecuritySummarySchema,
            ElectionId: GetElectionId(run),
            ProgramVersion: ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
            DeploymentProfileId: ElectionSp10ProfileIds.DeploymentProfileManagedAwsContainerV1,
            EvidenceState: MapSp10EvidenceState(checkResult),
            DoesNotCompleteFeat106Readiness: true,
            Feat106ReadinessCaveat: "Operational evidence is limited to FEAT-133 internal rehearsal scope and remains separate from rollout readiness.",
            ReleaseEvidenceMode: ElectionSp08ProfileIds.EvidenceModeOfficial,
            ReleaseManifestHash: GetString(run["sp08Refs"] as JsonObject, "releaseManifestHash"),
            ImmutableDeploymentRef: GetString(run["sp08Refs"] as JsonObject, "immutableDeploymentRef"),
            CustodyMode: GetString(run["feat131Refs"] as JsonObject, "custodyMode"),
            ExecutorKeyLifecycle: null,
            AccessSnapshotHashOrRestrictedRef: GetString(access, "snapshotHash"),
            BackupRestoreHashOrRestrictedRef: GetString(backup, "restoreTestRef"),
            IncidentStatus: GetIncidentStatus(incident),
            AuditorRoomAccessLogHashOrRestrictedRef: GetString(auditorRoom, "accessLogRef"),
            BlocksHighAssurance: ElectionSp10OperationalSecurityRules.BlocksHighAssurance(MapSp10EvidenceState(checkResult)),
            PrimaryResultCode: ElectionSp10OperationalSecurityRules.GetPrimaryResultCode(MapSp10EvidenceState(checkResult)),
            PrimaryIssue: checkResult.Warnings.Count == 0
                ? null
                : $"Warnings allowed for internal non-binding rehearsal only: {string.Join(", ", checkResult.Warnings)}.",
            PublicEvidenceFiles: PublicSp10ArtifactPaths,
            RestrictedEvidenceFiles: [],
            PublicPrivacyBoundary:
            [
                "credential-free-public-output",
                "provider-key-references-excluded",
                "no_raw_logs",
                "no_operator_contact_details",
                "no_voter_data",
                "no_vote_choice",
                "trustee-secret-material-excluded",
            ]));

        summary["runId"] = checkResult.RunId;
        summary["claimLevel"] = GetString(run, "claimLevel");
        summary["generatedAt"] = FormatTimestamp(generatedAt);
        summary["sourceGap"] = GetString(run, "sourceGap");
        summary["acceptanceGate"] = GetString(run, "acceptanceGate");
        summary["sourceDeploymentProfile"] = Clone(run["deploymentProfile"]);
        summary["custodyProfile"] = Clone(run["custodyProfile"]);
        summary["opsSummary"] = new JsonObject
        {
            ["status"] = checkResult.Status,
            ["blockers"] = ToJsonArray(checkResult.Blockers),
            ["warnings"] = ToJsonArray(checkResult.Warnings),
            ["notApplicable"] = ToJsonArray(checkResult.NotApplicable),
            ["checks"] = ToJsonArray(checkResult.Checks.Select(check => check.CheckId)),
        };
        summary["restrictedEvidenceRefs"] = ToJsonArray(BuildRestrictedRefs(sources));
        summary["claimEffect"] = GetString(run, "claimEffect");
        summary["residualRisk"] = Clone(run["residualRisk"]);
        return summary;
    }

    private static JsonObject BuildOperationalDeploymentEvidence(
        JsonObject run,
        OperationalEvidenceCheckSetResult checkResult)
    {
        var sp08Refs = run["sp08Refs"] as JsonObject;
        var deployment = ToJsonObject(new ElectionSp10OperationalDeploymentEvidenceArtifactRecord(
            Schema: ElectionSp10ProfileIds.OperationalDeploymentEvidenceSchema,
            ElectionId: GetElectionId(run),
            ProgramVersion: ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
            DeploymentProfileId: ElectionSp10ProfileIds.DeploymentProfileManagedAwsContainerV1,
            EvidenceState: MapSp10EvidenceState(checkResult),
            ReleaseEvidenceMode: ElectionSp08ProfileIds.EvidenceModeOfficial,
            ReleaseManifestHash: GetString(sp08Refs, "releaseManifestHash"),
            ImmutableDeploymentRef: GetString(sp08Refs, "immutableDeploymentRef"),
            SourceAuthority: "FEAT-132 public Deployment Proof Packages catalog",
            PublicEvidenceFiles:
            [
                VerificationPackageFileNames.Sp10OperationalDeploymentEvidence,
            ],
            PublicPrivacyBoundary:
            [
                "deployment_proof_ids_hashes_and_catalog_refs_only",
                "no_kms_or_provider_account_material",
            ]));

        deployment["feat132Refs"] = Clone(run["feat132Refs"]);
        deployment["sp08Agreement"] = new JsonObject
        {
            ["releaseManifestHash"] = GetString(sp08Refs, "releaseManifestHash"),
            ["immutableDeploymentRef"] = GetString(sp08Refs, "immutableDeploymentRef"),
            ["agreesWithFeat132DeploymentRefs"] = sp08Refs?["agreesWithFeat132DeploymentRefs"]?.GetValue<bool>() == true,
        };
        return deployment;
    }

    private static JsonObject BuildOperationalCustodyEvidence(JsonObject run)
    {
        var refs = run["feat131Refs"] as JsonObject;
        var custody = ToJsonObject(new ElectionSp10OperationalCustodyEvidenceArtifactRecord(
            Schema: ElectionSp10ProfileIds.OperationalCustodyEvidenceSchema,
            ElectionId: GetElectionId(run),
            ProgramVersion: ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
            GovernanceMode: GetString(run, "claimLevel") ?? "internal_non_binding_rehearsal",
            CustodyMode: GetString(refs, "custodyMode") ?? ElectionSp10ProfileIds.CustodyModeAwsKmsPerElectionEnvelopeV1,
            ExecutorKeyLifecycle: "not_applicable_for_rehearsal_profile",
            TrusteeThresholdCustodyExpected: refs?["requiredForProfile"]?.GetValue<bool>() == true,
            PublicEvidenceFiles:
            [
                VerificationPackageFileNames.Sp10OperationalCustodyEvidence,
            ],
            PublicPrivacyBoundary:
            [
                "feat131_public_id_hash_and_gate_refs_only",
                "provider-key-references-excluded",
                "trustee-secret-material-excluded",
            ]));

        custody["feat131Refs"] = new JsonObject
        {
            ["acceptedGateIds"] = Clone(refs?["acceptedGateIds"]),
            ["custodyEvidenceId"] = GetString(refs, "custodyEvidenceId"),
            ["custodyMode"] = GetString(refs, "custodyMode"),
            ["custodyStatus"] = GetString(refs, "custodyStatus"),
            ["publicCustodyHash"] = GetString(refs, "publicCustodyHash"),
            ["requiredForProfile"] = refs?["requiredForProfile"]?.GetValue<bool>() == true,
            ["restrictedCustodyIndexRef"] = GetString(refs, "restrictedCustodyIndexRef"),
            ["residualRiskIds"] = Clone(refs?["residualRiskIds"]),
            ["unresolvedBlockers"] = Clone(refs?["unresolvedBlockers"]),
        };
        return custody;
    }

    private static JsonObject BuildOperationalVerifierOutput(
        JsonObject run,
        OperationalEvidenceCheckSetResult checkResult,
        DateTimeOffset generatedAt)
    {
        var verifierOutput = ToJsonObject(new ElectionSp10OperationalVerifierOutputArtifactRecord(
            ElectionId: GetElectionId(run),
            VerifierProfileId: VerificationProfileIds.PublicAnonymousV1,
            Schema: ElectionSp10ProfileIds.OperationalVerifierOutputSchema,
            VerifiedAt: generatedAt.UtcDateTime,
            Results: checkResult.Checks.Select(ToVerifierResult).ToArray()));

        verifierOutput["runId"] = checkResult.RunId;
        verifierOutput["status"] = checkResult.Status;
        verifierOutput["blockers"] = ToJsonArray(checkResult.Blockers);
        verifierOutput["warnings"] = ToJsonArray(checkResult.Warnings);
        verifierOutput["notApplicable"] = ToJsonArray(checkResult.NotApplicable);
        verifierOutput["placeholderFindings"] = ToJsonArray(checkResult.PlaceholderFindings);
        verifierOutput["forbiddenMaterialFindings"] = ToJsonArray(checkResult.ForbiddenMaterialFindings);
        return verifierOutput;
    }

    private static JsonObject BuildRestrictedAccessControl(
        JsonObject run,
        IReadOnlyDictionary<string, JsonObject> sources)
    {
        var source = GetSource(sources, "accessControlSource");
        return new JsonObject
        {
            ["schema"] = ElectionSp10ProfileIds.RestrictedAccessControlSnapshotSchema,
            ["electionId"] = GetElectionId(run),
            ["programVersion"] = ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
            ["accessPolicyId"] = GetString(source, "accessPolicyId"),
            ["accessPolicyVersion"] = GetString(source, "accessPolicyVersion"),
            ["evidenceStatus"] = GetString(source, "evidenceStatus") ?? "blocked",
            ["grantRevocationRefs"] = Clone(source?["grantRevocationRefs"]),
            ["restrictedActorHashesRef"] = GetString(source, "restrictedActorHashesRef"),
            ["roleCounts"] = Clone(source?["roleCounts"]),
            ["snapshotGeneratedAt"] = GetString(source, "snapshotGeneratedAt"),
            ["snapshotHash"] = GetString(source, "snapshotHash"),
            ["sourceRef"] = GetSourceRef(run, "accessControlSource"),
            ["supportAuditorReasonCodes"] = Clone(source?["supportAuditorReasonCodes"]),
        };
    }

    private static JsonObject BuildRestrictedLogging(
        JsonObject run,
        IReadOnlyDictionary<string, JsonObject> sources)
    {
        var source = GetSource(sources, "loggingSource");
        return new JsonObject
        {
            ["schema"] = ElectionSp10ProfileIds.RestrictedLoggingEvidenceSchema,
            ["electionId"] = GetElectionId(run),
            ["programVersion"] = ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
            ["evidenceStatus"] = GetString(source, "evidenceStatus") ?? "blocked",
            ["logEvidenceRefs"] = Clone(source?["logEvidenceRefs"]),
            ["loggingPolicyId"] = GetString(source, "loggingPolicyId"),
            ["loggingPolicyVersion"] = GetString(source, "loggingPolicyVersion"),
            ["monitoringWindow"] = Clone(source?["monitoringWindow"]),
            ["privacySafeSummary"] = GetString(source, "privacySafeSummary"),
            ["privacyScanResult"] = "public and restricted generated-output scans executed by FEAT-133 promoter",
            ["rawLogRestrictedRefs"] = Clone(source?["rawLogRestrictedRefs"]),
            ["sourceRef"] = GetSourceRef(run, "loggingSource"),
        };
    }

    private static JsonObject BuildRestrictedBackupRestore(
        JsonObject run,
        IReadOnlyDictionary<string, JsonObject> sources)
    {
        var source = GetSource(sources, "backupRestoreSource");
        return new JsonObject
        {
            ["schema"] = ElectionSp10ProfileIds.RestrictedBackupRestoreEvidenceSchema,
            ["electionId"] = GetElectionId(run),
            ["programVersion"] = ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
            ["backupPolicyId"] = GetString(source, "backupPolicyId"),
            ["deploymentProfileCovered"] = GetString(source, "deploymentProfileCovered"),
            ["evidenceHashOrRef"] = GetString(source, "restoreTestRef"),
            ["evidenceStatus"] = GetString(source, "evidenceStatus") ?? "blocked",
            ["residualRisk"] = GetString(source, "residualRisk"),
            ["restorePolicyId"] = GetString(source, "restorePolicyId"),
            ["restoreResult"] = GetString(source, "restoreResult"),
            ["restoreScope"] = GetString(source, "restoreScope"),
            ["restoreTestAt"] = GetString(source, "restoreTestAt"),
            ["restoreTestRef"] = GetString(source, "restoreTestRef"),
            ["sourceRef"] = GetSourceRef(run, "backupRestoreSource"),
        };
    }

    private static JsonObject BuildRestrictedIncident(
        JsonObject run,
        IReadOnlyDictionary<string, JsonObject> sources)
    {
        var source = GetSource(sources, "incidentSource");
        var incidentCount = source?["incidentCount"]?.GetValue<int>() ?? 0;
        return new JsonObject
        {
            ["schema"] = ElectionSp10ProfileIds.RestrictedIncidentEvidenceSchema,
            ["electionId"] = GetElectionId(run),
            ["programVersion"] = ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
            ["declarationActorHash"] = GetString(source, "declarationActorHash"),
            ["declarationAt"] = GetString(source, "declarationAt"),
            ["evidenceHashOrRef"] = GetString(source, "incidentRegisterHash"),
            ["evidenceStatus"] = GetString(source, "evidenceStatus") ?? "blocked",
            ["incidentCount"] = incidentCount,
            ["incidentPolicyId"] = GetString(source, "incidentPolicyId"),
            ["incidentRegisterHash"] = GetString(source, "incidentRegisterHash"),
            ["incidentRegisterVersion"] = GetString(source, "incidentRegisterVersion"),
            ["incidentStatus"] = incidentCount == 0
                ? ElectionSp10ProfileIds.IncidentStatusNoIncidentDeclared
                : ElectionSp10ProfileIds.IncidentStatusIncidentDeclared,
            ["materialElectionImpactDeclared"] = incidentCount > 0,
            ["monitoringWindow"] = Clone(source?["monitoringWindow"]),
            ["publicSafeStatement"] = GetString(source, "publicSafeStatement"),
            ["restrictedIncidentRefs"] = Clone(source?["restrictedIncidentRefs"]),
            ["sourceRef"] = GetSourceRef(run, "incidentSource"),
        };
    }

    private static JsonObject BuildRestrictedAuditorRoom(
        JsonObject run,
        IReadOnlyDictionary<string, JsonObject> sources)
    {
        var source = GetSource(sources, "auditorRoomSource");
        return new JsonObject
        {
            ["schema"] = ElectionSp10ProfileIds.RestrictedAuditorRoomAccessLogSchema,
            ["electionId"] = GetElectionId(run),
            ["programVersion"] = ElectionSp10ProfileIds.OperationalSecurityProgramVersion,
            ["accessLogHash"] = GetString(source, "accessLogRef"),
            ["accessLogRef"] = GetString(source, "accessLogRef"),
            ["accessModel"] = GetString(source, "auditorRoomModelVersion"),
            ["auditorRoomModelVersion"] = GetString(source, "auditorRoomModelVersion"),
            ["evidenceStatus"] = GetString(source, "evidenceStatus") ?? "blocked",
            ["grantCount"] = source?["grantCount"]?.GetValue<int>() ?? 0,
            ["noAccessDeclaration"] = GetString(source, "noAccessDeclaration"),
            ["restrictedGrantRefs"] = Clone(source?["restrictedGrantRefs"]),
            ["sourceRef"] = GetSourceRef(run, "auditorRoomSource"),
        };
    }

    private static JsonObject BuildOperationalRun(JsonObject run, DateTimeOffset generatedAt)
    {
        var generated = (JsonObject)run.DeepClone();
        generated["generatedAt"] = FormatTimestamp(generatedAt);
        generated["generatedBy"] = "OperationalEvidencePromoter";
        generated["artifactGenerationMode"] = "phase_4_in_memory";
        return generated;
    }

    private static JsonObject BuildOperationalCheckResults(
        JsonObject run,
        OperationalEvidenceCheckSetResult checkResult,
        IReadOnlyList<OperationalEvidenceMaterialFinding> scanFindings,
        DateTimeOffset generatedAt)
    {
        return new JsonObject
        {
            ["resultId"] = $"OPS-RESULT-{checkResult.RunId}",
            ["runId"] = checkResult.RunId,
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["status"] = scanFindings.Count > 0 ? "blocked" : checkResult.Status,
            ["claimLevel"] = GetString(run, "claimLevel"),
            ["checks"] = ToJsonArray(checkResult.Checks.Select(check => new JsonObject
            {
                ["checkId"] = check.CheckId,
                ["status"] = check.Status,
                ["severity"] = check.Severity,
                ["reason"] = check.Reason,
                ["evidenceRefs"] = ToJsonArray(check.EvidenceRefs),
                ["claimImpact"] = ToJsonArray(check.ClaimImpact),
            })),
            ["blockers"] = ToJsonArray(checkResult.Blockers),
            ["warnings"] = ToJsonArray(checkResult.Warnings),
            ["notApplicable"] = ToJsonArray(checkResult.NotApplicable),
            ["placeholderFindings"] = ToJsonArray(checkResult.PlaceholderFindings),
            ["forbiddenMaterialFindings"] = ToJsonArray(scanFindings.Select(FormatFinding)),
            ["scoreEffect"] = BuildScoreEffect(checkResult, scanFindings),
            ["claimEffect"] = scanFindings.Count > 0
                ? "Generated material scan findings block FEAT-130 promotion."
                : GetString(run, "claimEffect"),
            ["summary"] = scanFindings.Count > 0
                ? "Operational evidence generation is blocked by redaction findings."
                : "Operational evidence generated for internal non-binding rehearsal scope.",
        };
    }

    private static JsonObject BuildOperationalExceptions(
        JsonObject run,
        IReadOnlyDictionary<string, JsonObject> sources,
        OperationalEvidenceCheckSetResult checkResult,
        IReadOnlyList<OperationalEvidenceMaterialFinding> scanFindings,
        DateTimeOffset generatedAt)
    {
        var sourceExceptions = GetSource(sources, "exceptionsSource");
        var exceptions = new JsonArray();
        if (sourceExceptions?["exceptions"] is JsonArray sourceArray)
        {
            foreach (var item in sourceArray)
            {
                exceptions.Add(Clone(item));
            }
        }

        foreach (var finding in scanFindings)
        {
            exceptions.Add(new JsonObject
            {
                ["exceptionId"] = $"OPS-005-{finding.Boundary.ToUpperInvariant()}-{OperationalEvidenceCanonicalJson.ComputeSha256(finding.RelativePath + finding.Category)[..12]}",
                ["relatedOpsCheck"] = "OPS-005",
                ["severity"] = "blocker",
                ["state"] = "blocked",
                ["reason"] = $"{finding.Boundary} generated artifact '{finding.RelativePath}' contains forbidden {finding.Category} material.",
                ["claimImpact"] = finding.ClaimImpact,
                ["allowedForClaimLevel"] = "none",
                ["evidenceRefs"] = new JsonArray(finding.RelativePath),
                ["ownerRole"] = "engineeringOwner",
                ["createdAt"] = FormatTimestamp(generatedAt),
                ["expiresAt"] = null,
                ["residualRisk"] = "Redaction issue must be fixed before promotion.",
            });
        }

        var blockingIds = exceptions
            .OfType<JsonObject>()
            .Where(exception => GetString(exception, "severity") == "blocker" || GetString(exception, "state") == "blocked")
            .Select(exception => GetString(exception, "exceptionId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
        var warningIds = exceptions
            .OfType<JsonObject>()
            .Where(exception => GetString(exception, "severity") == "warning")
            .Select(exception => GetString(exception, "exceptionId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();

        return new JsonObject
        {
            ["runId"] = checkResult.RunId,
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["exceptions"] = exceptions,
            ["blockingExceptionIds"] = ToJsonArray(blockingIds),
            ["warningExceptionIds"] = ToJsonArray(warningIds),
            ["deferredExceptionIds"] = new JsonArray(),
            ["placeholderExceptionIds"] = ToJsonArray(checkResult.PlaceholderFindings),
            ["claimImpactSummary"] = blockingIds.Length > 0
                ? "Blocking exceptions prevent FEAT-130 score promotion."
                : "Warnings are allowed for internal non-binding rehearsal only and do not support stronger rollout claims.",
            ["sourceClaimEffect"] = GetString(run, "claimEffect"),
        };
    }

    private static JsonObject BuildOperationalReadinessFragment(
        JsonObject run,
        OperationalEvidenceCheckSetResult checkResult,
        IReadOnlyList<OperationalEvidenceMaterialFinding> scanFindings)
    {
        var accepted = !checkResult.BlocksAcceptedEvidence && scanFindings.Count == 0;
        return new JsonObject
        {
            ["fragmentId"] = "RDY-EVID-AT-RDY-006-FEAT-133-001",
            ["featureSlice"] = "FEAT-133",
            ["sourceGap"] = GetString(run, "sourceGap"),
            ["acceptanceGate"] = GetString(run, "acceptanceGate"),
            ["dimensionId"] = "RDY-DIM-007",
            ["evidenceRefs"] = ToJsonArray(PublicSp10ArtifactPaths.Concat([OperationalCheckResultsPath])),
            ["dimensionScoreChange"] = new JsonObject
            {
                ["previousScore"] = 6,
                ["acceptedScore"] = accepted ? 8 : 6,
                ["requiresAcceptedEvidence"] = true,
            },
            ["totalScoreChange"] = new JsonObject
            {
                ["previousScore"] = 59,
                ["acceptedScore"] = accepted ? 61 : 59,
            },
            ["blockerChanges"] = ToJsonArray(checkResult.Blockers.Concat(scanFindings.Select(finding => "OPS-005"))),
            ["claimEffect"] = accepted
                ? "Eligible to support internal non-binding rehearsal readiness after FEAT-130 promotion."
                : "Not eligible for FEAT-130 score promotion until blockers are resolved.",
            ["residualRisk"] = Clone(run["residualRisk"]),
            ["signoff"] = Clone(run["signoff"]),
            ["promotionInstructions"] = "FEAT-130 must promote this fragment; FEAT-133 must not update the readiness register directly.",
        };
    }

    private static JsonObject BuildDownstreamHandoff(
        JsonObject run,
        IReadOnlyList<OperationalEvidenceGeneratedArtifact> artifacts,
        OperationalEvidenceCheckSetResult checkResult,
        IReadOnlyList<OperationalEvidenceMaterialFinding> scanFindings,
        DateTimeOffset generatedAt)
    {
        var publicRefs = artifacts
            .Where(artifact => artifact.Visibility == OperationalEvidenceArtifactVisibility.Public)
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact => new JsonObject
            {
                ["path"] = artifact.RelativePath,
                ["sha256Hash"] = artifact.Sha256Hash,
            });
        var restrictedRefs = artifacts
            .Where(artifact => artifact.Visibility == OperationalEvidenceArtifactVisibility.Restricted)
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact => new JsonObject
            {
                ["refId"] = BuildRestrictedRefId(artifact.RelativePath),
                ["sha256Hash"] = artifact.Sha256Hash,
            });

        return new JsonObject
        {
            ["handoffId"] = $"OPS-HANDOFF-FEAT-133-{checkResult.RunId}",
            ["producerFeature"] = "FEAT-133",
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["runId"] = checkResult.RunId,
            ["status"] = scanFindings.Count > 0 ? "blocked" : checkResult.Status,
            ["publicPackageRefs"] = ToJsonArray(publicRefs),
            ["restrictedPackageRefs"] = ToJsonArray(restrictedRefs),
            ["opsSummaryRef"] = OperationalCheckResultsPath,
            ["readinessFragmentRef"] = OperationalReadinessFragmentPath,
            ["feat132Refs"] = BuildFeat132Handoff(run["feat132Refs"] as JsonObject),
            ["feat131Refs"] = BuildFeat131Handoff(run["feat131Refs"] as JsonObject),
            ["exceptions"] = ToJsonArray(checkResult.Warnings.Concat(checkResult.Blockers).Concat(scanFindings.Select(FormatFinding))),
            ["residualRisk"] = Clone(run["residualRisk"]),
            ["consumerInstructions"] = new JsonObject
            {
                ["FEAT-130"] = "Promote operational-readiness-fragment.json only when evidence state is accepted or accepted_with_warnings and no blockers exist.",
                ["FEAT-137"] = "Consume logging policy and restricted log refs without copying raw logs.",
                ["FEAT-141"] = "Include public package refs, restricted refs by id/hash, warnings, residual risk, and claim wording impact.",
                ["FEAT-142"] = "Read promoted FEAT-130 register state; do not consume FEAT-133 files directly.",
            },
        };
    }

    private static VerifierCheckResultRecord ToVerifierResult(OperationalEvidenceCheckResult check)
    {
        return new VerifierCheckResultRecord(
            check.CheckId,
            MapVerifierStatus(check.Status),
            MapVerifierResultCode(check.CheckId, check.Status),
            check.Reason,
            BuildEvidenceDictionary(check));
    }

    private static VerificationCheckStatus MapVerifierStatus(string status) =>
        status switch
        {
            OperationalEvidenceCheckStatuses.Passed => VerificationCheckStatus.Pass,
            OperationalEvidenceCheckStatuses.Warning => VerificationCheckStatus.Warn,
            OperationalEvidenceCheckStatuses.Blocked => VerificationCheckStatus.Fail,
            OperationalEvidenceCheckStatuses.NotApplicable => VerificationCheckStatus.NotApplicable,
            _ => VerificationCheckStatus.Fail,
        };

    private static string MapVerifierResultCode(string checkId, string status)
    {
        if (status is OperationalEvidenceCheckStatuses.Passed or OperationalEvidenceCheckStatuses.NotApplicable)
        {
            return checkId == ElectionSp10ProfileIds.DeploymentProfileDeclaredCheckCode
                ? VerificationResultCodes.OperationalSecurityProfileDeclared
                : VerificationResultCodes.OperationalSecurityEvidenceValid;
        }

        return checkId switch
        {
            ElectionSp10ProfileIds.ReleaseDeploymentBindingCheckCode => VerificationResultCodes.OperationalSecurityReleaseBindingMissing,
            ElectionSp10ProfileIds.AccessControlSnapshotCheckCode => VerificationResultCodes.OperationalSecurityAccessSnapshotMissing,
            ElectionSp10ProfileIds.CustodyModeDeclaredCheckCode => VerificationResultCodes.OperationalSecurityCustodyModeMissing,
            ElectionSp10ProfileIds.ExecutorKeyLifecycleCheckCode => VerificationResultCodes.OperationalSecurityExecutorKeyLifecycleMissing,
            ElectionSp10ProfileIds.ForbiddenMaterialScanCheckCode => VerificationResultCodes.OperationalSecurityForbiddenMaterial,
            ElectionSp10ProfileIds.BackupRestoreEvidenceCheckCode => VerificationResultCodes.OperationalSecurityBackupRestoreMissing,
            ElectionSp10ProfileIds.IncidentDeclarationCheckCode => VerificationResultCodes.OperationalSecurityIncidentDeclarationMissing,
            ElectionSp10ProfileIds.AuditorRoomAccessLogCheckCode => VerificationResultCodes.OperationalSecurityAuditorRoomMissing,
            _ => VerificationResultCodes.OperationalSecurityEvidenceMissing,
        };
    }

    private static IReadOnlyDictionary<string, string> BuildEvidenceDictionary(OperationalEvidenceCheckResult check)
    {
        var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["status"] = check.Status,
            ["severity"] = check.Severity,
        };
        foreach (var evidenceRef in check.EvidenceRefs)
        {
            evidence[evidenceRef] = "evidence_ref";
        }

        return evidence;
    }

    private static JsonObject BuildScoreEffect(
        OperationalEvidenceCheckSetResult checkResult,
        IReadOnlyList<OperationalEvidenceMaterialFinding> scanFindings)
    {
        var accepted = !checkResult.BlocksAcceptedEvidence && scanFindings.Count == 0;
        return new JsonObject
        {
            ["dimensionId"] = "RDY-DIM-007",
            ["previousScore"] = 6,
            ["acceptedScore"] = accepted ? 8 : 6,
            ["totalPreviousScore"] = 59,
            ["totalAcceptedScore"] = accepted ? 61 : 59,
        };
    }

    private static IReadOnlyList<JsonObject> BuildRestrictedRefs(IReadOnlyDictionary<string, JsonObject> sources)
    {
        var refs = new List<JsonObject>();
        AddRef(refs, "access-control", GetString(GetSource(sources, "accessControlSource"), "restrictedActorHashesRef"), GetString(GetSource(sources, "accessControlSource"), "snapshotHash"));
        AddRef(refs, "logging", FirstString(GetSource(sources, "loggingSource")?["rawLogRestrictedRefs"] as JsonArray), null);
        AddRef(refs, "backup-restore", GetString(GetSource(sources, "backupRestoreSource"), "restoreTestRef"), null);
        AddRef(refs, "incident", FirstString(GetSource(sources, "incidentSource")?["restrictedIncidentRefs"] as JsonArray), GetString(GetSource(sources, "incidentSource"), "incidentRegisterHash"));
        AddRef(refs, "auditor-room", GetString(GetSource(sources, "auditorRoomSource"), "accessLogRef"), null);
        return refs;
    }

    private static void AddRef(List<JsonObject> refs, string kind, string? refId, string? sha256Hash)
    {
        if (string.IsNullOrWhiteSpace(refId))
        {
            return;
        }

        refs.Add(new JsonObject
        {
            ["kind"] = kind,
            ["refId"] = refId,
            ["sha256Hash"] = sha256Hash,
        });
    }

    private static JsonObject BuildFeat132Handoff(JsonObject? refs)
    {
        var catalog = refs?["publicCatalogRef"] as JsonObject;
        var catalogRef = catalog is null
            ? null
            : $"{GetString(catalog, "repository")}/{GetString(catalog, "path")}@{GetString(catalog, "commit")}";
        return new JsonObject
        {
            ["bindingLedgerId"] = GetString(refs, "bindingLedgerId"),
            ["deploymentProofSetId"] = GetString(refs, "deploymentProofSetId"),
            ["publicCatalogRef"] = catalogRef,
            ["serverNodeProofHash"] = GetString(refs, "serverNodeProofHash"),
            ["serverNodeProofId"] = GetString(refs, "serverNodeProofId"),
            ["webClientProofHash"] = GetString(refs, "webClientProofHash"),
            ["webClientProofId"] = GetString(refs, "webClientProofId"),
        };
    }

    private static JsonObject BuildFeat131Handoff(JsonObject? refs) =>
        new()
        {
            ["custodyEvidenceId"] = GetString(refs, "custodyEvidenceId"),
            ["publicCustodyHash"] = GetString(refs, "publicCustodyHash"),
            ["restrictedCustodyIndexRef"] = GetString(refs, "restrictedCustodyIndexRef"),
        };

    private static IReadOnlyDictionary<string, JsonObject> LoadSources(
        OperationalEvidencePromotionPaths paths,
        JsonObject run)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (run["sourceRefs"] is not JsonObject sourceRefs)
        {
            return result;
        }

        foreach (var key in OperationalEvidenceContracts.RequiredSourceRefKeys)
        {
            var relativePath = GetString(sourceRefs, key);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var fullPath = Path.Combine(paths.SourceRoot, relativePath);
            if (File.Exists(fullPath))
            {
                result[key] = OperationalEvidenceContracts.ReadJsonObject(fullPath, relativePath);
            }
        }

        return result;
    }

    private static OperationalEvidenceGeneratedArtifact JsonArtifact(
        string relativePath,
        OperationalEvidenceArtifactVisibility visibility,
        JsonObject content) =>
        new(relativePath, visibility, "application/json", OperationalEvidenceCanonicalJson.Serialize(content));

    private static JsonObject ToJsonObject<T>(T value) =>
        JsonSerializer.SerializeToNode(value, VerificationJson.Options)?.AsObject() ??
        throw new InvalidOperationException("Generated artifact did not serialize to a JSON object.");

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonArray ToJsonArray(IEnumerable<JsonObject> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    private static JsonObject? GetSource(IReadOnlyDictionary<string, JsonObject> sources, string key) =>
        sources.TryGetValue(key, out var source) ? source : null;

    private static string? GetSourceRef(JsonObject run, string key) =>
        GetString(run["sourceRefs"] as JsonObject, key);

    private static string GetElectionId(JsonObject run) =>
        GetString(run, "electionPublicId") ?? GetString(run, "rehearsalPublicId") ?? GetString(run, "runId") ?? "unknown";

    private static string? GetString(JsonObject? obj, string propertyName)
    {
        if (obj is null)
        {
            return null;
        }

        try
        {
            return obj[propertyName]?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? FirstString(JsonArray? array)
    {
        if (array is null)
        {
            return null;
        }

        foreach (var item in array)
        {
            try
            {
                var value = item?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return null;
    }

    private static string MapSp10EvidenceState(OperationalEvidenceCheckSetResult checkResult)
    {
        if (checkResult.BlocksAcceptedEvidence || checkResult.Status == "blocked")
        {
            return ElectionSp10ProfileIds.EvidenceStateBlocked;
        }

        return checkResult.Warnings.Count > 0
            ? ElectionSp10ProfileIds.EvidenceStateManagedProfileExceptionDeclared
            : ElectionSp10ProfileIds.EvidenceStateManagedProfileEvidenceAvailable;
    }

    private static string GetIncidentStatus(JsonObject? incident)
    {
        var incidentCount = incident?["incidentCount"]?.GetValue<int>() ?? 0;
        return incidentCount == 0
            ? ElectionSp10ProfileIds.IncidentStatusNoIncidentDeclared
            : ElectionSp10ProfileIds.IncidentStatusIncidentDeclared;
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string BuildRestrictedRefId(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        return $"REST-FEAT-133-{fileName.ToUpperInvariant().Replace("-", "_", StringComparison.Ordinal)}";
    }

    private static string FormatFinding(OperationalEvidenceMaterialFinding finding) =>
        $"{finding.Boundary}:{finding.RelativePath}:{finding.Category}:{finding.Evidence}";
}
