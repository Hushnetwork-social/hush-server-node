using System.Text;
using System.Text.Json.Nodes;

namespace RetentionLogPrivacyRecurringScanPromoter;

public sealed record RetentionLogPrivacyRecurringScanArtifact(string RelativePath, string Content)
{
    public string Sha256Hash => RetentionLogPrivacyRecurringScanContracts.Sha256Hex(Content);
    public int SizeBytes => Encoding.UTF8.GetByteCount(Content);
    public string MediaType => RelativePath.EndsWith(".md", StringComparison.Ordinal) ? "text/markdown" : "application/json";
    public string Visibility => RelativePath.StartsWith("restricted/", StringComparison.Ordinal) ? "restricted-ref" : "public";
}

public sealed record RetentionLogPrivacyRecurringScanGeneratedPackage(
    string Status,
    string PackageRoot,
    JsonObject Source,
    IReadOnlyList<RetentionLogPrivacyRecurringScanArtifact> Artifacts)
{
    public int ArtifactCount => Artifacts.Count;
}

public static class RetentionLogPrivacyRecurringScanArtifactGenerator
{
    public const string ReadmePath = "README.md";
    public const string ManifestPath = "retention-log-privacy-recurring-scan-manifest.json";
    public const string PackageIndexPath = "retention-log-privacy-recurring-scan-package.json";
    public const string Feat137ProofCurrentnessSummaryPath = "validation/feat137-proof-currentness-summary.json";
    public const string RuntimeProofFamilyStatusSummaryPath = "validation/runtime-proof-family-status-summary.json";
    public const string LogDiagnosticScanSummaryPath = "validation/log-diagnostic-scan-summary.json";
    public const string TraceObservabilityDriftSummaryPath = "validation/trace-observability-drift-summary.json";
    public const string SupportExportScanSummaryPath = "validation/support-export-scan-summary.json";
    public const string PackageReportScanSummaryPath = "validation/package-report-scan-summary.json";
    public const string PublicOnlyValidationSummaryPath = "validation/public-only-validation-summary.json";
    public const string NoSecretScanResultPath = "validation/no-secret-scan-result.json";
    public const string ReadinessFragmentPath = "readiness/retention-log-privacy-recurring-scan-readiness-fragment.json";
    public const string ScoreProposalPath = "readiness/retention-log-privacy-recurring-scan-score-proposal.json";
    public const string DownstreamHandoffPath = "handoff/retention-log-privacy-recurring-scan-downstream-handoff.json";
    public const string RestrictedEvidenceIndexPath = "restricted/restricted-evidence-index.schema-note.md";

    public static readonly string[] RequiredArtifactPaths =
    [
        ReadmePath,
        ManifestPath,
        PackageIndexPath,
        Feat137ProofCurrentnessSummaryPath,
        RuntimeProofFamilyStatusSummaryPath,
        LogDiagnosticScanSummaryPath,
        TraceObservabilityDriftSummaryPath,
        SupportExportScanSummaryPath,
        PackageReportScanSummaryPath,
        PublicOnlyValidationSummaryPath,
        NoSecretScanResultPath,
        ReadinessFragmentPath,
        ScoreProposalPath,
        DownstreamHandoffPath,
        RestrictedEvidenceIndexPath,
    ];

    private static readonly string[] GeneratedForbiddenNeedles =
    [
        "rawLogPayload",
        "rawTraceSample",
        "supportCaseContent",
        "supportPayload",
        "voterIdentifier",
        "ballotReference",
        "receiptCapability",
        "privateFinding",
        "privateScannerFinding",
        "restrictedReviewerPayload",
        "privateDependencyRequired",
        "PrivateServer_ElectronicVoting",
        @"C:\",
        "/Users/",
        "/home/",
        "\"directRegisterMutation\": true",
        "external audit acceptance is accepted",
        "legal sufficiency is accepted",
        "public/state election readiness is accepted",
        "production rollout approval is accepted",
    ];

    public static RetentionLogPrivacyRecurringScanGeneratedPackage Generate(
        RetentionLogPrivacyRecurringScanPromotionPaths paths,
        string? sourceInput = null,
        string? outputRoot = null,
        DateTimeOffset? generatedAt = null,
        bool publicOnly = false)
    {
        var source = RetentionLogPrivacyRecurringScanContracts.ValidateForPromotion(paths, sourceInput, publicOnly);
        var generatedAtText = generatedAt?.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ??
            RetentionLogPrivacyRecurringScanContracts.GetString(source, "generatedAt");
        var packageRoot = ResolvePackageRoot(paths, source, outputRoot);

        var preScanArtifacts = new List<RetentionLogPrivacyRecurringScanArtifact>
        {
            new(ReadmePath, BuildReadme(source, generatedAtText)),
            JsonArtifact(Feat137ProofCurrentnessSummaryPath, BuildFeat137ProofCurrentnessSummary(source, generatedAtText)),
            JsonArtifact(RuntimeProofFamilyStatusSummaryPath, BuildRuntimeProofFamilyStatusSummary(source, generatedAtText)),
            JsonArtifact(LogDiagnosticScanSummaryPath, BuildOutputFamilySummary(source, generatedAtText, "retention-log-privacy-log-diagnostic-scan-summary.v1", "log_diagnostic_scan", ["logs", "diagnostics"])),
            JsonArtifact(TraceObservabilityDriftSummaryPath, BuildOutputFamilySummary(source, generatedAtText, "retention-log-privacy-trace-observability-drift-summary.v1", "trace_observability_drift", ["traces", "metrics"])),
            JsonArtifact(SupportExportScanSummaryPath, BuildOutputFamilySummary(source, generatedAtText, "retention-log-privacy-support-export-scan-summary.v1", "support_export_scan", ["support_exports", "restricted_indexes"])),
            JsonArtifact(PackageReportScanSummaryPath, BuildOutputFamilySummary(source, generatedAtText, "retention-log-privacy-package-report-scan-summary.v1", "package_report_scan", ["public_packages", "reviewer_reports", "ci_outputs"])),
            JsonArtifact(PublicOnlyValidationSummaryPath, BuildPublicOnlyValidationSummary(source, generatedAtText, publicOnly)),
            JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, generatedAtText)),
            JsonArtifact(ScoreProposalPath, BuildScoreProposal(source, generatedAtText)),
            JsonArtifact(DownstreamHandoffPath, BuildDownstreamHandoff(source, generatedAtText)),
            new(RestrictedEvidenceIndexPath, BuildRestrictedEvidenceIndexNote(source, generatedAtText)),
        };

        var findings = ScanGeneratedArtifacts(preScanArtifacts);
        var artifacts = new List<RetentionLogPrivacyRecurringScanArtifact>(preScanArtifacts)
        {
            JsonArtifact(NoSecretScanResultPath, BuildNoSecretScanResult(generatedAtText, preScanArtifacts.Count, findings)),
        };
        artifacts.Insert(1, JsonArtifact(PackageIndexPath, BuildPackageIndex(source, generatedAtText, artifacts)));
        artifacts.Insert(1, JsonArtifact(ManifestPath, BuildManifest(source, generatedAtText, artifacts)));

        findings = ScanGeneratedArtifacts(artifacts);
        if (findings.Count > 0)
        {
            throw new RetentionLogPrivacyRecurringScanPromotionException(
                "FEAT-164 generated retention/log privacy recurring scan public-safety scan failed.",
                findings);
        }

        return new RetentionLogPrivacyRecurringScanGeneratedPackage("accepted", packageRoot, source, artifacts);
    }

    public static string ResolvePackageRoot(
        RetentionLogPrivacyRecurringScanPromotionPaths paths,
        JsonObject source,
        string? outputRoot = null)
    {
        var root = Path.GetFullPath(outputRoot ?? paths.PackagesRoot);
        RetentionLogPrivacyRecurringScanContracts.EnsurePathUnder(paths.WorkspaceRoot, root, "FEAT-164 package output root");
        var scanRunId = RetentionLogPrivacyRecurringScanContracts.GetString(source, "scanRunId");
        var packageRoot = Path.GetFullPath(Path.Combine(root, RetentionLogPrivacyRecurringScanPromotionPaths.PackageFamilyFolder, scanRunId));
        RetentionLogPrivacyRecurringScanContracts.EnsurePathUnder(root, packageRoot, "FEAT-164 package root");
        return packageRoot;
    }

    public static IReadOnlyList<string> ScanGeneratedArtifacts(IReadOnlyList<RetentionLogPrivacyRecurringScanArtifact> artifacts)
    {
        var findings = new List<string>();
        foreach (var artifact in artifacts)
        {
            foreach (var forbidden in GeneratedForbiddenNeedles)
            {
                if (artifact.Content.Contains(forbidden, StringComparison.Ordinal))
                {
                    findings.Add($"{artifact.RelativePath} contains forbidden generated marker {forbidden}.");
                }
            }
        }

        return findings;
    }

    private static RetentionLogPrivacyRecurringScanArtifact JsonArtifact(string relativePath, JsonObject content) =>
        new(relativePath, RetentionLogPrivacyRecurringScanContracts.CanonicalJson(content));

    private static JsonObject BuildManifest(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<RetentionLogPrivacyRecurringScanArtifact> artifacts)
    {
        var scanRunId = RetentionLogPrivacyRecurringScanContracts.GetString(source, "scanRunId");
        return new JsonObject
        {
            ["schemaVersion"] = RetentionLogPrivacyRecurringScanContracts.PackageManifestSchemaVersion,
            ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
            ["packageId"] = PackageIdFor(scanRunId),
            ["scanRunId"] = scanRunId,
            ["generatedAt"] = generatedAt,
            ["sourceRef"] = SourceRef(scanRunId),
            ["artifactHashes"] = ToArtifactHashes(artifacts),
            ["validationStatus"] = new JsonObject
            {
                ["status"] = "accepted",
                ["allRequiredGatesAccepted"] = true,
                ["blockingResultCodes"] = new JsonArray(),
            },
            ["readinessOutput"] = ReadinessOutput(),
            ["restrictedEvidencePolicy"] = RestrictedEvidencePolicy(),
        };
    }

    private static JsonObject BuildPackageIndex(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<RetentionLogPrivacyRecurringScanArtifact> artifacts)
    {
        var scanRunId = RetentionLogPrivacyRecurringScanContracts.GetString(source, "scanRunId");
        return new JsonObject
        {
            ["schemaVersion"] = "retention-log-privacy-recurring-scan-package.v1",
            ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
            ["packageId"] = PackageIdFor(scanRunId),
            ["scanRunId"] = scanRunId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["allRequiredGatesAccepted"] = true,
            ["publicOnlyReplayStatus"] = "accepted",
            ["sourceRef"] = SourceRef(scanRunId),
            ["artifactCount"] = artifacts.Count,
            ["artifactHashes"] = ToArtifactHashes(artifacts),
            ["scoreMovement"] = new JsonObject
            {
                ["dimensionId"] = RetentionLogPrivacyRecurringScanContracts.TargetDimensionId,
                ["from"] = 8,
                ["to"] = 9,
                ["proposalOnly"] = true,
                ["directRegisterMutation"] = false,
            },
            ["claimBoundary"] = new JsonObject
            {
                ["strongestAllowedClaim"] = "internal_readiness_score_proposal_after_reviewer_acceptance",
                ["nonClaims"] = NonClaims(),
            },
            ["restrictedEvidencePolicy"] = RestrictedEvidencePolicy(),
        };
    }

    private static JsonObject BuildFeat137ProofCurrentnessSummary(JsonObject source, string generatedAt)
    {
        var proof = RetentionLogPrivacyRecurringScanContracts.RequireObject(source, "feat137Proof");
        return new JsonObject
        {
            ["schemaVersion"] = "retention-log-privacy-feat137-proof-currentness-summary.v1",
            ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["resultCode"] = "RLP164_ACCEPTED",
            ["packageId"] = RetentionLogPrivacyRecurringScanContracts.GetString(proof, "packageId"),
            ["packageHash"] = RetentionLogPrivacyRecurringScanContracts.GetString(proof, "packageHash"),
            ["privacyBoundaryVersion"] = RetentionLogPrivacyRecurringScanContracts.GetString(proof, "privacyBoundaryVersion"),
            ["evidenceStatus"] = RetentionLogPrivacyRecurringScanContracts.GetString(proof, "evidenceStatus"),
            ["dependencyUse"] = "accepted_one_time_baseline_currentness_only",
            ["doesNotRepeatFeat137ScoreMovement"] = true,
            ["blockedWhen"] = RetentionLogPrivacyRecurringScanContracts.ToStringArray(["missing", "stale", "mismatched", "superseded", "blocked", "unknown"]),
        };
    }

    private static JsonObject BuildRuntimeProofFamilyStatusSummary(JsonObject source, string generatedAt)
    {
        var runtime = RetentionLogPrivacyRecurringScanContracts.RequireObject(source, "runtimeProofFamily");
        return new JsonObject
        {
            ["schemaVersion"] = "retention-log-privacy-runtime-proof-family-status-summary.v1",
            ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["proofFamily"] = RetentionLogPrivacyRecurringScanContracts.GetString(runtime, "proofFamily"),
            ["currentStatus"] = RetentionLogPrivacyRecurringScanContracts.GetString(runtime, "currentStatus"),
            ["requiredWhenLiveReportClaimed"] = RetentionLogPrivacyRecurringScanContracts.GetBool(runtime, "requiredWhenLiveReportClaimed"),
            ["claimEffect"] = "currentness_wording_allowed_for_this_public_fixture",
            ["blockingStatuses"] = runtime["blockingStatuses"]!.DeepClone(),
        };
    }

    private static JsonObject BuildOutputFamilySummary(
        JsonObject source,
        string generatedAt,
        string schemaVersion,
        string summaryId,
        IReadOnlyList<string> familyIds)
    {
        var families = RetentionLogPrivacyRecurringScanContracts.RequireArray(source, "outputFamilies")
            .OfType<JsonObject>()
            .Where(family => familyIds.Contains(RetentionLogPrivacyRecurringScanContracts.GetString(family, "familyId"), StringComparer.Ordinal))
            .Select(family => new JsonObject
            {
                ["familyId"] = RetentionLogPrivacyRecurringScanContracts.GetString(family, "familyId"),
                ["visibility"] = RetentionLogPrivacyRecurringScanContracts.GetString(family, "visibility"),
                ["scannerDecision"] = RetentionLogPrivacyRecurringScanContracts.GetString(family, "scannerDecision"),
                ["payloadPublished"] = RetentionLogPrivacyRecurringScanContracts.GetBool(family, "publicPayloadAllowed"),
                ["resultCode"] = "RLP164_ACCEPTED",
            })
            .ToArray();
        var array = new JsonArray();
        foreach (var family in families)
        {
            array.Add(family);
        }

        return new JsonObject
        {
            ["schemaVersion"] = schemaVersion,
            ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
            ["summaryId"] = summaryId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["families"] = array,
            ["driftDecision"] = "fail_closed_when_changed",
            ["publicPayloadPublication"] = "policy_checked",
        };
    }

    private static JsonObject BuildPublicOnlyValidationSummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var policy = RetentionLogPrivacyRecurringScanContracts.RequireObject(source, "publicSafetyPolicy");
        return new JsonObject
        {
            ["schemaVersion"] = "retention-log-privacy-public-only-validation-summary.v1",
            ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = "accepted",
            ["resultCode"] = "RLP164_ACCEPTED",
            ["publicOnlyValidation"] = RetentionLogPrivacyRecurringScanContracts.GetBool(policy, "publicOnlyValidation"),
            ["publicOnlyReplayModeSupported"] = true,
            ["privateCheckoutRequired"] = false,
            ["privateCredentialsRequired"] = false,
            ["livePrivateServicesRequired"] = false,
        };
    }

    private static JsonObject BuildNoSecretScanResult(string generatedAt, int scannedArtifactCount, IReadOnlyList<string> findings)
    {
        var findingArray = new JsonArray();
        foreach (var finding in findings)
        {
            findingArray.Add(finding);
        }

        return new JsonObject
        {
            ["schemaVersion"] = "retention-log-privacy-no-secret-scan-result.v1",
            ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["status"] = findings.Count == 0 ? "accepted" : "blocked",
            ["resultCode"] = findings.Count == 0 ? "RLP164_ACCEPTED" : "RLP164_SECRET_FORBIDDEN",
            ["scannedArtifactCount"] = scannedArtifactCount,
            ["findingCount"] = findings.Count,
            ["findings"] = findingArray,
        };
    }

    private static JsonObject BuildReadinessFragment(JsonObject source, string generatedAt)
    {
        return new JsonObject
        {
            ["schemaVersion"] = "retention-log-privacy-recurring-scan-readiness-fragment.v1",
            ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["registerVersionId"] = RetentionLogPrivacyRecurringScanContracts.CurrentRegisterVersionId,
            ["dimensionId"] = RetentionLogPrivacyRecurringScanContracts.TargetDimensionId,
            ["targetBlockerId"] = RetentionLogPrivacyRecurringScanContracts.TargetBlockerId,
            ["scoreEffect"] = new JsonObject
            {
                ["from"] = 8,
                ["to"] = 9,
                ["proposalOnly"] = true,
                ["directRegisterMutation"] = false,
            },
            ["acceptedEvidenceRefs"] = RetentionLogPrivacyRecurringScanContracts.ToStringArray([
                Feat137ProofCurrentnessSummaryPath,
                LogDiagnosticScanSummaryPath,
                TraceObservabilityDriftSummaryPath,
                SupportExportScanSummaryPath,
                PackageReportScanSummaryPath,
                NoSecretScanResultPath,
            ]),
            ["residualRisk"] = "Future logging, diagnostics, observability, support exports, and package/report outputs must remain classified and fail closed when new output families appear.",
            ["downgradeOrBlockWhen"] = RetentionLogPrivacyRecurringScanContracts.ToStringArray([
                "FEAT-137 proof is missing, stale, mismatched, superseded, blocked, or unknown",
                "scanner baseline or output-family registry hash changes without acceptance",
                "public outputs contain restricted privacy material",
                "public-only validation requires private repositories, credentials, or live private services",
            ]),
        };
    }

    private static JsonObject BuildScoreProposal(JsonObject source, string generatedAt)
    {
        return new JsonObject
        {
            ["schemaVersion"] = "retention-log-privacy-recurring-scan-score-proposal.v1",
            ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
            ["generatedAt"] = generatedAt,
            ["dimensionId"] = RetentionLogPrivacyRecurringScanContracts.TargetDimensionId,
            ["targetBlockerId"] = RetentionLogPrivacyRecurringScanContracts.TargetBlockerId,
            ["proposedScoreFrom"] = 8,
            ["proposedScoreTo"] = 9,
            ["proposalOnly"] = true,
            ["directRegisterMutation"] = false,
            ["blockedUnlessAllGatesPass"] = true,
            ["feat137DependencyUse"] = "accepted_one_time_baseline_currentness_only",
            ["nonClaims"] = NonClaims(),
        };
    }

    private static JsonObject BuildDownstreamHandoff(JsonObject source, string generatedAt)
    {
        var scanRunId = RetentionLogPrivacyRecurringScanContracts.GetString(source, "scanRunId");
        return new JsonObject
        {
            ["schemaVersion"] = "retention-log-privacy-recurring-scan-downstream-handoff.v1",
            ["featureId"] = RetentionLogPrivacyRecurringScanContracts.FeatureId,
            ["scanRunId"] = scanRunId,
            ["generatedAt"] = generatedAt,
            ["consumers"] = new JsonArray(
                new JsonObject
                {
                    ["consumerId"] = "FEAT-166",
                    ["allowedUse"] = "governance customer handoff audit pack may reference accepted public package refs and non-claim wording.",
                },
                new JsonObject
                {
                    ["consumerId"] = "internal_audit_95_promotion_owner",
                    ["allowedUse"] = "promotion owner may review proposal-only RDY-DIM-008 8 -> 9 evidence before any separate register promotion.",
                }),
            ["publicRefs"] = RetentionLogPrivacyRecurringScanContracts.ToStringArray([
                ManifestPath,
                PackageIndexPath,
                ReadinessFragmentPath,
                ScoreProposalPath,
            ]),
            ["restrictedRefs"] = RetentionLogPrivacyRecurringScanContracts.ToStringArray([
                "REST-RLP164-20260603-001",
            ]),
            ["payloadPublication"] = "disabled",
            ["nonClaims"] = NonClaims(),
            ["residualRisk"] = "Reviewer acceptance is still required before any readiness-register promotion.",
        };
    }

    private static string BuildReadme(JsonObject source, string generatedAt)
    {
        var scanRunId = RetentionLogPrivacyRecurringScanContracts.GetString(source, "scanRunId");
        return RetentionLogPrivacyRecurringScanContracts.NormalizeLineEndings($"""
            # Retention Log Privacy Recurring Scan Package

            Feature: `FEAT-164`
            Scan run: `{scanRunId}`
            Generated at: `{generatedAt}`

            This package contains public-safe recurring retention/log privacy scan contracts,
            validation summaries, hash-bound artifact refs, and proposal-only readiness outputs.

            The package proposes only `RDY-DIM-008 8 -> 9`. It does not mutate the readiness
            register, repeat the FEAT-137 `4 -> 8` movement, certify privacy compliance, establish
            legal sufficiency, approve production rollout, or claim public/state election readiness.

            Restricted evidence is represented only by opaque ids, expected hashes, visibility
            markers, and no-payload notes. Restricted payloads remain in the approved private
            reviewer location and are not required for public-only replay.
            """);
    }

    private static string BuildRestrictedEvidenceIndexNote(JsonObject source, string generatedAt)
    {
        var scanRunId = RetentionLogPrivacyRecurringScanContracts.GetString(source, "scanRunId");
        return RetentionLogPrivacyRecurringScanContracts.NormalizeLineEndings($"""
            # Restricted Evidence Index Note

            Feature: `FEAT-164`
            Scan run: `{scanRunId}`
            Generated at: `{generatedAt}`

            Public payload publication is disabled.

            Public artifacts may reference restricted evidence id `REST-RLP164-20260603-001`,
            expected hash, visibility marker, and this no-payload note only. Raw logs, traces,
            diagnostics bundles, support exports, private scanner findings, reviewer payloads,
            voter/customer material, credentials, and local paths are not public package material.
            """);
    }

    private static JsonObject SourceRef(string scanRunId) =>
        new()
        {
            ["repository"] = "Hushnetwork-social/Retention-Log-Privacy-Scans",
            ["ref"] = "main",
            ["path"] = $"examples/release-baseline/{RetentionLogPrivacyRecurringScanPromotionPaths.SourceFileName}",
        };

    private static JsonObject ReadinessOutput() =>
        new()
        {
            ["dimensionId"] = RetentionLogPrivacyRecurringScanContracts.TargetDimensionId,
            ["proposedScoreFrom"] = 8,
            ["proposedScoreTo"] = 9,
            ["directRegisterMutation"] = false,
            ["targetBlockerId"] = RetentionLogPrivacyRecurringScanContracts.TargetBlockerId,
        };

    private static JsonObject RestrictedEvidencePolicy() =>
        new()
        {
            ["payloadPublished"] = false,
            ["publicRefFieldsOnly"] = true,
        };

    private static JsonArray NonClaims() =>
        new(
            "external_audit_acceptance",
            "certification",
            "legal_sufficiency",
            "public_state_election_readiness",
            "production_rollout_approval",
            "direct_register_mutation");

    private static JsonArray ToArtifactHashes(IReadOnlyList<RetentionLogPrivacyRecurringScanArtifact> artifacts)
    {
        var array = new JsonArray();
        foreach (var artifact in artifacts.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            array.Add(new JsonObject
            {
                ["path"] = artifact.RelativePath,
                ["sha256Hash"] = artifact.Sha256Hash,
                ["mediaType"] = artifact.MediaType,
                ["visibility"] = artifact.Visibility,
                ["sizeBytes"] = artifact.SizeBytes,
            });
        }

        return array;
    }

    private static string PackageIdFor(string scanRunId) => scanRunId.Replace("FEAT164-RLP-SCAN-", "FEAT164-RLP-SCAN-PACKAGE-", StringComparison.Ordinal);
}
