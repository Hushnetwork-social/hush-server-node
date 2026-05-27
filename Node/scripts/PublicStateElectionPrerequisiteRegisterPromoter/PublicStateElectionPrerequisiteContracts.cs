using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublicStateElectionPrerequisiteRegisterPromoter;

public static class PublicStateElectionPrerequisiteContracts
{
    public const string FeatureId = "FEAT-149";
    public const string SourceSchemaVersion = "public-state-prerequisite-register.v1";
    public const string CurrentRegisterId = "RDY-REG-v0.1.4";
    public const string ProductionBlockerId = "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001";
    public const string PublicStateBlockerId = "RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001";

    public static readonly string[] RequiredSchemaFiles =
    [
        "public-state-prerequisite-register.schema.json",
    ];

    public static readonly string[] RequiredPrerequisiteGroupIds =
    [
        "target_jurisdiction_and_election_type",
        "competent_election_authority",
        "applicable_law_and_legal_interpretation",
        "certification_testing_or_not_applicable",
        "independent_audit_examination",
        "accessibility_usability_language_assistance",
        "transparency_observer_chain_of_custody",
        "voter_eligibility_registration_identity_roll_custody",
        "ballot_secrecy_coercion_remote_device_policy",
        "dispute_recount_challenge_remedy_finality",
        "records_retention_public_records_privacy_residency_archival",
        "procurement_vendor_sla_insurance_accountability",
    ];

    private static readonly string[] RequiredGroupFields =
    [
        "groupId",
        "label",
        "ownerCategory",
        "evidenceType",
        "mandatory",
        "status",
        "validationSource",
        "blockerImpact",
        "claimImpact",
        "evidenceRefs",
        "blockerIds",
        "publicSafeWording",
        "futureResolutionCriteria",
    ];

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static JsonObject LoadSource(
        PublicStateElectionPrerequisitePromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "public/state prerequisite source");
        return ReadJsonObject(sourcePath, PublicStateElectionPrerequisitePromotionPaths.SourceFileName);
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
        var errors = ValidateJsonRequired(source, PublicStateElectionPrerequisitePromotionPaths.SourceFileName, [
            "schemaVersion",
            "sourceId",
            "featureId",
            "acceptanceGates",
            "status",
            "baselineRegister",
            "claimBoundary",
            "excludedMeanings",
            "feat148Dependency",
            "scorePolicy",
            "prerequisiteGroups",
            "externalReferences",
            "blockerPolicy",
            "publicSafeWording",
            "readinessFragmentPolicy",
            "publicArtifactSamples",
            "signoff",
        ]).ToList();

        RequireValue(source, "schemaVersion", SourceSchemaVersion, errors);
        RequireValue(source, "featureId", FeatureId, errors);
        ValidateBaselineRegister(source, errors);
        ValidateClaimBoundary(source, errors);
        ValidateFeat148Dependency(source, errors);
        ValidateScorePolicy(source, errors);
        ValidatePrerequisiteGroups(source, errors);
        ValidateBlockerPolicy(source, errors);
        ValidateReadinessFragmentPolicy(source, errors);
        ValidatePublicSafeWording(source, errors);

        return errors;
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new PublicStateElectionPrerequisitePromotionException($"{label} is not a JSON object.");
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

        throw new PublicStateElectionPrerequisitePromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new PublicStateElectionPrerequisitePromotionException($"Missing array property: {property}");
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

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    public static string CanonicalJson(JsonNode node) =>
        NormalizeLineEndings(node.ToJsonString(CanonicalJsonOptions)) + "\n";

    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    public static string ResolveSourceInput(
        PublicStateElectionPrerequisitePromotionPaths paths,
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
            ? Path.Combine(fullPath, PublicStateElectionPrerequisitePromotionPaths.SourceFileName)
            : fullPath;
    }

    public static void EnsurePathUnder(string root, string candidate, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new PublicStateElectionPrerequisitePromotionException(
                "Public/state prerequisite path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    private static void ValidateBaselineRegister(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "baselineRegister", errors) is not { } register)
        {
            return;
        }

        RequireValue(register, "registerVersionId", CurrentRegisterId, errors, "baselineRegister");
        RequireValue(register, "strongestAllowedClaim", "friendly_organization_pilot", errors, "baselineRegister");
        if (GetInt(register, "totalScore") != 71)
        {
            errors.Add("baselineRegister.totalScore must match RDY-REG-v0.1.4 score 71.");
        }

        ValidateBlockerState(register, "productionBlocker", ProductionBlockerId, errors);
        ValidateBlockerState(register, "publicStateBlocker", PublicStateBlockerId, errors);
    }

    private static void ValidateBlockerState(
        JsonObject source,
        string property,
        string expectedBlockerId,
        List<string> errors)
    {
        if (TryObject(source, property, errors) is not { } blocker)
        {
            return;
        }

        RequireValue(blocker, "blockerId", expectedBlockerId, errors, property);
        if (property == "publicStateBlocker")
        {
            RequireValue(blocker, "severity", "red", errors, property);
            RequireValue(blocker, "status", "open", errors, property);
        }
    }

    private static void ValidateClaimBoundary(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "claimBoundary", errors) is not { } boundary)
        {
            return;
        }

        RequireValue(boundary, "claimLevel", "public_or_state_election", errors, "claimBoundary");
        RequireValue(boundary, "publicSafeStatus", "public_claim_blocked", errors, "claimBoundary");
        foreach (var itemName in new[] { "targetJurisdiction", "electionType", "competentAuthority" })
        {
            if (TryObject(boundary, itemName, errors, $"claimBoundary.{itemName}") is not { } item)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(GetString(item, "status")))
            {
                errors.Add($"claimBoundary.{itemName}.status is required.");
            }

            if (!item.ContainsKey("evidenceRefs"))
            {
                errors.Add($"claimBoundary.{itemName}.evidenceRefs is required.");
            }
        }
    }

    private static void ValidateFeat148Dependency(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "feat148Dependency", errors) is not { } dependency)
        {
            return;
        }

        RequireValue(dependency, "featureId", "FEAT-148", errors, "feat148Dependency");
        RequireValue(dependency, "dependencyType", "necessary_but_not_sufficient", errors, "feat148Dependency");
        RequireValue(dependency, "sufficiency", "not_sufficient", errors, "feat148Dependency");
        if (string.IsNullOrWhiteSpace(GetString(dependency, "currentStatus")))
        {
            errors.Add("feat148Dependency.currentStatus is required.");
        }
    }

    private static void ValidateScorePolicy(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "scorePolicy", errors) is not { } policy)
        {
            return;
        }

        if (GetBool(policy, "scoreChangeAllowed", true))
        {
            errors.Add("FEAT149-SCORE-MOVEMENT-FORBIDDEN: scorePolicy.scoreChangeAllowed must be false.");
        }

        if (GetBool(policy, "directRegisterMutation", true))
        {
            errors.Add("FEAT149-DIRECT-REGISTER-MUTATION-FORBIDDEN: scorePolicy.directRegisterMutation must be false.");
        }

        if (GetInt(policy, "currentTotalScore") != GetInt(policy, "proposedTotalScore"))
        {
            errors.Add("scorePolicy.proposedTotalScore must equal currentTotalScore in FEAT-149 v1.");
        }

        RequireValue(policy, "registerPromotionOwner", "FEAT-130", errors, "scorePolicy");
    }

    private static void ValidatePrerequisiteGroups(JsonObject source, List<string> errors)
    {
        var groupsSource = TryArray(source, "prerequisiteGroups", errors);
        if (groupsSource is null)
        {
            return;
        }

        var groups = groupsSource.OfType<JsonObject>().ToArray();
        var groupIds = groups.Select(group => GetString(group, "groupId")).ToHashSet(StringComparer.Ordinal);
        foreach (var requiredGroupId in RequiredPrerequisiteGroupIds)
        {
            if (!groupIds.Contains(requiredGroupId))
            {
                errors.Add($"Missing prerequisite group {requiredGroupId}.");
            }
        }

        var externalReferenceIds = GetExternalReferenceIds(source);
        foreach (var group in groups)
        {
            var groupId = GetString(group, "groupId", "unknown");
            foreach (var requiredField in RequiredGroupFields)
            {
                if (!group.ContainsKey(requiredField) || group[requiredField] is null)
                {
                    errors.Add($"prerequisiteGroups[{groupId}].{requiredField} is required.");
                }
            }

            if (string.IsNullOrWhiteSpace(GetString(group, "ownerCategory")))
            {
                errors.Add($"prerequisiteGroups[{groupId}].ownerCategory is required.");
            }

            if (string.IsNullOrWhiteSpace(GetString(group, "evidenceType")))
            {
                errors.Add($"prerequisiteGroups[{groupId}].evidenceType is required.");
            }

            foreach (var evidenceRef in GetStringArray(group, "evidenceRefs"))
            {
                if (!externalReferenceIds.Contains(evidenceRef))
                {
                    errors.Add($"prerequisiteGroups[{groupId}] references unknown evidence {evidenceRef}.");
                }
            }
        }
    }

    private static void ValidateBlockerPolicy(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "blockerPolicy", errors) is not { } policy)
        {
            return;
        }

        RequireValue(policy, "publicStateBlockerId", PublicStateBlockerId, errors, "blockerPolicy");
        RequireValue(policy, "currentSeverity", "red", errors, "blockerPolicy");
        RequireValue(policy, "currentStatus", "open", errors, "blockerPolicy");
        RequireValue(policy, "v1Decision", "keep_policy_blocked", errors, "blockerPolicy");
        if (GetStringArray(policy, "requiredFutureResolution").Count == 0)
        {
            errors.Add("blockerPolicy.requiredFutureResolution must name future prerequisite requirements.");
        }
    }

    private static void ValidateReadinessFragmentPolicy(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "readinessFragmentPolicy", errors) is not { } policy)
        {
            return;
        }

        if (GetBool(policy, "scoreChangeAllowed", true))
        {
            errors.Add("readinessFragmentPolicy.scoreChangeAllowed must be false.");
        }

        if (GetBool(policy, "directRegisterMutation", true))
        {
            errors.Add("readinessFragmentPolicy.directRegisterMutation must be false.");
        }
    }

    private static void ValidatePublicSafeWording(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "publicSafeWording", errors) is not { } wording)
        {
            return;
        }

        if (GetStringArray(wording, "forbiddenPublicClaims").Count == 0)
        {
            errors.Add("publicSafeWording.forbiddenPublicClaims must not be empty.");
        }

        var publicSamples = TryArray(source, "publicArtifactSamples", errors);
        if (publicSamples is null)
        {
            return;
        }

        foreach (var sample in publicSamples.OfType<JsonObject>())
        {
            var content = GetString(sample, "content");
            foreach (var forbiddenClaim in PublicStateElectionPrerequisiteGateChecker.ForbiddenPublicClaimNeedles)
            {
                if (content.Contains(forbiddenClaim, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"FEAT149-PUBLIC-SAFE-WORDING-OVERCLAIM: public output contains '{forbiddenClaim}'.");
                }
            }

            foreach (var forbiddenMaterial in PublicStateElectionPrerequisiteGateChecker.ForbiddenRestrictedMaterialNeedles)
            {
                if (content.Contains(forbiddenMaterial, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"public output contains restricted material '{forbiddenMaterial}'.");
                }
            }
        }
    }

    private static HashSet<string> GetExternalReferenceIds(JsonObject source)
    {
        if (!source.TryGetPropertyValue("externalReferences", out var node) || node is not JsonArray refs)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return refs
            .OfType<JsonObject>()
            .Select(item => GetString(item, "evidenceId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static JsonObject? TryObject(
        JsonObject source,
        string property,
        List<string> errors,
        string? label = null)
    {
        if (source.TryGetPropertyValue(property, out var node) && node is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"{label ?? property} must be an object.");
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
