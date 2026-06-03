using System.Text;
using System.Text.Json.Nodes;

namespace SecondProductionLikeOperationalRunPromoter;

public sealed record SecondProductionLikeOperationalRunArtifact(string RelativePath, string Content)
{
    public string Sha256Hash => SecondProductionLikeOperationalRunContracts.Sha256Hex(Content);
    public int SizeBytes => Encoding.UTF8.GetByteCount(Content);
}

public sealed record SecondProductionLikeOperationalRunGeneratedPackage(
    string Status,
    string PackageRoot,
    JsonObject Source,
    IReadOnlyList<SecondProductionLikeOperationalRunArtifact> Artifacts)
{
    public int ArtifactCount => Artifacts.Count;
}

public static class SecondProductionLikeOperationalRunArtifactGenerator
{
    public const string ReadmePath = "README.md";
    public const string ManifestPath = "second-production-like-run-manifest.json";
    public const string PackageIndexPath = "second-production-like-run-package.json";
    public const string Feat154BaselineCurrentnessSummaryPath = "validation/feat154-baseline-currentness-summary.json";
    public const string DeploymentProofBindingSummaryPath = "validation/deployment-proof-binding-summary.json";
    public const string MonitoringAlertingSummaryPath = "validation/monitoring-alerting-summary.json";
    public const string BackupRestoreSummaryPath = "validation/backup-restore-summary.json";
    public const string SupportOperatorHandoffSummaryPath = "validation/support-operator-handoff-summary.json";
    public const string SecuritySupportFreshnessSummaryPath = "validation/security-support-freshness-summary.json";
    public const string IncidentResponseWalkthroughSummaryPath = "validation/incident-response-walkthrough-summary.json";
    public const string PostmortemSummaryPath = "validation/postmortem-summary.json";
    public const string NoSecretScanResultPath = "validation/no-secret-scan-result.json";
    public const string ReadinessFragmentPath = "readiness/second-production-like-run-readiness-fragment.json";
    public const string ScoreProposalPath = "readiness/second-production-like-run-score-proposal.json";
    public const string DownstreamHandoffPath = "handoff/second-production-like-run-downstream-handoff.json";
    public const string RestrictedEvidenceIndexPath = "restricted/restricted-evidence-index.schema-note.md";

    public static readonly string[] RequiredArtifactPaths =
    [
        ReadmePath,
        ManifestPath,
        PackageIndexPath,
        Feat154BaselineCurrentnessSummaryPath,
        DeploymentProofBindingSummaryPath,
        MonitoringAlertingSummaryPath,
        BackupRestoreSummaryPath,
        SupportOperatorHandoffSummaryPath,
        SecuritySupportFreshnessSummaryPath,
        IncidentResponseWalkthroughSummaryPath,
        PostmortemSummaryPath,
        NoSecretScanResultPath,
        ReadinessFragmentPath,
        ScoreProposalPath,
        DownstreamHandoffPath,
        RestrictedEvidenceIndexPath,
    ];

    private static readonly string[] GeneratedForbiddenNeedles =
    [
        "arn:aws",
        "aws_access_key_id",
        "aws_secret_access_key",
        "begin private key",
        "credential=",
        "password=",
        "connection string",
        "client_secret",
        "account_id",
        "operator identity:",
        "raw ci log",
        "runbook:",
        "provider_account",
        "incident_payload_raw",
        "private screenshot",
        "voter_data=",
        "kms_key=",
        "kms_alias=",
        @"c:\mywork\hushnetworkorg\hush-documents",
    ];

    public static SecondProductionLikeOperationalRunGeneratedPackage Generate(
        SecondProductionLikeOperationalRunPromotionPaths paths,
        string? sourceInput = null,
        string? outputRoot = null,
        DateTimeOffset? generatedAt = null,
        bool publicOnly = false)
    {
        var source = SecondProductionLikeOperationalRunContracts.ValidateForPromotion(paths, sourceInput, publicOnly);
        var packageRoot = ResolvePackageRoot(paths, source, outputRoot);
        var generatedAtText = generatedAt?.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ??
            SecondProductionLikeOperationalRunContracts.GetString(source, "generatedAt");

        var preScanArtifacts = new List<SecondProductionLikeOperationalRunArtifact>
        {
            new(ReadmePath, BuildReadme(source)),
            JsonArtifact(Feat154BaselineCurrentnessSummaryPath, BuildFeat154BaselineCurrentnessSummary(source, generatedAtText)),
            JsonArtifact(DeploymentProofBindingSummaryPath, BuildDeploymentProofBindingSummary(source, generatedAtText)),
            JsonArtifact(MonitoringAlertingSummaryPath, BuildOperationalGateSummary(source, generatedAtText, "monitoringAlerting", "second-production-like-run-monitoring-alerting-summary.v1")),
            JsonArtifact(BackupRestoreSummaryPath, BuildOperationalGateSummary(source, generatedAtText, "backupRestore", "second-production-like-run-backup-restore-summary.v1")),
            JsonArtifact(SupportOperatorHandoffSummaryPath, BuildOperationalGateSummary(source, generatedAtText, "supportOperatorHandoff", "second-production-like-run-support-operator-handoff-summary.v1")),
            JsonArtifact(SecuritySupportFreshnessSummaryPath, BuildOperationalGateSummary(source, generatedAtText, "securitySupportFreshness", "second-production-like-run-security-support-freshness-summary.v1")),
            JsonArtifact(IncidentResponseWalkthroughSummaryPath, BuildOperationalGateSummary(source, generatedAtText, "incidentResponseWalkthrough", "second-production-like-run-incident-response-walkthrough-summary.v1")),
            JsonArtifact(PostmortemSummaryPath, BuildOperationalGateSummary(source, generatedAtText, "postmortem", "second-production-like-run-postmortem-summary.v1")),
            JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, generatedAtText)),
            JsonArtifact(ScoreProposalPath, BuildScoreProposal(source, generatedAtText)),
            JsonArtifact(DownstreamHandoffPath, BuildDownstreamHandoff(source, generatedAtText)),
        };

        var findings = ScanGeneratedArtifacts(preScanArtifacts);
        var artifacts = new List<SecondProductionLikeOperationalRunArtifact>(preScanArtifacts)
        {
            JsonArtifact(NoSecretScanResultPath, BuildNoSecretScanResult(generatedAtText, preScanArtifacts.Count, findings)),
            new(RestrictedEvidenceIndexPath, BuildRestrictedEvidenceIndexNote(source)),
        };
        artifacts.Insert(1, JsonArtifact(PackageIndexPath, BuildPackageIndex(source, generatedAtText, artifacts)));
        artifacts.Insert(1, JsonArtifact(ManifestPath, BuildManifest(source, generatedAtText, artifacts)));

        findings = ScanGeneratedArtifacts(artifacts);
        if (findings.Count > 0)
        {
            throw new SecondProductionLikeOperationalRunPromotionException(
                "FEAT-163 generated second production-like run public-safety scan failed.",
                findings);
        }

        return new SecondProductionLikeOperationalRunGeneratedPackage("draft", packageRoot, source, artifacts);
    }

    public static string ResolvePackageRoot(
        SecondProductionLikeOperationalRunPromotionPaths paths,
        JsonObject source,
        string? outputRoot = null)
    {
        var root = Path.GetFullPath(outputRoot ?? paths.PackagesRoot);
        SecondProductionLikeOperationalRunContracts.EnsurePathUnder(paths.WorkspaceRoot, root, "FEAT-163 package output root");
        var runId = SecondProductionLikeOperationalRunContracts.GetString(
            SecondProductionLikeOperationalRunContracts.RequireObject(source, "secondRunProfile"),
            "runId");
        var packageRoot = Path.GetFullPath(Path.Combine(root, SecondProductionLikeOperationalRunPromotionPaths.PackageFamilyFolder, runId));
        SecondProductionLikeOperationalRunContracts.EnsurePathUnder(root, packageRoot, "FEAT-163 package root");
        return packageRoot;
    }

    public static IReadOnlyList<string> ScanGeneratedArtifacts(IReadOnlyList<SecondProductionLikeOperationalRunArtifact> artifacts)
    {
        var findings = new List<string>();
        foreach (var artifact in artifacts)
        {
            var text = artifact.Content.ToLowerInvariant();
            foreach (var forbidden in GeneratedForbiddenNeedles)
            {
                if (text.Contains(forbidden, StringComparison.Ordinal))
                {
                    findings.Add($"{artifact.RelativePath} contains forbidden generated marker {forbidden}.");
                }
            }
        }

        return findings;
    }

    private static SecondProductionLikeOperationalRunArtifact JsonArtifact(string relativePath, JsonObject content) =>
        new(relativePath, SecondProductionLikeOperationalRunContracts.CanonicalJson(content));

    private static JsonObject BuildManifest(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<SecondProductionLikeOperationalRunArtifact> artifacts)
    {
        var profile = SecondProductionLikeOperationalRunContracts.RequireObject(source, "secondRunProfile");
        var scoreProposal = SecondProductionLikeOperationalRunContracts.RequireObject(source, "scoreProposal");
        return new JsonObject
        {
            ["schemaVersion"] = SecondProductionLikeOperationalRunContracts.PackageManifestSchemaVersion,
            ["packageId"] = "FEAT163-SECOND-PRODUCTION-LIKE-RUN-PACKAGE-20260603-001",
            ["featureId"] = SecondProductionLikeOperationalRunContracts.FeatureId,
            ["runId"] = SecondProductionLikeOperationalRunContracts.GetString(profile, "runId"),
            ["sourceRef"] = new JsonObject
            {
                ["sourceId"] = SecondProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
                ["sourceRepo"] = "https://github.com/Hushnetwork-social/Operational-Evidence-Second-Run",
                ["sourceBranch"] = "main",
                ["sourceSha256Hash"] = SecondProductionLikeOperationalRunContracts.Sha256Hex(SecondProductionLikeOperationalRunContracts.CanonicalJson(source)),
            },
            ["generatedAt"] = generatedAt,
            ["status"] = "draft",
            ["artifacts"] = new JsonArray(artifacts
                .Where(artifact => artifact.RelativePath != ManifestPath)
                .Select(artifact => new JsonObject
                {
                    ["path"] = artifact.RelativePath,
                    ["sha256Hash"] = artifact.Sha256Hash,
                    ["mediaType"] = artifact.RelativePath.EndsWith(".md", StringComparison.Ordinal) ? "text/markdown" : "application/json",
                    ["sizeBytes"] = artifact.SizeBytes,
                    ["visibility"] = artifact.RelativePath.StartsWith("restricted/", StringComparison.Ordinal) ? "restricted_ref_only" : "public",
                })
                .ToArray<JsonNode?>()),
            ["validationSummary"] = new JsonObject
            {
                ["allRequiredGatesAccepted"] = true,
                ["resultCodes"] = new JsonArray(
                    "FEAT163_BASELINE_CURRENTNESS_ACCEPTED",
                    "FEAT163_RUNTIME_PROOF_ACCEPTED",
                    "FEAT163_MONITORING_ALERTING_ACCEPTED",
                    "FEAT163_BACKUP_RESTORE_ACCEPTED",
                    "FEAT163_SUPPORT_OPERATOR_HANDOFF_ACCEPTED",
                    "FEAT163_SECURITY_SUPPORT_FRESHNESS_ACCEPTED",
                    "FEAT163_INCIDENT_RESPONSE_ACCEPTED",
                    "FEAT163_POSTMORTEM_ACCEPTED",
                    "FEAT163_NO_SECRET_SCAN_ACCEPTED"),
                ["noSecretScanStatus"] = "accepted",
                ["publicOnlyReplayStatus"] = "accepted",
            },
            ["scoreProposal"] = new JsonObject
            {
                ["dimensionId"] = SecondProductionLikeOperationalRunContracts.GetString(scoreProposal, "dimensionId"),
                ["fromScore"] = SecondProductionLikeOperationalRunContracts.GetInt(scoreProposal, "fromScore"),
                ["toScore"] = SecondProductionLikeOperationalRunContracts.GetInt(scoreProposal, "toScore"),
                ["proposalOnly"] = true,
                ["directRegisterMutation"] = false,
            },
            ["restrictedEvidence"] = new JsonObject
            {
                ["payloadPublished"] = false,
                ["restrictedEvidenceRefId"] = SecondProductionLikeOperationalRunContracts.GetString(
                    SecondProductionLikeOperationalRunContracts.RequireObject(source, "restrictedEvidencePolicy"),
                    "refId"),
                ["restrictedEvidenceIndexRef"] = RestrictedEvidenceIndexPath,
                ["restrictedEvidenceIndexSha256Hash"] = artifacts.Single(artifact => artifact.RelativePath == RestrictedEvidenceIndexPath).Sha256Hash,
                ["privatePathRef"] = BuildPrivatePathRef(source),
                ["publicManifestIncludesPayload"] = false,
            },
        };
    }

    private static JsonObject BuildPackageIndex(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<SecondProductionLikeOperationalRunArtifact> artifacts)
    {
        var profile = SecondProductionLikeOperationalRunContracts.RequireObject(source, "secondRunProfile");
        var runId = SecondProductionLikeOperationalRunContracts.GetString(profile, "runId");
        return new JsonObject
        {
            ["schemaVersion"] = "second-production-like-run-package.v1",
            ["packageId"] = "FEAT163-SECOND-PRODUCTION-LIKE-RUN-PACKAGE-20260603-001",
            ["featureId"] = SecondProductionLikeOperationalRunContracts.FeatureId,
            ["runId"] = runId,
            ["generatedAt"] = generatedAt,
            ["status"] = "draft",
            ["canonicalizationVersion"] = SecondProductionLikeOperationalRunContracts.CanonicalizationVersion,
            ["sourceId"] = SecondProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["packageRoot"] = $"{SecondProductionLikeOperationalRunPromotionPaths.PackageFamilyFolder}/{runId}",
            ["validationRefs"] = new JsonArray(
                Feat154BaselineCurrentnessSummaryPath,
                DeploymentProofBindingSummaryPath,
                MonitoringAlertingSummaryPath,
                BackupRestoreSummaryPath,
                SupportOperatorHandoffSummaryPath,
                SecuritySupportFreshnessSummaryPath,
                IncidentResponseWalkthroughSummaryPath,
                PostmortemSummaryPath,
                NoSecretScanResultPath),
            ["readinessRefs"] = new JsonArray(ReadinessFragmentPath, ScoreProposalPath),
            ["handoffRefs"] = new JsonArray(DownstreamHandoffPath),
            ["restrictedRefs"] = new JsonArray(RestrictedEvidenceIndexPath),
            ["artifactCount"] = artifacts.Count + 2,
            ["manifestRef"] = ManifestPath,
            ["artifactHashes"] = new JsonArray(artifacts
                .Where(artifact => artifact.RelativePath != PackageIndexPath)
                .Select(artifact => new JsonObject
                {
                    ["path"] = artifact.RelativePath,
                    ["sha256Hash"] = artifact.Sha256Hash,
                })
                .ToArray<JsonNode?>()),
            ["restrictedEvidence"] = new JsonObject
            {
                ["payloadPublished"] = false,
                ["restrictedEvidenceIndexRef"] = RestrictedEvidenceIndexPath,
                ["privatePathRef"] = BuildPrivatePathRef(source),
            },
            ["nonClaims"] = new JsonArray("direct_readiness_register_mutation", "production_rollout_approval", "public_certification", "restricted_payload_publication"),
        };
    }

    private static JsonObject BuildReadinessFragment(JsonObject source, string generatedAt)
    {
        var baseline = SecondProductionLikeOperationalRunContracts.RequireObject(source, "readinessBaseline");
        var dimension = SecondProductionLikeOperationalRunContracts.RequireObject(baseline, "dimension");
        var profile = SecondProductionLikeOperationalRunContracts.RequireObject(source, "secondRunProfile");
        return new JsonObject
        {
            ["schemaVersion"] = "second-production-like-run-readiness-fragment.v1",
            ["fragmentId"] = "RDY-FRAG-INTERNAL-AUDIT-95-FEAT-163-001",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = SecondProductionLikeOperationalRunContracts.FeatureId,
            ["sourceId"] = SecondProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["runId"] = SecondProductionLikeOperationalRunContracts.GetString(profile, "runId"),
            ["dimensionId"] = SecondProductionLikeOperationalRunContracts.GetString(dimension, "dimensionId"),
            ["targetBlockerId"] = SecondProductionLikeOperationalRunContracts.GetString(dimension, "blockerId"),
            ["status"] = "candidate",
            ["claimEffect"] = "Supports proposal-only internal audit 95 movement when promotion owner accepts FEAT-163 package evidence.",
            ["scoreEffect"] = new JsonObject
            {
                ["proposedScoreFrom"] = 8,
                ["proposedScoreTo"] = 10,
                ["generatedDiff"] = "RDY-DIM-007 8 -> 10",
                ["directRegisterMutation"] = false,
                ["requiresLaterInternalAudit95PromotionPass"] = true,
            },
            ["packageRefs"] = new JsonArray(
                "second-production-like-run-package.json",
                "second-production-like-run-manifest.json",
                Feat154BaselineCurrentnessSummaryPath,
                DeploymentProofBindingSummaryPath,
                MonitoringAlertingSummaryPath,
                BackupRestoreSummaryPath,
                SupportOperatorHandoffSummaryPath,
                SecuritySupportFreshnessSummaryPath,
                IncidentResponseWalkthroughSummaryPath,
                PostmortemSummaryPath,
                NoSecretScanResultPath,
                RestrictedEvidenceIndexPath),
            ["evidenceHashes"] = new JsonObject
            {
                ["sourceSha256Hash"] = SecondProductionLikeOperationalRunContracts.Sha256Hex(SecondProductionLikeOperationalRunContracts.CanonicalJson(source)),
                ["feat154ManifestSha256Hash"] = SecondProductionLikeOperationalRunContracts.AcceptedFeat154ManifestHash,
                ["readinessBaselineManifestSha256Hash"] = SecondProductionLikeOperationalRunContracts.GetString(baseline, "registerManifestSha256Hash"),
            },
            ["nonClaims"] = NonClaims(),
        };
    }

    private static JsonObject BuildFeat154BaselineCurrentnessSummary(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "second-production-like-run-feat154-baseline-currentness-summary.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["resultCode"] = "FEAT163_BASELINE_CURRENTNESS_ACCEPTED",
            ["baselineUse"] = "baseline_currentness_only",
            ["firstRunBaseline"] = SecondProductionLikeOperationalRunContracts.Clone(source["firstRunBaseline"]),
            ["secondRunId"] = SecondProductionLikeOperationalRunContracts.GetString(
                SecondProductionLikeOperationalRunContracts.RequireObject(source, "secondRunProfile"),
                "runId"),
            ["claimImpact"] = "FEAT-154 remains accepted first-run evidence and is not reused as the FEAT-163 second run.",
        };

    private static JsonObject BuildDeploymentProofBindingSummary(JsonObject source, string generatedAt)
    {
        var upstream = SecondProductionLikeOperationalRunContracts.RequireObject(source, "upstreamRefs");
        return new JsonObject
        {
            ["schemaVersion"] = "second-production-like-run-deployment-proof-binding-summary.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["resultCode"] = "FEAT163_RUNTIME_PROOF_ACCEPTED",
            ["runtimeBindingClassification"] = "no_new_runtime_binding_required_by_default",
            ["consumedRefs"] = new JsonArray(new[] { "feat143", "feat144", "feat161", "feat162" }
                .Select(key => new JsonObject
                {
                    ["key"] = key,
                    ["ref"] = SecondProductionLikeOperationalRunContracts.Clone(upstream[key]),
                })
                .ToArray<JsonNode?>()),
            ["claimImpact"] = "Current upstream refs are consumed as public-safe proof context for the second run.",
        };
    }

    private static JsonObject BuildOperationalGateSummary(
        JsonObject source,
        string generatedAt,
        string gateKey,
        string schemaVersion)
    {
        var evidence = SecondProductionLikeOperationalRunContracts.RequireObject(source, "operationalEvidence");
        var gate = SecondProductionLikeOperationalRunContracts.RequireObject(evidence, gateKey);
        return new JsonObject
        {
            ["schemaVersion"] = schemaVersion,
            ["generatedAt"] = generatedAt,
            ["gate"] = gateKey,
            ["status"] = SecondProductionLikeOperationalRunContracts.GetString(gate, "status"),
            ["resultCode"] = SecondProductionLikeOperationalRunContracts.GetString(gate, "resultCode"),
            ["publicSummaryRef"] = SecondProductionLikeOperationalRunContracts.GetString(gate, "publicSummaryRef"),
            ["signoffRoles"] = SecondProductionLikeOperationalRunContracts.Clone(gate["signoffRoles"]),
            ["restrictedEvidenceRefs"] = SecondProductionLikeOperationalRunContracts.Clone(gate["restrictedEvidenceRefs"]),
            ["details"] = SecondProductionLikeOperationalRunContracts.Clone(gate),
            ["payloadPublished"] = false,
        };
    }

    private static JsonObject BuildScoreProposal(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "second-production-like-run-score-proposal.v1",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = SecondProductionLikeOperationalRunContracts.FeatureId,
            ["promotionOwner"] = "later_internal_audit_95_promotion_pass",
            ["dimensionId"] = SecondProductionLikeOperationalRunContracts.TargetDimensionId,
            ["fromScore"] = 8,
            ["toScore"] = 10,
            ["proposedScoreFrom"] = 8,
            ["proposedScoreTo"] = 10,
            ["generatedDiff"] = "RDY-DIM-007 8 -> 10",
            ["proposalOnly"] = true,
            ["directRegisterMutation"] = false,
            ["blockedUnlessAllGatesPass"] = true,
            ["targetBlockerId"] = SecondProductionLikeOperationalRunContracts.TargetBlockerId,
            ["score10Boundary"] = "Hush-owned internal-audit evidence only; no external audit acceptance is claimed.",
            ["staleScoreMovementRejected"] = "RDY-DIM-007 6 -> 8 belongs to accepted FEAT-154 first-run evidence.",
            ["nonClaims"] = NonClaims(),
        };

    private static JsonObject BuildDownstreamHandoff(JsonObject source, string generatedAt)
    {
        var profile = SecondProductionLikeOperationalRunContracts.RequireObject(source, "secondRunProfile");
        var runId = SecondProductionLikeOperationalRunContracts.GetString(profile, "runId");
        return new JsonObject
        {
            ["schemaVersion"] = "second-production-like-run-downstream-handoff.v1",
            ["handoffId"] = "FEAT163-DOWNSTREAM-HANDOFF-20260603-001",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = SecondProductionLikeOperationalRunContracts.FeatureId,
            ["sourceId"] = SecondProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["runId"] = runId,
            ["publicPackageRoot"] = $"{SecondProductionLikeOperationalRunPromotionPaths.PackageFamilyFolder}/{runId}/",
            ["consumers"] = new JsonArray(
                new JsonObject
                {
                    ["consumerId"] = "FEAT-166",
                    ["role"] = "governance_customer_handoff_audit_pack_input",
                    ["refs"] = new JsonArray(ReadinessFragmentPath, ScoreProposalPath, ManifestPath),
                },
                new JsonObject
                {
                    ["consumerId"] = "internal_audit_95_promotion_owner",
                    ["role"] = "proposal_review_and_register_application_owner",
                    ["refs"] = new JsonArray(ReadinessFragmentPath, ScoreProposalPath, ManifestPath),
                }),
            ["scoreProposal"] = new JsonObject
            {
                ["dimensionId"] = SecondProductionLikeOperationalRunContracts.TargetDimensionId,
                ["proposedScoreFrom"] = 8,
                ["proposedScoreTo"] = 10,
                ["directRegisterMutation"] = false,
                ["promotionOwnerAppliesRegisterChange"] = true,
            },
            ["refreshCadence"] = "Refresh before any later release, material operational profile change, or stale FEAT-134/143/144/161/162 upstream ref.",
            ["residualRisks"] = new JsonArray(
                "proposal_only_until_promotion_owner_applies_register_change",
                "restricted_payloads_require_private_reviewer_access",
                "public_package_does_not_establish_external_audit_acceptance"),
            ["nonClaims"] = NonClaims(),
        };
    }

    private static JsonObject BuildNoSecretScanResult(string generatedAt, int scannedArtifactCount, IReadOnlyList<string> findings) =>
        new()
        {
            ["schemaVersion"] = "second-production-like-run-no-secret-scan-result.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = findings.Count == 0 ? "accepted" : "blocked",
            ["unexpectedFindingCount"] = findings.Count,
            ["scannedArtifactCount"] = scannedArtifactCount,
            ["scannerFamilies"] = new JsonArray("credential_marker", "provider_identifier_marker", "restricted_payload_marker", "voter_privacy_marker", "local_path_marker"),
            ["findings"] = new JsonArray(findings.Select(item => JsonValue.Create(item)).ToArray<JsonNode?>()),
        };

    private static string BuildRestrictedEvidenceIndexNote(JsonObject source)
    {
        var policy = SecondProductionLikeOperationalRunContracts.RequireObject(source, "restrictedEvidencePolicy");
        var profile = SecondProductionLikeOperationalRunContracts.RequireObject(source, "secondRunProfile");
        var runId = SecondProductionLikeOperationalRunContracts.GetString(profile, "runId");
        var refId = SecondProductionLikeOperationalRunContracts.GetString(policy, "refId");
        var privatePathRef = BuildPrivatePathRef(source);
        return $"""
        # Restricted Evidence Index Note

        Restricted FEAT-163 operational evidence payloads are not public.

        Ref id: `{refId}`
        Run id: `{runId}`
        Private path ref: `{privatePathRef}`
        Payload published: `false`

        Public outputs may contain only ids, visibility markers, hashes, result codes, signoff roles,
        and public-safe summaries. Raw logs, monitoring internals, support case details, incident
        notes, provider/account ids, vulnerability details, custody payloads, voter material,
        credentials, and reviewer-only payloads must stay outside this repository.
        """ + Environment.NewLine;
    }

    private static string BuildReadme(JsonObject source)
    {
        var profile = SecondProductionLikeOperationalRunContracts.RequireObject(source, "secondRunProfile");
        return $"""
        # FEAT-163 Second Production-Like Operational Run Package

        Run id: `{SecondProductionLikeOperationalRunContracts.GetString(profile, "runId")}`
        Source id: `{SecondProductionLikeOperationalRunContracts.GetString(source, "sourceId")}`

        This draft package records public-safe baseline and runtime proof validation for the second
        production-like operational run. It supports proposal-only `RDY-DIM-007 8 -> 10` evidence
        after all later operational evidence gates pass.

        This package does not mutate the readiness register and does not publish restricted
        operational evidence payloads.
        """ + Environment.NewLine;
    }

    private static JsonArray NonClaims() =>
        new(
            "direct_readiness_register_mutation",
            "production_rollout_approval",
            "public_state_certification",
            "legal_sufficiency",
            "external_audit_acceptance",
            "restricted_payload_publication");

    private static string BuildPrivatePathRef(JsonObject source)
    {
        var profile = SecondProductionLikeOperationalRunContracts.RequireObject(source, "secondRunProfile");
        return "PrivateServer_ElectronicVoting/Operational-Evidence-Second-Run/" +
            SecondProductionLikeOperationalRunContracts.GetString(profile, "runId") + "/";
    }
}
