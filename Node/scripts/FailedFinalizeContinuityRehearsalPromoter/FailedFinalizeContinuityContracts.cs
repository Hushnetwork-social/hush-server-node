using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FailedFinalizeContinuityRehearsalPromoter;

public static class FailedFinalizeContinuityContracts
{
    public const string FeatureId = "FEAT-155";
    public const string SourceSchemaVersion = "failed-finalize-continuity-source.v1";
    public const string CurrentRegisterId = "RDY-REG-v0.1.5";
    public const string DimensionId = "RDY-DIM-009";
    public const string PromotionOwner = "FEAT-156";
    public const string CanonicalizationVersion = "hush-json-canonical-v1";

    private static readonly string[] RequiredSourceProperties =
    [
        "schemaVersion",
        "sourceId",
        "featureId",
        "status",
        "generatedAt",
        "baselineRegister",
        "productionLikeRunContext",
        "governedOutcome",
        "noCleanResult",
        "publicSafeStatus",
        "restrictedEvidenceRefs",
        "packageValidation",
        "readinessProposal",
        "downstreamHandoff",
        "publicArtifactSamples",
        "residualRisks",
        "signoff",
    ];

    public static JsonObject ReadJsonObject(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new InvalidOperationException($"{path} is not a JSON object.");
    }

    public static JsonObject LoadSource(FailedFinalizeContinuityPromotionPaths paths, string? sourceInput = null)
    {
        var sourcePath = string.IsNullOrWhiteSpace(sourceInput)
            ? paths.DefaultSourceInput
            : sourceInput;
        var fullPath = Path.IsPathRooted(sourcePath)
            ? sourcePath
            : Path.Combine(paths.WorkspaceRoot, sourcePath);

        return ReadJsonObject(Path.GetFullPath(fullPath));
    }

    public static IReadOnlyList<string> ValidateSchemaSet(string schemasRoot)
    {
        var errors = new List<string>();
        if (!Directory.Exists(schemasRoot))
        {
            return [$"Schema root does not exist: {schemasRoot}"];
        }

        var schemaFiles = Directory.GetFiles(schemasRoot, "*.schema.json", SearchOption.TopDirectoryOnly);
        if (schemaFiles.Length == 0)
        {
            errors.Add($"No schema files were found in {schemasRoot}.");
        }

        foreach (var schemaFile in schemaFiles.OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                var schema = ReadJsonObject(schemaFile);
                if (!schema.ContainsKey("$schema") || !schema.ContainsKey("title"))
                {
                    errors.Add($"{schemaFile} is missing $schema or title.");
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
            {
                errors.Add($"{schemaFile} is not a readable JSON schema: {ex.Message}");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSource(JsonObject source)
    {
        var errors = ValidateRequired(source, "failed-finalize continuity source", RequiredSourceProperties).ToList();

        RequireValue(source, "schemaVersion", SourceSchemaVersion, errors);
        RequireValue(source, "featureId", FeatureId, errors);
        RequireValue(source, "status", "accepted", errors);
        ValidateBaselineRegister(source, errors);
        ValidateProductionLikeRunContext(source, errors);
        ValidateGovernedOutcome(source, errors);
        ValidateNoCleanResult(source, errors);
        ValidatePublicSafeStatus(source, errors);
        ValidatePackageValidation(source, errors);
        ValidateReadinessProposal(source, errors);
        ValidateDownstreamHandoff(source, errors);

        return errors;
    }

    public static IReadOnlyList<string> ValidateRequired(
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

    public static JsonObject? TryObject(JsonObject value, string property, ICollection<string>? errors = null)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonObject obj)
        {
            return obj;
        }

        errors?.Add($"{property} must be an object.");
        return null;
    }

    public static JsonArray? TryArray(JsonObject value, string property, ICollection<string>? errors = null)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        errors?.Add($"{property} must be an array.");
        return null;
    }

    public static string GetString(JsonObject? value, string property, string fallback = "")
    {
        if (value is null ||
            !value.TryGetPropertyValue(property, out var node) ||
            node is null)
        {
            return fallback;
        }

        return node.GetValue<string>();
    }

    public static int GetInt(JsonObject? value, string property, int fallback = 0)
    {
        if (value is null ||
            !value.TryGetPropertyValue(property, out var node) ||
            node is null)
        {
            return fallback;
        }

        return node.GetValue<int>();
    }

    public static bool GetBool(JsonObject? value, string property, bool fallback = false)
    {
        if (value is null ||
            !value.TryGetPropertyValue(property, out var node) ||
            node is null)
        {
            return fallback;
        }

        return node.GetValue<bool>();
    }

    public static IReadOnlyList<string> GetStringArray(JsonObject? value, string property)
    {
        if (value is null ||
            !value.TryGetPropertyValue(property, out var node) ||
            node is not JsonArray array)
        {
            return [];
        }

        return array
            .Select(item => item?.GetValue<string>() ?? "")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    public static bool HasArrayItems(JsonObject? value, string property) =>
        value is not null &&
        value.TryGetPropertyValue(property, out var node) &&
        node is JsonArray array &&
        array.Count > 0;

    public static bool IsNull(JsonObject? value, string property) =>
        value is null ||
        !value.TryGetPropertyValue(property, out var node) ||
        node is null;

    public static JsonNode? Clone(JsonNode? value) =>
        value?.DeepClone();

    public static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());

    public static string CanonicalJson(JsonNode node) =>
        node.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        }) + Environment.NewLine;

    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n").Replace("\r", "\n");

    public static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeLineEndings(value)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    public static void EnsurePathUnder(string root, string candidate, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new FailedFinalizeContinuityPromotionException(
                "FEAT-155 failed-finalize continuity path validation failed.",
                [$"{label} resolves outside expected root: {fullCandidate}"]);
        }
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

        if (GetInt(register, "currentScore") != 6 ||
            GetInt(register, "targetScore") != 8)
        {
            errors.Add("baselineRegister must preserve RDY-DIM-009 6 -> 8.");
        }
    }

    private static void ValidateProductionLikeRunContext(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "productionLikeRunContext", errors) is not { } context)
        {
            return;
        }

        RequireValue(context, "sourceFeature", "FEAT-154", errors, "productionLikeRunContext");
        if (!GetBool(context, "contextOnly"))
        {
            errors.Add("productionLikeRunContext must be contextOnly for FEAT-155.");
        }
    }

    private static void ValidateGovernedOutcome(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "governedOutcome", errors) is not { } outcome)
        {
            return;
        }

        RequireValue(outcome, "decisionType", "record_failed_finalize_continuity", errors, "governedOutcome");
        RequireValue(outcome, "outcomeStatus", "failed_to_finalize", errors, "governedOutcome");
        RequireValue(outcome, "finalizationMode", "failed_finalization", errors, "governedOutcome");
        RequireValue(outcome, "previousLifecycleState", "Closed", errors, "governedOutcome");
        RequireValue(outcome, "resultingLifecycleState", "Closed", errors, "governedOutcome");
        RequireValue(outcome, "authorityRole", "ElectionOwner", errors, "governedOutcome");
        RequireValue(outcome, "authoritySource", "FEAT-140", errors, "governedOutcome");

        foreach (var property in new[]
                 {
                     "feat140HandoffRef",
                     "feat140HandoffHash",
                     "authorityDecisionRef",
                     "authorityDecisionHash",
                     "governanceRuleRef",
                     "closeBoundaryRef",
                     "publicSummary",
                 })
        {
            if (string.IsNullOrWhiteSpace(GetString(outcome, property)))
            {
                errors.Add($"governedOutcome.{property} is required.");
            }
        }

        if (GetBool(outcome, "cleanFinalization", true))
        {
            errors.Add("governedOutcome.cleanFinalization must be false.");
        }

        if (!IsNull(outcome, "officialResultRef") ||
            !IsNull(outcome, "finalizeBoundaryRef"))
        {
            errors.Add("governedOutcome must not reference official result or finalize boundary artifacts.");
        }
    }

    private static void ValidateNoCleanResult(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "noCleanResult", errors) is not { } noCleanResult)
        {
            return;
        }

        if (GetBool(noCleanResult, "officialResultArtifactPresent") ||
            GetBool(noCleanResult, "cleanFinalPackagePresent") ||
            GetBool(noCleanResult, "finalizeBoundaryArtifactPresent"))
        {
            errors.Add("noCleanResult must prove that no official result, clean package, or finalize boundary exists.");
        }

        RequireValue(noCleanResult, "verifierResultCode", "failed_finalize_continuity_valid", errors, "noCleanResult");
    }

    private static void ValidatePublicSafeStatus(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "publicSafeStatus", errors) is not { } status)
        {
            return;
        }

        RequireValue(status, "outcomeStatus", "failed_to_finalize", errors, "publicSafeStatus");
        RequireValue(status, "packageStatus", "accepted", errors, "publicSafeStatus");
        if (GetBool(status, "containsRestrictedDetails", true))
        {
            errors.Add("publicSafeStatus must not contain restricted details.");
        }
    }

    private static void ValidatePackageValidation(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "packageValidation", errors) is not { } validation)
        {
            return;
        }

        RequireValue(validation, "status", "accepted", errors, "packageValidation");
        RequireValue(validation, "publicSafetyScan", "passed", errors, "packageValidation");
        RequireValue(validation, "packageHashValidation", "passed", errors, "packageValidation");

        if (HasArrayItems(validation, "blockedBy"))
        {
            errors.Add("packageValidation.blockedBy must be empty.");
        }
    }

    private static void ValidateReadinessProposal(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "readinessProposal", errors) is not { } proposal)
        {
            return;
        }

        RequireValue(proposal, "featureId", FeatureId, errors, "readinessProposal");
        RequireValue(proposal, "dimensionId", DimensionId, errors, "readinessProposal");
        RequireValue(proposal, "status", "accepted", errors, "readinessProposal");
        RequireValue(proposal, "promotionOwner", PromotionOwner, errors, "readinessProposal");

        if (GetInt(proposal, "proposedScoreFrom") != 6 ||
            GetInt(proposal, "proposedScoreTo") != 8 ||
            !GetBool(proposal, "scoreChangeAllowed") ||
            GetBool(proposal, "directRegisterMutation", true) ||
            HasArrayItems(proposal, "blockedBy"))
        {
            errors.Add("readinessProposal must preserve RDY-DIM-009 6 -> 8 with no direct register mutation.");
        }
    }

    private static void ValidateDownstreamHandoff(JsonObject source, List<string> errors)
    {
        if (TryObject(source, "downstreamHandoff", errors) is not { } handoff)
        {
            return;
        }

        RequireValue(handoff, "featureId", FeatureId, errors, "downstreamHandoff");
        RequireValue(handoff, "status", "accepted", errors, "downstreamHandoff");
        RequireValue(handoff, "registerPromotionOwner", PromotionOwner, errors, "downstreamHandoff");

        if (GetBool(handoff, "directRegisterMutation", true))
        {
            errors.Add("downstreamHandoff.directRegisterMutation must be false.");
        }

        var consumers = GetStringArray(handoff, "consumers").ToHashSet(StringComparer.Ordinal);
        foreach (var required in new[] { "FEAT-148", "FEAT-156" })
        {
            if (!consumers.Contains(required))
            {
                errors.Add($"downstreamHandoff.consumers must include {required}.");
            }
        }
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
