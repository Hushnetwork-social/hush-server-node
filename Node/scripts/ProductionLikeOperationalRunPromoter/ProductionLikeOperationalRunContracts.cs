using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static class ProductionLikeOperationalRunContracts
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

    private static void ValidateBaselineRegister(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "baselineRegister", errors) is not { } register)
        {
            return;
        }

        RequireValue(register, "registerVersionId", CurrentRegisterId, errors, "baselineRegister");
        RequireValue(register, "registerStatus", "AcceptedInternal", errors, "baselineRegister");
        RequireValue(register, "dimensionId", DimensionId, errors, "baselineRegister");
        RequireValue(register, "strongestAllowedClaim", "friendly_organization_pilot", errors, "baselineRegister");

        if (GetInt(register, "totalScore") != 71 ||
            GetInt(register, "currentScore") != 6 ||
            GetInt(register, "targetScore") != 8)
        {
            errors.Add("baselineRegister must preserve RDY-REG-v0.1.5 score 71 and RDY-DIM-007 6 -> 8 target.");
        }
    }

    private static void ValidateRunProfile(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "runProfile", errors) is not { } profile)
        {
            return;
        }

        RequireValue(profile, "profileId", "controlled-hush-managed-staging-aws-like-v1", errors, "runProfile");
        RequireValue(profile, "environmentClass", "controlled_hush_managed_staging_aws_like", errors, "runProfile");
        RequireValue(profile, "deploymentProfile", "hush_saas_v1", errors, "runProfile");

        if (GetBool(profile, "localOnly") ||
            GetBool(profile, "privateChainOnly") ||
            GetBool(profile, "uncontrolledProduction") ||
            !GetBool(profile, "syntheticOrNonConfidentialData"))
        {
            errors.Add("runProfile must use controlled Hush-managed staging/AWS-like infrastructure with non-confidential data.");
        }
    }

    private static void ValidateDataScope(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "dataScope", errors) is not { } dataScope)
        {
            return;
        }

        if (GetBool(dataScope, "containsRealVoterPersonalData") ||
            GetBool(dataScope, "containsVoteChoiceData"))
        {
            errors.Add("dataScope cannot include real voter personal data or vote-choice data.");
        }
    }

    private static void ValidateReadinessProposal(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "readinessProposal", errors) is not { } proposal)
        {
            return;
        }

        RequireValue(proposal, "dimensionId", DimensionId, errors, "readinessProposal");
        RequireValue(proposal, "promotionOwner", PromotionOwner, errors, "readinessProposal");

        if (GetInt(proposal, "proposedScoreFrom") != 6 ||
            GetInt(proposal, "proposedScoreTo") != 8 ||
            !GetBool(proposal, "doesNotMutateRegister") ||
            GetBool(proposal, "directRegisterMutation", true) ||
            !GetBool(proposal, "scoreChangeRequiresPromotion"))
        {
            errors.Add("readinessProposal must preserve RDY-DIM-007 6 -> 8 with no direct register mutation.");
        }
    }

    private static void ValidateDownstreamHandoff(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "downstreamHandoff", errors) is not { } handoff)
        {
            return;
        }

        var targetFeatures = GetStringArray(handoff, "targetFeatures").ToHashSet(StringComparer.Ordinal);
        foreach (var required in new[] { "FEAT-148", "FEAT-155", "FEAT-156" })
        {
            if (!targetFeatures.Contains(required))
            {
                errors.Add($"downstreamHandoff.targetFeatures must include {required}.");
            }
        }
    }

    private static void ValidateRestrictedRefs(JsonObject source, List<string> errors)
    {
        if (TryArray(source, "restrictedEvidenceRefs", errors) is not { } refs)
        {
            return;
        }

        foreach (var reference in refs.OfType<JsonObject>())
        {
            if (GetBool(reference, "payloadCopied", true))
            {
                errors.Add($"{GetString(reference, "refId", "restricted evidence")} must not copy payload bodies.");
            }
        }
    }

    private static void ValidatePublicSafety(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "publicSafety", errors) is not { } publicSafety)
        {
            return;
        }

        RequireValue(publicSafety, "visibility", "public_safe", errors, "publicSafety");
        if (GetInt(publicSafety, "expectedFindingCountInGeneratedPublicOutputs") != 0)
        {
            errors.Add("publicSafety.expectedFindingCountInGeneratedPublicOutputs must be 0.");
        }
    }

    private static JsonObject? TryObject(JsonObject source, string property, List<string> errors)
    {
        if (source.TryGetPropertyValue(property, out var node) && node is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"{property} must be an object.");
        return null;
    }

    private static JsonArray? TryArray(JsonObject source, string property, List<string> errors)
    {
        if (source.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        errors.Add($"{property} must be an array.");
        return null;
    }

    private static void RequireValue(
        JsonObject value,
        string property,
        string expected,
        List<string> errors,
        string? prefix = null)
    {
        if (!string.Equals(GetString(value, property), expected, StringComparison.Ordinal))
        {
            errors.Add($"{(prefix is null ? property : $"{prefix}.{property}")} must be {expected}.");
        }
    }
}
