using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PublicStateElectionPrerequisiteRegisterPromoter;

public static class PublicStateElectionPrerequisiteArtifactGenerator
{
    public const string RegisterPath = "public-state-prerequisite-register.json";
    public const string DecisionLedgerPath = "public-state-prerequisite-decision-ledger.json";
    public const string BlockerPolicyPath = "public-state-blocker-policy.json";
    public const string PublicSafeSummaryPath = "public-state-public-safe-summary.md";
    public const string RestrictedReferenceIndexPath = "public-state-restricted-reference-index.json";
    public const string ReadinessFragmentPath = "public-state-readiness-fragment.json";
    public const string PackageHashValidationPath = "public-state-package-hash-validation.json";

    public static readonly string[] RequiredArtifactPaths =
    [
        BlockerPolicyPath,
        DecisionLedgerPath,
        PackageHashValidationPath,
        PublicSafeSummaryPath,
        ReadinessFragmentPath,
        RegisterPath,
        RestrictedReferenceIndexPath,
    ];

    public static PublicStateGeneratedPackage Generate(
        PublicStateElectionPrerequisitePromotionPaths paths,
        string? sourceInput = null,
        DateTimeOffset? generatedAt = null)
    {
        var source = PublicStateElectionPrerequisiteContracts.LoadSource(paths, sourceInput);
        var validationErrors = PublicStateElectionPrerequisiteContracts.ValidateSource(source);
        if (validationErrors.Count > 0)
        {
            throw new PublicStateElectionPrerequisitePromotionException(
                "Public/state prerequisite source validation failed.",
                validationErrors);
        }

        var effectiveGeneratedAt = generatedAt ?? DateTimeOffset.UtcNow;
        var gate = PublicStateElectionPrerequisiteGateChecker.Evaluate(source);
        var artifactsBeforeHash = new[]
        {
            JsonArtifact(RegisterPath, BuildRegister(source, gate, effectiveGeneratedAt)),
            JsonArtifact(DecisionLedgerPath, BuildDecisionLedger(source, gate, effectiveGeneratedAt)),
            JsonArtifact(BlockerPolicyPath, BuildBlockerPolicy(source, gate, effectiveGeneratedAt)),
            JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, gate, effectiveGeneratedAt)),
            TextArtifact(PublicSafeSummaryPath, BuildPublicSafeSummary(source, gate, effectiveGeneratedAt)),
            JsonArtifact(RestrictedReferenceIndexPath, BuildRestrictedReferenceIndex(source, effectiveGeneratedAt)),
        };
        var hashValidation = JsonArtifact(
            PackageHashValidationPath,
            BuildPackageHashValidation(source, gate, artifactsBeforeHash, effectiveGeneratedAt));

        var artifacts = artifactsBeforeHash
            .Append(hashValidation)
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new PublicStateGeneratedPackage(gate.Status, artifacts, gate);
    }

    private static JsonObject BuildRegister(
        JsonObject source,
        PublicStateGateEvaluation gate,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "public-state-prerequisite-package.v1",
            ["sourceSchemaVersion"] = PublicStateElectionPrerequisiteContracts.GetString(source, "schemaVersion"),
            ["sourceId"] = PublicStateElectionPrerequisiteContracts.GetString(source, "sourceId"),
            ["featureId"] = PublicStateElectionPrerequisiteContracts.FeatureId,
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["packageStatus"] = gate.Status,
            ["publicStateDecision"] = DecisionToJson(gate.PublicStateDecision),
            ["scoreChangeAllowed"] = gate.ScoreChangeAllowed,
            ["directRegisterMutation"] = gate.DirectRegisterMutation,
            ["publicStateClaimAllowed"] = gate.PublicStateClaimAllowed,
            ["prerequisiteGroups"] = PrerequisiteGroupSummaries(source),
            ["source"] = PublicStateElectionPrerequisiteContracts.Clone(source),
        };

    private static JsonObject BuildDecisionLedger(
        JsonObject source,
        PublicStateGateEvaluation gate,
        DateTimeOffset generatedAt)
    {
        var dependency = PublicStateElectionPrerequisiteContracts.RequireObject(source, "feat148Dependency");
        return new JsonObject
        {
            ["schemaVersion"] = "public-state-prerequisite-decision-ledger.v1",
            ["ledgerId"] = "FEAT149-PUBLIC-STATE-DECISION-LEDGER-001",
            ["sourceId"] = PublicStateElectionPrerequisiteContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["packageStatus"] = gate.Status,
            ["decisions"] = new JsonArray(
                BuildPublicStateDecision(gate),
                BuildFeat148DependencyDecision(dependency),
                BuildPrerequisiteDecisionSummary(source)),
            ["blockers"] = StringArray(gate.Blockers),
            ["diagnostics"] = StringArray(gate.Diagnostics),
        };
    }

    private static JsonObject BuildBlockerPolicy(
        JsonObject source,
        PublicStateGateEvaluation gate,
        DateTimeOffset generatedAt)
    {
        var policy = PublicStateElectionPrerequisiteContracts.RequireObject(source, "blockerPolicy");
        return new JsonObject
        {
            ["schemaVersion"] = "public-state-blocker-policy.v1",
            ["policyId"] = "FEAT149-PUBLIC-STATE-BLOCKER-POLICY-001",
            ["sourceId"] = PublicStateElectionPrerequisiteContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["blockerId"] = gate.PublicStateDecision.BlockerId,
            ["currentSeverity"] = gate.PublicStateDecision.Severity,
            ["currentStatus"] = gate.PublicStateDecision.Status,
            ["v1Decision"] = gate.PublicStateDecision.Decision,
            ["decisionReason"] = gate.PublicStateDecision.Reason,
            ["scoreChangeAllowed"] = false,
            ["directRegisterMutation"] = false,
            ["requiredFutureResolution"] = PublicStateElectionPrerequisiteContracts.Clone(policy["requiredFutureResolution"]),
            ["forbiddenTransitions"] = PublicStateElectionPrerequisiteContracts.Clone(policy["forbiddenTransitions"]),
        };
    }

    private static JsonObject BuildReadinessFragment(
        JsonObject source,
        PublicStateGateEvaluation gate,
        DateTimeOffset generatedAt)
    {
        var register = PublicStateElectionPrerequisiteContracts.RequireObject(source, "baselineRegister");
        var scorePolicy = PublicStateElectionPrerequisiteContracts.RequireObject(source, "scorePolicy");
        return new JsonObject
        {
            ["schemaVersion"] = "public-state-readiness-fragment.v1",
            ["fragmentId"] = "FEAT149-PUBLIC-STATE-READINESS-FRAGMENT-001",
            ["sourceId"] = PublicStateElectionPrerequisiteContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["targetRegisterVersionId"] = PublicStateElectionPrerequisiteContracts.GetString(register, "registerVersionId"),
            ["fragmentConsumer"] = "future FEAT-130 promotion only",
            ["scoreChangeAllowed"] = false,
            ["directRegisterMutation"] = false,
            ["currentTotalScore"] = PublicStateElectionPrerequisiteContracts.GetInt(scorePolicy, "currentTotalScore"),
            ["proposedTotalScore"] = PublicStateElectionPrerequisiteContracts.GetInt(scorePolicy, "currentTotalScore"),
            ["strongestAllowedClaimBefore"] = PublicStateElectionPrerequisiteContracts.GetString(register, "strongestAllowedClaim"),
            ["strongestAllowedClaimAfter"] = PublicStateElectionPrerequisiteContracts.GetString(register, "strongestAllowedClaim"),
            ["publicStateDecision"] = DecisionToJson(gate.PublicStateDecision),
            ["productionOrganizationalRolloutClaimChanged"] = false,
            ["publicStateClaimChanged"] = false,
        };
    }

    private static string BuildPublicSafeSummary(
        JsonObject source,
        PublicStateGateEvaluation gate,
        DateTimeOffset generatedAt)
    {
        var wording = PublicStateElectionPrerequisiteContracts.RequireObject(source, "publicSafeWording");
        var builder = new StringBuilder();
        builder.AppendLine("# Public/State Election Prerequisite Summary");
        builder.AppendLine();
        builder.AppendLine($"Generated: {FormatTimestamp(generatedAt)}");
        builder.AppendLine($"Source: {PublicStateElectionPrerequisiteContracts.GetString(source, "sourceId")}");
        builder.AppendLine($"Status: {gate.Status}");
        builder.AppendLine($"Public/state blocker: {gate.PublicStateDecision.Severity}/{gate.PublicStateDecision.Status}");
        builder.AppendLine();
        builder.AppendLine(PublicStateElectionPrerequisiteContracts.GetString(wording, "blockedWording"));
        builder.AppendLine();
        builder.AppendLine("## Allowed Non-Claims");
        foreach (var item in PublicStateElectionPrerequisiteContracts.GetStringArray(wording, "allowedNonClaims"))
        {
            builder.Append("- ");
            builder.AppendLine(item);
        }

        builder.AppendLine();
        builder.AppendLine("## Missing Or Open Prerequisites");
        foreach (var group in PublicStateElectionPrerequisiteContracts.RequireArray(source, "prerequisiteGroups").OfType<JsonObject>())
        {
            builder.Append("- ");
            builder.Append(PublicStateElectionPrerequisiteContracts.GetString(group, "label"));
            builder.Append(": ");
            builder.AppendLine(PublicStateElectionPrerequisiteContracts.GetString(group, "status"));
        }

        return NormalizeText(builder.ToString());
    }

    private static JsonObject BuildRestrictedReferenceIndex(JsonObject source, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "public-state-restricted-reference-index.v1",
            ["indexId"] = "FEAT149-PUBLIC-STATE-RESTRICTED-REFERENCE-INDEX-001",
            ["sourceId"] = PublicStateElectionPrerequisiteContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["copyPolicy"] = "Reference metadata and hashes only; restricted payload bodies are not copied.",
            ["references"] = new JsonArray(PublicStateElectionPrerequisiteContracts
                .RequireArray(source, "externalReferences")
                .OfType<JsonObject>()
                .OrderBy(item => PublicStateElectionPrerequisiteContracts.GetString(item, "evidenceId"), StringComparer.Ordinal)
                .Select(ReferenceIndexEntry)
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildPackageHashValidation(
        JsonObject source,
        PublicStateGateEvaluation gate,
        IReadOnlyCollection<PublicStateGeneratedArtifact> artifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "public-state-package-hash-validation.v1",
            ["validationId"] = "FEAT149-PUBLIC-STATE-PACKAGE-HASH-VALIDATION-001",
            ["sourceId"] = PublicStateElectionPrerequisiteContracts.GetString(source, "sourceId"),
            ["generatedAt"] = FormatTimestamp(generatedAt),
            ["status"] = gate.Status,
            ["generatedArtifactHashes"] = ArtifactRefs(artifacts),
            ["coveredArtifactCount"] = artifacts.Count,
            ["selfHashPolicy"] = "This validation artifact records every generated artifact except itself to avoid circular hashes.",
        };

    private static JsonObject BuildPublicStateDecision(PublicStateGateEvaluation gate) =>
        new()
        {
            ["decisionType"] = "public_state_blocker",
            ["blockerDecision"] = DecisionToJson(gate.PublicStateDecision),
        };

    private static JsonObject BuildFeat148DependencyDecision(JsonObject dependency) =>
        new()
        {
            ["decisionType"] = "feat148_dependency",
            ["featureId"] = PublicStateElectionPrerequisiteContracts.GetString(dependency, "featureId"),
            ["dependencyType"] = PublicStateElectionPrerequisiteContracts.GetString(dependency, "dependencyType"),
            ["currentStatus"] = PublicStateElectionPrerequisiteContracts.GetString(dependency, "currentStatus"),
            ["requiredMinimumStatus"] = PublicStateElectionPrerequisiteContracts.GetString(dependency, "requiredMinimumStatus"),
            ["sufficiency"] = PublicStateElectionPrerequisiteContracts.GetString(dependency, "sufficiency"),
            ["claimImpact"] = PublicStateElectionPrerequisiteContracts.GetString(dependency, "claimImpact"),
        };

    private static JsonObject BuildPrerequisiteDecisionSummary(JsonObject source) =>
        new()
        {
            ["decisionType"] = "prerequisite_groups",
            ["groups"] = PrerequisiteGroupSummaries(source),
        };

    private static JsonArray PrerequisiteGroupSummaries(JsonObject source) =>
        new(PublicStateElectionPrerequisiteContracts
            .RequireArray(source, "prerequisiteGroups")
            .OfType<JsonObject>()
            .OrderBy(group => PublicStateElectionPrerequisiteContracts.GetString(group, "groupId"), StringComparer.Ordinal)
            .Select(group => new JsonObject
            {
                ["groupId"] = PublicStateElectionPrerequisiteContracts.GetString(group, "groupId"),
                ["label"] = PublicStateElectionPrerequisiteContracts.GetString(group, "label"),
                ["ownerCategory"] = PublicStateElectionPrerequisiteContracts.GetString(group, "ownerCategory"),
                ["evidenceType"] = PublicStateElectionPrerequisiteContracts.GetString(group, "evidenceType"),
                ["mandatory"] = PublicStateElectionPrerequisiteContracts.GetBool(group, "mandatory"),
                ["status"] = PublicStateElectionPrerequisiteContracts.GetString(group, "status"),
                ["blockerImpact"] = PublicStateElectionPrerequisiteContracts.GetString(group, "blockerImpact"),
                ["claimImpact"] = PublicStateElectionPrerequisiteContracts.GetString(group, "claimImpact"),
                ["evidenceRefs"] = PublicStateElectionPrerequisiteContracts.Clone(group["evidenceRefs"]),
                ["blockerIds"] = PublicStateElectionPrerequisiteContracts.Clone(group["blockerIds"]),
                ["futureResolutionCriteria"] = PublicStateElectionPrerequisiteContracts.GetString(group, "futureResolutionCriteria"),
            })
            .ToArray<JsonNode?>());

    private static JsonObject ReferenceIndexEntry(JsonObject source) =>
        new()
        {
            ["evidenceId"] = PublicStateElectionPrerequisiteContracts.GetString(source, "evidenceId"),
            ["ownerCategory"] = PublicStateElectionPrerequisiteContracts.GetString(source, "ownerCategory"),
            ["status"] = PublicStateElectionPrerequisiteContracts.GetString(source, "status"),
            ["visibility"] = PublicStateElectionPrerequisiteContracts.GetString(source, "visibility"),
            ["publicRef"] = PublicStateElectionPrerequisiteContracts.GetString(source, "publicRef"),
            ["restrictedRef"] = PublicStateElectionPrerequisiteContracts.GetString(source, "restrictedRef"),
            ["sha256Hash"] = PublicStateElectionPrerequisiteContracts.GetString(source, "sha256Hash"),
            ["claimEffect"] = PublicStateElectionPrerequisiteContracts.GetString(source, "claimEffect"),
        };

    private static JsonObject DecisionToJson(PublicStateBlockerDecision decision) =>
        new()
        {
            ["blockerId"] = decision.BlockerId,
            ["severity"] = decision.Severity,
            ["status"] = decision.Status,
            ["decision"] = decision.Decision,
            ["reason"] = decision.Reason,
        };

    private static JsonArray ArtifactRefs(IReadOnlyCollection<PublicStateGeneratedArtifact> artifacts) =>
        new(artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact => new JsonObject
            {
                ["relativePath"] = artifact.RelativePath,
                ["sha256Hash"] = artifact.Sha256Hash,
                ["mediaType"] = artifact.MediaType,
                ["sizeBytes"] = Encoding.UTF8.GetByteCount(artifact.Content),
            })
            .ToArray<JsonNode?>());

    private static JsonArray StringArray(IEnumerable<string> values) =>
        new(values
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(value => JsonValue.Create(value))
            .ToArray<JsonNode?>());

    private static PublicStateGeneratedArtifact JsonArtifact(string relativePath, JsonNode json)
    {
        var content = PublicStateElectionPrerequisiteContracts.CanonicalJson(json);
        return new PublicStateGeneratedArtifact(relativePath, content, Sha256Hex(content), "application/json");
    }

    private static PublicStateGeneratedArtifact TextArtifact(string relativePath, string content)
    {
        var normalized = NormalizeText(content);
        return new PublicStateGeneratedArtifact(relativePath, normalized, Sha256Hex(normalized), "text/markdown");
    }

    private static string NormalizeText(string value) =>
        PublicStateElectionPrerequisiteContracts.NormalizeLineEndings(value).TrimEnd('\n') + "\n";

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
