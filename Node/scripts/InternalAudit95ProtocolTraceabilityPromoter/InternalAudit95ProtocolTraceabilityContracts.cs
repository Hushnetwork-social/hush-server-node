using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InternalAudit95ProtocolTraceabilityPromoter;

public static class InternalAudit95ProtocolTraceabilityContracts
{
    public const string FeatureId = "FEAT-157";
    public const string SourceSchemaVersion = "internal-audit-95-protocol-traceability-source.v1";
    public const string PackageSchemaVersion = "internal-audit-95-protocol-traceability-package.v1";
    public const string PackageAnchor = "IA95-PROTOCOL-TRACEABILITY-20260601-BASELINE";
    public const string BaselineRegisterVersionId = "RDY-REG-v0.1.7";
    public const string DriftCheckRegisterVersionId = "RDY-REG-v0.1.5";
    public const string TargetDimensionId = "RDY-DIM-001";
    public const string TargetBlockerId = "RDY-BLOCK-INTERNAL_AUDIT_95_DIM001-001";
    public const string CanonicalizationVersion = "internal-audit-95-protocol-traceability-canonical-json.v1";

    public static readonly string[] RequiredSchemaFiles =
    [
        "internal-audit-95-protocol-traceability-source.schema.json",
        "internal-audit-95-protocol-traceability-package.schema.json",
    ];

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static JsonObject LoadSource(
        InternalAudit95ProtocolTraceabilityPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "FEAT-157 source input");
        return ReadJsonObject(sourcePath, InternalAudit95ProtocolTraceabilityPaths.SourceFileName);
    }

    public static IReadOnlyList<string> ValidateSchemaSet(string schemasRoot)
    {
        var errors = new List<string>();
        foreach (var schemaFile in RequiredSchemaFiles)
        {
            var path = Path.Combine(schemasRoot, schemaFile);
            if (!File.Exists(path))
            {
                errors.Add($"Missing schema file: {schemaFile}");
                continue;
            }

            var schema = ReadJsonObject(path, schemaFile);
            if (!schema.ContainsKey("$schema"))
            {
                errors.Add($"Schema {schemaFile} is missing $schema.");
            }

            if (!schema.ContainsKey("required"))
            {
                errors.Add($"Schema {schemaFile} is missing required fields.");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSource(JsonObject source)
    {
        var errors = ValidateJsonRequired(source, InternalAudit95ProtocolTraceabilityPaths.SourceFileName, [
            "schemaVersion",
            "sourceId",
            "featureId",
            "status",
            "generatedAt",
            "packageAnchor",
            "baselineRegister",
            "scorePolicy",
            "sourceArtifacts",
            "traceRequirements",
            "generatedArtifactContracts",
            "validationRules",
            "publicSafeOutputRules",
            "restrictedReviewerRules",
            "downstreamConsumers",
            "signoff",
            "residualRisks",
        ]).ToList();

        RequireValue(source, "schemaVersion", SourceSchemaVersion, errors);
        RequireValue(source, "featureId", FeatureId, errors);
        RequireValue(source, "packageAnchor", PackageAnchor, errors);

        if (errors.Count > 0)
        {
            return errors;
        }

        ValidateBaselineRegister(RequireObject(source, "baselineRegister"), errors);
        ValidateScorePolicy(RequireObject(source, "scorePolicy"), errors);
        ValidateSourceArtifacts(RequireArray(source, "sourceArtifacts"), errors);
        ValidateGeneratedArtifactContracts(RequireArray(source, "generatedArtifactContracts"), errors);
        ValidateTraceRequirements(source, errors);
        ValidatePublicSafeRules(RequireObject(source, "publicSafeOutputRules"), errors);
        ValidateDownstreamConsumers(RequireArray(source, "downstreamConsumers"), errors);

        return errors;
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new InternalAudit95ProtocolTraceabilityException($"{label} is not a JSON object.");
    }

    public static IReadOnlyList<string> ValidateJsonRequired(
        JsonObject value,
        string label,
        IReadOnlyList<string> requiredProperties)
    {
        var errors = new List<string>();
        foreach (var property in requiredProperties)
        {
            if (!value.ContainsKey(property) || value[property] is null)
            {
                errors.Add($"{label} is missing required property {property}.");
            }
        }

        return errors;
    }

    public static JsonObject RequireObject(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonObject obj)
        {
            return obj;
        }

        throw new InternalAudit95ProtocolTraceabilityException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new InternalAudit95ProtocolTraceabilityException($"Missing array property: {property}");
    }

    public static string GetString(JsonObject? value, string property, string fallback = "")
    {
        if (value is null || !value.TryGetPropertyValue(property, out var node) || node is null)
        {
            return fallback;
        }

        return node.GetValue<string>();
    }

    public static int GetInt(JsonObject? value, string property, int fallback = 0)
    {
        if (value is null || !value.TryGetPropertyValue(property, out var node) || node is null)
        {
            return fallback;
        }

        return node.GetValue<int>();
    }

    public static bool GetBool(JsonObject? value, string property, bool fallback = false)
    {
        if (value is null || !value.TryGetPropertyValue(property, out var node) || node is null)
        {
            return fallback;
        }

        return node.GetValue<bool>();
    }

    public static IReadOnlyList<string> GetStringArray(JsonObject value, string property)
    {
        if (!value.TryGetPropertyValue(property, out var node) || node is not JsonArray array)
        {
            return [];
        }

        return array
            .Select(item => item?.GetValue<string>() ?? "")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    public static void EnsurePathUnder(string root, string candidate, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InternalAudit95ProtocolTraceabilityException(
                "FEAT-157 path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    public static string ResolveSourceInput(
        InternalAudit95ProtocolTraceabilityPaths paths,
        string? sourceInput)
    {
        if (string.IsNullOrWhiteSpace(sourceInput))
        {
            return Path.GetFullPath(paths.DefaultSourceInput);
        }

        var combined = Path.IsPathRooted(sourceInput)
            ? sourceInput
            : Path.Combine(paths.WorkspaceRoot, sourceInput);
        var fullPath = Path.GetFullPath(combined);
        return Directory.Exists(fullPath)
            ? Path.Combine(fullPath, InternalAudit95ProtocolTraceabilityPaths.SourceFileName)
            : fullPath;
    }

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    public static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    public static string CanonicalJson(JsonNode node) =>
        NormalizeLineEndings(node.ToJsonString(CanonicalJsonOptions)) + "\n";

    public static string Sha256Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(NormalizeLineEndings(content))))
            .ToLowerInvariant();

    public static string Sha256Hex(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    public static string NormalizeHash(string value) =>
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value["sha256:".Length..].ToLowerInvariant()
            : value.ToLowerInvariant();

    public static IReadOnlyList<string> ValidatePublicSafeContent(string content, JsonObject publicSafeOutputRules)
    {
        var errors = new List<string>();
        foreach (var needle in GetStringArray(publicSafeOutputRules, "forbiddenMaterialNeedles")
            .Concat(GetStringArray(publicSafeOutputRules, "forbiddenClaimNeedles")))
        {
            if (content.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"FEAT157_PUBLIC_SAFE_FORBIDDEN_MATERIAL: public-safe output contains forbidden material '{needle}'.");
            }
        }

        return errors;
    }

    private static void ValidateBaselineRegister(JsonObject baseline, List<string> errors)
    {
        var registerVersionId = GetString(baseline, "registerVersionId");
        if (string.Equals(registerVersionId, DriftCheckRegisterVersionId, StringComparison.Ordinal))
        {
            errors.Add($"FEAT157_DRIFT_SOURCE_USED_AS_BASELINE: registerVersionId must be {BaselineRegisterVersionId}; observed {registerVersionId}.");
        }
        else if (!string.Equals(registerVersionId, BaselineRegisterVersionId, StringComparison.Ordinal))
        {
            errors.Add($"registerVersionId must be {BaselineRegisterVersionId}; observed {registerVersionId}.");
        }

        RequireValue(baseline, "registerVersion", "v0.1.7", errors);
        RequireValue(baseline, "status", "AcceptedInternal", errors);
        RequireValue(baseline, "authoritativeSourceArtifactId", "RDY-REG-v0.1.7-MANIFEST", errors);
        RequireIntValue(baseline, "totalScore", 80, errors);
        RequireIntValue(baseline, "internalAuditTargetScore", 95, errors);

        var drift = RequireObject(baseline, "overviewDriftCheck");
        RequireValue(drift, "sourceArtifactId", "MEMORY-BANK-OVERVIEW-RDY-REG-v0.1.5", errors);
        RequireValue(drift, "expectedRegisterVersionId", DriftCheckRegisterVersionId, errors);
        RequireBoolValue(drift, "scoringBaseline", false, errors);
    }

    private static void ValidateScorePolicy(JsonObject scorePolicy, List<string> errors)
    {
        var targetDimensionId = GetString(scorePolicy, "targetDimensionId");
        if (!string.Equals(targetDimensionId, TargetDimensionId, StringComparison.Ordinal))
        {
            errors.Add($"FEAT157_SCORE_DIMENSION_INVALID: targetDimensionId must be {TargetDimensionId}; observed {targetDimensionId}.");
        }

        var targetBlockerId = GetString(scorePolicy, "targetBlockerId");
        if (!string.Equals(targetBlockerId, TargetBlockerId, StringComparison.Ordinal))
        {
            errors.Add($"FEAT157_BLOCKER_OWNERSHIP_INVALID: targetBlockerId must be {TargetBlockerId}; observed {targetBlockerId}.");
        }

        var blockerOwnerFeatureId = GetString(scorePolicy, "blockerOwnerFeatureId");
        if (!string.Equals(blockerOwnerFeatureId, FeatureId, StringComparison.Ordinal))
        {
            errors.Add($"FEAT157_BLOCKER_OWNERSHIP_INVALID: blockerOwnerFeatureId must be {FeatureId}; observed {blockerOwnerFeatureId}.");
        }

        RequireIntValue(scorePolicy, "currentScore", 8, errors);
        RequireIntValue(scorePolicy, "proposedScore", 10, errors);
        RequireBoolValue(scorePolicy, "scoreChangeAllowed", true, errors);

        var directRegisterMutation = GetBool(scorePolicy, "directRegisterMutation", true);
        if (directRegisterMutation)
        {
            errors.Add("FEAT157_DIRECT_REGISTER_MUTATION_FORBIDDEN: directRegisterMutation must be False; observed True.");
        }
    }

    private static void ValidateSourceArtifacts(JsonArray artifacts, List<string> errors)
    {
        if (artifacts.Count == 0)
        {
            errors.Add("sourceArtifacts must contain at least one artifact.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in artifacts.OfType<JsonObject>())
        {
            var artifactId = GetString(artifact, "artifactId");
            if (string.IsNullOrWhiteSpace(artifactId))
            {
                errors.Add("sourceArtifacts entry is missing artifactId.");
                continue;
            }

            if (!ids.Add(artifactId))
            {
                errors.Add($"Duplicate source artifact id: {artifactId}");
            }

            foreach (var required in new[] { "family", "logicalRef", "resolvedPath", "expectedSha256Hash", "role", "visibility", "classification" })
            {
                if (string.IsNullOrWhiteSpace(GetString(artifact, required)))
                {
                    errors.Add($"Source artifact {artifactId} is missing {required}.");
                }
            }

            if (GetString(artifact, "role") == "drift-check-only" && GetBool(artifact, "requiredForScore"))
            {
                errors.Add($"Drift-check-only artifact {artifactId} cannot be required for score movement.");
            }
        }
    }

    private static void ValidateGeneratedArtifactContracts(JsonArray contracts, List<string> errors)
    {
        if (contracts.Count == 0)
        {
            errors.Add("generatedArtifactContracts must contain at least one artifact contract.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in contracts.OfType<JsonObject>())
        {
            var artifactId = GetString(contract, "artifactId");
            if (string.IsNullOrWhiteSpace(artifactId))
            {
                errors.Add("generatedArtifactContracts entry is missing artifactId.");
                continue;
            }

            if (!ids.Add(artifactId))
            {
                errors.Add($"Duplicate generated artifact contract id: {artifactId}");
            }

            foreach (var required in new[] { "fileName", "artifactType", "visibility", "classification" })
            {
                if (string.IsNullOrWhiteSpace(GetString(contract, required)))
                {
                    errors.Add($"Generated artifact contract {artifactId} is missing {required}.");
                }
            }
        }
    }

    private static void ValidateTraceRequirements(JsonObject source, List<string> errors)
    {
        var sourceArtifactIds = RequireArray(source, "sourceArtifacts")
            .OfType<JsonObject>()
            .Select(item => GetString(item, "artifactId"))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.Ordinal);
        var generatedArtifactIds = RequireArray(source, "generatedArtifactContracts")
            .OfType<JsonObject>()
            .Select(item => GetString(item, "artifactId"))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.Ordinal);
        var traceRequirements = RequireArray(source, "traceRequirements");

        if (traceRequirements.Count == 0)
        {
            errors.Add("FEAT157_TRACE_ROW_MISSING: traceRequirements must contain at least one row.");
            return;
        }

        foreach (var trace in traceRequirements.OfType<JsonObject>())
        {
            var traceId = GetString(trace, "traceRequirementId");
            if (string.IsNullOrWhiteSpace(traceId))
            {
                errors.Add("Trace requirement is missing traceRequirementId.");
                continue;
            }

            RequireValue(trace, "dimensionId", TargetDimensionId, errors);
            RequireValue(trace, "blockerId", TargetBlockerId, errors);

            var traceSourceArtifactIds = GetStringArray(trace, "sourceArtifactIds");
            if (traceSourceArtifactIds.Count == 0)
            {
                errors.Add($"FEAT157_TRACE_ROW_MISSING: Trace requirement {traceId} must reference at least one source artifact.");
            }

            foreach (var artifactId in traceSourceArtifactIds)
            {
                if (!sourceArtifactIds.Contains(artifactId))
                {
                    errors.Add($"Trace requirement {traceId} references unknown source artifact {artifactId}.");
                }
            }

            var traceGeneratedArtifactIds = GetStringArray(trace, "requiredGeneratedArtifactIds");
            if (traceGeneratedArtifactIds.Count == 0)
            {
                errors.Add($"FEAT157_TRACE_ROW_MISSING: Trace requirement {traceId} must require at least one generated artifact.");
            }

            foreach (var artifactId in traceGeneratedArtifactIds)
            {
                if (!generatedArtifactIds.Contains(artifactId))
                {
                    errors.Add($"Trace requirement {traceId} references unknown generated artifact {artifactId}.");
                }
            }
        }
    }

    private static void ValidatePublicSafeRules(JsonObject rules, List<string> errors)
    {
        if (RequireArray(rules, "forbiddenMaterialNeedles").Count == 0)
        {
            errors.Add("publicSafeOutputRules.forbiddenMaterialNeedles must not be empty.");
        }

        if (RequireArray(rules, "forbiddenClaimNeedles").Count == 0)
        {
            errors.Add("publicSafeOutputRules.forbiddenClaimNeedles must not be empty.");
        }

        RequireBoolValue(rules, "numericScorePublicDisclosure", false, errors);
    }

    private static void ValidateDownstreamConsumers(JsonArray consumers, List<string> errors)
    {
        if (consumers.Count == 0)
        {
            errors.Add("downstreamConsumers must not be empty.");
        }

        foreach (var consumer in consumers.OfType<JsonObject>())
        {
            if (string.IsNullOrWhiteSpace(GetString(consumer, "featureId")) ||
                string.IsNullOrWhiteSpace(GetString(consumer, "dimensionId")) ||
                string.IsNullOrWhiteSpace(GetString(consumer, "allowedUse")) ||
                string.IsNullOrWhiteSpace(GetString(consumer, "forbiddenClaim")))
            {
                errors.Add("Each downstream consumer requires featureId, dimensionId, allowedUse, and forbiddenClaim.");
            }
        }
    }

    private static void RequireValue(JsonObject value, string property, string expected, List<string> errors)
    {
        var observed = GetString(value, property);
        if (!string.Equals(observed, expected, StringComparison.Ordinal))
        {
            errors.Add($"{property} must be {expected}; observed {observed}.");
        }
    }

    private static void RequireIntValue(JsonObject value, string property, int expected, List<string> errors)
    {
        var observed = GetInt(value, property, int.MinValue);
        if (observed != expected)
        {
            errors.Add($"{property} must be {expected}; observed {observed}.");
        }
    }

    private static void RequireBoolValue(JsonObject value, string property, bool expected, List<string> errors)
    {
        var observed = GetBool(value, property, !expected);
        if (observed != expected)
        {
            errors.Add($"{property} must be {expected}; observed {observed}.");
        }
    }
}
