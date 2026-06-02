using System.Text.Json.Nodes;

namespace KmsCustodyRehearsalPromoter;

public sealed record KmsCustodyRehearsalArtifact(string RelativePath, string Content)
{
    public string Sha256Hash => KmsCustodyRehearsalContracts.Sha256Hex(Content);
}

public sealed record KmsCustodyRehearsalGeneratedPackage(
    string Status,
    string PackageRoot,
    JsonObject Source,
    IReadOnlyList<KmsCustodyRehearsalArtifact> Artifacts);

public static class KmsCustodyRehearsalArtifactGenerator
{
    public const string ReadmePath = "README.md";
    public const string ManifestPath = "kms-custody-rehearsal-manifest.json";
    public const string IamDriftSummaryPath = "validation/iam-drift-scan-summary.json";
    public const string RotationRecoverySummaryPath = "validation/rotation-recovery-rehearsal-summary.json";
    public const string ProviderRegionalFailureSummaryPath = "validation/provider-regional-failure-summary.json";
    public const string DeletionScheduleDriftSummaryPath = "validation/deletion-schedule-drift-summary.json";
    public const string NoSecretScanResultPath = "validation/no-secret-scan-result.json";
    public const string ReadinessFragmentPath = "readiness/kms-custody-rehearsal-readiness-fragment.json";
    public const string ScoreProposalPath = "readiness/kms-custody-rehearsal-score-proposal.json";
    public const string DownstreamHandoffPath = "handoff/kms-custody-rehearsal-downstream-handoff.json";
    public const string ReviewerGuidePath = "handoff/reviewer-guide.md";
    public const string RestrictedEvidenceIndexPath = "restricted/restricted-evidence-index.schema-note.md";

    public static readonly string[] RequiredArtifactPaths =
    [
        ReadmePath,
        ManifestPath,
        IamDriftSummaryPath,
        RotationRecoverySummaryPath,
        ProviderRegionalFailureSummaryPath,
        DeletionScheduleDriftSummaryPath,
        NoSecretScanResultPath,
        ReadinessFragmentPath,
        ScoreProposalPath,
        DownstreamHandoffPath,
        ReviewerGuidePath,
        RestrictedEvidenceIndexPath,
    ];

    private static readonly string[] GeneratedForbiddenNeedles =
    [
        "arn:aws",
        "aws_access_key_id",
        "aws_secret_access_key",
        "secret_access_key",
        "begin private key",
        "credential=",
        "password=",
        "connection string",
        "client_secret",
        "aws_secret",
        "keyarn",
        "kmskey",
        "kmsalias",
        "operator identity",
        "operator_id",
        "account_id",
        "custody_row",
        "raw scalar",
        "scalar=",
        "decrypt authority",
        "decrypt_authority",
        "provider_error_payload",
        "x-amzn-errortype",
        "runbook:",
        "hush-documents/privateserver_electronicvoting",
    ];

    public static KmsCustodyRehearsalGeneratedPackage Generate(
        KmsCustodyRehearsalPromotionPaths paths,
        string? sourceInput = null,
        string? outputRoot = null,
        DateTimeOffset? generatedAt = null)
    {
        var source = KmsCustodyRehearsalContracts.ValidateForPromotion(paths, sourceInput);
        var packageRoot = ResolvePackageRoot(paths, source, outputRoot);
        var generatedAtText = ResolveGeneratedAt(source, generatedAt);

        var preScanArtifacts = new List<KmsCustodyRehearsalArtifact>
        {
            new(ReadmePath, BuildReadme(source)),
            JsonArtifact(IamDriftSummaryPath, BuildScenarioSummary(source, generatedAtText, "kms-custody-iam-drift-summary.v1", ["iam_drift"])),
            JsonArtifact(RotationRecoverySummaryPath, BuildScenarioSummary(source, generatedAtText, "kms-custody-rotation-recovery-summary.v1", ["accepted_baseline", "runtime_rotation", "alias_tag_drift", "restricted_boundary"])),
            JsonArtifact(ProviderRegionalFailureSummaryPath, BuildScenarioSummary(source, generatedAtText, "kms-custody-provider-regional-failure-summary.v1", ["provider_failure", "regional_failure"])),
            JsonArtifact(DeletionScheduleDriftSummaryPath, BuildScenarioSummary(source, generatedAtText, "kms-custody-deletion-schedule-drift-summary.v1", ["deletion_schedule_drift", "stale_orphaned_custody_state"])),
            JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, generatedAtText)),
            JsonArtifact(ScoreProposalPath, BuildScoreProposal(source, generatedAtText)),
            JsonArtifact(DownstreamHandoffPath, BuildDownstreamHandoff(source, generatedAtText)),
            new(ReviewerGuidePath, BuildReviewerGuide(source)),
            new(RestrictedEvidenceIndexPath, BuildRestrictedEvidenceIndexNote(source)),
        };

        var scanFindings = ScanGeneratedArtifacts(preScanArtifacts);
        if (scanFindings.Count > 0)
        {
            throw new KmsCustodyRehearsalPromotionException(
                "FEAT-161 generated KMS custody rehearsal package public-safety scan failed.",
                scanFindings);
        }

        var artifacts = new List<KmsCustodyRehearsalArtifact>(preScanArtifacts);
        artifacts.Insert(5, JsonArtifact(NoSecretScanResultPath, BuildNoSecretScanResult(generatedAtText, preScanArtifacts.Count)));
        artifacts.Insert(1, JsonArtifact(ManifestPath, BuildManifest(source, generatedAtText, artifacts)));

        return new KmsCustodyRehearsalGeneratedPackage("candidate", packageRoot, source, artifacts);
    }

    public static string ResolvePackageRoot(
        KmsCustodyRehearsalPromotionPaths paths,
        JsonObject source,
        string? outputRoot = null)
    {
        var root = Path.GetFullPath(outputRoot ?? paths.PublicCorpusRoot);
        KmsCustodyRehearsalContracts.EnsurePathUnder(paths.WorkspaceRoot, root, "FEAT-161 rehearsal output root");
        var packageRoot = Path.GetFullPath(Path.Combine(root, KmsCustodyRehearsalPromotionPaths.PackageRelativeRoot));
        KmsCustodyRehearsalContracts.EnsurePathUnder(root, packageRoot, "FEAT-161 rehearsal package root");

        var targetPath = KmsCustodyRehearsalContracts.GetString(
            KmsCustodyRehearsalContracts.RequireObject(source, "packageLayout"),
            "targetPackagePath");
        if (!string.Equals(targetPath, KmsCustodyRehearsalContracts.ExpectedTargetPackagePath, StringComparison.Ordinal))
        {
            throw new KmsCustodyRehearsalPromotionException(
                "FEAT-161 package layout target is not the expected v0.1.0 KMS custody rehearsal package.",
                [$"Observed: {targetPath}"]);
        }

        return packageRoot;
    }

    public static IReadOnlyList<string> ScanGeneratedArtifacts(IReadOnlyList<KmsCustodyRehearsalArtifact> artifacts)
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

    private static KmsCustodyRehearsalArtifact JsonArtifact(string relativePath, JsonObject content) =>
        new(relativePath, KmsCustodyRehearsalContracts.CanonicalJson(content));

    private static string ResolveGeneratedAt(JsonObject source, DateTimeOffset? generatedAt)
    {
        if (generatedAt is not null)
        {
            return generatedAt.Value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        return KmsCustodyRehearsalContracts.GetString(source, "generatedAt");
    }

    private static JsonObject BuildManifest(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<KmsCustodyRehearsalArtifact> artifacts)
    {
        var baseline = KmsCustodyRehearsalContracts.RequireObject(source, "baselineRegister");
        var scenarios = Scenarios(source).ToArray();
        return new JsonObject
        {
            ["schemaVersion"] = KmsCustodyRehearsalContracts.PackageManifestSchemaVersion,
            ["packageId"] = "hushvoting-kms-custody-rehearsal",
            ["packageVersion"] = KmsCustodyRehearsalContracts.TargetPackageVersion,
            ["producerFeature"] = KmsCustodyRehearsalContracts.FeatureId,
            ["sourceId"] = KmsCustodyRehearsalContracts.GetString(source, "sourceId"),
            ["generatedAt"] = generatedAt,
            ["baselineRegister"] = new JsonObject
            {
                ["registerVersionId"] = KmsCustodyRehearsalContracts.GetString(baseline, "registerVersionId"),
                ["status"] = KmsCustodyRehearsalContracts.GetString(baseline, "status"),
                ["dimensionId"] = KmsCustodyRehearsalContracts.GetString(baseline, "dimensionId"),
                ["proposedScoreFrom"] = 8,
                ["proposedScoreTo"] = 9,
                ["targetBlockerId"] = KmsCustodyRehearsalContracts.GetString(baseline, "targetBlockerId"),
                ["doesNotMutateRegister"] = true,
            },
            ["upstreamRefs"] = BuildUpstreamRefs(source),
            ["validationSummary"] = new JsonObject
            {
                ["status"] = "pass",
                ["scenarioCount"] = scenarios.Length,
                ["requiredScenarioCount"] = scenarios.Count(item => KmsCustodyRehearsalContracts.GetBool(item, "requiredForScore")),
                ["passedRequiredScenarioCount"] = scenarios.Count(item => KmsCustodyRehearsalContracts.GetBool(item, "requiredForScore")),
                ["blockedScenarioCount"] = scenarios.Count(item => KmsCustodyRehearsalContracts.GetString(item, "expectedResult") == "blocked"),
                ["summaryRefs"] = new JsonArray(IamDriftSummaryPath, RotationRecoverySummaryPath, ProviderRegionalFailureSummaryPath, DeletionScheduleDriftSummaryPath),
            },
            ["publicSafety"] = new JsonObject
            {
                ["status"] = "pass",
                ["unexpectedFindingCount"] = 0,
                ["scanResultRef"] = NoSecretScanResultPath,
            },
            ["currentnessSummary"] = new JsonObject
            {
                ["status"] = "source_validated",
                ["summaryRef"] = IamDriftSummaryPath,
                ["checks"] = new JsonArray("readiness-register", "feat131-custody", "feat143-runtime-binding", "feat154-operational-context", "feat156-promotion-register"),
            },
            ["reviewerHandoff"] = new JsonObject
            {
                ["status"] = "ready",
                ["downstreamHandoffRef"] = DownstreamHandoffPath,
                ["restrictedIndexRef"] = RestrictedEvidenceIndexPath,
                ["reviewerGuideRef"] = ReviewerGuidePath,
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
        };
    }

    private static JsonObject BuildScenarioSummary(
        JsonObject source,
        string generatedAt,
        string schemaVersion,
        IReadOnlyList<string> categories)
    {
        var categorySet = categories.ToHashSet(StringComparer.Ordinal);
        var scenarios = Scenarios(source)
            .Where(item => categorySet.Contains(KmsCustodyRehearsalContracts.GetString(item, "category")))
            .ToArray();
        return new JsonObject
        {
            ["schemaVersion"] = schemaVersion,
            ["generatedAt"] = generatedAt,
            ["status"] = "pass",
            ["evidenceMode"] = "deterministic_fake_provider",
            ["scenarioCount"] = scenarios.Length,
            ["cases"] = new JsonArray(scenarios
                .Select(item => new JsonObject
                {
                    ["scenarioId"] = KmsCustodyRehearsalContracts.GetString(item, "scenarioId"),
                    ["category"] = KmsCustodyRehearsalContracts.GetString(item, "category"),
                    ["expectedResult"] = KmsCustodyRehearsalContracts.GetString(item, "expectedResult"),
                    ["safeResultCodes"] = KmsCustodyRehearsalContracts.Clone(item["safeResultCodes"]),
                    ["readinessImpact"] = KmsCustodyRehearsalContracts.GetString(item, "readinessImpact"),
                    ["gateIds"] = KmsCustodyRehearsalContracts.Clone(item["gateIds"]),
                    ["restrictedEvidenceRefs"] = KmsCustodyRehearsalContracts.Clone(item["restrictedEvidenceRefs"]),
                    ["requiredForScore"] = KmsCustodyRehearsalContracts.GetBool(item, "requiredForScore"),
                })
                .ToArray<JsonNode?>()),
        };
    }

    private static JsonObject BuildNoSecretScanResult(string generatedAt, int scannedArtifactCount) =>
        new()
        {
            ["schemaVersion"] = "kms-custody-rehearsal-no-secret-scan-result.v1",
            ["generatedAt"] = generatedAt,
            ["status"] = "pass",
            ["unexpectedFindingCount"] = 0,
            ["scannedArtifactCount"] = scannedArtifactCount,
            ["scannerFamilies"] = new JsonArray("provider_identifier", "credential_marker", "identity_marker", "custody_payload_marker", "local_path_marker"),
            ["findings"] = new JsonArray(),
        };

    private static JsonObject BuildReadinessFragment(JsonObject source, string generatedAt)
    {
        var baseline = KmsCustodyRehearsalContracts.RequireObject(source, "baselineRegister");
        return new JsonObject
        {
            ["schemaVersion"] = "kms-custody-rehearsal-readiness-fragment.v1",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = KmsCustodyRehearsalContracts.FeatureId,
            ["dimensionId"] = KmsCustodyRehearsalContracts.TargetDimensionId,
            ["status"] = "candidate",
            ["targetBlockerId"] = KmsCustodyRehearsalContracts.GetString(baseline, "targetBlockerId"),
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
        var baseline = KmsCustodyRehearsalContracts.RequireObject(source, "baselineRegister");
        return new JsonObject
        {
            ["schemaVersion"] = "kms-custody-rehearsal-score-proposal.v1",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = KmsCustodyRehearsalContracts.FeatureId,
            ["status"] = "proposed",
            ["dimensionId"] = KmsCustodyRehearsalContracts.TargetDimensionId,
            ["targetBlockerId"] = KmsCustodyRehearsalContracts.GetString(baseline, "targetBlockerId"),
            ["proposedScoreFrom"] = 8,
            ["proposedScoreTo"] = 9,
            ["scoreChangeAllowed"] = false,
            ["doesNotMutateRegister"] = true,
            ["directRegisterMutation"] = false,
            ["evidencePackagePath"] = KmsCustodyRehearsalContracts.ExpectedTargetPackagePath,
            ["registerMutation"] = "not_performed",
            ["requiredValidationRefs"] = new JsonArray(IamDriftSummaryPath, RotationRecoverySummaryPath, ProviderRegionalFailureSummaryPath, DeletionScheduleDriftSummaryPath, NoSecretScanResultPath),
        };
    }

    private static JsonObject BuildDownstreamHandoff(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "kms-custody-rehearsal-downstream-handoff.v1",
            ["handoffId"] = "FEAT-161-v0.1.0-handoff",
            ["generatedAt"] = generatedAt,
            ["producerFeature"] = KmsCustodyRehearsalContracts.FeatureId,
            ["targetPackage"] = KmsCustodyRehearsalContracts.ExpectedTargetPackagePath,
            ["reviewerGuideRef"] = ReviewerGuidePath,
            ["restrictedIndexRef"] = RestrictedEvidenceIndexPath,
            ["consumers"] = KmsCustodyRehearsalContracts.Clone(source["downstreamConsumers"]),
            ["residualRisks"] = KmsCustodyRehearsalContracts.Clone(source["residualRisks"]),
            ["consumerInstructions"] = "FEAT-162, FEAT-163, and FEAT-166 may consume this public-safe candidate package after their own scope-specific checks pass. Canonical readiness mutation remains owned by a later internal-audit-95 promotion pass.",
        };

    private static string BuildReviewerGuide(JsonObject source)
    {
        var sourceId = KmsCustodyRehearsalContracts.GetString(source, "sourceId");
        return string.Join("\n", [
            "# KMS Custody Rehearsal Reviewer Guide",
            "",
            $"Source: {sourceId}",
            $"Package: {KmsCustodyRehearsalContracts.ExpectedTargetPackagePath}",
            "",
            "## Reviewer Commands",
            "",
            "Run these from the HushNetworkOrg workspace root after cloning the public source and verifier corpus repositories.",
            "",
            "```powershell",
            "dotnet build .\\hush-server-node\\Node\\scripts\\KmsCustodyRehearsalPromoter\\KmsCustodyRehearsalPromoter.csproj --no-restore --verbosity minimal",
            ".\\hush-server-node\\Node\\scripts\\promote-kms-custody-rehearsal.ps1 --validate-only --workspace-root <workspace-root>",
            ".\\hush-server-node\\Node\\scripts\\promote-kms-custody-rehearsal.ps1 --mode package --workspace-root <workspace-root>",
            ".\\hush-server-node\\Node\\scripts\\promote-kms-custody-rehearsal.ps1 --mode check-only --workspace-root <workspace-root>",
            "```",
            "",
            "## Review Scope",
            "",
            "Confirm source currentness, expected fail-closed scenarios, restricted hash-only references, no-secret scan status, and proposal-only score movement.",
            "The package proposes RDY-DIM-005 movement from 8 to 9 only and does not mutate the canonical readiness register.",
            "",
            "## Non-Claims",
            "",
            "- No production rollout execution claim.",
            "- No public or state election readiness claim.",
            "- No legal sufficiency, certification, or independent audit acceptance claim.",
            "",
        ]);
    }

    private static string BuildRestrictedEvidenceIndexNote(JsonObject source) =>
        string.Join("\n", [
            "# Restricted Evidence Index Schema Note",
            "",
            $"Source: {KmsCustodyRehearsalContracts.GetString(source, "sourceId")}",
            "",
            "Public artifacts expose only reviewer-scoped ref ids and SHA-256 hashes for restricted custody evidence.",
            "Payloads remain outside this public package. A reviewer with the proper restricted evidence access can match each ref id and hash against the private evidence index.",
            "",
        ]);

    private static string BuildReadme(JsonObject source)
    {
        var sourceId = KmsCustodyRehearsalContracts.GetString(source, "sourceId");
        return string.Join("\n", [
            "# HushVoting KMS Custody Rehearsal",
            "",
            $"Source: {sourceId}",
            $"Register baseline: {KmsCustodyRehearsalContracts.CurrentRegisterId}",
            $"Score proposal: {KmsCustodyRehearsalContracts.TargetDimensionId} 8 to 9",
            "",
            "This package is a public-safe KMS custody rehearsal candidate for FEAT-161.",
            "It records deterministic fake-provider checks, fail-closed drift expectations, restricted hash-only evidence references, and downstream handoff instructions.",
            "It does not mutate the readiness register and does not claim production rollout, public/state election readiness, legal sufficiency, certification, or independent audit acceptance.",
            "",
        ]);
    }

    private static JsonArray BuildUpstreamRefs(JsonObject source)
    {
        var upstream = KmsCustodyRehearsalContracts.RequireObject(source, "upstreamBaselines");
        var feat131 = KmsCustodyRehearsalContracts.RequireObject(upstream, "feat131");
        var feat143 = KmsCustodyRehearsalContracts.RequireObject(upstream, "feat143");
        var feat154 = KmsCustodyRehearsalContracts.RequireObject(upstream, "feat154");
        var feat156 = KmsCustodyRehearsalContracts.RequireObject(upstream, "feat156");
        return new JsonArray(
            UpstreamRef("feat131-public-safe-handoff", "FEAT-131", KmsCustodyRehearsalContracts.GetString(feat131, "publicSafeHandoffHash")),
            UpstreamRef("feat131-restricted-handoff", "FEAT-131", KmsCustodyRehearsalContracts.GetString(feat131, "restrictedHandoffHash")),
            UpstreamRef("feat143-runtime-binding-handoff", "FEAT-143", KmsCustodyRehearsalContracts.GetString(feat143, "referenceHash")),
            UpstreamRef("feat154-operational-context", "FEAT-154", KmsCustodyRehearsalContracts.GetString(feat154, "referenceHash")),
            UpstreamRef("feat156-promotion-register-manifest", "FEAT-156", KmsCustodyRehearsalContracts.GetString(feat156, "referenceHash")));
    }

    private static JsonObject UpstreamRef(string artifactId, string producerFeature, string hash) =>
        new()
        {
            ["producerFeature"] = producerFeature,
            ["artifactId"] = artifactId,
            ["hash"] = hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? hash : "sha256:" + hash,
        };

    private static IEnumerable<JsonObject> Scenarios(JsonObject source) =>
        KmsCustodyRehearsalContracts.RequireArray(
                KmsCustodyRehearsalContracts.RequireObject(source, "rehearsalMatrix"),
                "scenarios")
            .OfType<JsonObject>();

    private static JsonArray NonClaims() =>
        new(
            JsonValue.Create("No production rollout claim"),
            JsonValue.Create("No public or state election readiness claim"),
            JsonValue.Create("No legal sufficiency, certification, or independent audit acceptance claim"));
}
