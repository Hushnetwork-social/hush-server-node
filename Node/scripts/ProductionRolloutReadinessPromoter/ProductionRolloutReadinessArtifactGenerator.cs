using System.Text;
using System.Text.Json.Nodes;

namespace ProductionRolloutReadinessPromoter;

public static partial class ProductionRolloutReadinessArtifactGenerator
{
    public const string SourceEchoPath = "production-rollout-readiness-source.json";
    public const string EvidencePackagePath = "production-rollout-evidence-package.json";
    public const string CheckResultsPath = "production-rollout-check-results.json";
    public const string DecisionLedgerPath = "production-rollout-blocker-resolution-decision-ledger.json";
    public const string ArtifactHashAuditPath = "production-rollout-artifact-hash-audit.json";
    public const string ReadinessFragmentPath = "production-rollout-readiness-fragment.json";
    public const string PublicSafeSummaryPath = "production-rollout-public-safe-summary.md";
    public const string RestrictedReviewerIndexPath = "production-rollout-restricted-reviewer-index.json";
    public const string PackageHashValidationPath = "production-rollout-package-hash-validation.json";

    public static readonly string[] RequiredArtifactPaths =
    [
        ArtifactHashAuditPath,
        CheckResultsPath,
        DecisionLedgerPath,
        EvidencePackagePath,
        PackageHashValidationPath,
        PublicSafeSummaryPath,
        ReadinessFragmentPath,
        RestrictedReviewerIndexPath,
        SourceEchoPath,
    ];

    public static ProductionRolloutGeneratedPackage Generate(
        ProductionRolloutReadinessPromotionPaths paths,
        string? sourceInput = null,
        DateTimeOffset? generatedAt = null)
    {
        var source = ProductionRolloutReadinessContracts.LoadSource(paths, sourceInput);
        var validationErrors = ProductionRolloutReadinessContracts.ValidateSource(source);
        if (validationErrors.Count > 0)
        {
            throw new ProductionRolloutReadinessPromotionException(
                "FEAT-148 production rollout source validation failed.",
                validationErrors);
        }

        var effectiveGeneratedAt = generatedAt ?? DateTimeOffset.UtcNow;
        var gate = ProductionRolloutReadinessGateChecker.Evaluate(source);
        var artifactAudit = BuildArtifactHashAudit(source, paths.WorkspaceRoot, effectiveGeneratedAt);
        var auditFailures = CollectAuditFailures(artifactAudit);
        var packageStatus = gate.Status == "allowed_with_limitations_candidate" && auditFailures.Count == 0
            ? gate.Status
            : "blocked";

        var sourceArtifact = JsonArtifact(SourceEchoPath, source);
        var auditArtifact = JsonArtifact(ArtifactHashAuditPath, artifactAudit);
        var checkResultsArtifact = JsonArtifact(CheckResultsPath, BuildCheckResults(source, gate, auditFailures, packageStatus, effectiveGeneratedAt));
        var decisionLedgerArtifact = JsonArtifact(DecisionLedgerPath, BuildDecisionLedger(source, gate, auditFailures, packageStatus, effectiveGeneratedAt));
        var readinessArtifact = JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, gate, auditFailures, packageStatus, effectiveGeneratedAt));
        var publicSummaryArtifact = TextArtifact(PublicSafeSummaryPath, BuildPublicSafeSummary(source, gate, auditFailures, packageStatus, effectiveGeneratedAt));
        var restrictedIndexArtifact = JsonArtifact(RestrictedReviewerIndexPath, BuildRestrictedReviewerIndex(source, artifactAudit, effectiveGeneratedAt));
        var evidencePackageArtifact = JsonArtifact(
            EvidencePackagePath,
            BuildEvidencePackage(
                source,
                gate,
                auditFailures,
                packageStatus,
                [
                    sourceArtifact,
                    auditArtifact,
                    checkResultsArtifact,
                    decisionLedgerArtifact,
                    readinessArtifact,
                    publicSummaryArtifact,
                    restrictedIndexArtifact,
                ],
                effectiveGeneratedAt));
        var hashValidationArtifact = JsonArtifact(
            PackageHashValidationPath,
            BuildPackageHashValidation(
                source,
                packageStatus,
                [
                    sourceArtifact,
                    auditArtifact,
                    checkResultsArtifact,
                    decisionLedgerArtifact,
                    evidencePackageArtifact,
                    readinessArtifact,
                    publicSummaryArtifact,
                    restrictedIndexArtifact,
                ],
                effectiveGeneratedAt));

        var artifacts = new[]
        {
            sourceArtifact,
            auditArtifact,
            checkResultsArtifact,
            decisionLedgerArtifact,
            evidencePackageArtifact,
            hashValidationArtifact,
            readinessArtifact,
            publicSummaryArtifact,
            restrictedIndexArtifact,
        }
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new ProductionRolloutGeneratedPackage(packageStatus, artifacts, gate, auditFailures);
    }

    private static JsonObject BuildArtifactHashAudit(
        JsonObject source,
        string workspaceRoot,
        DateTimeOffset generatedAt)
    {
        var refs = CollectEvidenceRefs(source);
        var entries = new JsonArray(refs
            .OrderBy(item => ProductionRolloutReadinessContracts.GetString(item, "evidenceId"), StringComparer.Ordinal)
            .Select(item => BuildAuditEntry(item, workspaceRoot))
            .ToArray<JsonNode?>());
        var failedCount = entries.OfType<JsonObject>().Count(entry => IsFailedAuditResult(ProductionRolloutReadinessContracts.GetString(entry, "auditResult")));

        return new JsonObject
        {
            ["schemaVersion"] = "production-rollout-artifact-hash-audit.v1",
            ["auditId"] = "FEAT148-PRODUCTION-ROLLOUT-ARTIFACT-HASH-AUDIT-001",
            ["sourceId"] = ProductionRolloutReadinessContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionRolloutReadinessContracts.FormatTimestamp(generatedAt),
            ["status"] = failedCount == 0 ? "passed" : "blocked",
            ["artifacts"] = entries,
        };
    }

    private static IReadOnlyList<JsonObject> CollectEvidenceRefs(JsonObject source)
    {
        var refs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var groupName in new[]
        {
            "runEvidence",
            "operationalEvidence",
            "deploymentProofEvidence",
            "webClientProofEvidence",
            "governedOutcomeEvidence",
        })
        {
            AddEvidenceArray(ProductionRolloutReadinessContracts.RequireObject(source, groupName), "evidenceRefs");
        }

        AddEvidenceArray(source, "upstreamEvidence");
        AddEvidenceArray(source, "restrictedEvidenceRefs");
        return refs.Values.ToArray();

        void AddEvidenceArray(JsonObject container, string property)
        {
            foreach (var evidence in ProductionRolloutReadinessContracts.RequireArray(container, property).OfType<JsonObject>())
            {
                var id = ProductionRolloutReadinessContracts.GetString(evidence, "evidenceId",
                    ProductionRolloutReadinessContracts.GetString(evidence, "refId"));
                if (!string.IsNullOrWhiteSpace(id))
                {
                    refs.TryAdd(id, evidence);
                }
            }
        }
    }

    private static JsonObject BuildAuditEntry(JsonObject evidence, string workspaceRoot)
    {
        var declaredHash = ProductionRolloutReadinessContracts.GetString(evidence, "sha256Hash");
        var publicRef = ProductionRolloutReadinessContracts.GetString(evidence, "publicRef");
        var restrictedRef = ProductionRolloutReadinessContracts.GetString(evidence, "restrictedRef");
        var auditResult = "hash_only_accepted";
        var observedHash = declaredHash;
        var reason = "Evidence is represented by a declared restricted/external hash; payload body is not copied.";
        long sizeBytes = 0;

        if (string.IsNullOrWhiteSpace(restrictedRef) && !string.IsNullOrWhiteSpace(publicRef))
        {
            var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, publicRef));
            if (!fullPath.StartsWith(Path.GetFullPath(workspaceRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                auditResult = "failed";
                observedHash = "";
                reason = "Evidence path escapes workspace root.";
            }
            else if (!File.Exists(fullPath))
            {
                auditResult = "missing";
                observedHash = "";
                reason = "Local evidence file is missing.";
            }
            else
            {
                var bytes = File.ReadAllBytes(fullPath);
                sizeBytes = bytes.Length;
                observedHash = ProductionRolloutReadinessContracts.Sha256Hex(bytes);
                auditResult = string.Equals(observedHash, declaredHash, StringComparison.Ordinal)
                    ? "passed"
                    : "failed";
                reason = auditResult == "passed"
                    ? "Observed SHA-256 matches declared SHA-256."
                    : "Observed SHA-256 does not match declared SHA-256.";
            }
        }

        return new JsonObject
        {
            ["evidenceId"] = ProductionRolloutReadinessContracts.GetString(evidence, "evidenceId",
                ProductionRolloutReadinessContracts.GetString(evidence, "refId")),
            ["featureSlice"] = ProductionRolloutReadinessContracts.GetString(evidence, "featureSlice", ProductionRolloutReadinessContracts.FeatureId),
            ["visibility"] = ProductionRolloutReadinessContracts.GetString(evidence, "visibility"),
            ["publicRef"] = publicRef,
            ["restrictedRef"] = restrictedRef,
            ["expectedSha256Hash"] = declaredHash,
            ["observedSha256Hash"] = observedHash,
            ["hashFormat"] = "sha256-hex",
            ["sizeBytes"] = sizeBytes,
            ["auditResult"] = auditResult,
            ["reason"] = reason,
        };
    }

    private static IReadOnlyList<string> CollectAuditFailures(JsonObject artifactAudit) =>
        ProductionRolloutReadinessContracts.RequireArray(artifactAudit, "artifacts")
            .OfType<JsonObject>()
            .Where(entry => IsFailedAuditResult(ProductionRolloutReadinessContracts.GetString(entry, "auditResult")))
            .Select(entry => $"FEAT148-ARTIFACT-AUDIT-{ProductionRolloutReadinessContracts.GetString(entry, "evidenceId")}")
            .ToArray();

    private static bool IsFailedAuditResult(string auditResult) =>
        auditResult is "failed" or "missing";

    private static ProductionRolloutGeneratedArtifact JsonArtifact(string relativePath, JsonObject content)
    {
        var text = ProductionRolloutReadinessContracts.CanonicalJson(content);
        return new ProductionRolloutGeneratedArtifact(
            relativePath,
            text,
            ProductionRolloutReadinessContracts.Sha256Hex(text),
            "application/json");
    }

    private static ProductionRolloutGeneratedArtifact TextArtifact(string relativePath, string content)
    {
        var normalized = ProductionRolloutReadinessContracts.NormalizeLineEndings(content);
        if (!normalized.EndsWith('\n'))
        {
            normalized += "\n";
        }

        return new ProductionRolloutGeneratedArtifact(
            relativePath,
            normalized,
            ProductionRolloutReadinessContracts.Sha256Hex(normalized),
            "text/markdown");
    }

    private static JsonObject ArtifactRef(ProductionRolloutGeneratedArtifact artifact) =>
        new()
        {
            ["path"] = artifact.RelativePath,
            ["sha256Hash"] = artifact.Sha256Hash,
            ["hashFormat"] = "sha256-hex",
            ["mediaType"] = artifact.MediaType,
            ["sizeBytes"] = Encoding.UTF8.GetByteCount(artifact.Content),
        };
}
