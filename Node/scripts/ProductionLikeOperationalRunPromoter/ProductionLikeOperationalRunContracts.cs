using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static partial class ProductionLikeOperationalRunContracts
{
    public const string FeatureId = "FEAT-154";
    public const string SourceSchemaVersion = "production-like-operational-run-source.v1";
    public const string FixtureCatalogSchemaVersion = "production-like-operational-run-fixture-catalog.v1";
    public const string CurrentRegisterId = "RDY-REG-v0.1.5";
    public const string DimensionId = "RDY-DIM-007";
    public const string PromotionOwner = "FEAT-156";

    public static readonly string[] RequiredSchemaFiles =
    [
        "production-like-operational-run-source.schema.json",
    ];

    private static readonly string[] RequiredSourceProperties =
    [
        "schemaVersion",
        "sourceId",
        "featureId",
        "acceptanceGates",
        "status",
        "generatedAt",
        "baselineRegister",
        "runProfile",
        "dataScope",
        "deploymentProof",
        "runtimeBinding",
        "webClientObservation",
        "operationalEvidence",
        "securitySupportFreshness",
        "pilotLineage",
        "productionRolloutGateSource",
        "monitoring",
        "support",
        "backupRestore",
        "incidentDeclaration",
        "operatorHandoff",
        "postmortem",
        "publicSafety",
        "restrictedEvidenceRefs",
        "readinessProposal",
        "downstreamHandoff",
        "signoff",
        "residualRisks",
    ];

    public static JsonObject LoadSource(
        ProductionLikeOperationalRunPromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "production-like operational run source");
        return ReadJsonObject(sourcePath, ProductionLikeOperationalRunPromotionPaths.SourceFileName);
    }

    public static JsonObject LoadFixtureCatalog(ProductionLikeOperationalRunPromotionPaths paths) =>
        ReadJsonObject(paths.FixtureCatalogPath, ProductionLikeOperationalRunPromotionPaths.FixtureCatalogFileName);

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
        var errors = ValidateJsonRequired(
            source,
            ProductionLikeOperationalRunPromotionPaths.SourceFileName,
            RequiredSourceProperties).ToList();

        RequireValue(source, "schemaVersion", SourceSchemaVersion, errors);
        RequireValue(source, "featureId", FeatureId, errors);
        ValidateBaselineRegister(source, errors);
        ValidateRunProfile(source, errors);
        ValidateDataScope(source, errors);
        ValidateReadinessProposal(source, errors);
        ValidateDownstreamHandoff(source, errors);
        ValidateRestrictedRefs(source, errors);
        ValidatePublicSafety(source, errors);

        return errors;
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new ProductionLikeOperationalRunPromotionException($"{label} is not a JSON object.");
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

        throw new ProductionLikeOperationalRunPromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new ProductionLikeOperationalRunPromotionException($"Missing array property: {property}");
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

    public static bool HasArrayItems(JsonObject value, string property) =>
        value.TryGetPropertyValue(property, out var node) &&
        node is JsonArray array &&
        array.Count > 0;

    public static void EnsurePathUnder(string root, string candidate, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProductionLikeOperationalRunPromotionException(
                "Production-like operational run path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    public static string ResolveSourceInput(
        ProductionLikeOperationalRunPromotionPaths paths,
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
            ? Path.Combine(fullPath, ProductionLikeOperationalRunPromotionPaths.SourceFileName)
            : fullPath;
    }

}
