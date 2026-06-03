using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DisputeContinuityMatrixPromoter;

public static class DisputeContinuityMatrixContracts
{
    public const string FeatureId = "FEAT-165";
    public const string SourceSchemaVersion = "dispute-continuity-matrix-source/v1";
    public const string CurrentRegisterVersionId = "RDY-REG-v0.1.7";
    public const string TargetDimensionId = "RDY-DIM-009";
    public const string TargetBlockerId = "RDY-BLOCK-INTERNAL_AUDIT_95_DIM009-001";
    public const string AllowedScoreMovement = "8 -> 9";

    public static readonly string[] RequiredSchemaFiles =
    [
        DisputeContinuityMatrixPromotionPaths.SourceSchemaFileName,
        DisputeContinuityMatrixPromotionPaths.PackageManifestSchemaFileName,
    ];

    public static readonly string[] RequiredScenarioFiles =
    [
        DisputeContinuityMatrixPromotionPaths.ScenarioCatalogFileName,
        DisputeContinuityMatrixPromotionPaths.ResultCodesFileName,
    ];

    public static readonly string[] RequiredExampleFiles =
    [
        Path.Combine("release-baseline", DisputeContinuityMatrixPromotionPaths.SourceFileName),
        Path.Combine("negative", DisputeContinuityMatrixPromotionPaths.NegativeFixtureCatalogFileName),
    ];

    public static readonly string[] RequiredScenarioFamilies =
    [
        "void",
        "failed-finalize",
        "finalized-with-anomaly",
        "replacement-publication",
        "verifier-challenge",
        "customer-remedy-boundary",
        "matrix-currentness",
    ];

    public static readonly string[] RequiredResultCodeSeverities =
    [
        "accepted",
        "limited",
        "warning",
        "blocking",
    ];

    private static readonly string[] RequiredAcceptedCurrentFeatures =
    [
        "FEAT-138",
        "FEAT-140",
        "FEAT-146",
        "FEAT-155",
        "FEAT-156",
    ];

    private static readonly IReadOnlyDictionary<string, ScenarioFamilyRule> ScenarioFamilyRules = new Dictionary<string, ScenarioFamilyRule>(StringComparer.Ordinal)
    {
        ["void"] = new(
            ["scenario_void_accepted"],
            ["missing_void_evidence", "void_publication_not_current"],
            ["FEAT-138"]),
        ["failed-finalize"] = new(
            ["scenario_failed_finalize_accepted"],
            ["missing_failed_finalize_evidence", "failed_finalize_has_clean_result"],
            ["FEAT-155"]),
        ["finalized-with-anomaly"] = new(
            ["scenario_finalized_with_anomaly_accepted"],
            ["missing_governed_outcome_evidence", "anomaly_overclaimed_as_clean_finalization"],
            ["FEAT-140", "FEAT-146"]),
        ["replacement-publication"] = new(
            ["replacement_publication_current", "superseded_package_not_current", "replay_binding_valid"],
            ["replacement_publication_missing", "superseded_package_still_current", "replay_binding_mismatch"],
            ["FEAT-138", "FEAT-160"]),
        ["verifier-challenge"] = new(
            ["verifier_challenge_accepted", "verifier_challenge_limited"],
            ["verifier_challenge_payload_leak", "verifier_challenge_result_unknown", "verifier_challenge_replay_mismatch"],
            ["FEAT-160"]),
        ["customer-remedy-boundary"] = new(
            ["customer_remedy_boundary_present", "legal_sufficiency_not_claimed"],
            ["customer_remedy_boundary_missing", "legal_sufficiency_overclaim", "customer_decision_payload_published"],
            ["FEAT-140"]),
        ["matrix-currentness"] = new(
            ["matrix_currentness_accepted", "stale_feat139_rejected"],
            ["readiness_baseline_mismatch", "stale_feat139_accepted_as_current", "score_proposal_overclaim", "direct_register_mutation_attempted"],
            ["FEAT-139", "FEAT-156"]),
    };

    private sealed record ScenarioFamilyRule(
        IReadOnlyList<string> ExpectedResultCodes,
        IReadOnlyList<string> FailureResultCodes,
        IReadOnlyList<string> RequiredUpstreamFeatures);

    public static JsonObject ValidateForPromotion(
        DisputeContinuityMatrixPromotionPaths paths,
        string? sourceInput,
        bool publicOnly)
    {
        var errors = new List<string>();
        errors.AddRange(ValidateSchemaSet(paths.SchemasRoot));
        errors.AddRange(ValidatePublicRepositorySet(paths));

        var source = LoadSource(paths, sourceInput);
        var resultCodes = LoadResultCodeSeverities(paths);
        var scenarioCatalogFamilies = LoadScenarioCatalogFamilies(paths);
        errors.AddRange(ValidateResultCodeCatalog(resultCodes));
        errors.AddRange(ValidateSource(source, publicOnly, resultCodes, scenarioCatalogFamilies));
        errors.AddRange(ValidateScenarioCatalog(paths, resultCodes));
        errors.AddRange(ValidateNegativeFixtures(paths, resultCodes.Keys.ToHashSet(StringComparer.Ordinal)));

        if (errors.Count > 0)
        {
            throw new DisputeContinuityMatrixPromotionException(
                "FEAT-165 dispute continuity matrix validation failed.",
                errors.Distinct(StringComparer.Ordinal).ToArray());
        }

        return source;
    }

    public static IReadOnlyList<string> ValidateSchemaSet(string schemasRoot)
    {
        var errors = new List<string>();
        foreach (var fileName in RequiredSchemaFiles)
        {
            var path = Path.Combine(schemasRoot, fileName);
            if (!File.Exists(path))
            {
                errors.Add($"DCM165_SCHEMA_MISSING: required schema is missing: {fileName}");
                continue;
            }

            try
            {
                var schema = ReadJsonObject(path, $"schema {fileName}");
                if (!schema.ContainsKey("$schema"))
                {
                    errors.Add($"DCM165_SCHEMA_INVALID: schema has no $schema field: {fileName}");
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"DCM165_SCHEMA_INVALID: schema {fileName} is not valid JSON: {ex.Message}");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidatePublicRepositorySet(DisputeContinuityMatrixPromotionPaths paths)
    {
        var errors = new List<string>();
        if (!Directory.Exists(paths.PublicRepositoryRoot))
        {
            errors.Add($"DCM165_PUBLIC_REPOSITORY_MISSING: {paths.PublicRepositoryRoot}");
            return errors;
        }

        foreach (var fileName in RequiredScenarioFiles)
        {
            var path = Path.Combine(paths.ScenariosRoot, fileName);
            if (!File.Exists(path))
            {
                errors.Add($"DCM165_SCENARIO_FILE_MISSING: {fileName}");
            }
            else
            {
                _ = ReadJsonObject(path, $"scenario file {fileName}");
            }
        }

        foreach (var fileName in RequiredExampleFiles)
        {
            var path = Path.Combine(paths.ExamplesRoot, fileName);
            if (!File.Exists(path))
            {
                errors.Add($"DCM165_EXAMPLE_FILE_MISSING: {fileName}");
            }
            else
            {
                _ = ReadJsonObject(path, $"example file {fileName}");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSource(
        JsonObject source,
        bool publicOnly = false,
        IReadOnlyDictionary<string, string>? resultCodeSeverities = null,
        IReadOnlyDictionary<string, JsonObject>? scenarioCatalogFamilies = null)
    {
        var errors = new List<string>();
        if (GetStringOrNull(source, "schemaVersion") != SourceSchemaVersion)
        {
            errors.Add("DCM165_SOURCE_SCHEMA_INVALID: source schemaVersion is not supported.");
        }

        if (GetStringOrNull(source, "featureId") != FeatureId)
        {
            errors.Add("DCM165_FEATURE_ID_INVALID: source featureId must be FEAT-165.");
        }

        ValidateReadinessBaseline(source, errors);
        ValidateScoreProposal(source, errors);
        ValidateUpstreamEvidence(source, errors);
        ValidateScenarioFamilies(source, errors, resultCodeSeverities, scenarioCatalogFamilies);
        ValidatePublicBoundary(source, publicOnly, errors);
        ValidateRestrictedEvidencePolicy(source, errors);
        ScanForbiddenMaterial(source, "$", errors);

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static JsonObject LoadSource(DisputeContinuityMatrixPromotionPaths paths, string? sourceInput = null)
    {
        var path = string.IsNullOrWhiteSpace(sourceInput) ? paths.DefaultSourcePath : sourceInput;
        return ReadJsonObject(path, "FEAT-165 source fixture");
    }

    public static JsonObject ReadJsonObject(string path, string description)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        if (node is not JsonObject obj)
        {
            throw new DisputeContinuityMatrixPromotionException(
                $"Expected {description} to be a JSON object.",
                [$"Path: {path}"]);
        }

        return obj;
    }

    public static string GetString(JsonObject obj, string propertyName)
    {
        var value = GetStringOrNull(obj, propertyName);
        if (value is null)
        {
            throw new DisputeContinuityMatrixPromotionException(
                "FEAT-165 generated package construction failed.",
                [$"Missing string property: {propertyName}"]);
        }

        return value;
    }

    public static JsonObject RequireObject(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonObject child)
        {
            return child;
        }

        throw new DisputeContinuityMatrixPromotionException(
            "FEAT-165 generated package construction failed.",
            [$"Missing object property: {propertyName}"]);
    }

    public static JsonArray RequireArray(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonArray child)
        {
            return child;
        }

        throw new DisputeContinuityMatrixPromotionException(
            "FEAT-165 generated package construction failed.",
            [$"Missing array property: {propertyName}"]);
    }

    public static string CanonicalJson(JsonNode node) => DisputeContinuityMatrixCanonicalJson.Serialize(node);

    public static string NormalizeLineEndings(string value) => DisputeContinuityMatrixCanonicalJson.NormalizeLineEndings(value);

    public static string Sha256Hex(string content) => DisputeContinuityMatrixCanonicalJson.ComputeSha256(content);

    public static string FileSha256Hex(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public static void EnsurePathUnder(string root, string candidate, string description)
    {
        var rootFullPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidateFullPath = Path.GetFullPath(candidate);
        if (!candidateFullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new DisputeContinuityMatrixPromotionException(
                "FEAT-165 path containment check failed.",
                [$"{description}: {candidateFullPath} is outside {rootFullPath}"]);
        }
    }

    public static JsonArray ToStringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static void ValidateReadinessBaseline(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "readinessBaseline", out var baseline))
        {
            errors.Add("DCM165_STALE_READINESS_BASELINE: readinessBaseline is missing.");
            return;
        }

        if (GetStringOrNull(baseline, "registerVersion") != CurrentRegisterVersionId ||
            GetStringOrNull(baseline, "dimension") != TargetDimensionId ||
            GetStringOrNull(baseline, "blocker") != TargetBlockerId ||
            GetIntOrNull(baseline, "currentScore") != 8 ||
            GetIntOrNull(baseline, "targetScore") != 9)
        {
            errors.Add("DCM165_STALE_READINESS_BASELINE: source must bind RDY-REG-v0.1.7 RDY-DIM-009 8 -> 9.");
        }
    }

    private static void ValidateScoreProposal(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "scoreProposal", out var proposal))
        {
            errors.Add("DCM165_SCORE_PROPOSAL_OVERCLAIM: scoreProposal is missing.");
            return;
        }

        if (GetStringOrNull(proposal, "dimension") != TargetDimensionId ||
            GetStringOrNull(proposal, "movement") != AllowedScoreMovement)
        {
            errors.Add("DCM165_SCORE_PROPOSAL_OVERCLAIM: FEAT-165 can only propose RDY-DIM-009 8 -> 9.");
        }

        if (GetBoolOrNull(proposal, "directRegisterMutation") != false)
        {
            errors.Add("DCM165_DIRECT_REGISTER_MUTATION_FORBIDDEN: FEAT-165 must not mutate the readiness register directly.");
        }
    }

    private static void ValidateUpstreamEvidence(JsonObject source, List<string> errors)
    {
        if (!TryGetArray(source, "upstreamEvidence", out var evidence) || evidence.Count == 0)
        {
            errors.Add("DCM165_UPSTREAM_EVIDENCE_MISSING: upstreamEvidence is missing.");
            return;
        }

        var byFeature = evidence
            .OfType<JsonObject>()
            .Where(item => !string.IsNullOrWhiteSpace(GetStringOrNull(item, "featureId")))
            .GroupBy(item => GetString(item, "featureId"), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var feature in RequiredAcceptedCurrentFeatures)
        {
            if (!byFeature.TryGetValue(feature, out var items) || items.Length == 0)
            {
                errors.Add($"DCM165_UPSTREAM_CURRENTNESS_BLOCKED: {feature} evidence is missing.");
                continue;
            }

            if (!items.Any(item => GetStringOrNull(item, "status") == "accepted-current"))
            {
                errors.Add($"DCM165_UPSTREAM_CURRENTNESS_BLOCKED: {feature} must be accepted-current.");
            }
        }

        if (!byFeature.TryGetValue("FEAT-160", out var feat160) || feat160.Length == 0)
        {
            errors.Add("DCM165_UPSTREAM_CURRENTNESS_BLOCKED: FEAT-160 context is missing.");
        }
        else if (!feat160.Any(item => GetStringOrNull(item, "status") is "context-only" or "accepted-current"))
        {
            errors.Add("DCM165_UPSTREAM_CURRENTNESS_BLOCKED: FEAT-160 must be context-only or accepted-current when verifier/replay semantics are claimed.");
        }

        if (!byFeature.TryGetValue("FEAT-139", out var feat139) || feat139.Length == 0)
        {
            errors.Add("DCM165_FEAT139_STALE_GATE_MISSING: FEAT-139 stale/currentness decision is missing.");
        }
        else
        {
            foreach (var item in feat139)
            {
                var status = GetStringOrNull(item, "status");
                var freshness = GetStringOrNull(item, "freshness") ?? string.Empty;
                if (status == "accepted-current")
                {
                    errors.Add("DCM165_STALE_FEAT139_ACCEPTED_AS_CURRENT: FEAT-139 is stale_after_feat146 and cannot be consumed as current accepted evidence.");
                }

                if (status is not ("historical-category-input" or "blocked-stale") ||
                    !freshness.Contains("stale_after_feat146", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("DCM165_FEAT139_STALE_GATE_MISSING: FEAT-139 must be historical/category or blocked-stale with stale_after_feat146 recorded.");
                }
            }
        }

        foreach (var item in evidence.OfType<JsonObject>())
        {
            if (TryGetArray(item, "evidenceRefs", out var refs))
            {
                foreach (var reference in refs.OfType<JsonObject>())
                {
                    ValidatePublicRef(reference, $"upstreamEvidence.{GetStringOrNull(item, "featureId")}", errors);
                }
            }
        }
    }

    private static void ValidateScenarioFamilies(
        JsonObject source,
        List<string> errors,
        IReadOnlyDictionary<string, string>? resultCodeSeverities,
        IReadOnlyDictionary<string, JsonObject>? scenarioCatalogFamilies)
    {
        if (!TryGetArray(source, "scenarioFamilies", out var families))
        {
            errors.Add("DCM165_SCENARIO_FAMILY_MISSING: scenarioFamilies is missing.");
            return;
        }

        var byFamily = families
            .OfType<JsonObject>()
            .Where(family => !string.IsNullOrWhiteSpace(GetStringOrNull(family, "family")))
            .GroupBy(family => GetString(family, "family"), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var family in RequiredScenarioFamilies)
        {
            if (!byFamily.TryGetValue(family, out var matches) || matches.Length == 0)
            {
                errors.Add($"DCM165_SCENARIO_FAMILY_MISSING: required scenario family is missing: {family}");
                continue;
            }

            if (matches.Length > 1)
            {
                errors.Add($"DCM165_SCENARIO_FAMILY_DUPLICATE: scenario family appears more than once: {family}");
                continue;
            }

            ValidateScenarioFamily(matches[0], resultCodeSeverities, scenarioCatalogFamilies, errors);
        }
    }

    private static void ValidateScenarioFamily(
        JsonObject family,
        IReadOnlyDictionary<string, string>? resultCodeSeverities,
        IReadOnlyDictionary<string, JsonObject>? scenarioCatalogFamilies,
        List<string> errors)
    {
        var familyName = GetStringOrNull(family, "family") ?? "<missing>";
        if (GetBoolOrNull(family, "required") != true)
        {
            errors.Add($"DCM165_SCENARIO_FAMILY_MISSING: scenario family must be required: {familyName}");
        }

        if (!ScenarioFamilyRules.TryGetValue(familyName, out var rule))
        {
            errors.Add($"DCM165_SCENARIO_FAMILY_UNKNOWN: scenario family is not recognized: {familyName}");
            return;
        }

        var expectedCodes = GetStringSet(family, "expectedResultCodes");
        var failureCodes = GetStringSet(family, "failureResultCodes");
        if (expectedCodes.Count == 0)
        {
            errors.Add($"DCM165_RESULT_CODE_MISSING: expectedResultCodes is missing for {familyName}.");
        }

        if (failureCodes.Count == 0)
        {
            errors.Add($"DCM165_RESULT_CODE_MISSING: failureResultCodes is missing for {familyName}.");
        }

        RequireCodes(familyName, "expectedResultCodes", expectedCodes, rule.ExpectedResultCodes, errors);
        RequireCodes(familyName, "failureResultCodes", failureCodes, rule.FailureResultCodes, errors);
        ValidateSourceResultCodeSemantics(familyName, "expectedResultCodes", expectedCodes, resultCodeSeverities, expectedFailure: false, errors);
        ValidateSourceResultCodeSemantics(familyName, "failureResultCodes", failureCodes, resultCodeSeverities, expectedFailure: true, errors);

        if (!TryGetArray(family, "evidenceRefs", out var evidenceRefs) || evidenceRefs.Count == 0)
        {
            errors.Add($"DCM165_SCENARIO_REF_MISSING: scenario family has no evidence refs: {familyName}");
        }
        else
        {
            foreach (var reference in evidenceRefs.OfType<JsonObject>())
            {
                ValidatePublicRef(reference, $"scenarioFamilies.{familyName}", errors);
            }

            foreach (var requiredFeature in rule.RequiredUpstreamFeatures)
            {
                if (!evidenceRefs.OfType<JsonObject>().Any(reference => ReferenceMentionsFeature(reference, requiredFeature)))
                {
                    errors.Add($"DCM165_SCENARIO_REF_MISSING: {familyName} requires {requiredFeature} evidence ref.");
                }
            }
        }

        if (scenarioCatalogFamilies is not null &&
            scenarioCatalogFamilies.TryGetValue(familyName, out var catalogFamily) &&
            TryGetArray(catalogFamily, "requiredUpstreamRefs", out var catalogRefs))
        {
            foreach (var requiredFeature in catalogRefs.Select(item => item?.GetValue<string>()).Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                if (!rule.RequiredUpstreamFeatures.Contains(requiredFeature, StringComparer.Ordinal))
                {
                    errors.Add($"DCM165_SCENARIO_CATALOG_INVALID: {familyName} catalog requires unsupported upstream ref: {requiredFeature}");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(GetStringOrNull(family, "publicSafeSummary")))
        {
            errors.Add($"DCM165_SCENARIO_PUBLIC_SUMMARY_MISSING: scenario family publicSafeSummary is missing: {familyName}");
        }

        switch (familyName)
        {
            case "replacement-publication":
                ValidateReplacementPublicationFamily(expectedCodes, failureCodes, errors);
                break;
            case "verifier-challenge":
                ValidateVerifierChallengeFamily(evidenceRefs, expectedCodes, failureCodes, errors);
                break;
            case "customer-remedy-boundary":
                ValidateCustomerRemedyBoundaryFamily(family, expectedCodes, failureCodes, errors);
                break;
        }
    }

    private static void RequireCodes(
        string familyName,
        string propertyName,
        IReadOnlySet<string> actual,
        IReadOnlyList<string> required,
        List<string> errors)
    {
        foreach (var code in required)
        {
            if (!actual.Contains(code))
            {
                errors.Add($"DCM165_RESULT_CODE_MISSING: {familyName} {propertyName} must include {code}.");
            }
        }
    }

    private static void ValidateSourceResultCodeSemantics(
        string familyName,
        string propertyName,
        IReadOnlySet<string> codes,
        IReadOnlyDictionary<string, string>? resultCodeSeverities,
        bool expectedFailure,
        List<string> errors)
    {
        if (resultCodeSeverities is null || resultCodeSeverities.Count == 0)
        {
            return;
        }

        foreach (var code in codes)
        {
            if (!resultCodeSeverities.TryGetValue(code, out var severity))
            {
                errors.Add($"DCM165_RESULT_CODE_UNKNOWN: {familyName} {propertyName} references unknown result code: {code}");
                continue;
            }

            if (expectedFailure && severity != "blocking")
            {
                errors.Add($"DCM165_RESULT_CODE_SEVERITY_INVALID: {familyName} failure result code must be blocking: {code}");
            }
        }
    }

    private static void ValidateReplacementPublicationFamily(
        IReadOnlySet<string> expectedCodes,
        IReadOnlySet<string> failureCodes,
        List<string> errors)
    {
        if (!expectedCodes.Contains("replacement_publication_current") ||
            !expectedCodes.Contains("superseded_package_not_current") ||
            !failureCodes.Contains("superseded_package_still_current"))
        {
            errors.Add("DCM165_REPLACEMENT_PUBLICATION_INVALID: VOID replacement-publication and supersession behavior must be explicit.");
        }

        if (!expectedCodes.Contains("replay_binding_valid") || !failureCodes.Contains("replay_binding_mismatch"))
        {
            errors.Add("DCM165_REPLAY_BINDING_MISMATCH: replacement-publication replay/package mismatch must fail closed.");
        }
    }

    private static void ValidateVerifierChallengeFamily(
        JsonArray evidenceRefs,
        IReadOnlySet<string> expectedCodes,
        IReadOnlySet<string> failureCodes,
        List<string> errors)
    {
        if (!expectedCodes.Contains("verifier_challenge_accepted") ||
            !expectedCodes.Contains("verifier_challenge_limited") ||
            !failureCodes.Contains("verifier_challenge_result_unknown"))
        {
            errors.Add("DCM165_VERIFIER_CHALLENGE_INVALID: verifier challenge accepted, limited, and unknown-result states must be explicit.");
        }

        if (!failureCodes.Contains("verifier_challenge_replay_mismatch"))
        {
            errors.Add("DCM165_REPLAY_BINDING_MISMATCH: verifier challenge replay mismatch must fail closed.");
        }

        if (!evidenceRefs.OfType<JsonObject>().Any(reference => GetStringOrNull(reference, "visibility") == "public" && ReferenceMentionsFeature(reference, "FEAT-160")))
        {
            errors.Add("DCM165_VERIFIER_CHALLENGE_INVALID: verifier challenge results must bind to the public FEAT-160 replay/package ref.");
        }
    }

    private static void ValidateCustomerRemedyBoundaryFamily(
        JsonObject family,
        IReadOnlySet<string> expectedCodes,
        IReadOnlySet<string> failureCodes,
        List<string> errors)
    {
        if (!expectedCodes.Contains("customer_remedy_boundary_present") ||
            !expectedCodes.Contains("legal_sufficiency_not_claimed") ||
            !failureCodes.Contains("legal_sufficiency_overclaim") ||
            !failureCodes.Contains("customer_decision_payload_published"))
        {
            errors.Add("DCM165_CUSTOMER_REMEDY_BOUNDARY_MISSING: customer-owned remedy boundary and non-claim gates must be explicit.");
        }

        var summary = GetStringOrNull(family, "publicSafeSummary") ?? string.Empty;
        if (!summary.Contains("technical proof", StringComparison.OrdinalIgnoreCase) ||
            !summary.Contains("customer", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("DCM165_CUSTOMER_REMEDY_BOUNDARY_MISSING: public summary must keep technical proof separate from customer remedy decisions.");
        }
    }

    private static void ValidatePublicBoundary(JsonObject source, bool publicOnly, List<string> errors)
    {
        if (!TryGetObject(source, "publicBoundary", out var boundary))
        {
            errors.Add("DCM165_PUBLIC_ONLY_PRIVATE_DEPENDENCY: publicBoundary is missing.");
            return;
        }

        if (GetBoolOrNull(boundary, "publicOnlyValidation") != true)
        {
            errors.Add("DCM165_PUBLIC_ONLY_PRIVATE_DEPENDENCY: public-only validation must be enabled.");
        }

        if (publicOnly && GetBoolOrNull(boundary, "publicOnlyValidation") != true)
        {
            errors.Add("DCM165_PUBLIC_ONLY_PRIVATE_DEPENDENCY: -PublicOnly cannot depend on private checkouts or credentials.");
        }
    }

    private static void ValidateRestrictedEvidencePolicy(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "restrictedEvidencePolicy", out var policy))
        {
            errors.Add("DCM165_RESTRICTED_PAYLOAD_FORBIDDEN: restrictedEvidencePolicy is missing.");
            return;
        }

        if (GetBoolOrNull(policy, "publicRefsOnly") != true || GetBoolOrNull(policy, "payloadsPublishedHere") != false)
        {
            errors.Add("DCM165_RESTRICTED_PAYLOAD_FORBIDDEN: restricted payloads must not be published.");
        }
    }

    public static IReadOnlyDictionary<string, string> LoadResultCodeSeverities(DisputeContinuityMatrixPromotionPaths paths)
    {
        var path = Path.Combine(paths.ScenariosRoot, DisputeContinuityMatrixPromotionPaths.ResultCodesFileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var catalog = ReadJsonObject(path, "result-code catalog");
        if (!TryGetArray(catalog, "resultCodes", out var codes))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in codes.OfType<JsonObject>())
        {
            var code = GetStringOrNull(item, "code");
            if (!string.IsNullOrWhiteSpace(code))
            {
                result[code] = GetStringOrNull(item, "severity") ?? string.Empty;
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, JsonObject> LoadScenarioCatalogFamilies(DisputeContinuityMatrixPromotionPaths paths)
    {
        var path = Path.Combine(paths.ScenariosRoot, DisputeContinuityMatrixPromotionPaths.ScenarioCatalogFileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        }

        var catalog = ReadJsonObject(path, "scenario catalog");
        if (!TryGetArray(catalog, "requiredFamilies", out var families))
        {
            return new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        }

        return families
            .OfType<JsonObject>()
            .Where(family => !string.IsNullOrWhiteSpace(GetStringOrNull(family, "family")))
            .GroupBy(family => GetString(family, "family"), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> ValidateResultCodeCatalog(IReadOnlyDictionary<string, string> resultCodes)
    {
        var errors = new List<string>();
        if (resultCodes.Count == 0)
        {
            errors.Add("DCM165_RESULT_CODE_CATALOG_INVALID: result code catalog is missing or empty.");
            return errors;
        }

        var severities = resultCodes.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var severity in RequiredResultCodeSeverities)
        {
            if (!severities.Contains(severity))
            {
                errors.Add($"DCM165_RESULT_CODE_SEVERITY_MISSING: result-code catalog must include {severity} severity.");
            }
        }

        foreach (var (code, severity) in resultCodes)
        {
            if (!RequiredResultCodeSeverities.Contains(severity, StringComparer.Ordinal))
            {
                errors.Add($"DCM165_VERIFIER_CHALLENGE_STATE_UNKNOWN: result code has unsupported severity/state: {code}={severity}");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateScenarioCatalog(
        DisputeContinuityMatrixPromotionPaths paths,
        IReadOnlyDictionary<string, string> resultCodes)
    {
        var errors = new List<string>();
        var path = Path.Combine(paths.ScenariosRoot, DisputeContinuityMatrixPromotionPaths.ScenarioCatalogFileName);
        if (!File.Exists(path))
        {
            errors.Add($"DCM165_SCENARIO_FILE_MISSING: {DisputeContinuityMatrixPromotionPaths.ScenarioCatalogFileName}");
            return errors;
        }

        var catalog = ReadJsonObject(path, "scenario catalog");
        if (!TryGetArray(catalog, "requiredFamilies", out var families))
        {
            errors.Add("DCM165_SCENARIO_CATALOG_INVALID: requiredFamilies is missing.");
            return errors;
        }

        var familyNames = families
            .OfType<JsonObject>()
            .Select(family => GetStringOrNull(family, "family"))
            .Where(family => !string.IsNullOrWhiteSpace(family))
            .Select(family => family!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredFamily in RequiredScenarioFamilies)
        {
            if (!familyNames.Contains(requiredFamily))
            {
                errors.Add($"DCM165_SCENARIO_FAMILY_MISSING: scenario catalog is missing required family: {requiredFamily}");
            }
        }

        var resultCodeSet = resultCodes.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var family in families.OfType<JsonObject>())
        {
            var familyName = GetStringOrNull(family, "family") ?? "<missing>";
            if (GetBoolOrNull(family, "blocksWhenMissing") != true)
            {
                errors.Add($"DCM165_SCENARIO_CATALOG_INVALID: {familyName} must block when missing.");
            }

            ValidateCodeArray(family, "expectedResultCodes", resultCodeSet, errors);
            ValidateCodeArray(family, "failureResultCodes", resultCodeSet, errors);
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateNegativeFixtures(DisputeContinuityMatrixPromotionPaths paths, IReadOnlySet<string> resultCodes)
    {
        var errors = new List<string>();
        var path = Path.Combine(paths.ExamplesRoot, "negative", DisputeContinuityMatrixPromotionPaths.NegativeFixtureCatalogFileName);
        if (!File.Exists(path))
        {
            errors.Add($"DCM165_EXAMPLE_FILE_MISSING: {DisputeContinuityMatrixPromotionPaths.NegativeFixtureCatalogFileName}");
            return errors;
        }

        var catalog = ReadJsonObject(path, "negative fixture catalog");
        if (!TryGetArray(catalog, "cases", out var cases) || cases.Count == 0)
        {
            errors.Add("DCM165_NEGATIVE_FIXTURE_CATALOG_INVALID: cases are missing.");
            return errors;
        }

        foreach (var item in cases.OfType<JsonObject>())
        {
            var code = GetStringOrNull(item, "expectedFailureCode");
            if (string.IsNullOrWhiteSpace(code) || !resultCodes.Contains(code))
            {
                errors.Add($"DCM165_RESULT_CODE_UNKNOWN: negative fixture references unknown result code: {code ?? "<missing>"}");
            }
        }

        return errors;
    }

    private static void ValidateCodeArray(
        JsonObject obj,
        string propertyName,
        IReadOnlySet<string> resultCodes,
        List<string> errors)
    {
        if (!TryGetArray(obj, propertyName, out var codes) || codes.Count == 0)
        {
            errors.Add($"DCM165_RESULT_CODE_MISSING: {propertyName} is missing.");
            return;
        }

        foreach (var code in codes)
        {
            var value = code?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value) || !resultCodes.Contains(value))
            {
                errors.Add($"DCM165_RESULT_CODE_UNKNOWN: unknown result code: {value ?? "<missing>"}");
            }
        }
    }

    private static IReadOnlySet<string> GetStringSet(JsonObject obj, string propertyName)
    {
        if (!TryGetArray(obj, propertyName, out var values))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return values
            .Select(value => value?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool ReferenceMentionsFeature(JsonObject reference, string featureId)
    {
        var searchable = string.Join(
            "|",
            GetStringOrNull(reference, "id"),
            GetStringOrNull(reference, "source"),
            GetStringOrNull(reference, "ref"));
        return searchable.Contains(featureId, StringComparison.Ordinal);
    }

    private static void ValidatePublicRef(JsonObject publicRef, string context, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(GetStringOrNull(publicRef, "id")) ||
            string.IsNullOrWhiteSpace(GetStringOrNull(publicRef, "source")) ||
            string.IsNullOrWhiteSpace(GetStringOrNull(publicRef, "ref")))
        {
            errors.Add($"DCM165_PUBLIC_REF_INVALID: {context} must use id, source, and ref.");
        }

        if (!IsSha256(GetStringOrNull(publicRef, "hash")))
        {
            errors.Add($"DCM165_PUBLIC_REF_INVALID: {context} hash is not a SHA-256 hex value.");
        }

        var visibility = GetStringOrNull(publicRef, "visibility");
        if (visibility is not ("public" or "restricted-ref-only"))
        {
            errors.Add($"DCM165_PUBLIC_REF_INVALID: {context} visibility must be public or restricted-ref-only.");
        }
    }

    private static void ScanForbiddenMaterial(JsonNode? node, string path, List<string> errors)
    {
        if (ShouldSkipForbiddenScan(path))
        {
            return;
        }

        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    AddForbiddenNameFinding(name, errors);
                    ScanForbiddenMaterial(child, $"{path}.{name}", errors);
                }

                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    ScanForbiddenMaterial(array[index], $"{path}[{index}]", errors);
                }

                break;
            case JsonValue value:
                AddForbiddenValueFinding(value.ToJsonString().Trim('"'), errors);
                break;
        }
    }

    private static bool ShouldSkipForbiddenScan(string path) =>
        path.Contains(".forbiddenPublicMaterials", StringComparison.Ordinal) ||
        path.Contains(".failureResultCodes", StringComparison.Ordinal) ||
        path.Contains(".expectedResultCodes", StringComparison.Ordinal) ||
        path.Contains(".expectedFailureCode", StringComparison.Ordinal);

    private static void AddForbiddenNameFinding(string name, List<string> errors)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("customerlegaldecision", StringComparison.Ordinal) ||
            lower.Contains("rawdisputebody", StringComparison.Ordinal) ||
            lower.Contains("disputepayload", StringComparison.Ordinal) ||
            lower.Contains("anomalythread", StringComparison.Ordinal) ||
            lower.Contains("challengethread", StringComparison.Ordinal))
        {
            errors.Add("DCM165_RESTRICTED_PAYLOAD_FORBIDDEN: source contains restricted dispute/governance payload material.");
        }
        else if (lower.Contains("votermaterial", StringComparison.Ordinal) ||
            lower.Contains("trusteematerial", StringComparison.Ordinal) ||
            lower.Contains("trusteeshare", StringComparison.Ordinal))
        {
            errors.Add("DCM165_RESTRICTED_PAYLOAD_FORBIDDEN: source contains voter or trustee material.");
        }
        else if (lower.Contains("privatepath", StringComparison.Ordinal) ||
            lower.Contains("localpath", StringComparison.Ordinal))
        {
            errors.Add("DCM165_PRIVATE_PATH_FORBIDDEN: source contains private path material.");
        }
        else if (lower.Contains("secret", StringComparison.Ordinal) ||
            lower.Contains("credential", StringComparison.Ordinal) ||
            lower.Contains("token", StringComparison.Ordinal) ||
            lower.Contains("privatekey", StringComparison.Ordinal))
        {
            errors.Add("DCM165_SECRET_FORBIDDEN: source contains secret or credential material.");
        }
    }

    private static void AddForbiddenValueFinding(string value, List<string> errors)
    {
        if (ContainsPrivatePath(value))
        {
            errors.Add("DCM165_PRIVATE_PATH_FORBIDDEN: source contains a private or local path.");
        }

        var lower = value.ToLowerInvariant();
        if (lower.Contains("legal sufficiency is accepted", StringComparison.Ordinal) ||
            lower.Contains("legal sufficiency accepted", StringComparison.Ordinal) ||
            lower.Contains("agm management is accepted", StringComparison.Ordinal) ||
            lower.Contains("agm-management accepted", StringComparison.Ordinal) ||
            lower.Contains("certification is accepted", StringComparison.Ordinal) ||
            lower.Contains("external audit acceptance", StringComparison.Ordinal) ||
            lower.Contains("external-audit accepted", StringComparison.Ordinal) ||
            lower.Contains("public/state election readiness is accepted", StringComparison.Ordinal) ||
            lower.Contains("public state election readiness accepted", StringComparison.Ordinal) ||
            lower.Contains("production rollout approval is accepted", StringComparison.Ordinal) ||
            lower.Contains("production rollout accepted", StringComparison.Ordinal))
        {
            errors.Add("DCM165_OVERCLAIM_FORBIDDEN: source contains forbidden claim wording.");
        }
    }

    private static bool ContainsPrivatePath(string? value) =>
        value is not null &&
        (value.Contains("PrivateServer_ElectronicVoting", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("hush-documents/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(@"C:\", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("/Users/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("/home/", StringComparison.OrdinalIgnoreCase));

    private static bool IsSha256(string? value) =>
        value is not null &&
        value.Length == 64 &&
        value.All(ch => (ch >= 'a' && ch <= 'f') || (ch >= '0' && ch <= '9'));

    private static bool TryGetObject(JsonObject obj, string propertyName, out JsonObject value)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonObject objectValue)
        {
            value = objectValue;
            return true;
        }

        value = new JsonObject();
        return false;
    }

    private static bool TryGetArray(JsonObject obj, string propertyName, out JsonArray value)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonArray arrayValue)
        {
            value = arrayValue;
            return true;
        }

        value = new JsonArray();
        return false;
    }

    private static string? GetStringOrNull(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is not null)
        {
            return node.GetValue<string>();
        }

        return null;
    }

    private static int? GetIntOrNull(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is not null)
        {
            return node.GetValue<int>();
        }

        return null;
    }

    private static bool? GetBoolOrNull(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is not null)
        {
            return node.GetValue<bool>();
        }

        return null;
    }
}
