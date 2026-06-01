using System.Text;
using System.Text.Json.Nodes;

namespace InternalAudit95ProtocolTraceabilityPromoter;

public static class InternalAudit95ProtocolTraceabilityArtifactGenerator
{
    public const string SourceSnapshotPath = "feat157-traceability-source-snapshot.json";
    public const string TraceMatrixPath = "feat157-auditor-trace-matrix.json";
    public const string ArtifactInventoryPath = "feat157-artifact-inventory.json";
    public const string StaleReferenceValidationPath = "feat157-stale-reference-validation.json";
    public const string OrphanArtifactReportPath = "feat157-orphan-artifact-report.json";

    public static readonly string[] RequiredArtifactPaths =
    [
        ArtifactInventoryPath,
        OrphanArtifactReportPath,
        SourceSnapshotPath,
        StaleReferenceValidationPath,
        TraceMatrixPath,
    ];

    public static InternalAudit95ProtocolTraceabilityGeneratedPackage Generate(
        InternalAudit95ProtocolTraceabilityPaths paths,
        string? sourceInput = null,
        DateTimeOffset? generatedAt = null)
    {
        var source = InternalAudit95ProtocolTraceabilityContracts.LoadSource(paths, sourceInput);
        var validationErrors = InternalAudit95ProtocolTraceabilityContracts.ValidateSource(source);
        if (validationErrors.Count > 0)
        {
            throw new InternalAudit95ProtocolTraceabilityException(
                "FEAT-157 protocol traceability source validation failed.",
                validationErrors);
        }

        var effectiveGeneratedAt = generatedAt ?? DateTimeOffset.UtcNow;
        var traceMatrix = BuildTraceMatrix(source, effectiveGeneratedAt);
        var inventory = BuildArtifactInventory(source, effectiveGeneratedAt);
        var staleValidation = BuildStaleReferenceValidation(source, paths.WorkspaceRoot, effectiveGeneratedAt);
        var orphanReport = BuildOrphanArtifactReport(source, effectiveGeneratedAt);
        var blockers = CollectBlockers(staleValidation, orphanReport);
        var diagnostics = CollectDiagnostics(staleValidation, orphanReport);
        var packageStatus = blockers.Count == 0 ? "accepted_candidate" : "blocked";

        var artifacts = new[]
        {
            JsonArtifact(SourceSnapshotPath, BuildSourceSnapshot(source, effectiveGeneratedAt)),
            JsonArtifact(TraceMatrixPath, traceMatrix),
            JsonArtifact(ArtifactInventoryPath, inventory),
            JsonArtifact(StaleReferenceValidationPath, staleValidation),
            JsonArtifact(OrphanArtifactReportPath, orphanReport),
        }
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new InternalAudit95ProtocolTraceabilityGeneratedPackage(
            packageStatus,
            artifacts,
            blockers,
            diagnostics);
    }

    private static JsonObject BuildSourceSnapshot(JsonObject source, DateTimeOffset generatedAt)
    {
        var snapshot = source.DeepClone().AsObject();
        snapshot["snapshotSchemaVersion"] = "feat157-traceability-source-snapshot.v1";
        snapshot["snapshotGeneratedAt"] = InternalAudit95ProtocolTraceabilityContracts.FormatTimestamp(generatedAt);
        snapshot["canonicalizationVersion"] = InternalAudit95ProtocolTraceabilityContracts.CanonicalizationVersion;
        return snapshot;
    }

    private static JsonObject BuildTraceMatrix(JsonObject source, DateTimeOffset generatedAt)
    {
        var artifactsById = InternalAudit95ProtocolTraceabilityContracts.RequireArray(source, "sourceArtifacts")
            .OfType<JsonObject>()
            .ToDictionary(item => InternalAudit95ProtocolTraceabilityContracts.GetString(item, "artifactId"), StringComparer.Ordinal);
        var rows = new List<JsonObject>();

        foreach (var requirement in InternalAudit95ProtocolTraceabilityContracts.RequireArray(source, "traceRequirements").OfType<JsonObject>())
        {
            var requirementId = InternalAudit95ProtocolTraceabilityContracts.GetString(requirement, "traceRequirementId");
            foreach (var artifactId in InternalAudit95ProtocolTraceabilityContracts.GetStringArray(requirement, "sourceArtifactIds"))
            {
                if (!artifactsById.TryGetValue(artifactId, out var artifact))
                {
                    continue;
                }

                rows.Add(new JsonObject
                {
                    ["traceId"] = $"{requirementId}:{artifactId}",
                    ["claimLevel"] = InternalAudit95ProtocolTraceabilityContracts.GetString(requirement, "claimLevel"),
                    ["dimensionId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(requirement, "dimensionId"),
                    ["blockerId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(requirement, "blockerId"),
                    ["acceptanceGateIds"] = InternalAudit95ProtocolTraceabilityContracts.Clone(requirement["acceptanceGateIds"]),
                    ["sourceRequirement"] = InternalAudit95ProtocolTraceabilityContracts.GetString(requirement, "sourceRequirement"),
                    ["sourceFeatureId"] = InternalAudit95ProtocolTraceabilityContracts.GetStringArray(requirement, "sourceFeatureIds").FirstOrDefault() ?? InternalAudit95ProtocolTraceabilityContracts.FeatureId,
                    ["artifactRef"] = InternalAudit95ProtocolTraceabilityContracts.GetString(artifact, "logicalRef"),
                    ["artifactHash"] = InternalAudit95ProtocolTraceabilityContracts.GetString(artifact, "expectedSha256Hash"),
                    ["releaseScope"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "packageAnchor"),
                    ["visibility"] = InternalAudit95ProtocolTraceabilityContracts.GetString(artifact, "visibility"),
                    ["freshnessCheck"] = "pending_validation",
                    ["invalidationTriggers"] = InternalAudit95ProtocolTraceabilityContracts.Clone(artifact["staleWhen"]),
                    ["residualRisk"] = "Trace row proves release-bound reference integrity only; it does not prove external review, certification, legal sufficiency, or downstream feature completion.",
                });
            }
        }

        return new JsonObject
        {
            ["schemaVersion"] = "feat157-auditor-trace-matrix.v1",
            ["sourceId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = InternalAudit95ProtocolTraceabilityContracts.FormatTimestamp(generatedAt),
            ["rows"] = new JsonArray(rows.OrderBy(row => row["traceId"]!.GetValue<string>(), StringComparer.Ordinal).ToArray<JsonNode?>()),
        };
    }

    private static JsonObject BuildArtifactInventory(JsonObject source, DateTimeOffset generatedAt)
    {
        var sourceEntries = InternalAudit95ProtocolTraceabilityContracts.RequireArray(source, "sourceArtifacts")
            .OfType<JsonObject>()
            .Select(item => new JsonObject
            {
                ["artifactId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "artifactId"),
                ["path"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "resolvedPath"),
                ["sha256Hash"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "expectedSha256Hash"),
                ["visibility"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "visibility"),
                ["classification"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "classification"),
                ["releaseScope"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "packageAnchor"),
                ["inventorySource"] = "sourceArtifact",
            });
        var generatedEntries = InternalAudit95ProtocolTraceabilityContracts.RequireArray(source, "generatedArtifactContracts")
            .OfType<JsonObject>()
            .Select(item => new JsonObject
            {
                ["artifactId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "artifactId"),
                ["path"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "fileName"),
                ["sha256Hash"] = "pending-generated",
                ["visibility"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "visibility"),
                ["classification"] = InternalAudit95ProtocolTraceabilityContracts.GetString(item, "classification"),
                ["releaseScope"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "packageAnchor"),
                ["inventorySource"] = "generatedArtifactContract",
            });

        return new JsonObject
        {
            ["schemaVersion"] = "feat157-artifact-inventory.v1",
            ["sourceId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = InternalAudit95ProtocolTraceabilityContracts.FormatTimestamp(generatedAt),
            ["entries"] = new JsonArray(sourceEntries.Concat(generatedEntries)
                .OrderBy(item => item["artifactId"]!.GetValue<string>(), StringComparer.Ordinal)
                .ToArray<JsonNode?>()),
        };
    }

    private static JsonObject BuildStaleReferenceValidation(JsonObject source, string workspaceRoot, DateTimeOffset generatedAt)
    {
        var checks = InternalAudit95ProtocolTraceabilityContracts.RequireArray(source, "sourceArtifacts")
            .OfType<JsonObject>()
            .Select(item => BuildStaleReferenceCheck(item, workspaceRoot))
            .OrderBy(item => item["checkId"]!.GetValue<string>(), StringComparer.Ordinal)
            .ToArray<JsonNode?>();
        var failedCount = checks.OfType<JsonObject>().Count(item => item["status"]!.GetValue<string>() == "failed" && item["blocksScoreMovement"]!.GetValue<bool>());

        return new JsonObject
        {
            ["schemaVersion"] = "feat157-stale-reference-validation.v1",
            ["sourceId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = InternalAudit95ProtocolTraceabilityContracts.FormatTimestamp(generatedAt),
            ["status"] = failedCount == 0 ? "passed" : "failed",
            ["checks"] = new JsonArray(checks),
        };
    }

    private static JsonObject BuildStaleReferenceCheck(JsonObject artifact, string workspaceRoot)
    {
        var artifactId = InternalAudit95ProtocolTraceabilityContracts.GetString(artifact, "artifactId");
        var relativePath = InternalAudit95ProtocolTraceabilityContracts.GetString(artifact, "resolvedPath");
        var expectedHash = InternalAudit95ProtocolTraceabilityContracts.NormalizeHash(InternalAudit95ProtocolTraceabilityContracts.GetString(artifact, "expectedSha256Hash"));
        var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
        var fullRoot = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var blocksScore = InternalAudit95ProtocolTraceabilityContracts.GetBool(artifact, "requiredForScore");
        var status = "passed";
        var observedHash = "";
        var diagnostic = "Observed SHA-256 matches expected SHA-256.";

        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            status = "failed";
            diagnostic = "Resolved path escapes workspace root.";
        }
        else if (!File.Exists(fullPath))
        {
            status = "failed";
            diagnostic = "Resolved path does not exist.";
        }
        else
        {
            observedHash = InternalAudit95ProtocolTraceabilityContracts.Sha256Hex(File.ReadAllBytes(fullPath));
            if (!string.Equals(observedHash, expectedHash, StringComparison.Ordinal))
            {
                status = "failed";
                diagnostic = "Observed SHA-256 does not match expected SHA-256.";
            }
        }

        return new JsonObject
        {
            ["checkId"] = $"FEAT157-STALE-REF-{artifactId}",
            ["artifactId"] = artifactId,
            ["path"] = relativePath,
            ["expectedSha256Hash"] = expectedHash,
            ["observedSha256Hash"] = observedHash,
            ["status"] = status,
            ["blocksScoreMovement"] = blocksScore,
            ["failureCode"] = status == "passed" ? "" : "FEAT157_BASELINE_REGISTER_INVALID",
            ["diagnostic"] = diagnostic,
        };
    }

    private static JsonObject BuildOrphanArtifactReport(JsonObject source, DateTimeOffset generatedAt)
    {
        var referencedSourceIds = new HashSet<string>(StringComparer.Ordinal);
        var referencedGeneratedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trace in InternalAudit95ProtocolTraceabilityContracts.RequireArray(source, "traceRequirements").OfType<JsonObject>())
        {
            foreach (var id in InternalAudit95ProtocolTraceabilityContracts.GetStringArray(trace, "sourceArtifactIds"))
            {
                referencedSourceIds.Add(id);
            }

            foreach (var id in InternalAudit95ProtocolTraceabilityContracts.GetStringArray(trace, "requiredGeneratedArtifactIds"))
            {
                referencedGeneratedIds.Add(id);
            }
        }

        var checks = new List<JsonObject>();
        foreach (var artifact in InternalAudit95ProtocolTraceabilityContracts.RequireArray(source, "sourceArtifacts").OfType<JsonObject>())
        {
            var artifactId = InternalAudit95ProtocolTraceabilityContracts.GetString(artifact, "artifactId");
            var classification = InternalAudit95ProtocolTraceabilityContracts.GetString(artifact, "classification");
            checks.Add(BuildOrphanCheck(
                artifactId,
                "sourceArtifact",
                referencedSourceIds.Contains(artifactId) || IsAllowedUnreferencedClassification(classification),
                classification));
        }

        foreach (var artifact in InternalAudit95ProtocolTraceabilityContracts.RequireArray(source, "generatedArtifactContracts").OfType<JsonObject>())
        {
            var artifactId = InternalAudit95ProtocolTraceabilityContracts.GetString(artifact, "artifactId");
            var classification = InternalAudit95ProtocolTraceabilityContracts.GetString(artifact, "classification");
            checks.Add(BuildOrphanCheck(
                artifactId,
                "generatedArtifactContract",
                referencedGeneratedIds.Contains(artifactId) || IsAllowedUnreferencedClassification(classification),
                classification));
        }

        var failedCount = checks.Count(item => item["status"]!.GetValue<string>() == "failed");

        return new JsonObject
        {
            ["schemaVersion"] = "feat157-orphan-artifact-report.v1",
            ["sourceId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = InternalAudit95ProtocolTraceabilityContracts.FormatTimestamp(generatedAt),
            ["status"] = failedCount == 0 ? "passed" : "failed",
            ["checks"] = new JsonArray(checks.OrderBy(item => item["artifactId"]!.GetValue<string>(), StringComparer.Ordinal).ToArray<JsonNode?>()),
        };
    }

    private static JsonObject BuildOrphanCheck(string artifactId, string artifactKind, bool passed, string classification) =>
        new()
        {
            ["checkId"] = $"FEAT157-ORPHAN-{artifactId}",
            ["artifactId"] = artifactId,
            ["artifactKind"] = artifactKind,
            ["classification"] = classification,
            ["status"] = passed ? "passed" : "failed",
            ["blocksScoreMovement"] = !passed,
            ["failureCode"] = passed ? "" : "FEAT157_ORPHAN_ARTIFACT",
            ["diagnostic"] = passed
                ? "Artifact is traced or explicitly allowed as non-score/drift/superseded."
                : "Artifact is not traced and is not explicitly classified as a permitted non-score artifact.",
        };

    private static bool IsAllowedUnreferencedClassification(string classification) =>
        classification is "supporting" or "restricted-reviewer" or "public-safe-summary" or "non-score" or "superseded" or "drift-check-only";

    private static IReadOnlyList<string> CollectBlockers(params JsonObject[] reports) =>
        reports
            .SelectMany(report => InternalAudit95ProtocolTraceabilityContracts.RequireArray(report, "checks").OfType<JsonObject>())
            .Where(check => check["status"]!.GetValue<string>() == "failed" && check["blocksScoreMovement"]!.GetValue<bool>())
            .Select(check => InternalAudit95ProtocolTraceabilityContracts.GetString(check, "failureCode", InternalAudit95ProtocolTraceabilityContracts.GetString(check, "checkId")))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> CollectDiagnostics(params JsonObject[] reports) =>
        reports
            .SelectMany(report => InternalAudit95ProtocolTraceabilityContracts.RequireArray(report, "checks").OfType<JsonObject>())
            .Where(check => check["status"]!.GetValue<string>() == "failed")
            .Select(check => InternalAudit95ProtocolTraceabilityContracts.GetString(check, "diagnostic"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static InternalAudit95ProtocolTraceabilityGeneratedArtifact JsonArtifact(string relativePath, JsonObject content)
    {
        var text = InternalAudit95ProtocolTraceabilityContracts.CanonicalJson(content);
        return new InternalAudit95ProtocolTraceabilityGeneratedArtifact(
            relativePath,
            text,
            InternalAudit95ProtocolTraceabilityContracts.Sha256Hex(text),
            "application/json");
    }

    public static InternalAudit95ProtocolTraceabilityGeneratedArtifact TextArtifact(string relativePath, string content)
    {
        var normalized = InternalAudit95ProtocolTraceabilityContracts.NormalizeLineEndings(content);
        if (!normalized.EndsWith('\n'))
        {
            normalized += "\n";
        }

        return new InternalAudit95ProtocolTraceabilityGeneratedArtifact(
            relativePath,
            normalized,
            InternalAudit95ProtocolTraceabilityContracts.Sha256Hex(normalized),
            "text/markdown");
    }
}
