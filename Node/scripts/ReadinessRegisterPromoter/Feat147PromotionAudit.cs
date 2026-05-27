using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReadinessRegisterPromoter;

internal sealed record Feat147AuditArtifact(
    string RelativePath,
    string Content,
    string MediaType);

internal sealed record Feat147GeneratedAuditPackage(
    string SourceId,
    string Status,
    IReadOnlyList<Feat147AuditArtifact> Artifacts);

internal static class Feat147PromotionAudit
{
    public const string DecisionLedgerPath = "feat147-blocker-resolution-decision-ledger.json";
    public const string ArtifactHashAuditPath = "feat147-artifact-hash-audit.json";
    public const string DecisionSummaryPath = "feat147-promotion-decision-summary.md";
    public const string PublicSafeSummaryPath = "feat147-public-safe-promotion-summary.md";
    public const string RestrictedReviewerIndexPath = "feat147-restricted-reviewer-index.json";
    public const string HashValidationPath = "feat147-promotion-hash-validation.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string SourcePath(ReadinessRegisterPromotionPaths paths) => Path.Combine(
        paths.WorkspaceRoot,
        "hush-memory-bank",
        "Overview",
        "HushVotingReadiness",
        "Friendly-Pilot-Readiness-Promotion",
        "examples",
        "release-baseline",
        "feat147-promotion-source.json");

    public static string PackageRoot(ReadinessRegisterPromotionPaths paths) => Path.Combine(
        paths.WorkspaceRoot,
        "hush-documents",
        "PrivateServer_ElectronicVoting",
        "Friendly-Pilot-Readiness-Promotion",
        "package");

    public static Feat147GeneratedAuditPackage? TryGenerate(
        ReadinessRegisterPromotionPaths paths,
        JsonObject promotedRegister,
        DateTimeOffset generatedAt)
    {
        var sourcePath = SourcePath(paths);
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        var source = ReadJsonObject(sourcePath, "FEAT-147 promotion source");
        var errors = ValidateSource(source, promotedRegister);
        if (errors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "FEAT-147 promotion source validation failed.",
                errors);
        }

        var artifactAudit = BuildArtifactAudit(source, paths.WorkspaceRoot, generatedAt);
        var auditEntries = artifactAudit["artifacts"]!.AsArray()
            .Select(node => node!.AsObject())
            .ToArray();
        var decisions = BuildDecisionLedger(source, auditEntries, generatedAt);
        var failedResolvedArtifacts = FindFailedResolvedArtifacts(decisions);
        if (failedResolvedArtifacts.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "FEAT-147 artifact audit failed for a resolving blocker decision.",
                failedResolvedArtifacts);
        }

        var status = auditEntries.Any(entry => GetString(entry, "auditResult") == "failed")
            ? "failed"
            : "passed";
        artifactAudit["status"] = status;

        var sourceId = GetString(source, "sourceId");
        var artifacts = new List<Feat147AuditArtifact>
        {
            JsonArtifact(DecisionLedgerPath, decisions),
            JsonArtifact(ArtifactHashAuditPath, artifactAudit),
            TextArtifact(DecisionSummaryPath, BuildDecisionSummary(source, decisions, artifactAudit, promotedRegister, generatedAt)),
            TextArtifact(PublicSafeSummaryPath, BuildPublicSafeSummary(source, promotedRegister, generatedAt)),
            JsonArtifact(RestrictedReviewerIndexPath, BuildRestrictedIndex(source, decisions, artifactAudit, generatedAt)),
        };

        artifacts.Add(JsonArtifact(
            HashValidationPath,
            BuildHashValidation(sourceId, artifacts, status, generatedAt)));

        return new Feat147GeneratedAuditPackage(
            sourceId,
            status,
            artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray());
    }

    public static void WriteArtifacts(
        string packageRoot,
        IReadOnlyList<Feat147AuditArtifact> artifacts,
        List<string> writtenFiles)
    {
        Directory.CreateDirectory(packageRoot);
        foreach (var artifact in artifacts.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            var path = Path.GetFullPath(Path.Combine(packageRoot, artifact.RelativePath));
            EnsureContained(packageRoot, path, artifact.RelativePath);
            File.WriteAllText(path, artifact.Content, new UTF8Encoding(false));
            writtenFiles.Add(path);
        }
    }

    public static IReadOnlyList<string> ValidateExistingArtifacts(
        string packageRoot,
        IReadOnlyList<Feat147AuditArtifact> artifacts)
    {
        var errors = new List<string>();
        foreach (var artifact in artifacts.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            var path = Path.Combine(packageRoot, artifact.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Missing FEAT-147 audit artifact: {artifact.RelativePath}");
                continue;
            }

            var actual = NormalizeLineEndings(File.ReadAllText(path));
            var expected = NormalizeLineEndings(artifact.Content);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                errors.Add($"FEAT-147 audit artifact mismatch: {artifact.RelativePath}");
            }
        }

        return errors;
    }

    private static List<string> ValidateSource(JsonObject source, JsonObject promotedRegister)
    {
        var errors = new List<string>();
        if (GetString(source, "schemaVersion") != "feat147-promotion-source.v1")
        {
            errors.Add("schemaVersion must be feat147-promotion-source.v1.");
        }

        if (string.IsNullOrWhiteSpace(GetString(source, "sourceId")))
        {
            errors.Add("sourceId is required.");
        }

        var target = RequireObject(source, "targetRegister", errors);
        if (target is not null)
        {
            if (GetString(target, "registerVersion") != GetString(promotedRegister, "registerVersion"))
            {
                errors.Add("targetRegister.registerVersion must match promoted register version.");
            }

            if (GetString(target, "registerVersionId") != GetString(promotedRegister, "registerVersionId"))
            {
                errors.Add("targetRegister.registerVersionId must match promoted register version id.");
            }

            var targetScore = GetInt(target, "targetTotalScore");
            var promotedScore = GetInt(RequireObject(promotedRegister, "score", errors), "total");
            if (targetScore != promotedScore)
            {
                errors.Add($"targetRegister.targetTotalScore must match promoted score total {promotedScore}.");
            }

            var expectedClaim = GetString(target, "strongestAllowedClaim");
            var promotedClaim = GetStrongestAllowedClaim(promotedRegister);
            if (expectedClaim != promotedClaim)
            {
                errors.Add($"targetRegister.strongestAllowedClaim must match promoted strongest claim {promotedClaim}.");
            }
        }

        var decisions = RequireArray(source, "blockerDecisions", errors);
        var blockers = RequireArray(promotedRegister, "blockers", errors);
        if (decisions is null || blockers is null)
        {
            return errors;
        }

        var decisionById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var decision in decisions.Select((node, index) => (node, index)))
        {
            if (decision.node is not JsonObject item)
            {
                errors.Add($"blockerDecisions[{decision.index}] must be an object.");
                continue;
            }

            var blockerId = GetString(item, "blockerId");
            if (string.IsNullOrWhiteSpace(blockerId))
            {
                errors.Add($"blockerDecisions[{decision.index}].blockerId is required.");
                continue;
            }

            if (!decisionById.TryAdd(blockerId, item))
            {
                errors.Add($"Duplicate FEAT-147 blocker decision: {blockerId}");
            }

            var decisionValue = GetString(item, "decision");
            if (decisionValue is not ("resolve" or "keep_open" or "keep_policy_blocked" or "keep_limited"))
            {
                errors.Add($"{blockerId}.decision is invalid.");
            }

            RequireArray(item, "evidenceRefs", errors);
            RequireObject(item, "scoreImpact", errors);
            RequireObject(item, "signoffs", errors);
            RequireNonEmpty(item, "decisionReason", errors);
            RequireNonEmpty(item, "claimImpact", errors);
            RequireNonEmpty(item, "residualRisk", errors);
        }

        foreach (var blocker in blockers.Select(node => node!.AsObject()))
        {
            var blockerId = GetString(blocker, "blockerId");
            if (!decisionById.TryGetValue(blockerId, out var decision))
            {
                errors.Add($"Missing FEAT-147 blocker decision for {blockerId}.");
                continue;
            }

            if (GetString(blocker, "severity") != GetString(decision, "proposedSeverity") ||
                GetString(blocker, "status") != GetString(decision, "proposedStatus"))
            {
                errors.Add($"{blockerId} promoted severity/status must match FEAT-147 proposed state.");
            }
        }

        return errors;
    }

    private static JsonObject BuildArtifactAudit(JsonObject source, string workspaceRoot, DateTimeOffset generatedAt)
    {
        var entries = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decision in ArrayOrEmpty(source, "blockerDecisions").Select(node => node!.AsObject()))
        {
            foreach (var evidenceRef in ArrayOrEmpty(decision, "evidenceRefs").Select(node => node!.AsObject()))
            {
                var artifactId = GetString(evidenceRef, "artifactId");
                var relativePath = GetString(evidenceRef, "relativePath");
                var dedupeKey = $"{artifactId}|{relativePath}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                var expectedHash = GetString(evidenceRef, "sha256Hash");
                var auditResult = "failed";
                string observedHash = "";
                long sizeBytes = 0;
                string reason;
                if (relativePath.StartsWith("restricted-ref://", StringComparison.Ordinal))
                {
                    auditResult = "hash_only_accepted";
                    observedHash = expectedHash;
                    reason = "Restricted external evidence cannot be read locally; declared hash retained.";
                }
                else
                {
                    var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
                    if (!fullPath.StartsWith(Path.GetFullPath(workspaceRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        reason = "Path escapes workspace root.";
                    }
                    else if (!File.Exists(fullPath))
                    {
                        reason = "Referenced file is missing.";
                    }
                    else
                    {
                        var bytes = File.ReadAllBytes(fullPath);
                        observedHash = ComputeSha256Hex(bytes);
                        sizeBytes = bytes.Length;
                        auditResult = string.Equals(observedHash, expectedHash, StringComparison.Ordinal)
                            ? "passed"
                            : "failed";
                        reason = auditResult == "passed"
                            ? "Observed SHA-256 matches expected SHA-256."
                            : "Observed SHA-256 does not match expected SHA-256.";
                    }
                }

                entries.Add(new JsonObject
                {
                    ["artifactId"] = artifactId,
                    ["evidenceId"] = GetString(evidenceRef, "evidenceId"),
                    ["featureSlice"] = GetString(evidenceRef, "featureSlice"),
                    ["relativePath"] = relativePath,
                    ["expectedSha256Hash"] = expectedHash,
                    ["observedSha256Hash"] = observedHash,
                    ["hashAlgorithm"] = GetString(evidenceRef, "hashAlgorithm"),
                    ["mediaType"] = GetString(evidenceRef, "mediaType"),
                    ["visibility"] = GetString(evidenceRef, "visibility"),
                    ["sizeBytes"] = sizeBytes,
                    ["auditResult"] = auditResult,
                    ["reason"] = reason,
                });
            }
        }

        return new JsonObject
        {
            ["schemaVersion"] = "feat147-artifact-hash-audit.v1",
            ["auditId"] = "FEAT147-ARTIFACT-HASH-AUDIT-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["status"] = "passed",
            ["artifacts"] = entries,
        };
    }

    private static JsonObject BuildDecisionLedger(
        JsonObject source,
        IReadOnlyCollection<JsonObject> auditEntries,
        DateTimeOffset generatedAt)
    {
        var auditByArtifact = auditEntries.ToDictionary(
            entry => GetString(entry, "artifactId"),
            StringComparer.Ordinal);
        var decisions = new JsonArray();
        foreach (var decision in ArrayOrEmpty(source, "blockerDecisions").Select(node => node!.AsObject()))
        {
            var evidenceRefs = new JsonArray();
            var decisionAuditResults = new List<string>();
            foreach (var evidenceRef in ArrayOrEmpty(decision, "evidenceRefs").Select(node => node!.AsObject()))
            {
                var artifactId = GetString(evidenceRef, "artifactId");
                var audit = auditByArtifact.GetValueOrDefault(artifactId);
                var auditResult = audit is null ? "not_required" : GetString(audit, "auditResult");
                decisionAuditResults.Add(auditResult);
                evidenceRefs.Add(new JsonObject
                {
                    ["artifactId"] = artifactId,
                    ["evidenceId"] = GetString(evidenceRef, "evidenceId"),
                    ["featureSlice"] = GetString(evidenceRef, "featureSlice"),
                    ["relativePath"] = GetString(evidenceRef, "relativePath"),
                    ["expectedSha256Hash"] = GetString(evidenceRef, "sha256Hash"),
                    ["observedSha256Hash"] = audit is null ? "" : GetString(audit, "observedSha256Hash"),
                    ["status"] = "accepted",
                    ["freshness"] = GetString(evidenceRef, "freshness"),
                    ["artifactAuditResult"] = auditResult,
                });
            }

            decisions.Add(new JsonObject
            {
                ["blockerId"] = GetString(decision, "blockerId"),
                ["currentSeverity"] = GetString(decision, "currentSeverity"),
                ["currentStatus"] = GetString(decision, "currentStatus"),
                ["proposedSeverity"] = GetString(decision, "proposedSeverity"),
                ["proposedStatus"] = GetString(decision, "proposedStatus"),
                ["featureSlice"] = GetString(decision, "featureSlice"),
                ["acceptanceGateIds"] = Clone(decision["acceptanceGateIds"]),
                ["dimensionIds"] = Clone(decision["dimensionIds"]),
                ["decision"] = GetString(decision, "decision"),
                ["decisionReason"] = GetString(decision, "decisionReason"),
                ["evidenceRefs"] = evidenceRefs,
                ["artifactAuditResult"] = CollapseAuditResult(decisionAuditResults),
                ["scoreImpact"] = Clone(decision["scoreImpact"]),
                ["claimImpact"] = GetString(decision, "claimImpact"),
                ["residualRisk"] = GetString(decision, "residualRisk"),
                ["signoffs"] = Clone(decision["signoffs"]),
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "feat147-blocker-resolution-decision-ledger.v1",
            ["ledgerId"] = "FEAT147-BLOCKER-RESOLUTION-LEDGER-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["baselineRegister"] = Clone(source["baselineRegister"]),
            ["targetRegister"] = Clone(source["targetRegister"]),
            ["decisions"] = decisions,
        };
    }

    private static IReadOnlyList<string> FindFailedResolvedArtifacts(JsonObject decisionLedger)
    {
        var errors = new List<string>();
        foreach (var decision in ArrayOrEmpty(decisionLedger, "decisions").Select(node => node!.AsObject()))
        {
            if (GetString(decision, "decision") != "resolve")
            {
                continue;
            }

            var auditResult = GetString(decision, "artifactAuditResult");
            if (auditResult == "failed")
            {
                errors.Add($"{GetString(decision, "blockerId")} has failed artifact audit.");
            }
        }

        return errors;
    }

    private static JsonObject BuildRestrictedIndex(
        JsonObject source,
        JsonObject decisionLedger,
        JsonObject artifactAudit,
        DateTimeOffset generatedAt)
    {
        var refs = new JsonArray();
        foreach (var artifact in ArrayOrEmpty(artifactAudit, "artifacts").Select(node => node!.AsObject()))
        {
            refs.Add(new JsonObject
            {
                ["artifactId"] = GetString(artifact, "artifactId"),
                ["featureSlice"] = GetString(artifact, "featureSlice"),
                ["relativePath"] = GetString(artifact, "relativePath"),
                ["sha256Hash"] = GetString(artifact, "expectedSha256Hash"),
                ["auditResult"] = GetString(artifact, "auditResult"),
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "feat147-restricted-reviewer-index.v1",
            ["indexId"] = "FEAT147-RESTRICTED-REVIEWER-INDEX-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["restrictedPayloadsExcluded"] = true,
            ["decisionLedgerRef"] = new JsonObject
            {
                ["path"] = DecisionLedgerPath,
                ["decisionCount"] = ArrayOrEmpty(decisionLedger, "decisions").Count,
            },
            ["artifactRefs"] = refs,
        };
    }

    private static JsonObject BuildHashValidation(
        string sourceId,
        IReadOnlyList<Feat147AuditArtifact> artifacts,
        string status,
        DateTimeOffset generatedAt)
    {
        var hashes = new JsonArray();
        foreach (var artifact in artifacts.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            hashes.Add(new JsonObject
            {
                ["path"] = artifact.RelativePath,
                ["sha256Hash"] = ComputeSha256Hex(EncodingWithoutBom(artifact.Content)),
                ["hashFormat"] = "sha256-hex",
                ["mediaType"] = artifact.MediaType,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "feat147-promotion-hash-validation.v1",
            ["validationId"] = "FEAT147-PROMOTION-HASH-VALIDATION-001",
            ["sourceId"] = sourceId,
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["status"] = status,
            ["generatedArtifactHashes"] = hashes,
        };
    }

    private static string BuildDecisionSummary(
        JsonObject source,
        JsonObject decisionLedger,
        JsonObject artifactAudit,
        JsonObject promotedRegister,
        DateTimeOffset generatedAt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- Generated by ReadinessRegisterPromoter. Do not edit by hand. -->");
        sb.AppendLine();
        sb.AppendLine("# FEAT-147 Promotion Decision Summary");
        sb.AppendLine();
        sb.AppendLine($"Generated at: {FormatTimestamp(generatedAt)}");
        sb.AppendLine($"Source: {GetString(source, "sourceId")}");
        sb.AppendLine($"Target register: {GetString(promotedRegister, "registerVersionId")}");
        sb.AppendLine($"Promoted score: {GetInt(ObjectOrEmpty(promotedRegister, "score"), "total")}/100");
        sb.AppendLine($"Strongest allowed claim: {GetStrongestAllowedClaim(promotedRegister)}");
        sb.AppendLine();
        sb.AppendLine("## Decision Counts");
        sb.AppendLine();
        foreach (var group in ArrayOrEmpty(decisionLedger, "decisions")
            .Select(node => node!.AsObject())
            .GroupBy(decision => GetString(decision, "decision"))
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"- {group.Key}: {group.Count()}");
        }

        sb.AppendLine();
        sb.AppendLine("## Blocker Decisions");
        sb.AppendLine();
        foreach (var decision in ArrayOrEmpty(decisionLedger, "decisions").Select(node => node!.AsObject()))
        {
            sb.AppendLine($"- `{GetString(decision, "blockerId")}`: {GetString(decision, "currentSeverity")}/{GetString(decision, "currentStatus")} -> {GetString(decision, "proposedSeverity")}/{GetString(decision, "proposedStatus")} ({GetString(decision, "decision")})");
        }

        sb.AppendLine();
        sb.AppendLine("## Artifact Audit");
        sb.AppendLine();
        sb.AppendLine($"Status: {GetString(artifactAudit, "status")}");
        foreach (var artifact in ArrayOrEmpty(artifactAudit, "artifacts").Select(node => node!.AsObject()))
        {
            sb.AppendLine($"- `{GetString(artifact, "artifactId")}`: {GetString(artifact, "auditResult")}");
        }

        return NormalizeLineEndings(sb.ToString());
    }

    private static string BuildPublicSafeSummary(
        JsonObject source,
        JsonObject promotedRegister,
        DateTimeOffset generatedAt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- Generated by ReadinessRegisterPromoter. Do not edit by hand. -->");
        sb.AppendLine();
        sb.AppendLine("# FEAT-147 Public-Safe Promotion Summary");
        sb.AppendLine();
        sb.AppendLine($"Generated at: {FormatTimestamp(generatedAt)}");
        sb.AppendLine($"Register: {GetString(promotedRegister, "registerVersionId")}");
        sb.AppendLine();
        sb.AppendLine("## Current Public-Safe Status");
        sb.AppendLine();
        sb.AppendLine("HushVoting has enough promoted internal evidence for controlled friendly-organization pilot planning when the pilot remains explicitly bounded and reviewed through private readiness materials.");
        sb.AppendLine();
        sb.AppendLine("## Approved Public-Safe Claim Wording");
        sb.AppendLine();
        sb.AppendLine("HushVoting may be discussed for controlled friendly-organization pilot use with explicit limitations. It is not presented as production rollout software, public/state election software, legal sufficiency validation, or independent certification.");
        sb.AppendLine();
        sb.AppendLine("## Known Limitations");
        sb.AppendLine();
        sb.AppendLine("- The promoted evidence is internal and controlled-scope evidence.");
        sb.AppendLine("- Broader operating history, deployment variance, failed-finalize coverage, accessibility/device breadth, and customer-specific governance remain limitations.");
        sb.AppendLine("- Production organizational rollout and public/state election claims remain unavailable.");
        sb.AppendLine();
        sb.AppendLine("## Non-Claims");
        sb.AppendLine();
        sb.AppendLine("- This summary is not certification, legal approval, public election authorization, independent validation, or AGM software validation.");
        sb.AppendLine("- This summary does not publish private readiness scoring or restricted evidence.");
        return NormalizeLineEndings(sb.ToString());
    }

    private static Feat147AuditArtifact JsonArtifact(string relativePath, JsonNode node) =>
        new(relativePath, SerializeJson(node), "application/json");

    private static Feat147AuditArtifact TextArtifact(string relativePath, string content) =>
        new(relativePath, NormalizeLineEndings(content) + (content.EndsWith('\n') ? "" : "\n"), "text/markdown");

    private static JsonObject ReadJsonObject(string path, string displayName)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new JsonException("Root is not an object.");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new ReadinessRegisterPromotionException(
                $"Could not read {displayName}.",
                [$"{path}: {ex.Message}"]);
        }
    }

    private static JsonObject? RequireObject(JsonObject? item, string propertyName, List<string> errors)
    {
        if (item is not null && item[propertyName] is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"{propertyName} must be an object.");
        return null;
    }

    private static JsonArray? RequireArray(JsonObject? item, string propertyName, List<string> errors)
    {
        if (item is not null && item[propertyName] is JsonArray array)
        {
            return array;
        }

        errors.Add($"{propertyName} must be an array.");
        return null;
    }

    private static void RequireNonEmpty(JsonObject item, string propertyName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(GetString(item, propertyName)))
        {
            errors.Add($"{propertyName} is required.");
        }
    }

    private static string CollapseAuditResult(IReadOnlyList<string> results)
    {
        if (results.Count == 0)
        {
            return "not_required";
        }

        if (results.Contains("failed", StringComparer.Ordinal))
        {
            return "failed";
        }

        if (results.Contains("hash_only_accepted", StringComparer.Ordinal))
        {
            return "hash_only_accepted";
        }

        if (results.Contains("source_controlled", StringComparer.Ordinal))
        {
            return "source_controlled";
        }

        return "passed";
    }

    private static string GetStrongestAllowedClaim(JsonObject register)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["internal_development"] = 0,
            ["internal_non_binding_rehearsal"] = 1,
            ["friendly_organization_pilot"] = 2,
            ["production_organizational_rollout"] = 3,
            ["public_or_state_election"] = 4,
        };
        return ArrayOrEmpty(register, "claimLevels")
            .Select(node => node!.AsObject())
            .Where(claim =>
                GetString(claim, "status") is "allowed" or "allowed_with_limitations" &&
                GetString(claim, "blockerSeverity") != "red")
            .OrderByDescending(claim => rank.GetValueOrDefault(GetString(claim, "claimLevel"), -1))
            .Select(claim => GetString(claim, "claimLevel"))
            .FirstOrDefault() ?? "none";
    }

    private static void EnsureContained(string root, string child, string label)
    {
        var fullRoot = Path.GetFullPath(root);
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!child.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReadinessRegisterPromotionException(
                "FEAT-147 audit output path escapes package root.",
                [label]);
        }
    }

    private static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    private static JsonArray ArrayOrEmpty(JsonObject item, string propertyName) =>
        item[propertyName] as JsonArray ?? [];

    private static JsonObject ObjectOrEmpty(JsonObject item, string propertyName) =>
        item[propertyName] as JsonObject ?? new JsonObject();

    private static string GetString(JsonObject? item, string propertyName) =>
        item is not null && item[propertyName] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : string.Empty;

    private static int GetInt(JsonObject? item, string propertyName) =>
        item is not null && item[propertyName] is JsonValue value && value.TryGetValue<int>(out var result)
            ? result
            : 0;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static string SerializeJson(JsonNode node) => NormalizeLineEndings(node.ToJsonString(JsonOptions)) + "\n";

    private static byte[] EncodingWithoutBom(string value) => new UTF8Encoding(false).GetBytes(NormalizeLineEndings(value));

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static string ComputeSha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
