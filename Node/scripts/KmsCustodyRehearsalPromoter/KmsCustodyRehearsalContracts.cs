using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KmsCustodyRehearsalPromoter;

public static class KmsCustodyRehearsalContracts
{
    public const string FeatureId = "FEAT-161";
    public const string SourceSchemaVersion = "kms-custody-rehearsal-source.v1";
    public const string PackageManifestSchemaVersion = "kms-custody-rehearsal-package-manifest.v1";
    public const string CurrentRegisterId = "RDY-REG-v0.1.7";
    public const string CurrentRegisterVersion = "v0.1.7";
    public const string TargetDimensionId = "RDY-DIM-005";
    public const string TargetDimensionName = "Per-election KMS custody lifecycle";
    public const string TargetBlockerId = "RDY-BLOCK-INTERNAL_AUDIT_95_DIM005-001";
    public const string TargetPackageVersion = "v0.1.0";
    public const string ExpectedTargetPackagePath = "HushVoting-Verifier-Corpus/hushvoting-v1/kms-custody-rehearsal/v0.1.0/";
    public const string CanonicalizationVersion = "kms-custody-rehearsal-canonical-json.v1";

    public static readonly string[] RequiredSchemaFiles =
    [
        KmsCustodyRehearsalPromotionPaths.SourceSchemaFileName,
        KmsCustodyRehearsalPromotionPaths.PackageManifestSchemaFileName,
    ];

    public static readonly string[] RequiredScenarioIds =
    [
        "KMS-CUSTODY-ACCEPTED-FEAT131-BASELINE",
        "KMS-CUSTODY-IAM-PERMISSION-DRIFT",
        "KMS-CUSTODY-IAM-POLICY-DRIFT",
        "KMS-CUSTODY-RUNTIME-ROLE-ROTATION",
        "KMS-CUSTODY-ALIAS-TAG-DRIFT",
        "KMS-CUSTODY-PROVIDER-UNAVAILABLE-BEFORE-OPEN",
        "KMS-CUSTODY-PROVIDER-UNAVAILABLE-DURING-CLEANUP",
        "KMS-CUSTODY-REGIONAL-DEGRADED-CLEANUP",
        "KMS-CUSTODY-DELETION-SCHEDULE-DRIFT",
        "KMS-CUSTODY-STALE-ORPHANED-CUSTODY-STATE",
        "KMS-CUSTODY-RESTRICTED-BOUNDARY",
    ];

    public static readonly string[] RequiredNegativeCaseIds =
    [
        "NEG-STALE-READINESS-REGISTER",
        "NEG-STALE-FEAT131-REF",
        "NEG-STALE-FEAT143-REF",
        "NEG-STALE-FEAT154-REF",
        "NEG-STALE-FEAT156-REF",
        "NEG-DIRECT-REGISTER-MUTATION",
        "NEG-FORBIDDEN-KMS-IDENTIFIER",
        "NEG-LIVE-AWS-DEFAULT-REQUIRED",
        "NEG-OVERCLAIM-SCORE-TO-10",
        "NEG-PRIVATE-LOCAL-PATH",
    ];

    public static readonly string[] RequiredDownstreamConsumerIds =
    [
        "FEAT-162",
        "FEAT-163",
        "FEAT-166",
    ];

    private static readonly HashSet<string> RelativePathProperties = new(StringComparer.Ordinal)
    {
        "targetPackagePath",
        "expectedArtifacts",
        "restrictedIndexPath",
        "readinessFragment",
        "scoreProposal",
        "path",
        "scanResultRef",
        "summaryRef",
        "downstreamHandoffRef",
        "restrictedIndexRef",
        "reviewerGuideRef",
        "scoreProposalRef",
        "readinessFragmentRef",
    };

    private static readonly HashSet<string> ForbiddenNeedleAllowListProperties = new(StringComparer.Ordinal)
    {
        "forbiddenMaterialClasses",
        "mutation",
        "expectedDiagnostic",
    };

    private static readonly string[] ForbiddenPrivateNeedles =
    [
        "arn:aws",
        "aws_access_key_id",
        "aws_secret_access_key",
        "secret_access_key",
        "begin private key",
        "credential=",
        "password=",
        "connection string",
        "client_secret",
        "aws_secret",
        "keyarn",
        "kmskey",
        "kmsalias",
        "policydocument",
        "raw policy document",
        "operator identity:",
        "operator_id",
        "custody_row",
        "decrypt_authority",
        "provider_error_payload",
        "hush-documents/privateserver_electronicvoting",
        @"c:\mywork\hushnetworkorg\hush-documents",
    ];

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly IReadOnlyDictionary<string, ScenarioPolicy> RequiredScenarioPolicies =
        new Dictionary<string, ScenarioPolicy>(StringComparer.Ordinal)
        {
            ["KMS-CUSTODY-ACCEPTED-FEAT131-BASELINE"] = new(
                "accepted_baseline",
                "pass",
                "accepted_baseline_verified",
                "supports_score_proposal",
                "RESTRICTED-FEAT131-CUSTODY-HANDOFF",
                "FEAT161_ACCEPTED_BASELINE_POLICY_INVALID"),
            ["KMS-CUSTODY-IAM-PERMISSION-DRIFT"] = new(
                "iam_drift",
                "blocked",
                "iam_permission_drift_blocked",
                "blocks_score_proposal",
                "RESTRICTED-IAM-PERMISSION-DRIFT",
                "FEAT161_IAM_DRIFT_POLICY_INVALID"),
            ["KMS-CUSTODY-IAM-POLICY-DRIFT"] = new(
                "iam_drift",
                "blocked",
                "iam_policy_drift_blocked",
                "blocks_score_proposal",
                "RESTRICTED-IAM-POLICY-DRIFT",
                "FEAT161_IAM_DRIFT_POLICY_INVALID"),
            ["KMS-CUSTODY-RUNTIME-ROLE-ROTATION"] = new(
                "runtime_rotation",
                "pass",
                "runtime_rotation_recovered",
                "supports_score_proposal",
                "RESTRICTED-RUNTIME-ROTATION",
                "FEAT161_RUNTIME_ROTATION_REF_INVALID"),
            ["KMS-CUSTODY-ALIAS-TAG-DRIFT"] = new(
                "alias_tag_drift",
                "blocked",
                "alias_tag_drift_blocked",
                "blocks_score_proposal",
                "RESTRICTED-ALIAS-TAG-DRIFT",
                "FEAT161_ALIAS_TAG_DRIFT_POLICY_INVALID"),
            ["KMS-CUSTODY-PROVIDER-UNAVAILABLE-BEFORE-OPEN"] = new(
                "provider_failure",
                "blocked",
                "provider_unavailable_blocked",
                "blocks_score_proposal",
                "RESTRICTED-PROVIDER-UNAVAILABLE",
                "FEAT161_PROVIDER_FAILURE_POLICY_INVALID"),
            ["KMS-CUSTODY-PROVIDER-UNAVAILABLE-DURING-CLEANUP"] = new(
                "provider_failure",
                "degraded",
                "provider_cleanup_retry_recorded",
                "records_residual_risk",
                "RESTRICTED-PROVIDER-CLEANUP-RETRY",
                "FEAT161_PROVIDER_FAILURE_POLICY_INVALID"),
            ["KMS-CUSTODY-REGIONAL-DEGRADED-CLEANUP"] = new(
                "regional_failure",
                "degraded",
                "regional_degraded_residual_recorded",
                "records_residual_risk",
                "RESTRICTED-REGIONAL-DEGRADED",
                "FEAT161_REGIONAL_FAILURE_POLICY_INVALID"),
            ["KMS-CUSTODY-DELETION-SCHEDULE-DRIFT"] = new(
                "deletion_schedule_drift",
                "blocked",
                "deletion_schedule_drift_blocked",
                "blocks_score_proposal",
                "RESTRICTED-DELETION-SCHEDULE-DRIFT",
                "FEAT161_DELETION_DRIFT_POLICY_INVALID"),
            ["KMS-CUSTODY-STALE-ORPHANED-CUSTODY-STATE"] = new(
                "stale_orphaned_custody_state",
                "blocked",
                "orphaned_custody_state_blocked",
                "blocks_score_proposal",
                "RESTRICTED-ORPHANED-CUSTODY-STATE",
                "FEAT161_ORPHANED_CUSTODY_POLICY_INVALID"),
            ["KMS-CUSTODY-RESTRICTED-BOUNDARY"] = new(
                "restricted_boundary",
                "restricted_only",
                "restricted_boundary_preserved",
                "preserves_private_boundary",
                "RESTRICTED-BOUNDARY-INDEX",
                "FEAT161_RESTRICTED_BOUNDARY_POLICY_INVALID"),
        };

    public static JsonObject LoadSource(
        KmsCustodyRehearsalPromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "FEAT-161 KMS custody rehearsal source");
        if (!File.Exists(sourcePath))
        {
            throw new KmsCustodyRehearsalPromotionException(
                "FEAT-161 KMS custody rehearsal source input is missing.",
                [$"Source input was not found: {sourcePath}"]);
        }

        return ReadJsonObject(sourcePath, KmsCustodyRehearsalPromotionPaths.SourceFileName);
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
        var errors = ValidateJsonRequired(source, KmsCustodyRehearsalPromotionPaths.SourceFileName, [
            "schemaVersion",
            "sourceId",
            "producerFeature",
            "status",
            "generatedAt",
            "baselineRegister",
            "upstreamBaselines",
            "scorePolicy",
            "rehearsalMatrix",
            "negativeMatrix",
            "packageLayout",
            "publicSafety",
            "restrictedEvidenceBoundary",
            "readinessOutput",
            "downstreamConsumers",
            "residualRisks",
        ]).ToList();

        RequireValue(source, "schemaVersion", SourceSchemaVersion, errors);
        RequireValue(source, "producerFeature", FeatureId, errors);
        ValidateBaselineRegister(source, errors);
        ValidateUpstreamBaselines(source, errors);
        ValidateScorePolicy(source, errors);
        ValidateRehearsalMatrix(source, errors);
        ValidateNegativeMatrix(source, errors);
        ValidatePackageLayout(source, errors);
        ValidatePublicSafety(source, errors);
        ValidateRestrictedEvidenceBoundary(source, errors);
        ValidateReadinessOutput(source, errors);
        ValidateDownstreamConsumers(source, errors);
        ValidateRelativePaths(source, errors);
        ValidateForbiddenNeedles(source, errors);

        return errors;
    }

    public static JsonObject ValidateForPromotion(
        KmsCustodyRehearsalPromotionPaths paths,
        string? sourceInput = null)
    {
        var schemaErrors = ValidateSchemaSet(paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new KmsCustodyRehearsalPromotionException(
                "FEAT-161 KMS custody rehearsal schema validation failed.",
                schemaErrors);
        }

        var source = LoadSource(paths, sourceInput);
        var errors = ValidateSource(source).ToList();
        errors.AddRange(ValidateCurrentRefs(paths, source));
        if (errors.Count > 0)
        {
            throw new KmsCustodyRehearsalPromotionException(
                "FEAT-161 KMS custody rehearsal source validation failed.",
                errors);
        }

        return source;
    }

    public static IReadOnlyList<string> ValidateCurrentRefs(
        KmsCustodyRehearsalPromotionPaths paths,
        JsonObject source)
    {
        var errors = new List<string>();
        var upstream = RequireObject(source, "upstreamBaselines");
        var feat131 = RequireObject(upstream, "feat131");
        var feat143 = RequireObject(upstream, "feat143");
        var feat154 = RequireObject(upstream, "feat154");
        var feat156 = RequireObject(upstream, "feat156");

        RequireFileHash(
            Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-131-per-election-kms-custody-lifecycle", "downstream-handoff.md"),
            GetString(feat131, "publicSafeHandoffHash"),
            "upstreamBaselines.feat131.publicSafeHandoffHash",
            errors);
        RequireFileHash(
            Path.Combine(paths.WorkspaceRoot, "hush-documents", "PrivateServer_ElectronicVoting", "Operational-Security", "FEAT-131-Custody-Evidence-Handoff.md"),
            GetString(feat131, "restrictedHandoffHash"),
            "upstreamBaselines.feat131.restrictedHandoffHash",
            errors);
        RequireFileHash(
            Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-143-runtime-deployment-proof-binding-ledger", "readiness-handoff-20260526.md"),
            GetString(feat143, "referenceHash"),
            "upstreamBaselines.feat143.referenceHash",
            errors);
        RequireFileHash(
            Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-154-production-like-operational-run-evidence", "FeatureDescription.md"),
            GetString(feat154, "referenceHash"),
            "upstreamBaselines.feat154.referenceHash",
            errors);
        RequireFileHash(
            Path.Combine(paths.WorkspaceRoot, "hush-documents", "PrivateServer_ElectronicVoting", "Production-Rollout-Promotion-Register", "package", "feat156-package-manifest.json"),
            GetString(feat156, "referenceHash"),
            "upstreamBaselines.feat156.referenceHash",
            errors);

        return errors;
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new KmsCustodyRehearsalPromotionException($"{label} is not a JSON object.");
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

        throw new KmsCustodyRehearsalPromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new KmsCustodyRehearsalPromotionException($"Missing array property: {property}");
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

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    public static void EnsurePathUnder(string root, string candidate, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new KmsCustodyRehearsalPromotionException(
                "KMS custody rehearsal path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    public static string ResolveSourceInput(
        KmsCustodyRehearsalPromotionPaths paths,
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
            ? Path.Combine(fullPath, KmsCustodyRehearsalPromotionPaths.SourceFileName)
            : fullPath;
    }

    public static string CanonicalJson(JsonNode node) =>
        NormalizeLineEndings(node.ToJsonString(CanonicalJsonOptions)) + "\n";

    public static string Sha256Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(NormalizeLineEndings(content))))
            .ToLowerInvariant();

    public static string Sha256File(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    public static bool HashesEqual(string observed, string expected) =>
        string.Equals(NormalizeHash(observed), NormalizeHash(expected), StringComparison.OrdinalIgnoreCase);

    private static void ValidateBaselineRegister(JsonObject source, List<string> errors)
    {
        var baseline = RequireObject(source, "baselineRegister");
        RequireValue(baseline, "registerVersionId", CurrentRegisterId, errors, "FEAT161_STALE_READINESS_BASELINE");
        RequireValue(baseline, "registerVersion", CurrentRegisterVersion, errors, "FEAT161_STALE_READINESS_BASELINE");
        RequireValue(baseline, "status", "AcceptedInternal", errors, "FEAT161_STALE_READINESS_BASELINE");
        RequireValue(baseline, "totalScore", 80, errors, "FEAT161_STALE_READINESS_BASELINE");
        RequireValue(baseline, "internalAuditTargetScore", 95, errors, "FEAT161_STALE_READINESS_BASELINE");
        RequireValue(baseline, "dimensionId", TargetDimensionId, errors, "FEAT161_SCORE_DIMENSION_INVALID");
        RequireValue(baseline, "dimensionName", TargetDimensionName, errors);
        RequireValue(baseline, "currentScore", 8, errors, "FEAT161_SCORE_DIMENSION_INVALID");
        RequireValue(baseline, "proposedScore", 9, errors, "FEAT161_SCORE_DIMENSION_INVALID");
        RequireValue(baseline, "targetBlockerId", TargetBlockerId, errors, "FEAT161_BLOCKER_OWNERSHIP_INVALID");
        RequireValue(baseline, "blockerOwnerFeatureId", FeatureId, errors, "FEAT161_BLOCKER_OWNERSHIP_INVALID");
    }

    private static void ValidateUpstreamBaselines(JsonObject source, List<string> errors)
    {
        var upstream = RequireObject(source, "upstreamBaselines");
        var feat131 = RequireObject(upstream, "feat131");
        RequireValue(feat131, "producerFeature", "FEAT-131", errors);
        RequireValue(feat131, "evidenceId", "RDY-EVID-AT-RDY-002-FEAT-131-001", errors, "FEAT161_STALE_FEAT131_REF");
        RequireValue(feat131, "dimensionId", TargetDimensionId, errors);
        RequireValue(feat131, "productionCustodyMode", "aws_kms_per_election_envelope_v1", errors);
        RequireSha256(feat131, "publicSafeHandoffHash", errors);
        RequireSha256(feat131, "restrictedHandoffHash", errors);
        RequireArrayValues(feat131, "acceptedGateIds", ["AT-RDY-002", "AT-RDY-003", "AT-RDY-004"], errors);

        ValidateUpstreamFeatureRef(upstream, "feat143", "FEAT-143", errors, "FEAT161_STALE_FEAT143_REF");
        ValidateUpstreamFeatureRef(upstream, "feat154", "FEAT-154", errors, "FEAT161_STALE_FEAT154_REF");
        ValidateUpstreamFeatureRef(upstream, "feat156", "FEAT-156", errors, "FEAT161_STALE_FEAT156_REF");
    }

    private static void ValidateUpstreamFeatureRef(
        JsonObject upstream,
        string property,
        string producerFeature,
        List<string> errors,
        string code)
    {
        var value = RequireObject(upstream, property);
        RequireValue(value, "producerFeature", producerFeature, errors, code);
        RequireValue(value, "status", "completed", errors, code);
        RequireNonEmpty(value, "role", errors);
        RequireSha256(value, "referenceHash", errors);
    }

    private static void ValidateScorePolicy(JsonObject source, List<string> errors)
    {
        var policy = RequireObject(source, "scorePolicy");
        RequireValue(policy, "dimensionId", TargetDimensionId, errors, "FEAT161_SCORE_DIMENSION_INVALID");
        RequireValue(policy, "proposedScoreFrom", 8, errors, "FEAT161_SCORE_DIMENSION_INVALID");
        RequireValue(policy, "proposedScoreTo", 9, errors, "FEAT161_SCORE_OVERCLAIM_FORBIDDEN");
        RequireValue(policy, "directRegisterMutation", false, errors, "FEAT161_DIRECT_REGISTER_MUTATION_FORBIDDEN");
        RequireValue(policy, "doesNotMutateRegister", true, errors, "FEAT161_DIRECT_REGISTER_MUTATION_FORBIDDEN");
        RequireValue(policy, "canonicalRegisterMutationOwner", "later_internal_audit_95_promotion_pass", errors);
        RequireValue(policy, "scoreMovementBlockedUnlessAllCasesPass", true, errors);
    }

    private static void ValidateRehearsalMatrix(JsonObject source, List<string> errors)
    {
        var matrix = RequireObject(source, "rehearsalMatrix");
        RequireValue(matrix, "providerFamily", "aws-kms", errors);
        RequireValue(matrix, "defaultValidationMode", "deterministic_fake_provider", errors);
        RequireValue(matrix, "liveProviderRequiredForDefaultValidation", false, errors, "FEAT161_LIVE_PROVIDER_DEFAULT_FORBIDDEN");

        var scenarios = RequireArray(matrix, "scenarios").OfType<JsonObject>().ToArray();
        var scenarioIds = scenarios
            .Select(item => GetString(item, "scenarioId"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredId in RequiredScenarioIds)
        {
            if (!scenarioIds.Contains(requiredId))
            {
                errors.Add($"FEAT161_REQUIRED_SCENARIO_MISSING: rehearsalMatrix.scenarios missing {requiredId}.");
            }
        }

        foreach (var scenario in scenarios)
        {
            RequireNonEmpty(scenario, "scenarioId", errors);
            RequireNonEmpty(scenario, "category", errors);
            RequireNonEmpty(scenario, "description", errors);
            RequireNonEmpty(scenario, "expectedResult", errors);
            RequireNonEmpty(scenario, "readinessImpact", errors);
            RequireValue(scenario, "requiredForScore", true, errors);
            if (RequireArray(scenario, "gateIds").Count == 0)
            {
                errors.Add($"{GetString(scenario, "scenarioId")}.gateIds must not be empty.");
            }

            if (RequireArray(scenario, "safeResultCodes").Count == 0)
            {
                errors.Add($"{GetString(scenario, "scenarioId")}.safeResultCodes must not be empty.");
            }

            foreach (var restrictedRef in RequireArray(scenario, "restrictedEvidenceRefs").OfType<JsonObject>())
            {
                RequireNonEmpty(restrictedRef, "refId", errors);
                RequireValue(restrictedRef, "visibility", "restricted_reviewer", errors);
                RequireSha256(restrictedRef, "hash", errors);
                RequireValue(restrictedRef, "payloadPublished", false, errors, "FEAT161_RESTRICTED_PAYLOAD_FORBIDDEN");
            }

            ValidateScenarioPolicy(scenario, errors);
        }
    }

    private static void ValidateScenarioPolicy(JsonObject scenario, List<string> errors)
    {
        var scenarioId = GetString(scenario, "scenarioId");
        if (!RequiredScenarioPolicies.TryGetValue(scenarioId, out var policy))
        {
            return;
        }

        RequireValue(scenario, "category", policy.Category, errors, policy.ErrorCode);
        RequireValue(scenario, "expectedResult", policy.ExpectedResult, errors, policy.ErrorCode);
        RequireValue(scenario, "readinessImpact", policy.ReadinessImpact, errors, policy.ErrorCode);
        RequireArrayContains(scenario, "safeResultCodes", policy.SafeResultCode, errors, policy.ErrorCode);
        RequireRestrictedEvidenceRef(scenario, policy.RestrictedRefId, errors, policy.ErrorCode);
    }

    private static void ValidateNegativeMatrix(JsonObject source, List<string> errors)
    {
        var cases = RequireArray(source, "negativeMatrix").OfType<JsonObject>().ToArray();
        var caseIds = cases
            .Select(item => GetString(item, "caseId"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredId in RequiredNegativeCaseIds)
        {
            if (!caseIds.Contains(requiredId))
            {
                errors.Add($"FEAT161_NEGATIVE_CASE_MISSING: negativeMatrix missing {requiredId}.");
            }
        }

        foreach (var item in cases)
        {
            RequireNonEmpty(item, "caseId", errors);
            RequireNonEmpty(item, "mutation", errors);
            RequireNonEmpty(item, "expectedDiagnostic", errors);
            RequireValue(item, "blocksScoreMovement", true, errors);
        }
    }

    private static void ValidatePackageLayout(JsonObject source, List<string> errors)
    {
        var layout = RequireObject(source, "packageLayout");
        RequireValue(layout, "targetPackagePath", ExpectedTargetPackagePath, errors);
        RequireValue(layout, "packageVersion", TargetPackageVersion, errors);

        var artifacts = RequireArray(layout, "expectedArtifacts")
            .Select(item => item?.GetValue<string>() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var path in KmsCustodyRehearsalArtifactGenerator.RequiredArtifactPaths)
        {
            if (!artifacts.Contains(path))
            {
                errors.Add($"FEAT161_EXPECTED_ARTIFACT_MISSING: packageLayout.expectedArtifacts missing {path}.");
            }
        }
    }

    private static void ValidatePublicSafety(JsonObject source, List<string> errors)
    {
        var safety = RequireObject(source, "publicSafety");
        RequireValue(safety, "noSecretScanRequired", true, errors);
        RequireValue(safety, "publicProviderDetailLevel", "provider_family_only", errors);
        RequireValue(safety, "liveAwsKmsRequiredForDefaultCi", false, errors, "FEAT161_LIVE_PROVIDER_DEFAULT_FORBIDDEN");
        if (RequireArray(safety, "forbiddenMaterialClasses").Count < 10)
        {
            errors.Add("FEAT161_PUBLIC_SAFETY_BOUNDARY_TOO_SMALL: publicSafety.forbiddenMaterialClasses must include the restricted material classes.");
        }
    }

    private static void ValidateRestrictedEvidenceBoundary(JsonObject source, List<string> errors)
    {
        var boundary = RequireObject(source, "restrictedEvidenceBoundary");
        RequireValue(boundary, "payloadsRemainPrivate", true, errors, "FEAT161_RESTRICTED_PAYLOAD_FORBIDDEN");
        RequireValue(boundary, "publicRefsAreHashOnly", true, errors);
        RequireNonEmpty(boundary, "restrictedOwner", errors);
        RequireValue(boundary, "restrictedIndexPath", KmsCustodyRehearsalArtifactGenerator.RestrictedEvidenceIndexPath, errors);
    }

    private static void ValidateReadinessOutput(JsonObject source, List<string> errors)
    {
        var output = RequireObject(source, "readinessOutput");
        RequireValue(output, "readinessFragment", KmsCustodyRehearsalArtifactGenerator.ReadinessFragmentPath, errors);
        RequireValue(output, "scoreProposal", KmsCustodyRehearsalArtifactGenerator.ScoreProposalPath, errors);
        RequireValue(output, "directRegisterMutation", false, errors, "FEAT161_DIRECT_REGISTER_MUTATION_FORBIDDEN");
        RequireValue(output, "doesNotMutateRegister", true, errors, "FEAT161_DIRECT_REGISTER_MUTATION_FORBIDDEN");
    }

    private static void ValidateDownstreamConsumers(JsonObject source, List<string> errors)
    {
        var consumers = RequireArray(source, "downstreamConsumers").OfType<JsonObject>().ToArray();
        var consumerIds = consumers
            .Select(consumer => GetString(consumer, "featureId"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredConsumerId in RequiredDownstreamConsumerIds)
        {
            if (!consumerIds.Contains(requiredConsumerId))
            {
                errors.Add($"FEAT161_DOWNSTREAM_CONSUMER_MISSING: downstreamConsumers missing {requiredConsumerId}.");
            }
        }

        foreach (var consumer in consumers)
        {
            RequireNonEmpty(consumer, "featureId", errors);
            RequireNonEmpty(consumer, "consumes", errors);
        }
    }

    private static void ValidateRelativePaths(JsonNode? node, List<string> errors, string? propertyName = null)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    ValidateRelativePaths(child, errors, name);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    ValidateRelativePaths(child, errors, propertyName);
                }

                break;
            case JsonValue value when propertyName is not null && RelativePathProperties.Contains(propertyName):
                var path = value.GetValue<string>();
                if (Path.IsPathRooted(path) ||
                    path.Contains('\\', StringComparison.Ordinal) ||
                    path.StartsWith("/", StringComparison.Ordinal))
                {
                    errors.Add($"FEAT161_LOCAL_ABSOLUTE_PATH_FORBIDDEN: {propertyName} must be workspace-relative and use forward slashes: {path}");
                }

                break;
        }
    }

    private static void ValidateForbiddenNeedles(
        JsonNode? node,
        List<string> errors,
        string? propertyName = null)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    ValidateForbiddenNeedles(child, errors, name);
                }

                break;
            case JsonArray array:
                if (propertyName is not null && ForbiddenNeedleAllowListProperties.Contains(propertyName))
                {
                    break;
                }

                foreach (var child in array)
                {
                    ValidateForbiddenNeedles(child, errors, propertyName);
                }

                break;
            case JsonValue value:
                var text = value.ToJsonString().Trim('"').ToLowerInvariant();
                foreach (var needle in ForbiddenPrivateNeedles)
                {
                    if (text.Contains(needle, StringComparison.Ordinal))
                    {
                        errors.Add($"FEAT161_PRIVATE_MATERIAL_FORBIDDEN: {propertyName ?? "value"} contains forbidden private material marker '{needle}'.");
                    }
                }

                break;
        }
    }

    private static void RequireFileHash(string path, string expectedHash, string label, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"{label} target file does not exist: {path}");
            return;
        }

        var observed = Sha256File(path);
        if (!HashesEqual(observed, expectedHash))
        {
            errors.Add($"{label} mismatch: expected {expectedHash}, observed {observed}");
        }
    }

    private static void RequireArrayValues(JsonObject value, string property, IReadOnlyList<string> expected, List<string> errors)
    {
        var observed = RequireArray(value, property)
            .Select(item => item?.GetValue<string>() ?? "")
            .ToArray();
        if (!observed.SequenceEqual(expected, StringComparer.Ordinal))
        {
            errors.Add($"{property} must be [{string.Join(", ", expected)}].");
        }
    }

    private static void RequireArrayContains(
        JsonObject value,
        string property,
        string expected,
        List<string> errors,
        string code)
    {
        var observed = RequireArray(value, property)
            .Select(item => item?.GetValue<string>() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        if (!observed.Contains(expected))
        {
            errors.Add($"{Prefix(code)}{property} must include {expected}.");
        }
    }

    private static void RequireRestrictedEvidenceRef(
        JsonObject scenario,
        string expectedRefId,
        List<string> errors,
        string code)
    {
        var matchingRef = RequireArray(scenario, "restrictedEvidenceRefs")
            .OfType<JsonObject>()
            .FirstOrDefault(item => string.Equals(GetString(item, "refId"), expectedRefId, StringComparison.Ordinal));
        if (matchingRef is null)
        {
            errors.Add($"{Prefix(code)}restrictedEvidenceRefs must include {expectedRefId}.");
            return;
        }

        if (!IsSha256(GetString(matchingRef, "hash")))
        {
            errors.Add($"{Prefix(code)}restrictedEvidenceRefs.{expectedRefId}.hash must be SHA-256.");
        }

        if (GetBool(matchingRef, "payloadPublished", fallback: true))
        {
            errors.Add($"{Prefix(code)}restrictedEvidenceRefs.{expectedRefId}.payloadPublished must be false.");
        }
    }

    private static void RequireValue(JsonObject value, string property, string expected, List<string> errors, string? code = null)
    {
        var observed = GetString(value, property);
        if (!string.Equals(observed, expected, StringComparison.Ordinal))
        {
            errors.Add($"{Prefix(code)}{property} must be {expected}; observed {observed}.");
        }
    }

    private static void RequireValue(JsonObject value, string property, int expected, List<string> errors, string? code = null)
    {
        var observed = GetInt(value, property, int.MinValue);
        if (observed != expected)
        {
            errors.Add($"{Prefix(code)}{property} must be {expected}; observed {observed}.");
        }
    }

    private static void RequireValue(JsonObject value, string property, bool expected, List<string> errors, string? code = null)
    {
        var observed = GetBool(value, property, !expected);
        if (observed != expected)
        {
            errors.Add($"{Prefix(code)}{property} must be {expected.ToString().ToLowerInvariant()}.");
        }
    }

    private static void RequireNonEmpty(JsonObject value, string property, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(GetString(value, property)))
        {
            errors.Add($"{property} must not be empty.");
        }
    }

    private static void RequireSha256(JsonObject value, string property, List<string> errors)
    {
        var observed = GetString(value, property);
        if (!IsSha256(observed))
        {
            errors.Add($"{property} must be a sha256:<64 hex> or <64 hex> value.");
        }
    }

    private static bool IsSha256(string value)
    {
        var normalized = NormalizeHash(value);
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit);
    }

    private static string NormalizeHash(string value) =>
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value[7..]
            : value;

    private static string Prefix(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code + ": ";

    private sealed record ScenarioPolicy(
        string Category,
        string ExpectedResult,
        string SafeResultCode,
        string ReadinessImpact,
        string RestrictedRefId,
        string ErrorCode);
}
