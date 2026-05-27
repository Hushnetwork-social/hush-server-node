using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReadinessRegisterPromoter;

internal sealed record Feat150CleanupArtifact(
    string RelativePath,
    string Content,
    string MediaType);

internal sealed record Feat150GeneratedCleanupPackage(
    string SourceId,
    string Status,
    IReadOnlyList<Feat150CleanupArtifact> Artifacts);

internal static class Feat150CleanupAudit
{
    public const string DecisionLedgerPath = "feat150-blocker-cleanup-decision-ledger.json";
    public const string GeneratedViewConsistencyPath = "feat150-generated-view-consistency-check.json";
    public const string PublicSafeScanPath = "feat150-public-safe-scan.json";
    public const string ArtifactHashAuditPath = "feat150-artifact-hash-audit.json";
    public const string DecisionSummaryPath = "feat150-cleanup-decision-summary.md";

    private const string TargetBlockerId = "RDY-BLOCK-INTERNAL_NON_BINDING_REHEARSAL-001";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string SourcePath(ReadinessRegisterPromotionPaths paths) => Path.Combine(
        paths.WorkspaceRoot,
        "hush-memory-bank",
        "Overview",
        "HushVotingReadiness",
        "Internal-Non-Binding-Rehearsal-Cleanup",
        "examples",
        "release-baseline",
        "feat150-cleanup-source.json");

    public static string PackageRoot(ReadinessRegisterPromotionPaths paths) => Path.Combine(
        paths.WorkspaceRoot,
        "hush-documents",
        "PrivateServer_ElectronicVoting",
        "Internal-Non-Binding-Rehearsal-Cleanup",
        "package");

    public static Feat150GeneratedCleanupPackage? TryGenerate(
        ReadinessRegisterPromotionPaths paths,
        JsonObject promotedRegister,
        DateTimeOffset generatedAt,
        string registerJson,
        string scorecardMarkdown,
        string restrictedReviewerMarkdown,
        string publicSafeSummaryMarkdown)
    {
        var sourcePath = SourcePath(paths);
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        var source = ReadJsonObject(sourcePath, "FEAT-150 cleanup source");
        var errors = ValidateSource(source, promotedRegister);
        var consistencyCheck = BuildGeneratedViewConsistencyCheck(
            source,
            promotedRegister,
            registerJson,
            scorecardMarkdown,
            restrictedReviewerMarkdown,
            publicSafeSummaryMarkdown,
            generatedAt);
        var publicSafeScan = BuildPublicSafeScan(source, publicSafeSummaryMarkdown, generatedAt);

        errors.AddRange(CollectFailedChecks(consistencyCheck, "generated-view consistency"));
        errors.AddRange(CollectFailedChecks(publicSafeScan, "public-safe scan"));
        if (errors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "FEAT-150 cleanup source validation failed.",
                errors);
        }

        var artifactAudit = BuildArtifactHashAudit(
            source,
            sourcePath,
            registerJson,
            scorecardMarkdown,
            restrictedReviewerMarkdown,
            publicSafeSummaryMarkdown,
            generatedAt);
        var decisionLedger = BuildDecisionLedger(source, promotedRegister, consistencyCheck, publicSafeScan, generatedAt);
        var artifacts = new List<Feat150CleanupArtifact>
        {
            JsonArtifact(DecisionLedgerPath, decisionLedger),
            JsonArtifact(GeneratedViewConsistencyPath, consistencyCheck),
            JsonArtifact(PublicSafeScanPath, publicSafeScan),
            JsonArtifact(ArtifactHashAuditPath, artifactAudit),
            TextArtifact(DecisionSummaryPath, BuildDecisionSummary(source, promotedRegister, decisionLedger, consistencyCheck, publicSafeScan, generatedAt)),
        };

        return new Feat150GeneratedCleanupPackage(
            GetString(source, "sourceId"),
            "passed",
            artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray());
    }

    public static void WriteArtifacts(
        string packageRoot,
        IReadOnlyList<Feat150CleanupArtifact> artifacts,
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
        IReadOnlyList<Feat150CleanupArtifact> artifacts)
    {
        var errors = new List<string>();
        foreach (var artifact in artifacts.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            var path = Path.Combine(packageRoot, artifact.RelativePath);
            if (!File.Exists(path))
            {
                errors.Add($"Missing FEAT-150 cleanup artifact: {artifact.RelativePath}");
                continue;
            }

            var actual = NormalizeLineEndings(File.ReadAllText(path));
            var expected = NormalizeLineEndings(artifact.Content);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                errors.Add($"FEAT-150 cleanup artifact mismatch: {artifact.RelativePath}");
            }
        }

        return errors;
    }

    private static List<string> ValidateSource(JsonObject source, JsonObject promotedRegister)
    {
        var errors = new List<string>();
        if (GetString(source, "schemaVersion") != "feat150-cleanup-source.v1")
        {
            errors.Add("schemaVersion must be feat150-cleanup-source.v1.");
        }

        if (string.IsNullOrWhiteSpace(GetString(source, "sourceId")))
        {
            errors.Add("sourceId is required.");
        }

        var target = RequireObject(source, "targetRegister", errors);
        if (target is not null)
        {
            RequireMatchesPromoted(target, promotedRegister, "registerVersion", errors);
            RequireMatchesPromoted(target, promotedRegister, "registerVersionId", errors);
            var targetScore = GetInt(target, "targetTotalScore");
            var promotedScore = GetInt(ObjectOrEmpty(promotedRegister, "score"), "total");
            if (targetScore != promotedScore)
            {
                errors.Add($"targetRegister.targetTotalScore must match promoted score total {promotedScore}.");
            }

            var strongestAllowedClaim = GetStrongestAllowedClaim(promotedRegister);
            if (GetString(target, "strongestAllowedClaim") != strongestAllowedClaim)
            {
                errors.Add($"targetRegister.strongestAllowedClaim must match promoted strongest claim {strongestAllowedClaim}.");
            }
        }

        var decision = RequireObject(source, "blockerDecision", errors);
        if (decision is not null)
        {
            if (GetString(decision, "blockerId") != TargetBlockerId)
            {
                errors.Add($"blockerDecision.blockerId must be {TargetBlockerId}.");
            }

            if (GetString(decision, "decision") != "resolve")
            {
                errors.Add("blockerDecision.decision must be resolve.");
            }

            RequireArray(decision, "evidenceRefs", errors);
            RequireObject(decision, "scoreImpact", errors);
            RequireObject(decision, "signoffs", errors);
            RequireNonEmpty(decision, "decisionReason", errors);
            RequireNonEmpty(decision, "claimImpact", errors);
            RequireNonEmpty(decision, "residualRisk", errors);
        }

        var targetBlocker = FindBlocker(promotedRegister, TargetBlockerId);
        if (targetBlocker is null)
        {
            errors.Add($"{TargetBlockerId} must exist in promoted register.");
        }
        else
        {
            if (GetString(targetBlocker, "severity") != "green" || GetString(targetBlocker, "status") != "resolved")
            {
                errors.Add($"{TargetBlockerId} must be green/resolved in promoted register.");
            }

            if (GetString(targetBlocker, "featureId") != "FEAT-150")
            {
                errors.Add($"{TargetBlockerId}.featureId must be FEAT-150.");
            }
        }

        var internalClaim = FindClaim(promotedRegister, "internal_non_binding_rehearsal");
        if (internalClaim is null)
        {
            errors.Add("internal_non_binding_rehearsal claim level is missing.");
        }
        else
        {
            if (GetString(internalClaim, "status") != "allowed_with_limitations")
            {
                errors.Add("internal_non_binding_rehearsal must remain allowed_with_limitations.");
            }

            if (GetString(internalClaim, "blockerSeverity") != "amber")
            {
                errors.Add("internal_non_binding_rehearsal must remain amber because the non-binding limitation still applies.");
            }

            if (ArrayOrEmpty(internalClaim, "blockerIds").Count != 0)
            {
                errors.Add("internal_non_binding_rehearsal.blockerIds must be empty after blocker cleanup.");
            }
        }

        var friendlyClaim = FindClaim(promotedRegister, "friendly_organization_pilot");
        if (friendlyClaim is null ||
            GetString(friendlyClaim, "status") != "allowed_with_limitations" ||
            GetString(friendlyClaim, "blockerSeverity") != "amber")
        {
            errors.Add("friendly_organization_pilot must remain amber/allowed_with_limitations.");
        }

        RequireClaimBlocked(promotedRegister, "production_organizational_rollout", errors);
        RequireClaimBlocked(promotedRegister, "public_or_state_election", errors);
        return errors;
    }

    private static JsonObject BuildGeneratedViewConsistencyCheck(
        JsonObject source,
        JsonObject promotedRegister,
        string registerJson,
        string scorecardMarkdown,
        string restrictedReviewerMarkdown,
        string publicSafeSummaryMarkdown,
        DateTimeOffset generatedAt)
    {
        var checks = new JsonArray();
        var expectations = ObjectOrEmpty(source, "generatedViewExpectations");
        AddPhraseChecks(checks, "scorecard-required", "readiness-scorecard.md", scorecardMarkdown, ArrayOrEmpty(expectations, "scorecardRequiredPhrases"), shouldExist: true);
        AddPhraseChecks(checks, "scorecard-forbidden", "readiness-scorecard.md", scorecardMarkdown, ArrayOrEmpty(expectations, "scorecardForbiddenPhrases"), shouldExist: false);
        AddPhraseChecks(checks, "restricted-required", "restricted-reviewer-extract.md", restrictedReviewerMarkdown, ArrayOrEmpty(expectations, "restrictedRequiredPhrases"), shouldExist: true);
        AddPhraseChecks(checks, "public-safe-required", "public-safe-summary.md", publicSafeSummaryMarkdown, ArrayOrEmpty(expectations, "publicSafeRequiredPhrases"), shouldExist: true);
        AddPhraseChecks(checks, "public-safe-forbidden", "public-safe-summary.md", publicSafeSummaryMarkdown, ArrayOrEmpty(expectations, "publicSafeForbiddenPhrases"), shouldExist: false);

        var register = JsonNode.Parse(registerJson)!.AsObject();
        AddBooleanCheck(
            checks,
            "register-target-blocker-green-resolved",
            "readiness-register.json",
            FindBlocker(register, TargetBlockerId) is { } blocker &&
                GetString(blocker, "severity") == "green" &&
                GetString(blocker, "status") == "resolved",
            $"{TargetBlockerId} is green/resolved.");
        AddBooleanCheck(
            checks,
            "register-strongest-claim-friendly-pilot",
            "readiness-register.json",
            GetStrongestAllowedClaim(register) == "friendly_organization_pilot",
            "Current strongest allowed claim is friendly_organization_pilot.");
        AddBooleanCheck(
            checks,
            "register-score-unchanged",
            "readiness-register.json",
            GetInt(ObjectOrEmpty(register, "score"), "total") == GetInt(ObjectOrEmpty(source, "targetRegister"), "targetTotalScore"),
            "Register total matches FEAT-150 target score.");

        return new JsonObject
        {
            ["schemaVersion"] = "feat150-generated-view-consistency-check.v1",
            ["checkId"] = "FEAT150-GENERATED-VIEW-CONSISTENCY-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["registerVersionId"] = GetString(promotedRegister, "registerVersionId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["status"] = checks.Select(node => node!.AsObject()).All(check => GetString(check, "result") == "passed") ? "passed" : "failed",
            ["checks"] = checks,
        };
    }

    private static JsonObject BuildPublicSafeScan(JsonObject source, string publicSafeSummaryMarkdown, DateTimeOffset generatedAt)
    {
        var findings = new JsonArray();
        foreach (var phrase in ArrayOrEmpty(ObjectOrEmpty(source, "generatedViewExpectations"), "publicSafeForbiddenPhrases")
            .Select(node => node?.GetValue<string>())
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase)))
        {
            var found = publicSafeSummaryMarkdown.Contains(phrase!, StringComparison.OrdinalIgnoreCase);
            findings.Add(new JsonObject
            {
                ["term"] = phrase,
                ["result"] = found ? "failed" : "passed",
                ["reason"] = found ? "Forbidden public-safe term was found." : "Forbidden public-safe term was not found.",
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "feat150-public-safe-scan.v1",
            ["scanId"] = "FEAT150-PUBLIC-SAFE-SCAN-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["status"] = findings.Select(node => node!.AsObject()).All(finding => GetString(finding, "result") == "passed") ? "passed" : "failed",
            ["scannedArtifact"] = "public-safe-summary.md",
            ["findings"] = findings,
        };
    }

    private static JsonObject BuildArtifactHashAudit(
        JsonObject source,
        string sourcePath,
        string registerJson,
        string scorecardMarkdown,
        string restrictedReviewerMarkdown,
        string publicSafeSummaryMarkdown,
        DateTimeOffset generatedAt)
    {
        var artifacts = new JsonArray
        {
            BuildHashEntry("FEAT150-CLEANUP-SOURCE", "feat150-cleanup-source.json", File.ReadAllBytes(sourcePath), "restricted_reviewer"),
            BuildHashEntry("FEAT150-READINESS-REGISTER", "readiness-register.json", EncodingWithoutBom(registerJson), "internal"),
            BuildHashEntry("FEAT150-SCORECARD", "readiness-scorecard.md", EncodingWithoutBom(scorecardMarkdown), "restricted_reviewer"),
            BuildHashEntry("FEAT150-RESTRICTED-EXTRACT", "restricted-reviewer-extract.md", EncodingWithoutBom(restrictedReviewerMarkdown), "restricted_reviewer"),
            BuildHashEntry("FEAT150-PUBLIC-SAFE-SUMMARY", "public-safe-summary.md", EncodingWithoutBom(publicSafeSummaryMarkdown), "public_safe"),
        };

        return new JsonObject
        {
            ["schemaVersion"] = "feat150-artifact-hash-audit.v1",
            ["auditId"] = "FEAT150-ARTIFACT-HASH-AUDIT-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["status"] = "passed",
            ["artifacts"] = artifacts,
        };
    }

    private static JsonObject BuildDecisionLedger(
        JsonObject source,
        JsonObject promotedRegister,
        JsonObject consistencyCheck,
        JsonObject publicSafeScan,
        DateTimeOffset generatedAt)
    {
        var sourceDecision = ObjectOrEmpty(source, "blockerDecision");
        var promotedBlocker = FindBlocker(promotedRegister, TargetBlockerId) ?? new JsonObject();
        var decision = new JsonObject
        {
            ["blockerId"] = TargetBlockerId,
            ["currentSeverity"] = GetString(sourceDecision, "currentSeverity"),
            ["currentStatus"] = GetString(sourceDecision, "currentStatus"),
            ["proposedSeverity"] = GetString(promotedBlocker, "severity"),
            ["proposedStatus"] = GetString(promotedBlocker, "status"),
            ["featureSlice"] = "FEAT-150",
            ["acceptanceGateIds"] = Clone(sourceDecision["acceptanceGateIds"]),
            ["dimensionIds"] = Clone(sourceDecision["dimensionIds"]),
            ["decision"] = GetString(sourceDecision, "decision"),
            ["decisionReason"] = GetString(sourceDecision, "decisionReason"),
            ["evidenceRefs"] = Clone(sourceDecision["evidenceRefs"]),
            ["generatedViewConsistencyStatus"] = GetString(consistencyCheck, "status"),
            ["publicSafeScanStatus"] = GetString(publicSafeScan, "status"),
            ["scoreImpact"] = Clone(sourceDecision["scoreImpact"]),
            ["claimImpact"] = GetString(sourceDecision, "claimImpact"),
            ["residualRisk"] = GetString(sourceDecision, "residualRisk"),
            ["signoffs"] = Clone(sourceDecision["signoffs"]),
        };

        return new JsonObject
        {
            ["schemaVersion"] = "feat150-blocker-cleanup-decision-ledger.v1",
            ["ledgerId"] = "FEAT150-BLOCKER-CLEANUP-LEDGER-001",
            ["sourceId"] = GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["baselineRegister"] = Clone(source["baselineRegister"]),
            ["targetRegister"] = Clone(source["targetRegister"]),
            ["decisions"] = new JsonArray(decision),
        };
    }

    private static string BuildDecisionSummary(
        JsonObject source,
        JsonObject promotedRegister,
        JsonObject decisionLedger,
        JsonObject consistencyCheck,
        JsonObject publicSafeScan,
        DateTimeOffset generatedAt)
    {
        var decision = decisionLedger["decisions"]!.AsArray()[0]!.AsObject();
        var sb = new StringBuilder();
        sb.AppendLine("<!-- Generated by ReadinessRegisterPromoter. Do not edit by hand. -->");
        sb.AppendLine();
        sb.AppendLine("# FEAT-150 Cleanup Decision Summary");
        sb.AppendLine();
        sb.AppendLine($"Generated at: {FormatTimestamp(generatedAt)}");
        sb.AppendLine($"Source: {GetString(source, "sourceId")}");
        sb.AppendLine($"Target register: {GetString(promotedRegister, "registerVersionId")}");
        sb.AppendLine($"Score: {GetInt(ObjectOrEmpty(promotedRegister, "score"), "total")}/100");
        sb.AppendLine($"Strongest allowed claim: {GetStrongestAllowedClaim(promotedRegister)}");
        sb.AppendLine();
        sb.AppendLine("## Decision");
        sb.AppendLine();
        sb.AppendLine($"`{TargetBlockerId}` moved from `{GetString(decision, "currentSeverity")} / {GetString(decision, "currentStatus")}` to `{GetString(decision, "proposedSeverity")} / {GetString(decision, "proposedStatus")}`.");
        sb.AppendLine();
        sb.AppendLine("This is blocker hygiene only. Internal non-binding rehearsal and friendly-organization pilot remain limited, production rollout remains blocked, public/state election readiness remains blocked, and the global score is unchanged.");
        sb.AppendLine();
        sb.AppendLine("## Checks");
        sb.AppendLine();
        sb.AppendLine($"- Generated-view consistency: {GetString(consistencyCheck, "status")}");
        sb.AppendLine($"- Public-safe scan: {GetString(publicSafeScan, "status")}");
        sb.AppendLine();
        sb.AppendLine("## Residual Risk");
        sb.AppendLine();
        sb.AppendLine(GetString(decision, "residualRisk"));
        return NormalizeLineEndings(sb.ToString());
    }

    private static void AddPhraseChecks(JsonArray checks, string idPrefix, string artifact, string content, JsonArray phrases, bool shouldExist)
    {
        var index = 1;
        foreach (var phrase in phrases.Select(node => node?.GetValue<string>()).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var found = content.Contains(phrase!, StringComparison.Ordinal);
            var passed = shouldExist ? found : !found;
            checks.Add(new JsonObject
            {
                ["checkId"] = $"{idPrefix}-{index:000}",
                ["artifact"] = artifact,
                ["phrase"] = phrase,
                ["expectation"] = shouldExist ? "present" : "absent",
                ["result"] = passed ? "passed" : "failed",
            });
            index++;
        }
    }

    private static void AddBooleanCheck(JsonArray checks, string checkId, string artifact, bool passed, string reason)
    {
        checks.Add(new JsonObject
        {
            ["checkId"] = checkId,
            ["artifact"] = artifact,
            ["expectation"] = "true",
            ["result"] = passed ? "passed" : "failed",
            ["reason"] = reason,
        });
    }

    private static IReadOnlyList<string> CollectFailedChecks(JsonObject checkDocument, string label) =>
        ArrayOrEmpty(checkDocument, checkDocument.ContainsKey("checks") ? "checks" : "findings")
            .Select(node => node!.AsObject())
            .Where(check => GetString(check, "result") == "failed")
            .Select(check => $"{label} failed: {GetString(check, "checkId")}{GetString(check, "term")}")
            .ToArray();

    private static JsonObject BuildHashEntry(string artifactId, string path, byte[] bytes, string visibility) =>
        new()
        {
            ["artifactId"] = artifactId,
            ["path"] = path,
            ["sha256Hash"] = ComputeSha256Hex(bytes),
            ["hashAlgorithm"] = "SHA-256",
            ["visibility"] = visibility,
            ["sizeBytes"] = bytes.Length,
            ["auditResult"] = "passed",
        };

    private static void RequireMatchesPromoted(JsonObject target, JsonObject promotedRegister, string propertyName, List<string> errors)
    {
        if (GetString(target, propertyName) != GetString(promotedRegister, propertyName))
        {
            errors.Add($"targetRegister.{propertyName} must match promoted register {propertyName}.");
        }
    }

    private static void RequireClaimBlocked(JsonObject register, string claimLevel, List<string> errors)
    {
        var claim = FindClaim(register, claimLevel);
        if (claim is null ||
            GetString(claim, "blockerSeverity") != "red" ||
            GetString(claim, "status") != "blocked")
        {
            errors.Add($"{claimLevel} must remain red/blocked.");
        }
    }

    private static JsonObject? FindClaim(JsonObject register, string claimLevel) =>
        ArrayOrEmpty(register, "claimLevels")
            .Select(node => node!.AsObject())
            .FirstOrDefault(claim => GetString(claim, "claimLevel") == claimLevel);

    private static JsonObject? FindBlocker(JsonObject register, string blockerId) =>
        ArrayOrEmpty(register, "blockers")
            .Select(node => node!.AsObject())
            .FirstOrDefault(blocker => GetString(blocker, "blockerId") == blockerId);

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

    private static Feat150CleanupArtifact JsonArtifact(string relativePath, JsonNode node) =>
        new(relativePath, SerializeJson(node), "application/json");

    private static Feat150CleanupArtifact TextArtifact(string relativePath, string content) =>
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

    private static void EnsureContained(string root, string child, string label)
    {
        var fullRoot = Path.GetFullPath(root);
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!child.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReadinessRegisterPromotionException(
                "FEAT-150 cleanup output path escapes package root.",
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
