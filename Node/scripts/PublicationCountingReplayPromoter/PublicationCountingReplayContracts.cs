using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublicationCountingReplayPromoter;

public static class PublicationCountingReplayContracts
{
    public const string FeatureId = "FEAT-160";
    public const string SourceSchemaVersion = "publication-counting-replay-source.v1";
    public const string PackageManifestSchemaVersion = "publication-counting-replay-package-manifest.v1";
    public const string CurrentRegisterId = "RDY-REG-v0.1.7";
    public const string CurrentRegisterVersion = "v0.1.7";
    public const string TargetDimensionId = "RDY-DIM-004";
    public const string TargetBlockerId = "RDY-BLOCK-INTERNAL_AUDIT_95_DIM004-001";
    public const string TargetPackageVersion = "v0.2.0";
    public const string ExpectedTargetPackagePath = "HushVoting-Verifier-Corpus/hushvoting-v1/publication-counting-replay/v0.2.0/";
    public const string CanonicalizationVersion = "publication-counting-replay-canonical-json.v1";

    public static readonly string[] RequiredSchemaFiles =
    [
        PublicationCountingReplayPromotionPaths.SourceSchemaFileName,
        PublicationCountingReplayPromotionPaths.PackageManifestSchemaFileName,
    ];

    public static readonly string[] RequiredGoodProfileIds =
    [
        "sample-good-finalized-election",
        "sample-good-larger-electorate",
        "sample-good-low-turnout",
        "sample-good-multi-option-single-winner",
        "sample-good-trustee-threshold",
    ];

    public static readonly string[] RequiredNegativeFixtureIds =
    [
        "tamper-missing-artifact",
        "tamper-malformed-package-json",
        "tamper-trustee-release-wrong-target",
        "tamper-trustee-release-threshold-not-met",
    ];

    private static readonly string[] ForbiddenMaterialCategories =
    [
        "shuffle_maps",
        "rerandomization_randomness",
        "plaintext_choices",
        "voter_identity_joins",
        "kms_secrets",
        "support_case_data",
        "local_absolute_paths",
        "private_backend_logs",
        "cloud_account_identifiers",
        "database_connection_strings",
        "legal_sufficiency_claims",
        "public_state_election_claims",
        "production_rollout_claims",
    ];

    private static readonly string[] ForbiddenClaimCategories =
    [
        "production_ready",
        "public_state_ready",
        "legally_sufficient",
        "certified",
        "external_crypto_review_complete",
    ];

    private static readonly string[] ForbiddenPrivateNeedles =
    [
        "private key",
        "seed phrase",
        "mnemonic",
        "credential=",
        "password=",
        "connection string",
        "aws_secret",
        "client_secret",
        "hush-documents/privateserver_electronicvoting",
        @"c:\mywork\hushnetworkorg\hush-documents",
    ];

    private static readonly HashSet<string> RelativePathProperties = new(StringComparer.Ordinal)
    {
        "corpusPath",
        "packagePath",
        "expectedResultRef",
        "path",
        "targetPackagePath",
        "readinessFragmentPath",
        "scoreProposalPath",
    };

    private static readonly HashSet<string> ForbiddenNeedleAllowListProperties = new(StringComparer.Ordinal)
    {
        "forbiddenMaterialCategories",
        "forbiddenClaimCategories",
    };

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static JsonObject LoadSource(
        PublicationCountingReplayPromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "FEAT-160 replay source");
        if (!File.Exists(sourcePath))
        {
            throw new PublicationCountingReplayPromotionException(
                "FEAT-160 publication/counting replay source input is missing.",
                [$"Source input was not found: {sourcePath}"]);
        }

        return ReadJsonObject(sourcePath, PublicationCountingReplayPromotionPaths.SourceFileName);
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
        var errors = ValidateJsonRequired(source, PublicationCountingReplayPromotionPaths.SourceFileName, [
            "schemaVersion",
            "sourceId",
            "producerFeature",
            "status",
            "generatedAt",
            "baselineRegister",
            "upstreamBaselines",
            "scorePolicy",
            "replayMatrix",
            "negativeMatrix",
            "packageLayout",
            "publicSafety",
            "readinessOutput",
            "downstreamConsumers",
            "residualRisks",
        ]).ToList();

        RequireValue(source, "schemaVersion", SourceSchemaVersion, errors);
        RequireValue(source, "producerFeature", FeatureId, errors);
        ValidateBaselineRegister(source, errors);
        ValidateUpstreamBaselines(source, errors);
        ValidateScorePolicy(source, errors);
        ValidateReplayMatrix(source, errors);
        ValidateNegativeMatrix(source, errors);
        ValidatePackageLayout(source, errors);
        ValidatePublicSafety(source, errors);
        ValidateReadinessOutput(source, errors);
        ValidateDownstreamConsumers(source, errors);
        ValidateRelativePaths(source, errors);
        ValidateForbiddenNeedles(source, errors);

        return errors;
    }

    public static JsonObject ValidateForPromotion(
        PublicationCountingReplayPromotionPaths paths,
        string? sourceInput = null)
    {
        var schemaErrors = ValidateSchemaSet(paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new PublicationCountingReplayPromotionException(
                "FEAT-160 publication/counting replay schema validation failed.",
                schemaErrors);
        }

        var source = LoadSource(paths, sourceInput);
        var errors = ValidateSource(source).ToList();
        errors.AddRange(ValidateCurrentRefs(paths, source));
        if (errors.Count > 0)
        {
            throw new PublicationCountingReplayPromotionException(
                "FEAT-160 publication/counting replay source validation failed.",
                errors);
        }

        return source;
    }

    public static IReadOnlyList<string> ValidateCurrentRefs(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject source)
    {
        var errors = new List<string>();
        var upstream = RequireObject(source, "upstreamBaselines");
        ValidateFeat153Refs(paths, RequireObject(upstream, "feat153"), errors);
        ValidateFeat158Refs(paths, RequireObject(upstream, "feat158"), source, errors);
        return errors;
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new PublicationCountingReplayPromotionException($"{label} is not a JSON object.");
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

        throw new PublicationCountingReplayPromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new PublicationCountingReplayPromotionException($"Missing array property: {property}");
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
            throw new PublicationCountingReplayPromotionException(
                "Publication/counting replay path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    public static string ResolveSourceInput(
        PublicationCountingReplayPromotionPaths paths,
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
            ? Path.Combine(fullPath, PublicationCountingReplayPromotionPaths.SourceFileName)
            : fullPath;
    }

    public static string ResolveWorkspaceRelativePath(string workspaceRoot, string relativePath)
    {
        var combined = Path.Combine(workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var fullPath = Path.GetFullPath(combined);
        EnsurePathUnder(workspaceRoot, fullPath, relativePath);
        return fullPath;
    }

    public static string CanonicalJson(JsonNode node) =>
        NormalizeLineEndings(node.ToJsonString(CanonicalJsonOptions)) + "\n";

    public static string Sha256Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(NormalizeLineEndings(content))))
            .ToLowerInvariant();

    public static string Sha256File(string path) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static void ValidateBaselineRegister(JsonObject source, List<string> errors)
    {
        var baseline = RequireObject(source, "baselineRegister");
        RequireValue(baseline, "registerVersionId", CurrentRegisterId, errors, "FEAT160_STALE_READINESS_BASELINE");
        RequireValue(baseline, "registerVersion", CurrentRegisterVersion, errors, "FEAT160_STALE_READINESS_BASELINE");
        RequireValue(baseline, "status", "AcceptedInternal", errors, "FEAT160_STALE_READINESS_BASELINE");
        RequireValue(baseline, "totalScore", 80, errors, "FEAT160_STALE_READINESS_BASELINE");
        RequireValue(baseline, "internalAuditTargetScore", 95, errors, "FEAT160_STALE_READINESS_BASELINE");
        RequireValue(baseline, "dimensionId", TargetDimensionId, errors, "FEAT160_SCORE_DIMENSION_INVALID");
        RequireValue(baseline, "currentScore", 8, errors, "FEAT160_SCORE_DIMENSION_INVALID");
        RequireValue(baseline, "proposedScore", 10, errors, "FEAT160_SCORE_DIMENSION_INVALID");
        RequireValue(baseline, "targetBlockerId", TargetBlockerId, errors, "FEAT160_BLOCKER_OWNERSHIP_INVALID");
        RequireValue(baseline, "blockerOwnerFeatureId", FeatureId, errors, "FEAT160_BLOCKER_OWNERSHIP_INVALID");
    }

    private static void ValidateUpstreamBaselines(JsonObject source, List<string> errors)
    {
        var upstream = RequireObject(source, "upstreamBaselines");
        var feat153 = RequireObject(upstream, "feat153");
        var feat158 = RequireObject(upstream, "feat158");

        RequireValue(feat153, "packagePath", "HushVoting-Verifier-Corpus/hushvoting-v1/publication-counting-hardening/v0.1.0/", errors);
        RequireValue(feat153, "packageVersion", "v0.1.0", errors);
        RequireValue(feat153, "producerFeature", "FEAT-153", errors);
        RequireSha256(feat153, "manifestHash", errors);
        RequireSha256(feat153, "scoreProposalHash", errors);
        RequireSha256(feat153, "readinessFragmentHash", errors);
        RequireSha256(feat153, "handoffHash", errors);
        RequireNonEmpty(feat153, "verifierSourceRef", errors);
        RequireSha256(feat153, "verifierBinaryRelease", errors);

        RequireValue(feat158, "corpusPath", "HushVoting-Verifier-Corpus/hushvoting-v1/v0.3.0/", errors);
        RequireValue(feat158, "corpusVersion", "v0.3.0", errors);
        RequireValue(feat158, "status", "accepted", errors);
        RequireValue(feat158, "visibility", "public", errors);
        RequireSha256(feat158, "manifestHash", errors);
        RequireSha256(feat158, "fixtureIndexHash", errors);
        RequireSha256(feat158, "cleanMachineValidationHash", errors);
        RequireSha256(feat158, "resultCodeStabilityHash", errors);
        RequireSha256(feat158, "noSecretScanHash", errors);
        RequireValue(feat158, "protocolPackageVersion", "v1.2.0", errors);
        RequireNonEmpty(feat158, "verifierSourceRef", errors);
        RequireSha256(feat158, "verifierBinaryRelease", errors);
    }

    private static void ValidateScorePolicy(JsonObject source, List<string> errors)
    {
        var policy = RequireObject(source, "scorePolicy");
        RequireValue(policy, "dimensionId", TargetDimensionId, errors, "FEAT160_SCORE_DIMENSION_INVALID");
        RequireValue(policy, "proposedScoreFrom", 8, errors, "FEAT160_SCORE_DIMENSION_INVALID");
        RequireValue(policy, "proposedScoreTo", 10, errors, "FEAT160_SCORE_DIMENSION_INVALID");
        RequireValue(policy, "directRegisterMutation", false, errors, "FEAT160_DIRECT_REGISTER_MUTATION_FORBIDDEN");
        RequireValue(policy, "doesNotMutateRegister", true, errors, "FEAT160_DIRECT_REGISTER_MUTATION_FORBIDDEN");
        RequireValue(policy, "scoreMovementBlockedUnlessAllCasesPass", true, errors);
    }

    private static void ValidateReplayMatrix(JsonObject source, List<string> errors)
    {
        var matrix = RequireObject(source, "replayMatrix");
        var goodProfiles = RequireArray(matrix, "goodProfiles").OfType<JsonObject>().ToArray();
        var goodIds = goodProfiles
            .Select(profile => GetString(profile, "fixtureId"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredId in RequiredGoodProfileIds)
        {
            if (!goodIds.Contains(requiredId))
            {
                errors.Add($"FEAT160_REQUIRED_GOOD_PROFILE_MISSING: replayMatrix.goodProfiles missing {requiredId}.");
            }
        }

        foreach (var profile in goodProfiles)
        {
            RequireNonEmpty(profile, "fixtureId", errors);
            RequireNonEmpty(profile, "profileIntent", errors);
            RequireNonEmpty(profile, "packagePath", errors);
            RequireSha256(profile, "packageHash", errors);
            RequireNonEmpty(profile, "expectedResultRef", errors);
            RequireValue(profile, "expectedOverallStatus", "pass", errors);
            RequireValue(profile, "expectedExitCode", 0, errors);
            RequireValue(profile, "expectedPrimaryResultCode", "package_structure_valid", errors);
            RequireSha256(profile, "normalizedOutputHash", errors);
            RequireValue(profile, "requiredForScore", true, errors);
        }

        if (RequireArray(matrix, "requiredArtifactPaths").Count < 10)
        {
            errors.Add("FEAT160_REQUIRED_ARTIFACT_MATRIX_TOO_SMALL: replayMatrix.requiredArtifactPaths must have at least 10 entries.");
        }
    }

    private static void ValidateNegativeMatrix(JsonObject source, List<string> errors)
    {
        var cases = RequireArray(source, "negativeMatrix").OfType<JsonObject>().ToArray();
        if (cases.Length == 0)
        {
            errors.Add("FEAT160_NEGATIVE_MATRIX_EMPTY: negativeMatrix must not be empty.");
            return;
        }

        var fixtureIds = cases
            .Select(item => GetString(item, "fixtureId"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredId in RequiredNegativeFixtureIds)
        {
            if (!fixtureIds.Contains(requiredId))
            {
                errors.Add($"FEAT160_NEGATIVE_CASE_MISSING: negativeMatrix missing {requiredId}.");
            }
        }

        foreach (var item in cases)
        {
            RequireNonEmpty(item, "caseId", errors);
            RequireNonEmpty(item, "fixtureId", errors);
            RequireNonEmpty(item, "coverageArea", errors);
            RequireNonEmpty(item, "changedArtifactOrCondition", errors);
            RequireNonEmpty(item, "expectedPrimaryResultCode", errors);
            ValidateNegativeExpectedOutcome(item, errors);
            RequireValue(item, "blocksScoreMovement", true, errors);
        }
    }

    private static void ValidateNegativeExpectedOutcome(JsonObject item, List<string> errors)
    {
        var fixtureId = GetString(item, "fixtureId");
        var expectedStatus = GetString(item, "expectedOverallStatus");
        var expectedExitCode = GetInt(item, "expectedExitCode", int.MinValue);
        if (string.Equals(expectedStatus, "fail", StringComparison.Ordinal) && expectedExitCode == 1)
        {
            return;
        }

        if (string.Equals(fixtureId, "tamper-malformed-package-json", StringComparison.Ordinal) &&
            string.Equals(expectedStatus, "notAvailable", StringComparison.Ordinal) &&
            expectedExitCode == 2)
        {
            return;
        }

        errors.Add($"FEAT160_NEGATIVE_EXPECTED_OUTCOME_INVALID: {fixtureId} must expect fail/1, except tamper-malformed-package-json which must expect notAvailable/2.");
    }

    private static void ValidatePackageLayout(JsonObject source, List<string> errors)
    {
        var layout = RequireObject(source, "packageLayout");
        RequireValue(layout, "targetPackagePath", ExpectedTargetPackagePath, errors);
        RequireValue(layout, "immutableVersion", TargetPackageVersion, errors);
        var files = RequireArray(layout, "files").OfType<JsonObject>().ToArray();
        var paths = files
            .Select(file => GetString(file, "path"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredPath in PublicationCountingReplayArtifactGenerator.RequiredArtifactPaths)
        {
            if (!paths.Contains(requiredPath))
            {
                errors.Add($"FEAT160_PACKAGE_LAYOUT_MISSING_FILE: packageLayout.files missing {requiredPath}.");
            }
        }

        foreach (var file in files)
        {
            RequireNonEmpty(file, "path", errors);
            RequireNonEmpty(file, "purpose", errors);
            RequireValue(file, "publicSafe", true, errors);
            RequireValue(file, "requiredForManifest", true, errors);
        }
    }

    private static void ValidatePublicSafety(JsonObject source, List<string> errors)
    {
        var safety = RequireObject(source, "publicSafety");
        RequireValue(safety, "visibility", "public_safe", errors);
        RequireValue(safety, "expectedUnexpectedFindingCount", 0, errors);
        RequireNonEmpty(safety, "publicBoundaryStatement", errors);

        var categories = RequireArray(safety, "forbiddenMaterialCategories")
            .Select(item => item?.GetValue<string>() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var required in ForbiddenMaterialCategories)
        {
            if (!categories.Contains(required))
            {
                errors.Add($"FEAT160_PUBLIC_SAFETY_CATEGORY_MISSING: publicSafety.forbiddenMaterialCategories missing {required}.");
            }
        }

        var claims = RequireArray(safety, "forbiddenClaimCategories")
            .Select(item => item?.GetValue<string>() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var required in ForbiddenClaimCategories)
        {
            if (!claims.Contains(required))
            {
                errors.Add($"FEAT160_PUBLIC_SAFETY_CLAIM_MISSING: publicSafety.forbiddenClaimCategories missing {required}.");
            }
        }
    }

    private static void ValidateReadinessOutput(JsonObject source, List<string> errors)
    {
        var output = RequireObject(source, "readinessOutput");
        RequireValue(output, "readinessFragmentPath", PublicationCountingReplayArtifactGenerator.ReadinessFragmentPath, errors);
        RequireValue(output, "scoreProposalPath", PublicationCountingReplayArtifactGenerator.ScoreProposalPath, errors);
        RequireValue(output, "dimensionId", TargetDimensionId, errors, "FEAT160_SCORE_DIMENSION_INVALID");
        RequireValue(output, "proposedScoreFrom", 8, errors, "FEAT160_SCORE_DIMENSION_INVALID");
        RequireValue(output, "proposedScoreTo", 10, errors, "FEAT160_SCORE_DIMENSION_INVALID");
        RequireValue(output, "doesNotMutateRegister", true, errors, "FEAT160_DIRECT_REGISTER_MUTATION_FORBIDDEN");
        RequireValue(output, "targetBlockerId", TargetBlockerId, errors, "FEAT160_BLOCKER_OWNERSHIP_INVALID");
    }

    private static void ValidateDownstreamConsumers(JsonObject source, List<string> errors)
    {
        var consumers = RequireArray(source, "downstreamConsumers").OfType<JsonObject>().ToArray();
        if (consumers.Length == 0)
        {
            errors.Add("FEAT160_DOWNSTREAM_CONSUMER_MISSING: downstreamConsumers must not be empty.");
        }

        foreach (var consumer in consumers)
        {
            RequireNonEmpty(consumer, "featureId", errors);
            RequireNonEmpty(consumer, "allowedUse", errors);
            RequireNonEmpty(consumer, "forbiddenClaim", errors);
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
                    errors.Add($"FEAT160_LOCAL_ABSOLUTE_PATH_FORBIDDEN: {propertyName} must be workspace-relative and use forward slashes: {path}");
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
                        errors.Add($"FEAT160_PRIVATE_MATERIAL_FORBIDDEN: {propertyName ?? "value"} contains forbidden private material marker '{needle}'.");
                    }
                }

                break;
        }
    }

    private static void ValidateFeat153Refs(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject feat153,
        List<string> errors)
    {
        var packageRoot = ResolveWorkspaceRelativePath(paths.WorkspaceRoot, GetString(feat153, "packagePath"));
        if (!Directory.Exists(packageRoot))
        {
            errors.Add($"upstreamBaselines.feat153.packagePath does not exist: {GetString(feat153, "packagePath")}");
            return;
        }

        var manifestPath = Path.Combine(packageRoot, "publication-counting-hardening-manifest.json");
        var scoreProposalPath = Path.Combine(packageRoot, "readiness", "publication-counting-score-proposal.json");
        var readinessFragmentPath = Path.Combine(packageRoot, "readiness", "publication-counting-readiness-fragment.json");
        var handoffPath = Path.Combine(packageRoot, "handoff", "publication-counting-hardening-downstream-handoff.json");
        RequireFileHash(manifestPath, GetString(feat153, "manifestHash"), "upstreamBaselines.feat153.manifestHash", errors);
        RequireFileHash(scoreProposalPath, GetString(feat153, "scoreProposalHash"), "upstreamBaselines.feat153.scoreProposalHash", errors);
        RequireFileHash(readinessFragmentPath, GetString(feat153, "readinessFragmentHash"), "upstreamBaselines.feat153.readinessFragmentHash", errors);
        RequireFileHash(handoffPath, GetString(feat153, "handoffHash"), "upstreamBaselines.feat153.handoffHash", errors);

        if (File.Exists(manifestPath))
        {
            var manifest = ReadJsonObject(manifestPath, "FEAT-153 manifest");
            RequireValue(manifest, "producerFeature", "FEAT-153", errors);
            CompareValue(manifest, "packageVersion", feat153, "packageVersion", "upstreamBaselines.feat153.packageVersion", errors);
            var verifierRefs = RequireObject(manifest, "verifierRefs");
            CompareValue(verifierRefs, "sourceRef", feat153, "verifierSourceRef", "upstreamBaselines.feat153.verifierSourceRef", errors);
            CompareValue(verifierRefs, "binaryRelease", feat153, "verifierBinaryRelease", "upstreamBaselines.feat153.verifierBinaryRelease", errors);
        }
    }

    private static void ValidateFeat158Refs(
        PublicationCountingReplayPromotionPaths paths,
        JsonObject feat158,
        JsonObject source,
        List<string> errors)
    {
        var corpusRoot = ResolveWorkspaceRelativePath(paths.WorkspaceRoot, GetString(feat158, "corpusPath"));
        if (!Directory.Exists(corpusRoot))
        {
            errors.Add($"upstreamBaselines.feat158.corpusPath does not exist: {GetString(feat158, "corpusPath")}");
            return;
        }

        var manifestPath = Path.Combine(corpusRoot, "corpus-manifest.json");
        var fixtureIndexPath = Path.Combine(corpusRoot, "fixtures", "fixture-index.json");
        var cleanMachinePath = Path.Combine(corpusRoot, "validation", "clean-machine-validation-summary.json");
        var resultCodePath = Path.Combine(corpusRoot, "validation", "result-code-stability-summary.json");
        var noSecretPath = Path.Combine(corpusRoot, "validation", "no-secret-scan-result.json");
        RequireFileHash(manifestPath, GetString(feat158, "manifestHash"), "upstreamBaselines.feat158.manifestHash", errors);
        RequireFileHash(fixtureIndexPath, GetString(feat158, "fixtureIndexHash"), "upstreamBaselines.feat158.fixtureIndexHash", errors);
        RequireFileHash(cleanMachinePath, GetString(feat158, "cleanMachineValidationHash"), "upstreamBaselines.feat158.cleanMachineValidationHash", errors);
        RequireFileHash(resultCodePath, GetString(feat158, "resultCodeStabilityHash"), "upstreamBaselines.feat158.resultCodeStabilityHash", errors);
        RequireFileHash(noSecretPath, GetString(feat158, "noSecretScanHash"), "upstreamBaselines.feat158.noSecretScanHash", errors);

        if (File.Exists(manifestPath))
        {
            var manifest = ReadJsonObject(manifestPath, "FEAT-158 corpus manifest");
            CompareValue(manifest, "corpusVersion", feat158, "corpusVersion", "upstreamBaselines.feat158.corpusVersion", errors);
            CompareValue(manifest, "status", feat158, "status", "upstreamBaselines.feat158.status", errors);
            CompareValue(manifest, "visibility", feat158, "visibility", "upstreamBaselines.feat158.visibility", errors);
            var protocol = RequireObject(manifest, "protocolPackage");
            CompareValue(protocol, "packageVersion", feat158, "protocolPackageVersion", "upstreamBaselines.feat158.protocolPackageVersion", errors);
            var verifier = RequireObject(manifest, "verifier");
            CompareValue(verifier, "sourceRef", feat158, "verifierSourceRef", "upstreamBaselines.feat158.verifierSourceRef", errors);
            CompareValue(verifier, "binaryRelease", feat158, "verifierBinaryRelease", "upstreamBaselines.feat158.verifierBinaryRelease", errors);
        }

        if (File.Exists(fixtureIndexPath))
        {
            ValidateReplayMatrixAgainstFixtureIndex(paths.WorkspaceRoot, corpusRoot, source, ReadJsonObject(fixtureIndexPath, "FEAT-158 fixture index"), errors);
        }
    }

    private static void ValidateReplayMatrixAgainstFixtureIndex(
        string workspaceRoot,
        string corpusRoot,
        JsonObject source,
        JsonObject fixtureIndex,
        List<string> errors)
    {
        var fixtureMap = RequireArray(fixtureIndex, "fixtures")
            .OfType<JsonObject>()
            .ToDictionary(item => GetString(item, "fixtureId"), StringComparer.Ordinal);
        var replayMatrix = RequireObject(source, "replayMatrix");
        foreach (var profile in RequireArray(replayMatrix, "goodProfiles").OfType<JsonObject>())
        {
            var fixtureId = GetString(profile, "fixtureId");
            if (!fixtureMap.TryGetValue(fixtureId, out var indexEntry))
            {
                errors.Add($"FEAT160_FEAT158_GOOD_PROFILE_MISSING: fixture index missing {fixtureId}.");
                continue;
            }

            CompareFixtureEntry("goodProfiles", profile, indexEntry, errors);
            var packagePath = ResolveWorkspaceRelativePath(workspaceRoot, GetString(profile, "packagePath"));
            if (!Directory.Exists(packagePath))
            {
                errors.Add($"replayMatrix.goodProfiles packagePath does not exist: {GetString(profile, "packagePath")}");
            }

            ValidateExpectedResult(
                Path.Combine(corpusRoot, GetString(indexEntry, "expectedResultRef").Replace('/', Path.DirectorySeparatorChar)),
                profile,
                $"replayMatrix.goodProfiles[{fixtureId}]",
                requireNormalizedHash: true,
                errors);
        }

        foreach (var negative in RequireArray(source, "negativeMatrix").OfType<JsonObject>())
        {
            var fixtureId = GetString(negative, "fixtureId");
            if (GetString(negative, "source") == "feat160_required")
            {
                continue;
            }

            if (!fixtureMap.TryGetValue(fixtureId, out var indexEntry))
            {
                errors.Add($"FEAT160_FEAT158_NEGATIVE_CASE_MISSING: fixture index missing {fixtureId}.");
                continue;
            }

            CompareValue(indexEntry, "expectedPrimaryResultCode", negative, "expectedPrimaryResultCode", $"negativeMatrix[{fixtureId}].expectedPrimaryResultCode", errors);
            CompareValue(indexEntry, "expectedOverallStatus", negative, "expectedOverallStatus", $"negativeMatrix[{fixtureId}].expectedOverallStatus", errors);
            CompareValue(indexEntry, "expectedExitCode", negative, "expectedExitCode", $"negativeMatrix[{fixtureId}].expectedExitCode", errors);
            ValidateExpectedResult(
                Path.Combine(corpusRoot, GetString(indexEntry, "expectedResultRef").Replace('/', Path.DirectorySeparatorChar)),
                negative,
                $"negativeMatrix[{fixtureId}]",
                requireNormalizedHash: false,
                errors);
        }
    }

    private static void CompareFixtureEntry(
        string sourceCollection,
        JsonObject sourceFixture,
        JsonObject indexEntry,
        List<string> errors)
    {
        var expectedWorkspacePath = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.3.0/" + GetString(indexEntry, "packagePath");
        if (!string.Equals(GetString(sourceFixture, "packagePath"), expectedWorkspacePath, StringComparison.Ordinal))
        {
            errors.Add($"{sourceCollection}.{GetString(sourceFixture, "fixtureId")}.packagePath mismatch: expected {expectedWorkspacePath}, observed {GetString(sourceFixture, "packagePath")}");
        }

        CompareValue(indexEntry, "packageHash", sourceFixture, "packageHash", $"{sourceCollection}.{GetString(sourceFixture, "fixtureId")}.packageHash", errors);
        var expectedResultRef = "HushVoting-Verifier-Corpus/hushvoting-v1/v0.3.0/" + GetString(indexEntry, "expectedResultRef");
        if (!string.Equals(GetString(sourceFixture, "expectedResultRef"), expectedResultRef, StringComparison.Ordinal))
        {
            errors.Add($"{sourceCollection}.{GetString(sourceFixture, "fixtureId")}.expectedResultRef mismatch: expected {expectedResultRef}, observed {GetString(sourceFixture, "expectedResultRef")}");
        }

        CompareValue(indexEntry, "expectedPrimaryResultCode", sourceFixture, "expectedPrimaryResultCode", $"{sourceCollection}.{GetString(sourceFixture, "fixtureId")}.expectedPrimaryResultCode", errors);
        CompareValue(indexEntry, "expectedOverallStatus", sourceFixture, "expectedOverallStatus", $"{sourceCollection}.{GetString(sourceFixture, "fixtureId")}.expectedOverallStatus", errors);
        CompareValue(indexEntry, "expectedExitCode", sourceFixture, "expectedExitCode", $"{sourceCollection}.{GetString(sourceFixture, "fixtureId")}.expectedExitCode", errors);
    }

    private static void ValidateExpectedResult(
        string path,
        JsonObject sourceFixture,
        string label,
        bool requireNormalizedHash,
        List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"{label}.expectedResultRef target file does not exist: {path}");
            return;
        }

        var expected = ReadJsonObject(path, label);
        CompareValue(expected, "expectedOverallStatus", sourceFixture, "expectedOverallStatus", $"{label}.expectedOverallStatus", errors);
        CompareValue(expected, "expectedExitCode", sourceFixture, "expectedExitCode", $"{label}.expectedExitCode", errors);
        if (requireNormalizedHash)
        {
            CompareValue(expected, "normalizedOutputHash", sourceFixture, "normalizedOutputHash", $"{label}.normalizedOutputHash", errors);
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
        if (!string.Equals(observed, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{label} mismatch: expected {expectedHash}, observed {observed}");
        }
    }

    private static void CompareValue(
        JsonObject left,
        string leftProperty,
        JsonObject right,
        string rightProperty,
        string label,
        List<string> errors)
    {
        if (left.TryGetPropertyValue(leftProperty, out var leftNode) &&
            right.TryGetPropertyValue(rightProperty, out var rightNode) &&
            leftNode is not null &&
            rightNode is not null &&
            leftNode.GetValueKind() == JsonValueKind.Number &&
            rightNode.GetValueKind() == JsonValueKind.Number)
        {
            var observedInt = leftNode.GetValue<int>();
            var expectedInt = rightNode.GetValue<int>();
            if (observedInt != expectedInt)
            {
                errors.Add($"{label} mismatch: expected {expectedInt}, observed {observedInt}");
            }

            return;
        }

        var observed = GetString(left, leftProperty);
        var expected = GetString(right, rightProperty);
        if (!string.Equals(observed, expected, StringComparison.Ordinal))
        {
            errors.Add($"{label} mismatch: expected {expected}, observed {observed}");
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
        if (!observed.StartsWith("sha256:", StringComparison.Ordinal) || observed.Length != 71)
        {
            errors.Add($"{property} must be a sha256:<64 hex> value.");
        }
    }

    private static string Prefix(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code + ": ";
}
