using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeploymentRollbackRehearsalPromoter;

public static class DeploymentRollbackRehearsalContracts
{
    public const string FeatureId = "FEAT-162";
    public const string SourceSchemaVersion = "deployment-rollback-rehearsal-source.v1";
    public const string PackageManifestSchemaVersion = "deployment-rollback-rehearsal-package-manifest.v1";
    public const string CurrentRegisterId = "RDY-REG-v0.1.7";
    public const string CurrentRegisterVersion = "v0.1.7";
    public const string TargetDimensionId = "RDY-DIM-006";
    public const string TargetDimensionName = "Trusted deployment ceremony";
    public const string TargetBlockerId = "RDY-BLOCK-INTERNAL_AUDIT_95_DIM006-001";
    public const string TargetPackageVersion = "v0.1.0";
    public const string ExpectedTargetPackagePath = "Deployment-Proof-Packages/rehearsals/deployment-rollback-emergency/DRR-REHEARSAL-20260602-001/";
    public const string CanonicalizationVersion = "deployment-rollback-rehearsal-canonical-json.v1";

    public static readonly string[] RequiredSchemaFiles =
    [
        DeploymentRollbackRehearsalPromotionPaths.SourceSchemaFileName,
        DeploymentRollbackRehearsalPromotionPaths.PackageManifestSchemaFileName,
    ];

    public static readonly string[] RequiredScenarioIds =
    [
        "DEPLOY-ROLLBACK-ACCEPTED-FEAT132-BASELINE",
        "DEPLOY-ROLLBACK-SECOND-CEREMONY",
        "DEPLOY-ROLLBACK-NO-CHANGE-FREEZE",
        "DEPLOY-ROLLBACK-NON-VOTING-CHANGE",
        "DEPLOY-ROLLBACK-OPERATIONAL-CONFIG-CHANGE",
        "DEPLOY-ROLLBACK-TO-LAST-ACCEPTED",
        "DEPLOY-ROLLBACK-EMERGENCY-OPEN-ELECTION",
        "DEPLOY-ROLLBACK-WEBCLIENT-OBSERVED-PROOF",
        "DEPLOY-ROLLBACK-CUSTODY-IMPACT-CHECK",
        "DEPLOY-ROLLBACK-RESTRICTED-BOUNDARY",
    ];

    public static readonly string[] RequiredNegativeCaseIds =
    [
        "NEG-STALE-READINESS-REGISTER",
        "NEG-MISSING-FEAT132-PROOF",
        "NEG-STALE-FEAT143-REF",
        "NEG-STALE-FEAT144-REF",
        "NEG-UNKNOWN-EMERGENCY-CLASSIFICATION",
        "NEG-MISSING-WEBCLIENT-OBSERVED-PROOF",
        "NEG-DIRECT-REGISTER-MUTATION",
        "NEG-OVERCLAIM-SCORE-TO-10",
        "NEG-PRIVATE-LOCAL-PATH",
        "NEG-RESTRICTED-MATERIAL-PUBLISHED",
    ];

    public static readonly string[] RequiredDownstreamConsumerIds =
    [
        "FEAT-163",
        "FEAT-166",
    ];

    private static readonly IReadOnlyDictionary<string, ScenarioPolicy> RequiredScenarioPolicies =
        new Dictionary<string, ScenarioPolicy>(StringComparer.Ordinal)
        {
            ["DEPLOY-ROLLBACK-ACCEPTED-FEAT132-BASELINE"] = new("accepted_baseline", "pass", "accepted_feat132_baseline_verified", "supports_score_proposal", "FEAT162_ACCEPTED_BASELINE_POLICY_INVALID"),
            ["DEPLOY-ROLLBACK-SECOND-CEREMONY"] = new("second_ceremony", "pass", "second_ceremony_hash_bound", "supports_score_proposal", "FEAT162_SECOND_CEREMONY_POLICY_INVALID"),
            ["DEPLOY-ROLLBACK-NO-CHANGE-FREEZE"] = new("no_change", "pass", "no_change_freeze_verified", "supports_score_proposal", "FEAT162_NO_CHANGE_POLICY_INVALID"),
            ["DEPLOY-ROLLBACK-NON-VOTING-CHANGE"] = new("non_voting_change", "pass", "non_voting_change_classified", "supports_score_proposal", "FEAT162_NON_VOTING_POLICY_INVALID"),
            ["DEPLOY-ROLLBACK-OPERATIONAL-CONFIG-CHANGE"] = new("operational_config_change", "pass", "operational_config_rerun_checks_passed", "supports_score_proposal", "FEAT162_OPERATIONAL_CONFIG_POLICY_INVALID"),
            ["DEPLOY-ROLLBACK-TO-LAST-ACCEPTED"] = new("rollback", "pass", "rollback_to_accepted_artifact_set_verified", "supports_score_proposal", "FEAT162_ROLLBACK_POLICY_INVALID"),
            ["DEPLOY-ROLLBACK-EMERGENCY-OPEN-ELECTION"] = new("emergency_change", "pass", "emergency_change_rerun_checks_passed", "supports_score_proposal", "FEAT162_EMERGENCY_POLICY_INVALID"),
            ["DEPLOY-ROLLBACK-WEBCLIENT-OBSERVED-PROOF"] = new("webclient_observed_proof", "degraded", "webclient_observed_proof_limitation_preserved", "records_residual_risk", "FEAT162_WEBCLIENT_OBSERVED_PROOF_POLICY_INVALID"),
            ["DEPLOY-ROLLBACK-CUSTODY-IMPACT-CHECK"] = new("custody_impact", "pass", "custody_impact_handoff_current", "supports_score_proposal", "FEAT162_CUSTODY_IMPACT_POLICY_INVALID"),
            ["DEPLOY-ROLLBACK-RESTRICTED-BOUNDARY"] = new("restricted_boundary", "restricted_only", "restricted_boundary_preserved", "preserves_private_boundary", "FEAT162_RESTRICTED_BOUNDARY_POLICY_INVALID"),
        };

    private static readonly HashSet<string> RelativePathProperties = new(StringComparer.Ordinal)
    {
        "targetPackagePath",
        "expectedArtifacts",
        "expectedSecondCeremonyArtifacts",
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
        "begin private key",
        "credential=",
        "password=",
        "connection string",
        "client_secret",
        "account_id",
        "operator identity:",
        "raw ci log",
        "runbook:",
        "provider_account",
        "emergency_payload_raw",
        "private screenshot",
        "voter_data",
        "kms_key",
        "kms_alias",
        @"c:\mywork\hushnetworkorg\hush-documents",
    ];

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static JsonObject LoadSource(
        DeploymentRollbackRehearsalPromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "FEAT-162 deployment rollback rehearsal source");
        if (!File.Exists(sourcePath))
        {
            throw new DeploymentRollbackRehearsalPromotionException(
                "FEAT-162 deployment rollback rehearsal source input is missing.",
                [$"Source input was not found: {sourcePath}"]);
        }

        return ReadJsonObject(sourcePath, DeploymentRollbackRehearsalPromotionPaths.SourceFileName);
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
        var errors = ValidateJsonRequired(source, DeploymentRollbackRehearsalPromotionPaths.SourceFileName, [
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
        DeploymentRollbackRehearsalPromotionPaths paths,
        string? sourceInput = null,
        bool publicOnly = false)
    {
        var schemaErrors = ValidateSchemaSet(paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new DeploymentRollbackRehearsalPromotionException(
                "FEAT-162 deployment rollback rehearsal schema validation failed.",
                schemaErrors);
        }

        var source = LoadSource(paths, sourceInput);
        var errors = ValidateSource(source).ToList();
        errors.AddRange(ValidateCurrentRefs(paths, source, publicOnly));
        if (errors.Count > 0)
        {
            throw new DeploymentRollbackRehearsalPromotionException(
                "FEAT-162 deployment rollback rehearsal source validation failed.",
                errors);
        }

        return source;
    }

    public static IReadOnlyList<string> ValidateCurrentRefs(
        DeploymentRollbackRehearsalPromotionPaths paths,
        JsonObject source,
        bool publicOnly = false)
    {
        var errors = new List<string>();
        var upstream = RequireObject(source, "upstreamBaselines");
        var feat132 = RequireObject(upstream, "feat132");
        var feat143 = RequireObject(upstream, "feat143");
        var feat144 = RequireObject(upstream, "feat144");
        var feat154 = RequireObject(upstream, "feat154");
        var feat156 = RequireObject(upstream, "feat156");
        var feat161 = RequireObject(upstream, "feat161");

        var catalogPath = Path.Combine(paths.PublicProofPackagesRoot, "deployment-proof-catalog.json");
        if (!File.Exists(catalogPath))
        {
            errors.Add("Deployment-Proof-Packages deployment-proof-catalog.json is missing.");
        }
        else
        {
            var catalogText = File.ReadAllText(catalogPath);
            foreach (var expected in new[]
            {
                GetString(feat132, "ceremonyId"),
                GetString(feat132, "webClientProofId"),
                GetString(feat132, "serverProofId"),
                GetString(feat132, "proofSetId"),
                GetString(feat132, "ledgerId"),
            })
            {
                if (!catalogText.Contains(expected, StringComparison.Ordinal))
                {
                    errors.Add($"Deployment-Proof-Packages catalog is missing expected FEAT-132 ref {expected}.");
                }
            }
        }

        RequireFileHash(
            Path.Combine(paths.PublicProofPackagesRoot, "ceremonies", GetString(feat132, "ceremonyId"), "readiness-fragment.json"),
            GetString(feat132, "readinessFragmentHash"),
            "upstreamBaselines.feat132.readinessFragmentHash",
            errors);
        if (!publicOnly)
        {
            RequireFileHash(
                Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-143-runtime-deployment-proof-binding-ledger", "readiness-handoff-20260526.md"),
                GetString(feat143, "referenceHash"),
                "upstreamBaselines.feat143.referenceHash",
                errors);
            RequireFileHash(
                Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-144-hushwebclient-deployment-proof-exposure-handshake", "FeatureDescription.md"),
                GetString(feat144, "referenceHash"),
                "upstreamBaselines.feat144.referenceHash",
                errors);
            RequireFileHash(
                Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-154-production-like-operational-run-evidence", "feature-completion-report.md"),
                GetString(feat154, "referenceHash"),
                "upstreamBaselines.feat154.referenceHash",
                errors);
            RequireFileHash(
                Path.Combine(paths.WorkspaceRoot, "hush-documents", "PrivateServer_ElectronicVoting", "Production-Rollout-Promotion-Register", "package", "feat156-package-manifest.json"),
                GetString(feat156, "referenceHash"),
                "upstreamBaselines.feat156.referenceHash",
                errors);
            RequireFileHash(
                Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-161-kms-custody-drift-rotation-recovery-rehearsal", "feature-completion-report.md"),
                GetString(feat161, "referenceHash"),
                "upstreamBaselines.feat161.referenceHash",
                errors);
        }

        return errors;
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new DeploymentRollbackRehearsalPromotionException($"{label} is not a JSON object.");
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

        throw new DeploymentRollbackRehearsalPromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new DeploymentRollbackRehearsalPromotionException($"Missing array property: {property}");
    }

    public static string GetString(JsonObject value, string property, string? defaultValue = null)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonValue jsonValue)
        {
            return jsonValue.GetValue<string>();
        }

        if (defaultValue is not null)
        {
            return defaultValue;
        }

        throw new DeploymentRollbackRehearsalPromotionException($"Missing string property: {property}");
    }

    public static bool GetBool(JsonObject value, string property, bool defaultValue = false)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonValue jsonValue)
        {
            return jsonValue.GetValue<bool>();
        }

        return defaultValue;
    }

    public static int GetInt(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonValue jsonValue)
        {
            return jsonValue.GetValue<int>();
        }

        throw new DeploymentRollbackRehearsalPromotionException($"Missing integer property: {property}");
    }

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    public static string CanonicalJson(JsonNode node)
    {
        return node.ToJsonString(CanonicalJsonOptions) + Environment.NewLine;
    }

    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    public static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeLineEndings(value)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string FileSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static void EnsurePathUnder(string root, string path, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new DeploymentRollbackRehearsalPromotionException(
                "FEAT-162 deployment rollback rehearsal path escaped the expected root.",
                [$"{label}: {fullPath}", $"Root: {fullRoot}"]);
        }
    }

    private static string ResolveSourceInput(
        DeploymentRollbackRehearsalPromotionPaths paths,
        string? sourceInput)
    {
        if (string.IsNullOrWhiteSpace(sourceInput))
        {
            return Path.GetFullPath(paths.DefaultSourceInput);
        }

        return Path.GetFullPath(Path.IsPathRooted(sourceInput)
            ? sourceInput
            : Path.Combine(paths.SourceRoot, sourceInput));
    }

    private static void ValidateBaselineRegister(JsonObject source, List<string> errors)
    {
        var baseline = RequireObject(source, "baselineRegister");
        RequireValue(baseline, "registerVersionId", CurrentRegisterId, errors, "FEAT162_STALE_READINESS_BASELINE");
        RequireValue(baseline, "registerVersion", CurrentRegisterVersion, errors, "FEAT162_STALE_READINESS_BASELINE");
        RequireValue(baseline, "status", "AcceptedInternal", errors, "FEAT162_STALE_READINESS_BASELINE");
        RequireValue(baseline, "dimensionId", TargetDimensionId, errors, "FEAT162_TARGET_DIMENSION_MISMATCH");
        RequireValue(baseline, "dimensionName", TargetDimensionName, errors, "FEAT162_TARGET_DIMENSION_MISMATCH");
        RequireValue(baseline, "targetBlockerId", TargetBlockerId, errors, "FEAT162_TARGET_BLOCKER_MISMATCH");
        RequireValue(baseline, "blockerOwnerFeatureId", FeatureId, errors, "FEAT162_TARGET_BLOCKER_MISMATCH");
        RequireInt(baseline, "totalScore", 80, errors, "FEAT162_STALE_READINESS_BASELINE");
        RequireInt(baseline, "internalAuditTargetScore", 95, errors, "FEAT162_STALE_READINESS_BASELINE");
        RequireInt(baseline, "currentScore", 8, errors, "FEAT162_SCORE_BASELINE_INVALID");
        RequireInt(baseline, "proposedScore", 9, errors, "FEAT162_SCORE_OVERCLAIM_FORBIDDEN");
    }

    private static void ValidateUpstreamBaselines(JsonObject source, List<string> errors)
    {
        var upstream = RequireObject(source, "upstreamBaselines");
        if (!upstream.ContainsKey("feat132"))
        {
            errors.Add("FEAT162_MISSING_FEAT132_REF: upstreamBaselines.feat132 is required.");
            return;
        }

        var feat132 = RequireObject(upstream, "feat132");
        RequireValue(feat132, "producerFeature", "FEAT-132", errors, "FEAT162_MISSING_FEAT132_REF");
        RequireValue(feat132, "evidenceId", "RDY-EVID-AT-RDY-005-FEAT-132-001", errors, "FEAT162_MISSING_FEAT132_REF");
        RequireValue(feat132, "dimensionId", TargetDimensionId, errors, "FEAT162_MISSING_FEAT132_REF");
        RequireValue(feat132, "publicRepository", "https://github.com/Hushnetwork-social/Deployment-Proof-Packages", errors, "FEAT162_MISSING_FEAT132_REF");
        RequireValue(feat132, "publicBranch", "main", errors, "FEAT162_MISSING_FEAT132_REF");
        RequireValue(feat132, "ceremonyId", "DPC-REHEARSAL-20260519-001", errors, "FEAT162_MISSING_FEAT132_REF");
        RequireValue(feat132, "webClientProofId", "DPP-WEB-20260519-001", errors, "FEAT162_MISSING_FEAT132_REF");
        RequireValue(feat132, "serverProofId", "DPP-SERVER-20260519-001", errors, "FEAT162_MISSING_FEAT132_REF");
        RequireValue(feat132, "proofSetId", "DPS-REHEARSAL-20260519-001", errors, "FEAT162_MISSING_FEAT132_REF");
        RequireValue(feat132, "ledgerId", "DPBL-REHEARSAL-20260519-001", errors, "FEAT162_MISSING_FEAT132_REF");
        RequireSha256(feat132, "readinessFragmentHash", errors);

        ValidateUpstreamFeature(upstream, "feat143", "FEAT-143", "FEAT162_STALE_FEAT143_REF", errors);
        ValidateUpstreamFeature(upstream, "feat144", "FEAT-144", "FEAT162_STALE_FEAT144_REF", errors);
        ValidateUpstreamFeature(upstream, "feat154", "FEAT-154", "FEAT162_STALE_FEAT154_REF", errors);
        ValidateUpstreamFeature(upstream, "feat156", "FEAT-156", "FEAT162_STALE_FEAT156_REF", errors);
        ValidateUpstreamFeature(upstream, "feat161", "FEAT-161", "FEAT162_STALE_FEAT161_REF", errors);
    }

    private static void ValidateUpstreamFeature(
        JsonObject upstream,
        string key,
        string expectedFeature,
        string diagnostic,
        List<string> errors)
    {
        if (!upstream.ContainsKey(key))
        {
            errors.Add($"{diagnostic}: upstreamBaselines.{key} is required.");
            return;
        }

        var value = RequireObject(upstream, key);
        RequireValue(value, "producerFeature", expectedFeature, errors, diagnostic);
        RequireSha256(value, "referenceHash", errors);
    }

    private static void ValidateScorePolicy(JsonObject source, List<string> errors)
    {
        var policy = RequireObject(source, "scorePolicy");
        RequireValue(policy, "dimensionId", TargetDimensionId, errors, "FEAT162_SCORE_POLICY_INVALID");
        RequireInt(policy, "proposedScoreFrom", 8, errors, "FEAT162_SCORE_POLICY_INVALID");
        RequireInt(policy, "proposedScoreTo", 9, errors, "FEAT162_SCORE_OVERCLAIM_FORBIDDEN");
        RequireBool(policy, "directRegisterMutation", false, errors, "FEAT162_DIRECT_REGISTER_MUTATION_FORBIDDEN");
        RequireBool(policy, "doesNotMutateRegister", true, errors, "FEAT162_DIRECT_REGISTER_MUTATION_FORBIDDEN");
        RequireBool(policy, "scoreMovementBlockedUnlessAllCasesPass", true, errors, "FEAT162_SCORE_POLICY_INVALID");
    }

    private static void ValidateRehearsalMatrix(JsonObject source, List<string> errors)
    {
        var matrix = RequireObject(source, "rehearsalMatrix");
        RequireValue(matrix, "defaultValidationMode", "deterministic_rehearsal_fixture", errors, "FEAT162_VALIDATION_MODE_INVALID");
        RequireBool(matrix, "liveDeploymentRequiredForDefaultValidation", false, errors, "FEAT162_LIVE_DEPLOYMENT_DEFAULT_FORBIDDEN");

        var scenarios = RequireArray(matrix, "scenarios").OfType<JsonObject>().ToArray();
        foreach (var scenarioId in RequiredScenarioIds)
        {
            if (scenarios.All(item => GetString(item, "scenarioId", "") != scenarioId))
            {
                errors.Add($"FEAT162_REQUIRED_SCENARIO_MISSING: {scenarioId}");
            }
        }

        foreach (var scenario in scenarios)
        {
            ValidateScenario(scenario, errors);
        }
    }

    private static void ValidateScenario(JsonObject scenario, List<string> errors)
    {
        var scenarioId = GetString(scenario, "scenarioId", "");
        if (!RequiredScenarioPolicies.TryGetValue(scenarioId, out var policy))
        {
            return;
        }

        if (GetString(scenario, "category", "") != policy.Category ||
            GetString(scenario, "expectedResult", "") != policy.ExpectedResult ||
            GetString(scenario, "readinessImpact", "") != policy.ReadinessImpact ||
            !RequireArray(scenario, "safeResultCodes").Any(item => item?.GetValue<string>() == policy.RequiredSafeResultCode) ||
            !GetBool(scenario, "requiredForScore"))
        {
            errors.Add($"{policy.Diagnostic}: {scenarioId} does not match required policy.");
        }

        foreach (var restrictedRef in RequireArray(scenario, "restrictedEvidenceRefs").OfType<JsonObject>())
        {
            RequireBool(restrictedRef, "payloadPublished", false, errors, "FEAT162_RESTRICTED_PAYLOAD_FORBIDDEN");
            RequireSha256(restrictedRef, "hash", errors);
        }
    }

    private static void ValidateNegativeMatrix(JsonObject source, List<string> errors)
    {
        var negatives = RequireArray(source, "negativeMatrix").OfType<JsonObject>().ToArray();
        foreach (var caseId in RequiredNegativeCaseIds)
        {
            if (negatives.All(item => GetString(item, "caseId", "") != caseId))
            {
                errors.Add($"FEAT162_REQUIRED_NEGATIVE_CASE_MISSING: {caseId}");
            }
        }
    }

    private static void ValidatePackageLayout(JsonObject source, List<string> errors)
    {
        var layout = RequireObject(source, "packageLayout");
        RequireValue(layout, "targetPackagePath", ExpectedTargetPackagePath, errors, "FEAT162_PACKAGE_TARGET_INVALID");

        var expected = RequireArray(layout, "expectedArtifacts").Select(item => item?.GetValue<string>()).Where(item => item is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var artifact in DeploymentRollbackRehearsalArtifactGenerator.RequiredArtifactPaths)
        {
            if (!expected.Contains(artifact))
            {
                errors.Add($"FEAT162_PACKAGE_ARTIFACT_MISSING: {artifact}");
            }
        }

        var expectedSecondCeremonyArtifacts = RequireArray(layout, "expectedSecondCeremonyArtifacts")
            .Select(item => item?.GetValue<string>())
            .Where(item => item is not null)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var artifact in DeploymentRollbackRehearsalArtifactGenerator.RequiredSecondCeremonyArtifactPaths)
        {
            if (!expectedSecondCeremonyArtifacts.Contains(artifact))
            {
                errors.Add($"FEAT162_SECOND_CEREMONY_ARTIFACT_MISSING: {artifact}");
            }
        }
    }

    private static void ValidatePublicSafety(JsonObject source, List<string> errors)
    {
        var safety = RequireObject(source, "publicSafety");
        RequireBool(safety, "publicOnlyValidation", true, errors, "FEAT162_PUBLIC_ONLY_VALIDATION_REQUIRED");
        RequireBool(safety, "liveCredentialRequiredForDefaultValidation", false, errors, "FEAT162_LIVE_DEPLOYMENT_DEFAULT_FORBIDDEN");
        RequireBool(safety, "directPrivateRepoDependency", false, errors, "FEAT162_PRIVATE_REPO_DEPENDENCY_FORBIDDEN");
    }

    private static void ValidateRestrictedEvidenceBoundary(JsonObject source, List<string> errors)
    {
        var boundary = RequireObject(source, "restrictedEvidenceBoundary");
        RequireBool(boundary, "payloadPublished", false, errors, "FEAT162_RESTRICTED_PAYLOAD_FORBIDDEN");
    }

    private static void ValidateReadinessOutput(JsonObject source, List<string> errors)
    {
        var readiness = RequireObject(source, "readinessOutput");
        RequireBool(readiness, "directRegisterMutation", false, errors, "FEAT162_DIRECT_REGISTER_MUTATION_FORBIDDEN");
    }

    private static void ValidateDownstreamConsumers(JsonObject source, List<string> errors)
    {
        var consumers = RequireArray(source, "downstreamConsumers").OfType<JsonObject>().ToArray();
        foreach (var consumerId in RequiredDownstreamConsumerIds)
        {
            if (consumers.All(item => GetString(item, "consumerId", "") != consumerId))
            {
                errors.Add($"FEAT162_DOWNSTREAM_CONSUMER_MISSING: {consumerId}");
            }
        }
    }

    private static void ValidateRelativePaths(JsonObject source, List<string> errors) =>
        WalkJson(source, (property, node) =>
        {
            if (!RelativePathProperties.Contains(property) || node is not JsonValue jsonValue)
            {
                return;
            }

            if (!jsonValue.TryGetValue<string>(out var text))
            {
                return;
            }

            foreach (var value in SplitMaybeArrayValue(text))
            {
                if (Path.IsPathRooted(value) || value.StartsWith("/", StringComparison.Ordinal))
                {
                    errors.Add($"FEAT162_LOCAL_ABSOLUTE_PATH_FORBIDDEN: {property} contains {value}.");
                }
            }
        });

    private static void ValidateForbiddenNeedles(JsonObject source, List<string> errors) =>
        WalkJson(source, (property, node) =>
        {
            if (ForbiddenNeedleAllowListProperties.Contains(property) || node is not JsonValue jsonValue)
            {
                return;
            }

            if (!jsonValue.TryGetValue<string>(out var text))
            {
                return;
            }

            var lower = text.ToLowerInvariant();
            foreach (var forbidden in ForbiddenPrivateNeedles)
            {
                if (lower.Contains(forbidden, StringComparison.Ordinal))
                {
                    errors.Add($"FEAT162_PRIVATE_MATERIAL_FORBIDDEN: {property} contains forbidden marker {forbidden}.");
                }
            }
        });

    private static void WalkJson(JsonNode? node, Action<string, JsonNode> visitor, string property = "")
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (pair.Value is not null)
                    {
                        WalkJson(pair.Value, visitor, pair.Key);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                    {
                        WalkJson(item, visitor, property);
                    }
                }

                break;
            case JsonValue value:
                visitor(property, value);
                break;
        }
    }

    private static IEnumerable<string> SplitMaybeArrayValue(string value)
    {
        yield return value;
    }

    private static void RequireValue(
        JsonObject value,
        string property,
        string expected,
        List<string> errors,
        string diagnostic = "FEAT162_VALUE_MISMATCH")
    {
        var actual = GetString(value, property, "");
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            errors.Add($"{diagnostic}: {property} expected {expected} but found {actual}.");
        }
    }

    private static void RequireInt(JsonObject value, string property, int expected, List<string> errors, string diagnostic)
    {
        try
        {
            if (GetInt(value, property) != expected)
            {
                errors.Add($"{diagnostic}: {property} expected {expected}.");
            }
        }
        catch (DeploymentRollbackRehearsalPromotionException)
        {
            errors.Add($"{diagnostic}: {property} is required.");
        }
    }

    private static void RequireBool(JsonObject value, string property, bool expected, List<string> errors, string diagnostic)
    {
        if (!value.TryGetPropertyValue(property, out var node) || node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<bool>(out var actual) || actual != expected)
        {
            errors.Add($"{diagnostic}: {property} expected {expected}.");
        }
    }

    private static void RequireSha256(JsonObject value, string property, List<string> errors)
    {
        var observed = GetString(value, property, "");
        var text = observed.StartsWith("sha256:", StringComparison.Ordinal) ? observed[7..] : observed;
        if (text.Length != 64 || text.Any(c => !Uri.IsHexDigit(c)))
        {
            errors.Add($"{property} must be a SHA-256 hash.");
        }
    }

    private static void RequireFileHash(string path, string expectedHash, string label, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"{label} file missing: {path}");
            return;
        }

        var expected = expectedHash.StartsWith("sha256:", StringComparison.Ordinal) ? expectedHash[7..] : expectedHash;
        var observed = FileSha256Hex(path);
        if (!string.Equals(observed, expected, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{label} mismatch. Expected {expected}, observed {observed}.");
        }
    }

    private sealed record ScenarioPolicy(
        string Category,
        string ExpectedResult,
        string RequiredSafeResultCode,
        string ReadinessImpact,
        string Diagnostic);
}
