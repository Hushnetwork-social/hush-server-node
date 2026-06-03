using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RetentionLogPrivacyRecurringScanPromoter;

public static class RetentionLogPrivacyRecurringScanContracts
{
    public const string FeatureId = "FEAT-164";
    public const string SourceSchemaVersion = "retention-log-privacy-recurring-scan-source.v1";
    public const string PackageManifestSchemaVersion = "retention-log-privacy-recurring-scan-package-manifest.v1";
    public const string CurrentRegisterVersionId = "RDY-REG-v0.1.7";
    public const string TargetDimensionId = "RDY-DIM-008";
    public const string TargetBlockerId = "RDY-BLOCK-INTERNAL_AUDIT_95_DIM008-001";
    public const string AcceptedFeat137PackageId = "RETENTION-LOG-PRIVACY-PROOF-HUSHVOTING-V1";
    public const string AcceptedFeat137PackageHash = "974a462ff80c84716f0945103a624bc30d0fe7b201d5d3224fd274c42ce4bbfe";
    public const string AcceptedFeat137PrivacyBoundaryVersion = "hushvoting-retention-log-privacy-boundary-v1";
    public const string RequiredScannerVersion = "retention-log-privacy-recurring-scan-promoter.v1";
    public const string RequiredRulesetVersion = "rlp164-public-safe-ruleset-v1";

    public static readonly string[] RequiredSchemaFiles =
    [
        RetentionLogPrivacyRecurringScanPromotionPaths.SourceSchemaFileName,
        RetentionLogPrivacyRecurringScanPromotionPaths.PackageManifestSchemaFileName,
    ];

    public static readonly string[] RequiredRuleFiles =
    [
        RetentionLogPrivacyRecurringScanPromotionPaths.ForbiddenMaterialCatalogFileName,
        RetentionLogPrivacyRecurringScanPromotionPaths.OutputFamilyRegistryFileName,
    ];

    public static readonly string[] RequiredExampleFiles =
    [
        Path.Combine("release-baseline", RetentionLogPrivacyRecurringScanPromotionPaths.SourceFileName),
        RetentionLogPrivacyRecurringScanPromotionPaths.ResultCodesFileName,
        Path.Combine("negative", RetentionLogPrivacyRecurringScanPromotionPaths.NegativeFixtureCatalogFileName),
    ];

    private static readonly Regex ScanRunIdPattern = new("^FEAT164-RLP-SCAN-[0-9]{8}-[0-9]{3}$", RegexOptions.CultureInvariant);

    public static JsonObject ValidateForPromotion(
        RetentionLogPrivacyRecurringScanPromotionPaths paths,
        string? sourceInput,
        bool publicOnly)
    {
        var errors = new List<string>();
        errors.AddRange(ValidateSchemaSet(paths.SchemasRoot));
        errors.AddRange(ValidatePublicRepositorySet(paths));

        var source = LoadSource(paths, sourceInput);
        var forbiddenCatalogHash = FileSha256Hex(Path.Combine(paths.RulesRoot, RetentionLogPrivacyRecurringScanPromotionPaths.ForbiddenMaterialCatalogFileName));
        var outputFamilyRegistryHash = FileSha256Hex(Path.Combine(paths.RulesRoot, RetentionLogPrivacyRecurringScanPromotionPaths.OutputFamilyRegistryFileName));
        var knownFamilies = LoadKnownOutputFamilyIds(paths);
        errors.AddRange(ValidateSource(source, forbiddenCatalogHash, outputFamilyRegistryHash, knownFamilies, publicOnly));

        if (errors.Count > 0)
        {
            throw new RetentionLogPrivacyRecurringScanPromotionException(
                "FEAT-164 retention/log privacy recurring scan validation failed.",
                errors);
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
                errors.Add($"RLP164_SCHEMA_MISSING: required schema is missing: {fileName}");
                continue;
            }

            try
            {
                var schema = ReadJsonObject(path, $"schema {fileName}");
                if (!schema.ContainsKey("$schema"))
                {
                    errors.Add($"RLP164_SCHEMA_INVALID: schema has no $schema field: {fileName}");
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"RLP164_SCHEMA_INVALID: schema {fileName} is not valid JSON: {ex.Message}");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidatePublicRepositorySet(RetentionLogPrivacyRecurringScanPromotionPaths paths)
    {
        var errors = new List<string>();
        if (!Directory.Exists(paths.PublicRepositoryRoot))
        {
            errors.Add($"RLP164_PUBLIC_REPOSITORY_MISSING: {paths.PublicRepositoryRoot}");
            return errors;
        }

        foreach (var fileName in RequiredRuleFiles)
        {
            var path = Path.Combine(paths.RulesRoot, fileName);
            if (!File.Exists(path))
            {
                errors.Add($"RLP164_RULE_FILE_MISSING: {fileName}");
            }
            else
            {
                _ = ReadJsonObject(path, $"rule file {fileName}");
            }
        }

        foreach (var fileName in RequiredExampleFiles)
        {
            var path = Path.Combine(paths.ExamplesRoot, fileName);
            if (!File.Exists(path))
            {
                errors.Add($"RLP164_EXAMPLE_FILE_MISSING: {fileName}");
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
        string? expectedForbiddenCatalogHash = null,
        string? expectedOutputFamilyRegistryHash = null,
        IReadOnlySet<string>? knownOutputFamilies = null,
        bool publicOnly = false)
    {
        var errors = new List<string>();
        if (GetStringOrNull(source, "schemaVersion") != SourceSchemaVersion)
        {
            errors.Add("RLP164_SOURCE_SCHEMA_INVALID: source schemaVersion is not supported.");
        }

        if (GetStringOrNull(source, "featureId") != FeatureId)
        {
            errors.Add("RLP164_FEATURE_ID_INVALID: source featureId must be FEAT-164.");
        }

        var scanRunId = GetStringOrNull(source, "scanRunId");
        if (string.IsNullOrWhiteSpace(scanRunId) || !ScanRunIdPattern.IsMatch(scanRunId))
        {
            errors.Add("RLP164_SCAN_RUN_ID_INVALID: scanRunId must match FEAT164-RLP-SCAN-yyyyMMdd-nnn.");
        }

        ValidateReadinessBaseline(source, errors);
        ValidateFeat137Proof(source, errors);
        ValidateRuntimeProofFamily(source, errors);
        ValidateScannerBaseline(source, expectedForbiddenCatalogHash, expectedOutputFamilyRegistryHash, errors);
        ValidateOutputFamilies(source, knownOutputFamilies, errors);
        ValidateScanInputs(source, knownOutputFamilies, errors);
        ValidateDriftChecks(source, errors);
        ValidatePublicSafetyPolicy(source, publicOnly, errors);
        ValidateScorePolicy(source, errors);
        ValidateRestrictedEvidencePolicy(source, errors);
        ScanForbiddenMaterial(source, "$", errors);

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static JsonObject LoadSource(RetentionLogPrivacyRecurringScanPromotionPaths paths, string? sourceInput = null)
    {
        var path = string.IsNullOrWhiteSpace(sourceInput) ? paths.DefaultSourcePath : sourceInput;
        return ReadJsonObject(path, "FEAT-164 source fixture");
    }

    public static JsonObject ReadJsonObject(string path, string description)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        if (node is not JsonObject obj)
        {
            throw new RetentionLogPrivacyRecurringScanPromotionException(
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
            throw new RetentionLogPrivacyRecurringScanPromotionException(
                "FEAT-164 generated package construction failed.",
                [$"Missing string property: {propertyName}"]);
        }

        return value;
    }

    public static int GetInt(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is not null)
        {
            return node.GetValue<int>();
        }

        throw new RetentionLogPrivacyRecurringScanPromotionException(
            "FEAT-164 generated package construction failed.",
            [$"Missing integer property: {propertyName}"]);
    }

    public static bool GetBool(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is not null)
        {
            return node.GetValue<bool>();
        }

        throw new RetentionLogPrivacyRecurringScanPromotionException(
            "FEAT-164 generated package construction failed.",
            [$"Missing boolean property: {propertyName}"]);
    }

    public static JsonObject RequireObject(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonObject child)
        {
            return child;
        }

        throw new RetentionLogPrivacyRecurringScanPromotionException(
            "FEAT-164 generated package construction failed.",
            [$"Missing object property: {propertyName}"]);
    }

    public static JsonArray RequireArray(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonArray child)
        {
            return child;
        }

        throw new RetentionLogPrivacyRecurringScanPromotionException(
            "FEAT-164 generated package construction failed.",
            [$"Missing array property: {propertyName}"]);
    }

    public static string CanonicalJson(JsonNode node) => RetentionLogPrivacyRecurringScanCanonicalJson.Serialize(node);

    public static string NormalizeLineEndings(string value) => RetentionLogPrivacyRecurringScanCanonicalJson.NormalizeLineEndings(value);

    public static string Sha256Hex(string content) => RetentionLogPrivacyRecurringScanCanonicalJson.ComputeSha256(content);

    public static string FileSha256Hex(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public static JsonArray ToStringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    public static void EnsurePathUnder(string root, string candidate, string description)
    {
        var rootFullPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidateFullPath = Path.GetFullPath(candidate);
        if (!candidateFullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new RetentionLogPrivacyRecurringScanPromotionException(
                "FEAT-164 path containment check failed.",
                [$"{description}: {candidateFullPath} is outside {rootFullPath}"]);
        }
    }

    private static void ValidateReadinessBaseline(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "readinessBaseline", out var baseline))
        {
            errors.Add("RLP164_STALE_READINESS_BASELINE: readinessBaseline is missing.");
            return;
        }

        if (GetStringOrNull(baseline, "registerVersionId") != CurrentRegisterVersionId ||
            GetStringOrNull(baseline, "dimensionId") != TargetDimensionId ||
            GetIntOrNull(baseline, "currentScore") != 8 ||
            GetIntOrNull(baseline, "proposedScore") != 9 ||
            GetStringOrNull(baseline, "targetBlockerId") != TargetBlockerId)
        {
            errors.Add("RLP164_STALE_READINESS_BASELINE: source must bind RDY-REG-v0.1.7 RDY-DIM-008 8 -> 9.");
        }
    }

    private static void ValidateFeat137Proof(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "feat137Proof", out var proof))
        {
            errors.Add("RLP164_FEAT137_PROOF_CURRENTNESS_BLOCKED: feat137Proof is missing.");
            return;
        }

        if (GetStringOrNull(proof, "packageId") != AcceptedFeat137PackageId ||
            GetStringOrNull(proof, "packageHash") != AcceptedFeat137PackageHash ||
            GetStringOrNull(proof, "privacyBoundaryVersion") != AcceptedFeat137PrivacyBoundaryVersion ||
            GetStringOrNull(proof, "evidenceStatus") != "accepted")
        {
            errors.Add("RLP164_FEAT137_PROOF_CURRENTNESS_BLOCKED: FEAT-137 proof id, hash, boundary, or accepted status does not match the accepted handoff.");
        }

        if (!TryGetArray(proof, "sourceRefs", out var refs) || refs.Count == 0)
        {
            errors.Add("RLP164_FEAT137_PROOF_CURRENTNESS_BLOCKED: FEAT-137 sourceRefs are missing.");
            return;
        }

        foreach (var item in refs.OfType<JsonObject>())
        {
            ValidatePublicRef(item, "feat137Proof.sourceRefs", errors);
        }
    }

    private static void ValidateRuntimeProofFamily(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "runtimeProofFamily", out var runtime))
        {
            return;
        }

        var currentStatus = GetStringOrNull(runtime, "currentStatus");
        var requiredWhenLive = GetBoolOrNull(runtime, "requiredWhenLiveReportClaimed") == true;
        var allowed = GetStringValues(runtime, "allowedStatuses").ToHashSet(StringComparer.Ordinal);
        var blocking = GetStringValues(runtime, "blockingStatuses").ToHashSet(StringComparer.Ordinal);
        if (requiredWhenLive && (string.IsNullOrWhiteSpace(currentStatus) || blocking.Contains(currentStatus) || !allowed.Contains(currentStatus)))
        {
            errors.Add("RLP164_RUNTIME_PROOF_FAMILY_BLOCKED: FEAT-143 proof-family status is missing, stale, mismatched, superseded, blocked, or unknown.");
        }
    }

    private static void ValidateScannerBaseline(
        JsonObject source,
        string? expectedForbiddenCatalogHash,
        string? expectedOutputFamilyRegistryHash,
        List<string> errors)
    {
        if (!TryGetObject(source, "scannerBaseline", out var baseline))
        {
            errors.Add("RLP164_SCANNER_BASELINE_MISSING: scannerBaseline is missing.");
            return;
        }

        var scannerVersion = GetStringOrNull(baseline, "scannerVersion");
        var rulesetVersion = GetStringOrNull(baseline, "rulesetVersion");
        var catalogHash = GetStringOrNull(baseline, "forbiddenMaterialCatalogHash");
        var registryHash = GetStringOrNull(baseline, "outputFamilyRegistryHash");
        if (scannerVersion != RequiredScannerVersion || rulesetVersion != RequiredRulesetVersion ||
            !IsSha256(catalogHash) || !IsSha256(registryHash))
        {
            errors.Add("RLP164_SCANNER_BASELINE_MISSING: scanner version, ruleset version, and hash-bound catalogs are required.");
        }

        if (expectedForbiddenCatalogHash is not null && catalogHash != expectedForbiddenCatalogHash)
        {
            errors.Add("RLP164_RULE_HASH_MISMATCH: forbidden-material catalog hash does not match the public rule file.");
        }

        if (expectedOutputFamilyRegistryHash is not null && registryHash != expectedOutputFamilyRegistryHash)
        {
            errors.Add("RLP164_RULE_HASH_MISMATCH: output-family registry hash does not match the public rule file.");
        }
    }

    private static void ValidateOutputFamilies(
        JsonObject source,
        IReadOnlySet<string>? knownOutputFamilies,
        List<string> errors)
    {
        if (!TryGetArray(source, "outputFamilies", out var families) || families.Count == 0)
        {
            errors.Add("RLP164_UNCLASSIFIED_OUTPUT_FAMILY: outputFamilies are missing.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var family in families.OfType<JsonObject>())
        {
            var familyId = GetStringOrNull(family, "familyId");
            if (string.IsNullOrWhiteSpace(familyId) || !seen.Add(familyId))
            {
                errors.Add("RLP164_UNCLASSIFIED_OUTPUT_FAMILY: every output family must have one stable unique familyId.");
                continue;
            }

            if (knownOutputFamilies is not null && !knownOutputFamilies.Contains(familyId))
            {
                errors.Add($"RLP164_UNCLASSIFIED_OUTPUT_FAMILY: output family '{familyId}' is not in the public registry.");
            }

            var decision = GetStringOrNull(family, "scannerDecision");
            var visibility = GetStringOrNull(family, "visibility");
            if (string.IsNullOrWhiteSpace(decision) || decision == "not_applicable")
            {
                errors.Add($"RLP164_UNCLASSIFIED_OUTPUT_FAMILY: output family '{familyId}' has no active scanner decision.");
            }

            if ((visibility == "restricted" || visibility == "private") && GetBoolOrNull(family, "publicPayloadAllowed") == true)
            {
                errors.Add($"RLP164_PRIVATE_PATH_FORBIDDEN: output family '{familyId}' cannot publish restricted/private payloads.");
            }

            if (!TryGetArray(family, "forbiddenFields", out var forbiddenFields) || forbiddenFields.Count == 0)
            {
                errors.Add($"RLP164_UNCLASSIFIED_OUTPUT_FAMILY: output family '{familyId}' must declare forbiddenFields.");
            }
        }
    }

    private static void ValidateScanInputs(
        JsonObject source,
        IReadOnlySet<string>? knownOutputFamilies,
        List<string> errors)
    {
        if (!TryGetArray(source, "scanInputs", out var inputs) || inputs.Count == 0)
        {
            errors.Add("RLP164_PUBLIC_ONLY_PRIVATE_DEPENDENCY: scanInputs are missing.");
            return;
        }

        var sourceFamilies = new HashSet<string>(StringComparer.Ordinal);
        if (TryGetArray(source, "outputFamilies", out var families))
        {
            foreach (var family in families.OfType<JsonObject>())
            {
                var sourceFamilyId = GetStringOrNull(family, "familyId");
                if (!string.IsNullOrWhiteSpace(sourceFamilyId))
                {
                    sourceFamilies.Add(sourceFamilyId);
                }
            }
        }
        foreach (var input in inputs.OfType<JsonObject>())
        {
            var familyId = GetStringOrNull(input, "outputFamilyId");
            if (string.IsNullOrWhiteSpace(familyId) ||
                !sourceFamilies.Contains(familyId) ||
                (knownOutputFamilies is not null && !knownOutputFamilies.Contains(familyId)))
            {
                errors.Add($"RLP164_UNCLASSIFIED_OUTPUT_FAMILY: scan input '{GetStringOrNull(input, "inputId") ?? "<missing>"}' references unclassified family '{familyId}'.");
            }

            if (TryGetObject(input, "sourceRef", out var sourceRef))
            {
                ValidatePublicRef(sourceRef, $"scanInputs.{GetStringOrNull(input, "inputId")}", errors);
            }
            else
            {
                errors.Add("RLP164_PUBLIC_ONLY_PRIVATE_DEPENDENCY: scan input sourceRef is missing.");
            }
        }
    }

    private static void ValidateDriftChecks(JsonObject source, List<string> errors)
    {
        if (!TryGetArray(source, "driftChecks", out var checks) || checks.Count == 0)
        {
            errors.Add("RLP164_DRIFT_CHECK_MISSING: driftChecks are missing.");
            return;
        }

        foreach (var check in checks.OfType<JsonObject>())
        {
            if (GetBoolOrNull(check, "failClosedWhenChanged") != true)
            {
                errors.Add($"RLP164_DRIFT_CHECK_MISSING: drift check '{GetStringOrNull(check, "checkId") ?? "<missing>"}' must fail closed.");
            }
        }
    }

    private static void ValidatePublicSafetyPolicy(JsonObject source, bool publicOnly, List<string> errors)
    {
        if (!TryGetObject(source, "publicSafetyPolicy", out var policy))
        {
            errors.Add("RLP164_PUBLIC_ONLY_PRIVATE_DEPENDENCY: publicSafetyPolicy is missing.");
            return;
        }

        if (GetBoolOrNull(policy, "publicOnlyValidation") != true)
        {
            errors.Add("RLP164_PUBLIC_ONLY_PRIVATE_DEPENDENCY: public-only validation must be enabled.");
        }

        if (publicOnly && GetBoolOrNull(policy, "publicOnlyValidation") != true)
        {
            errors.Add("RLP164_PUBLIC_ONLY_PRIVATE_DEPENDENCY: -PublicOnly cannot depend on private checkouts or credentials.");
        }
    }

    private static void ValidateScorePolicy(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "scorePolicy", out var policy))
        {
            errors.Add("RLP164_WRONG_SCORE_RANGE: scorePolicy is missing.");
            return;
        }

        if (GetStringOrNull(policy, "dimensionId") != TargetDimensionId ||
            GetIntOrNull(policy, "proposedScoreFrom") != 8 ||
            GetIntOrNull(policy, "proposedScoreTo") != 9)
        {
            errors.Add("RLP164_WRONG_SCORE_RANGE: FEAT-164 can only propose RDY-DIM-008 8 -> 9.");
        }

        if (GetBoolOrNull(policy, "directRegisterMutation") != false)
        {
            errors.Add("RLP164_DIRECT_REGISTER_MUTATION_FORBIDDEN: FEAT-164 must not mutate the readiness register directly.");
        }
    }

    private static void ValidateRestrictedEvidencePolicy(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "restrictedEvidencePolicy", out var policy))
        {
            errors.Add("RLP164_PRIVATE_PATH_FORBIDDEN: restrictedEvidencePolicy is missing.");
            return;
        }

        if (GetBoolOrNull(policy, "payloadPublished") != false || GetBoolOrNull(policy, "publicRefFieldsOnly") != true)
        {
            errors.Add("RLP164_PRIVATE_PATH_FORBIDDEN: restricted evidence payloads must not be published.");
        }
    }

    private static void ValidatePublicRef(JsonObject publicRef, string context, List<string> errors)
    {
        var path = GetStringOrNull(publicRef, "path");
        if (string.IsNullOrWhiteSpace(GetStringOrNull(publicRef, "repository")) ||
            string.IsNullOrWhiteSpace(GetStringOrNull(publicRef, "ref")) ||
            string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"RLP164_PUBLIC_ONLY_PRIVATE_DEPENDENCY: {context} must use repository, ref, and repository-relative path.");
        }

        if (ContainsPrivatePath(path))
        {
            errors.Add($"RLP164_PRIVATE_PATH_FORBIDDEN: {context} path is private or local.");
        }

        var hash = GetStringOrNull(publicRef, "sha256");
        if (hash is not null && !IsSha256(hash))
        {
            errors.Add($"RLP164_RULE_HASH_MISMATCH: {context} hash is not a SHA-256 hex value.");
        }
    }

    private static HashSet<string> LoadKnownOutputFamilyIds(RetentionLogPrivacyRecurringScanPromotionPaths paths)
    {
        var registryPath = Path.Combine(paths.RulesRoot, RetentionLogPrivacyRecurringScanPromotionPaths.OutputFamilyRegistryFileName);
        if (!File.Exists(registryPath))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var registry = ReadJsonObject(registryPath, "output-family registry");
        if (!TryGetArray(registry, "families", out var families))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var family in families.OfType<JsonObject>())
        {
            var id = GetStringOrNull(family, "familyId");
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids;
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
                var text = value.ToJsonString().Trim('"');
                AddForbiddenValueFinding(text, errors);
                break;
        }
    }

    private static bool ShouldSkipForbiddenScan(string path) =>
        path.Contains(".forbiddenFields", StringComparison.Ordinal) ||
        path.Contains(".forbiddenClaimPhrases", StringComparison.Ordinal) ||
        path.Contains(".forbiddenPublicPathPatterns", StringComparison.Ordinal) ||
        path.Contains(".resultCodes", StringComparison.Ordinal) ||
        path.Contains(".blockingStatuses", StringComparison.Ordinal);

    private static void AddForbiddenNameFinding(string name, List<string> errors)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("voteridentifier", StringComparison.Ordinal) || lower.Contains("shareholderidentifier", StringComparison.Ordinal))
        {
            errors.Add("RLP164_FORBIDDEN_VOTER_MATERIAL: source contains voter/shareholder identity material.");
        }
        else if (lower.Contains("ballotreference", StringComparison.Ordinal) ||
            lower.Contains("ballotchoice", StringComparison.Ordinal) ||
            lower.Contains("acceptedtopublished", StringComparison.Ordinal) ||
            lower.Contains("receiptcapability", StringComparison.Ordinal))
        {
            errors.Add("RLP164_BALLOT_LINKAGE_FORBIDDEN: source contains ballot linkage or receipt recovery material.");
        }
        else if (lower.Contains("rawlogpayload", StringComparison.Ordinal) || lower.Contains("diagnosticpayload", StringComparison.Ordinal))
        {
            errors.Add("RLP164_RAW_LOG_FORBIDDEN: source contains a raw log or diagnostic payload marker.");
        }
        else if (lower.Contains("rawtracesample", StringComparison.Ordinal) || lower.Contains("correlationlabel", StringComparison.Ordinal) || lower.Contains("metricspayload", StringComparison.Ordinal))
        {
            errors.Add("RLP164_TRACE_PAYLOAD_FORBIDDEN: source contains a trace, metrics, or correlation payload marker.");
        }
        else if (lower.Contains("supportcasecontent", StringComparison.Ordinal) || lower.Contains("supportpayload", StringComparison.Ordinal))
        {
            errors.Add("RLP164_SUPPORT_PAYLOAD_FORBIDDEN: source contains support payload material.");
        }
        else if (lower.Contains("privatedependencyrequired", StringComparison.Ordinal) ||
            lower.Contains("privatepath", StringComparison.Ordinal) ||
            lower.Contains("localpath", StringComparison.Ordinal))
        {
            errors.Add("RLP164_PRIVATE_PATH_FORBIDDEN: source contains private path or dependency material.");
        }
        else if (lower.Contains("secret", StringComparison.Ordinal) ||
            lower.Contains("credential", StringComparison.Ordinal) ||
            lower.Contains("token", StringComparison.Ordinal) ||
            lower.Contains("privatekey", StringComparison.Ordinal))
        {
            errors.Add("RLP164_SECRET_FORBIDDEN: source contains secret or credential material.");
        }
        else if (lower.Contains("privatefinding", StringComparison.Ordinal) ||
            lower.Contains("privatescannerfinding", StringComparison.Ordinal) ||
            lower.Contains("restrictedreviewerpayload", StringComparison.Ordinal))
        {
            errors.Add("RLP164_PRIVATE_FINDING_FORBIDDEN: source contains private scanner or restricted reviewer payload material.");
        }
    }

    private static void AddForbiddenValueFinding(string value, List<string> errors)
    {
        if (ContainsPrivatePath(value))
        {
            errors.Add("RLP164_PRIVATE_PATH_FORBIDDEN: source contains a private or local path.");
        }

        var lower = value.ToLowerInvariant();
        if (lower.Contains("external audit acceptance", StringComparison.Ordinal) ||
            lower.Contains("legal sufficiency", StringComparison.Ordinal) ||
            lower.Contains("public/state election readiness", StringComparison.Ordinal) ||
            lower.Contains("production rollout approval", StringComparison.Ordinal))
        {
            errors.Add("RLP164_OVERCLAIM_FORBIDDEN: source contains forbidden public claim wording.");
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

    private static IEnumerable<string> GetStringValues(JsonObject obj, string propertyName)
    {
        if (!TryGetArray(obj, propertyName, out var array))
        {
            yield break;
        }

        foreach (var item in array)
        {
            if (item is not null)
            {
                yield return item.GetValue<string>();
            }
        }
    }
}
