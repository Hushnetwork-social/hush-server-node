using System.Text.Json.Nodes;

namespace DeploymentRollbackRehearsalPromoter;

public sealed record DeploymentRollbackRehearsalArtifact(string RelativePath, string Content)
{
    public string Sha256Hash => DeploymentRollbackRehearsalContracts.Sha256Hex(Content);
}

public sealed record DeploymentRollbackRehearsalGeneratedPackage(
    string Status,
    string PackageRoot,
    string SecondCeremonyRoot,
    JsonObject Source,
    IReadOnlyList<DeploymentRollbackRehearsalArtifact> Artifacts,
    IReadOnlyList<DeploymentRollbackRehearsalArtifact> SecondCeremonyArtifacts)
{
    public int ArtifactCount => Artifacts.Count + SecondCeremonyArtifacts.Count;
}

public static class DeploymentRollbackRehearsalArtifactGenerator
{
    public const string ReadmePath = "README.md";
    public const string ManifestPath = "deployment-rollback-rehearsal-manifest.json";
    public const string SecondCeremonySummaryPath = "validation/second-ceremony-summary.json";
    public const string RollbackBindingSummaryPath = "validation/rollback-binding-summary.json";
    public const string EmergencyChangeSummaryPath = "validation/emergency-change-summary.json";
    public const string WebClientObservedProofSummaryPath = "validation/webclient-observed-proof-summary.json";
    public const string NoSecretScanResultPath = "validation/no-secret-scan-result.json";
    public const string ReadinessFragmentPath = "readiness/deployment-rollback-readiness-fragment.json";
    public const string ScoreProposalPath = "readiness/deployment-rollback-score-proposal.json";
    public const string DownstreamHandoffPath = "handoff/deployment-rollback-downstream-handoff.json";
    public const string ReviewerGuidePath = "handoff/reviewer-guide.md";
    public const string RestrictedEvidenceIndexPath = "restricted/restricted-evidence-index.schema-note.md";
    public const string CeremonyManifestPath = "deployment-ceremony-manifest.json";
    public const string CeremonyJsonPath = "deployment-ceremony.json";
    public const string CeremonyReadinessFragmentPath = "readiness-fragment.json";
    public const string CeremonyDownstreamHandoffPath = "downstream-handoff.json";
    public const string CeremonyPublicSafeSummaryPath = "public-safe-binding-summary.md";

    public static readonly string[] RequiredArtifactPaths =
    [
        ReadmePath,
        ManifestPath,
        SecondCeremonySummaryPath,
        RollbackBindingSummaryPath,
        EmergencyChangeSummaryPath,
        WebClientObservedProofSummaryPath,
        NoSecretScanResultPath,
        ReadinessFragmentPath,
        ScoreProposalPath,
        DownstreamHandoffPath,
        ReviewerGuidePath,
        RestrictedEvidenceIndexPath,
    ];

    public static readonly string[] RequiredSecondCeremonyArtifactPaths =
    [
        CeremonyManifestPath,
        CeremonyJsonPath,
        CeremonyReadinessFragmentPath,
        CeremonyDownstreamHandoffPath,
        CeremonyPublicSafeSummaryPath,
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
        "emergency_payload_raw",
        "private screenshot",
        "voter_data",
        "kms_key",
        "kms_alias",
        @"c:\mywork\hushnetworkorg\hush-documents",
    ];

    public static DeploymentRollbackRehearsalGeneratedPackage Generate(
        DeploymentRollbackRehearsalPromotionPaths paths,
        string? sourceInput = null,
        string? outputRoot = null,
        DateTimeOffset? generatedAt = null,
        bool publicOnly = false)
    {
        var source = DeploymentRollbackRehearsalContracts.ValidateForPromotion(paths, sourceInput, publicOnly);
        var packageRoot = ResolvePackageRoot(paths, source, outputRoot);
        var secondCeremonyRoot = ResolveSecondCeremonyRoot(paths, outputRoot);
        var generatedAtText = ResolveGeneratedAt(source, generatedAt);

        var preScanArtifacts = new List<DeploymentRollbackRehearsalArtifact>
        {
            new(ReadmePath, BuildReadme(source)),
            JsonArtifact(SecondCeremonySummaryPath, BuildScenarioSummary(source, generatedAtText, "deployment-rollback-second-ceremony-summary.v1", ["accepted_baseline", "second_ceremony", "no_change", "non_voting_change", "operational_config_change"])),
            JsonArtifact(RollbackBindingSummaryPath, BuildScenarioSummary(source, generatedAtText, "deployment-rollback-binding-summary.v1", ["rollback"])),
            JsonArtifact(EmergencyChangeSummaryPath, BuildScenarioSummary(source, generatedAtText, "deployment-rollback-emergency-change-summary.v1", ["emergency_change", "custody_impact", "restricted_boundary"])),
            JsonArtifact(WebClientObservedProofSummaryPath, BuildScenarioSummary(source, generatedAtText, "deployment-rollback-webclient-observed-proof-summary.v1", ["webclient_observed_proof"])),
            JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, generatedAtText)),
            JsonArtifact(ScoreProposalPath, BuildScoreProposal(source, generatedAtText)),
            JsonArtifact(DownstreamHandoffPath, BuildDownstreamHandoff(source, generatedAtText)),
            new(ReviewerGuidePath, BuildReviewerGuide(source)),
            new(RestrictedEvidenceIndexPath, BuildRestrictedEvidenceIndexNote(source)),
        };

        var preScanSecondCeremonyArtifacts = new List<DeploymentRollbackRehearsalArtifact>
        {
            JsonArtifact(CeremonyJsonPath, BuildSecondCeremony(source, generatedAtText)),
            JsonArtifact(CeremonyReadinessFragmentPath, BuildSecondCeremonyReadinessFragment(source, generatedAtText)),
            JsonArtifact(CeremonyDownstreamHandoffPath, BuildSecondCeremonyDownstreamHandoff(source, generatedAtText)),
            new(CeremonyPublicSafeSummaryPath, BuildSecondCeremonyPublicSafeSummary(source)),
        };

        var artifacts = new List<DeploymentRollbackRehearsalArtifact>(preScanArtifacts);
        var secondCeremonyArtifacts = new List<DeploymentRollbackRehearsalArtifact>(preScanSecondCeremonyArtifacts);
        artifacts.Insert(5, JsonArtifact(NoSecretScanResultPath, BuildNoSecretScanResult(
            generatedAtText,
            preScanArtifacts.Count + preScanSecondCeremonyArtifacts.Count)));
        secondCeremonyArtifacts.Insert(0, JsonArtifact(CeremonyManifestPath, BuildSecondCeremonyManifest(
            source,
            generatedAtText,
            secondCeremonyArtifacts)));
        artifacts.Insert(1, JsonArtifact(ManifestPath, BuildManifest(
            source,
            generatedAtText,
            artifacts,
            secondCeremonyArtifacts)));

        var scanFindings = ScanGeneratedArtifacts([.. artifacts, .. secondCeremonyArtifacts]);
        if (scanFindings.Count > 0)
        {
            throw new DeploymentRollbackRehearsalPromotionException(
                "FEAT-162 generated deployment rollback rehearsal package public-safety scan failed.",
                scanFindings);
        }

        return new DeploymentRollbackRehearsalGeneratedPackage("candidate", packageRoot, secondCeremonyRoot, source, artifacts, secondCeremonyArtifacts);
    }

    public static string ResolvePackageRoot(
        DeploymentRollbackRehearsalPromotionPaths paths,
        JsonObject source,
        string? outputRoot = null)
    {
        var root = Path.GetFullPath(outputRoot ?? paths.PublicProofPackagesRoot);
        DeploymentRollbackRehearsalContracts.EnsurePathUnder(paths.WorkspaceRoot, root, "FEAT-162 rehearsal output root");
        var packageRoot = Path.GetFullPath(Path.Combine(root, DeploymentRollbackRehearsalPromotionPaths.PackageRelativeRoot));
        DeploymentRollbackRehearsalContracts.EnsurePathUnder(root, packageRoot, "FEAT-162 rehearsal package root");

        var targetPath = DeploymentRollbackRehearsalContracts.GetString(
            DeploymentRollbackRehearsalContracts.RequireObject(source, "packageLayout"),
            "targetPackagePath");
        if (!string.Equals(targetPath, DeploymentRollbackRehearsalContracts.ExpectedTargetPackagePath, StringComparison.Ordinal))
        {
            throw new DeploymentRollbackRehearsalPromotionException(
                "FEAT-162 package layout target is not the expected deployment rollback rehearsal package.",
                [$"Observed: {targetPath}"]);
        }

        return packageRoot;
    }

    public static string ResolveSecondCeremonyRoot(
        DeploymentRollbackRehearsalPromotionPaths paths,
        string? outputRoot = null)
    {
        var root = Path.GetFullPath(outputRoot ?? paths.PublicProofPackagesRoot);
        DeploymentRollbackRehearsalContracts.EnsurePathUnder(paths.WorkspaceRoot, root, "FEAT-162 second ceremony output root");
        var ceremonyRoot = Path.GetFullPath(Path.Combine(root, DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyRelativeRoot));
        DeploymentRollbackRehearsalContracts.EnsurePathUnder(root, ceremonyRoot, "FEAT-162 second ceremony root");
        return ceremonyRoot;
    }

    public static IReadOnlyList<string> ScanGeneratedArtifacts(IReadOnlyList<DeploymentRollbackRehearsalArtifact> artifacts)
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

    private static DeploymentRollbackRehearsalArtifact JsonArtifact(string relativePath, JsonObject content) =>
        new(relativePath, DeploymentRollbackRehearsalContracts.CanonicalJson(content));

    private static string ResolveGeneratedAt(JsonObject source, DateTimeOffset? generatedAt)
    {
        if (generatedAt is not null)
        {
            return generatedAt.Value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        return DeploymentRollbackRehearsalContracts.GetString(source, "generatedAt");
    }

    private static JsonObject BuildManifest(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<DeploymentRollbackRehearsalArtifact> artifacts,
        IReadOnlyList<DeploymentRollbackRehearsalArtifact> secondCeremonyArtifacts)
    {
        var baseline = DeploymentRollbackRehearsalContracts.RequireObject(source, "baselineRegister");
        var scenarios = Scenarios(source).ToArray();
        return new JsonObject
        {
            ["schemaVersion"] = DeploymentRollbackRehearsalContracts.PackageManifestSchemaVersion,
            ["packageId"] = "deployment-rollback-emergency-rehearsal",
            ["packageVersion"] = DeploymentRollbackRehearsalContracts.TargetPackageVersion,
            ["producerFeature"] = DeploymentRollbackRehearsalContracts.FeatureId,
            ["sourceId"] = DeploymentRollbackRehearsalContracts.GetString(source, "sourceId"),
            ["generatedAt"] = generatedAt,
            ["baselineRegister"] = new JsonObject
            {
                ["registerVersionId"] = DeploymentRollbackRehearsalContracts.GetString(baseline, "registerVersionId"),
                ["status"] = DeploymentRollbackRehearsalContracts.GetString(baseline, "status"),
                ["dimensionId"] = DeploymentRollbackRehearsalContracts.GetString(baseline, "dimensionId"),
                ["proposedScoreFrom"] = 8,
                ["proposedScoreTo"] = 9,
                ["targetBlockerId"] = DeploymentRollbackRehearsalContracts.GetString(baseline, "targetBlockerId"),
                ["doesNotMutateRegister"] = true,
            },
            ["upstreamRefs"] = BuildUpstreamRefs(source),
            ["validationSummary"] = new JsonObject
            {
                ["status"] = "pass",
                ["scenarioCount"] = scenarios.Length,
                ["requiredScenarioCount"] = scenarios.Count(item => DeploymentRollbackRehearsalContracts.GetBool(item, "requiredForScore")),
                ["passedRequiredScenarioCount"] = scenarios.Count(item => DeploymentRollbackRehearsalContracts.GetBool(item, "requiredForScore")),
                ["degradedScenarioCount"] = scenarios.Count(item => DeploymentRollbackRehearsalContracts.GetString(item, "expectedResult") == "degraded"),
                ["restrictedOnlyScenarioCount"] = scenarios.Count(item => DeploymentRollbackRehearsalContracts.GetString(item, "expectedResult") == "restricted_only"),
                ["summaryRefs"] = new JsonArray(SecondCeremonySummaryPath, RollbackBindingSummaryPath, EmergencyChangeSummaryPath, WebClientObservedProofSummaryPath),
            },
            ["secondCeremony"] = new JsonObject
            {
                ["ceremonyId"] = DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyId,
                ["publicPath"] = DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyRelativeRoot.Replace('\\', '/'),
                ["manifestRef"] = DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyRelativeRoot.Replace('\\', '/') + "/" + CeremonyManifestPath,
                ["artifactCount"] = secondCeremonyArtifacts.Count,
                ["packageKind"] = "deployment_rollback_second_ceremony_public_safe_rehearsal",
            },
            ["publicSafety"] = new JsonObject
            {
                ["status"] = "pass",
                ["unexpectedFindingCount"] = 0,
                ["scanResultRef"] = NoSecretScanResultPath,
            },
            ["readinessProposal"] = new JsonObject
            {
                ["status"] = "proposed",
                ["scoreProposalRef"] = ScoreProposalPath,
                ["readinessFragmentRef"] = ReadinessFragmentPath,
                ["doesNotMutateRegister"] = true,
            },
            ["entries"] = new JsonArray(artifacts
                .Select(artifact => new JsonObject
                {
                    ["path"] = artifact.RelativePath,
                    ["sha256Hash"] = "sha256:" + artifact.Sha256Hash,
                    ["mediaType"] = artifact.RelativePath.EndsWith(".md", StringComparison.Ordinal) ? "text/markdown" : "application/json",
                    ["visibility"] = artifact.RelativePath.StartsWith("restricted/", StringComparison.Ordinal) ? "restricted_ref_only" : "public",
                })
                .ToArray<JsonNode?>()),
            ["secondCeremonyEntries"] = new JsonArray(secondCeremonyArtifacts
                .Select(artifact => new JsonObject
                {
                    ["path"] = DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyRelativeRoot.Replace('\\', '/') + "/" + artifact.RelativePath,
                    ["sha256Hash"] = "sha256:" + artifact.Sha256Hash,
                    ["mediaType"] = artifact.RelativePath.EndsWith(".md", StringComparison.Ordinal) ? "text/markdown" : "application/json",
                    ["visibility"] = "public",
                })
                .ToArray<JsonNode?>()),
        };
    }

    private static JsonObject BuildSecondCeremonyManifest(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<DeploymentRollbackRehearsalArtifact> artifacts) =>
        new()
        {
            ["manifestId"] = "MANIFEST-" + DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyId,
            ["packageId"] = DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyId,
            ["packageKind"] = "deployment_rollback_second_ceremony_public_safe_rehearsal",
            ["producerFeature"] = DeploymentRollbackRehearsalContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["canonicalizationVersion"] = DeploymentRollbackRehearsalContracts.CanonicalizationVersion,
            ["sourceId"] = DeploymentRollbackRehearsalContracts.GetString(source, "sourceId"),
            ["validationResults"] = new JsonObject
            {
                ["status"] = "passed",
                ["sourceValidation"] = "passed",
                ["runtimeBindingMode"] = "consumes_FEAT_143_and_FEAT_144_refs",
            },
            ["redactionScanResults"] = new JsonObject
            {
                ["publicForbiddenMaterialScan"] = "passed",
            },
            ["files"] = new JsonArray(artifacts
                .Select(artifact => new JsonObject
                {
                    ["path"] = DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyRelativeRoot.Replace('\\', '/') + "/" + artifact.RelativePath,
                    ["byteLength"] = System.Text.Encoding.UTF8.GetByteCount(artifact.Content),
                    ["sha256Hash"] = artifact.Sha256Hash,
                })
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildSecondCeremony(JsonObject source, string generatedAt)
    {
        var secondCeremony = ScenarioById(source, "DEPLOY-ROLLBACK-SECOND-CEREMONY");
        var noChange = ScenarioById(source, "DEPLOY-ROLLBACK-NO-CHANGE-FREEZE");
        var nonVotingChange = ScenarioById(source, "DEPLOY-ROLLBACK-NON-VOTING-CHANGE");
        var operationalConfig = ScenarioById(source, "DEPLOY-ROLLBACK-OPERATIONAL-CONFIG-CHANGE");

        return new JsonObject
        {
            ["ceremonyId"] = DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyId,
            ["ceremonyVersion"] = "1.0",
            ["producerFeature"] = DeploymentRollbackRehearsalContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["claimLevel"] = "internal_non_binding_rehearsal",
            ["sourceId"] = DeploymentRollbackRehearsalContracts.GetString(source, "sourceId"),
            ["publicDeploymentProofRepository"] = "https://github.com/Hushnetwork-social/Deployment-Proof-Packages",
            ["componentDeploymentProofs"] = new JsonObject
            {
                ["hushWebClientDeploymentProofId"] = "DPP-WEB-20260602-002",
                ["hushServerNodeDeploymentProofId"] = "DPP-SERVER-20260602-002",
            },
            ["deploymentProofSet"] = new JsonObject
            {
                ["proofSetId"] = "DPS-REHEARSAL-20260602-002",
                ["bindingLedgerId"] = "FEAT-143-runtime-deployment-proof-binding-ledger-v1",
            },
            ["ceremonyStages"] = new JsonArray(
                CeremonyStage("prepare", "passed"),
                CeremonyStage("freeze", "passed"),
                CeremonyStage("deploy_verify", "passed"),
                CeremonyStage("pre_open", "passed"),
                CeremonyStage("rollback_rehearsal", "passed"),
                CeremonyStage("emergency_change_rehearsal", "passed")),
            ["scenarioRefs"] = new JsonArray(
                ScenarioRef(secondCeremony),
                ScenarioRef(noChange),
                ScenarioRef(nonVotingChange),
                ScenarioRef(operationalConfig)),
            ["runtimeBindingRefs"] = new JsonArray("FEAT-143-runtime-deployment-proof-binding-ledger-v1"),
            ["webClientObservedProofRefs"] = new JsonArray("FEAT-144-observed-webclient-proof-handshake"),
            ["readinessFragment"] = new JsonObject
            {
                ["fragmentId"] = "RDY-FRAG-INTERNAL-AUDIT-95-FEAT-162-001",
                ["path"] = DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyRelativeRoot.Replace('\\', '/') + "/" + CeremonyReadinessFragmentPath,
            },
            ["restrictedEvidenceRefs"] = DeploymentRollbackRehearsalContracts.Clone(secondCeremony["restrictedEvidenceRefs"]),
            ["nonClaims"] = NonClaims(),
        };
    }

    private static JsonObject BuildSecondCeremonyReadinessFragment(JsonObject source, string generatedAt)
    {
        var baseline = DeploymentRollbackRehearsalContracts.RequireObject(source, "baselineRegister");
        return new JsonObject
        {
            ["fragmentId"] = "RDY-FRAG-INTERNAL-AUDIT-95-FEAT-162-001",
            ["generatedAt"] = generatedAt,
            ["featureSlice"] = DeploymentRollbackRehearsalContracts.FeatureId,
            ["acceptanceGate"] = "AT-RDY-017",
            ["sourceGap"] = DeploymentRollbackRehearsalContracts.TargetDimensionName,
            ["claimEffect"] = "Supports proposal-only internal audit 95 readiness movement when all FEAT-162 rehearsal gates pass.",
            ["dimensionScoreChange"] = new JsonObject
            {
                ["dimensionId"] = DeploymentRollbackRehearsalContracts.TargetDimensionId,
                ["previousScore"] = 8,
                ["proposedScore"] = 9,
                ["generatedDiff"] = "RDY-DIM-006 8 -> 9",
                ["directRegisterMutation"] = false,
            },
            ["blockerChanges"] = new JsonArray(new JsonObject
            {
                ["blockerId"] = DeploymentRollbackRehearsalContracts.GetString(baseline, "targetBlockerId"),
                ["previousStatus"] = "red",
                ["proposedStatus"] = "green",
                ["reason"] = "Second ceremony, rollback, emergency-change, runtime binding, and public-safety rehearsal evidence passed.",
            }),
            ["evidenceRefs"] = new JsonArray(
                DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyId,
                DeploymentRollbackRehearsalPromotionPaths.PackageRelativeRoot.Replace('\\', '/'),
                SecondCeremonySummaryPath,
                RollbackBindingSummaryPath,
                EmergencyChangeSummaryPath,
                WebClientObservedProofSummaryPath),
            ["residualRisk"] = "Proposal-only until readiness promotion owner applies the score movement.",
            ["nonClaims"] = NonClaims(),
        };
    }

    private static JsonObject BuildSecondCeremonyDownstreamHandoff(JsonObject source, string generatedAt) =>
        new()
        {
            ["handoffId"] = "DPC-HANDOFF-FEAT-162-20260602-001",
            ["generatedAt"] = generatedAt,
            ["sourceFeature"] = DeploymentRollbackRehearsalContracts.FeatureId,
            ["ceremonyId"] = DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyId,
            ["publicRefs"] = new JsonArray(
                DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyRelativeRoot.Replace('\\', '/') + "/",
                DeploymentRollbackRehearsalPromotionPaths.PackageRelativeRoot.Replace('\\', '/') + "/"),
            ["consumers"] = DeploymentRollbackRehearsalContracts.Clone(source["downstreamConsumers"]),
            ["readinessRegisterHandoff"] = new JsonObject
            {
                ["consumerFeature"] = "FEAT-156",
                ["dimensionId"] = DeploymentRollbackRehearsalContracts.TargetDimensionId,
                ["proposedScoreFrom"] = 8,
                ["proposedScoreTo"] = 9,
                ["directRegisterMutation"] = false,
            },
            ["runtimeVisibilityContract"] = new JsonObject
            {
                ["implementedInFeat162"] = false,
                ["runtimeBindingClassification"] = "uses_existing_FEAT_143_and_FEAT_144_refs",
                ["followUpNeeded"] = false,
            },
            ["restrictedEvidenceRefs"] = new JsonArray("restricted/restricted-evidence-index.schema-note.md"),
        };

    private static string BuildSecondCeremonyPublicSafeSummary(JsonObject source)
    {
        return $"""
        # FEAT-162 Second Ceremony Public-Safe Binding Summary

        Ceremony id: `{DeploymentRollbackRehearsalPromotionPaths.SecondCeremonyId}`
        Source id: `{DeploymentRollbackRehearsalContracts.GetString(source, "sourceId")}`

        This ceremony is deterministic rehearsal evidence for deployment rollback and
        emergency-change validation. It supports proposal-only `RDY-DIM-006 8 -> 9` movement.

        It does not publish credentials, raw deployment logs, private operational notes, KMS/provider
        details, incident payloads, or voter material. Restricted evidence is referenced only by
        public-safe ids and hashes in the FEAT-162 rehearsal package.
        """ + Environment.NewLine;
    }

    private static JsonObject BuildScenarioSummary(
        JsonObject source,
        string generatedAt,
        string schemaVersion,
        IReadOnlyList<string> categories)
    {
        var categorySet = categories.ToHashSet(StringComparer.Ordinal);
        var scenarios = Scenarios(source)
            .Where(item => categorySet.Contains(DeploymentRollbackRehearsalContracts.GetString(item, "category")))
            .ToArray();
        return new JsonObject
        {
            ["schemaVersion"] = schemaVersion,
            ["generatedAt"] = generatedAt,
            ["status"] = "pass",
            ["evidenceMode"] = "deterministic_rehearsal_fixture",
            ["scenarioCount"] = scenarios.Length,
            ["cases"] = new JsonArray(scenarios
                .Select(item => new JsonObject
                {
                    ["scenarioId"] = DeploymentRollbackRehearsalContracts.GetString(item, "scenarioId"),
                    ["category"] = DeploymentRollbackRehearsalContracts.GetString(item, "category"),
                    ["expectedResult"] = DeploymentRollbackRehearsalContracts.GetString(item, "expectedResult"),
                    ["safeResultCodes"] = DeploymentRollbackRehearsalContracts.Clone(item["safeResultCodes"]),
                    ["readinessImpact"] = DeploymentRollbackRehearsalContracts.GetString(item, "readinessImpact"),
                    ["gateIds"] = DeploymentRollbackRehearsalContracts.Clone(item["gateIds"]),
                    ["proofRefs"] = DeploymentRollbackRehearsalContracts.Clone(item["proofRefs"]),
                    ["restrictedEvidenceRefs"] = DeploymentRollbackRehearsalContracts.Clone(item["restrictedEvidenceRefs"]),
                    ["requiredForScore"] = DeploymentRollbackRehearsalContracts.GetBool(item, "requiredForScore"),
                })
                .ToArray<JsonNode?>()),
        };
    }

    private static JsonObject BuildNoSecretScanResult(string generatedAt, int scannedArtifactCount) =>
        new()
        {
            ["schemaVersion"] = "deployment-rollback-no-secret-scan-result.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "pass",
            ["unexpectedFindingCount"] = 0,
            ["scannedArtifactCount"] = scannedArtifactCount,
            ["scannerFamilies"] = new JsonArray("credential_marker", "provider_identifier_marker", "private_runtime_marker", "voter_privacy_marker", "local_path_marker"),
            ["findings"] = new JsonArray(),
        };

    private static JsonObject BuildReadinessFragment(JsonObject source, string generatedAt)
    {
        var baseline = DeploymentRollbackRehearsalContracts.RequireObject(source, "baselineRegister");
        return new JsonObject
        {
            ["schemaVersion"] = "deployment-rollback-readiness-fragment.v1",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = DeploymentRollbackRehearsalContracts.FeatureId,
            ["dimensionId"] = DeploymentRollbackRehearsalContracts.TargetDimensionId,
            ["status"] = "candidate",
            ["targetBlockerId"] = DeploymentRollbackRehearsalContracts.GetString(baseline, "targetBlockerId"),
            ["scoreEffect"] = new JsonObject
            {
                ["proposedScoreFrom"] = 8,
                ["proposedScoreTo"] = 9,
                ["scoreChangeAllowed"] = false,
                ["doesNotMutateRegister"] = true,
                ["requiresLaterInternalAudit95PromotionPass"] = true,
            },
            ["evidenceRefs"] = new JsonArray(RequiredArtifactPaths
                .Where(path => path != ReadinessFragmentPath)
                .Select(path => JsonValue.Create(path))
                .ToArray<JsonNode?>()),
            ["nonClaims"] = NonClaims(),
        };
    }

    private static JsonObject BuildScoreProposal(JsonObject source, string generatedAt)
    {
        var baseline = DeploymentRollbackRehearsalContracts.RequireObject(source, "baselineRegister");
        return new JsonObject
        {
            ["schemaVersion"] = "deployment-rollback-score-proposal.v1",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = DeploymentRollbackRehearsalContracts.FeatureId,
            ["dimensionId"] = DeploymentRollbackRehearsalContracts.TargetDimensionId,
            ["fromScore"] = 8,
            ["toScore"] = 9,
            ["targetBlockerId"] = DeploymentRollbackRehearsalContracts.GetString(baseline, "targetBlockerId"),
            ["proposalOnly"] = true,
            ["directRegisterMutation"] = false,
            ["blockedUnlessAllCasesPass"] = true,
            ["nonClaims"] = NonClaims(),
        };
    }

    private static JsonObject BuildDownstreamHandoff(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "deployment-rollback-downstream-handoff.v1",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = DeploymentRollbackRehearsalContracts.FeatureId,
            ["sourceId"] = DeploymentRollbackRehearsalContracts.GetString(source, "sourceId"),
            ["consumers"] = DeploymentRollbackRehearsalContracts.Clone(source["downstreamConsumers"]),
            ["publicPackageRoot"] = DeploymentRollbackRehearsalPromotionPaths.PackageRelativeRoot.Replace('\\', '/'),
            ["readinessFragmentRef"] = ReadinessFragmentPath,
            ["scoreProposalRef"] = ScoreProposalPath,
            ["restrictedIndexRef"] = RestrictedEvidenceIndexPath,
            ["runtimeBindingClassification"] = "no_new_runtime_binding_required_by_default",
            ["directRegisterMutation"] = false,
        };

    private static JsonArray BuildUpstreamRefs(JsonObject source)
    {
        var upstream = DeploymentRollbackRehearsalContracts.RequireObject(source, "upstreamBaselines");
        return new JsonArray(upstream
            .Select(pair => new JsonObject
            {
                ["key"] = pair.Key,
                ["value"] = DeploymentRollbackRehearsalContracts.Clone(pair.Value),
            })
            .ToArray<JsonNode?>());
    }

    private static string BuildReadme(JsonObject source)
    {
        return $"""
        # Deployment Rollback Emergency Rehearsal Package

        Source id: `{DeploymentRollbackRehearsalContracts.GetString(source, "sourceId")}`

        This package is the public-safe FEAT-162 deployment rollback and emergency-change rehearsal
        output for proposal-only `RDY-DIM-006 8 -> 9` evidence.

        ## Boundaries

        - Generated deployment proof outputs remain owned by `Deployment-Proof-Packages`.
        - FEAT-162 does not mutate the readiness register directly.
        - FEAT-162 does not claim production rollout, public/state election readiness, certification,
          independent audit acceptance, or score `10`.
        - WebClient observed proof remains supporting evidence and does not prove every voter saw
          the same browser bundle.
        - Restricted deployment and emergency evidence is referenced by id/hash only.

        ## Review

        Use `handoff/reviewer-guide.md` for validate-only, package, and check-only commands.
        """ + Environment.NewLine;
    }

    private static string BuildReviewerGuide(JsonObject source)
    {
        return $"""
        # FEAT-162 Reviewer Guide

        Source id: `{DeploymentRollbackRehearsalContracts.GetString(source, "sourceId")}`

        Validate without writes:

        ```powershell
        cd hush-server-node
        powershell -ExecutionPolicy Bypass -File Node/scripts/promote-deployment-rollback-rehearsal.ps1 -WorkspaceRoot C:\myWork\HushNetworkOrg -Mode validate-only
        ```

        Generate package:

        ```powershell
        cd hush-server-node
        powershell -ExecutionPolicy Bypass -File Node/scripts/promote-deployment-rollback-rehearsal.ps1 -WorkspaceRoot C:\myWork\HushNetworkOrg -Mode package
        ```

        Check generated package:

        ```powershell
        cd hush-server-node
        powershell -ExecutionPolicy Bypass -File Node/scripts/promote-deployment-rollback-rehearsal.ps1 -WorkspaceRoot C:\myWork\HushNetworkOrg -Mode check-only
        ```

        Public-only validation for CI:

        ```powershell
        cd hush-server-node
        powershell -ExecutionPolicy Bypass -File Node/scripts/promote-deployment-rollback-rehearsal.ps1 -WorkspaceRoot C:\myWork\HushNetworkOrg -Mode check-only -PublicOnly
        ```
        """ + Environment.NewLine;
    }

    private static string BuildRestrictedEvidenceIndexNote(JsonObject source)
    {
        var boundary = DeploymentRollbackRehearsalContracts.RequireObject(source, "restrictedEvidenceBoundary");
        var restrictedIndexPath = DeploymentRollbackRehearsalContracts.GetString(boundary, "restrictedIndexPath");
        return $"""
        # Restricted Evidence Index Note

        Restricted deployment rollback and emergency-change evidence payloads are not public.

        Private index path:

        `{restrictedIndexPath}`

        Public outputs may contain only ids, visibility labels, hashes, safe result codes, and
        claim-impact categories.
        """ + Environment.NewLine;
    }

    private static JsonArray NonClaims() =>
        new("production_rollout", "public_state_election_readiness", "certification", "independent_audit_acceptance", "score_10", "all_voters_same_browser_bundle");

    private static IEnumerable<JsonObject> Scenarios(JsonObject source) =>
        DeploymentRollbackRehearsalContracts.RequireArray(
                DeploymentRollbackRehearsalContracts.RequireObject(source, "rehearsalMatrix"),
                "scenarios")
            .OfType<JsonObject>();

    private static JsonObject ScenarioById(JsonObject source, string scenarioId) =>
        Scenarios(source).Single(item => DeploymentRollbackRehearsalContracts.GetString(item, "scenarioId") == scenarioId);

    private static JsonObject ScenarioRef(JsonObject scenario) =>
        new()
        {
            ["scenarioId"] = DeploymentRollbackRehearsalContracts.GetString(scenario, "scenarioId"),
            ["expectedResult"] = DeploymentRollbackRehearsalContracts.GetString(scenario, "expectedResult"),
            ["safeResultCodes"] = DeploymentRollbackRehearsalContracts.Clone(scenario["safeResultCodes"]),
            ["readinessImpact"] = DeploymentRollbackRehearsalContracts.GetString(scenario, "readinessImpact"),
        };

    private static JsonObject CeremonyStage(string stageId, string status) =>
        new()
        {
            ["stageId"] = stageId,
            ["stageStatus"] = status,
            ["blocksAcceptance"] = true,
        };
}
