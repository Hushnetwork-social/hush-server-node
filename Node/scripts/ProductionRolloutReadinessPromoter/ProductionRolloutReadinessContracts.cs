using System.Text.Json.Nodes;

namespace ProductionRolloutReadinessPromoter;

public static partial class ProductionRolloutReadinessContracts
{
    public const string FeatureId = "FEAT-148";
    public const string SourceSchemaVersion = "production-rollout-readiness-source.v1";
    public const string CurrentRegisterId = "RDY-REG-v0.1.4";
    public const string ProductionBlockerId = "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001";
    public const string PublicStateBlockerId = "RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001";
    public const int AmberMinimumScore = 80;
    public const int FullAllowedRecommendedScore = 85;

    public static readonly string[] RequiredSchemaFiles =
    [
        "production-rollout-readiness-source.schema.json",
    ];

    public static JsonObject LoadSource(
        ProductionRolloutReadinessPromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "production rollout source");
        return ReadJsonObject(sourcePath, ProductionRolloutReadinessPromotionPaths.SourceFileName);
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
        var errors = ValidateJsonRequired(source, ProductionRolloutReadinessPromotionPaths.SourceFileName, [
            "schemaVersion",
            "sourceId",
            "featureId",
            "acceptanceGates",
            "status",
            "baselineRegister",
            "rolloutProfile",
            "scorePolicy",
            "runEvidence",
            "operationalEvidence",
            "deploymentProofEvidence",
            "webClientProofEvidence",
            "governedOutcomeEvidence",
            "upstreamEvidence",
            "claimPolicy",
            "blockerDecisions",
            "restrictedEvidenceRefs",
            "publicArtifactSamples",
            "signoff",
        ]).ToList();

        RequireValue(source, "schemaVersion", SourceSchemaVersion, errors);
        RequireValue(source, "featureId", FeatureId, errors);
        ValidateBaselineRegister(source, errors);
        ValidateScorePolicy(source, errors);
        ValidateEvidenceGroups(source, errors);
        ValidateClaimPolicy(source, errors);
        ValidateBlockerDecisions(source, errors);
        ValidateRestrictedRefs(source, errors);
        ValidatePublicSamples(source, errors);

        return errors;
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new ProductionRolloutReadinessPromotionException($"{label} is not a JSON object.");
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

        throw new ProductionRolloutReadinessPromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new ProductionRolloutReadinessPromotionException($"Missing array property: {property}");
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
            throw new ProductionRolloutReadinessPromotionException(
                "Production rollout readiness path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    public static string ResolveSourceInput(
        ProductionRolloutReadinessPromotionPaths paths,
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
            ? Path.Combine(fullPath, ProductionRolloutReadinessPromotionPaths.SourceFileName)
            : fullPath;
    }

}
