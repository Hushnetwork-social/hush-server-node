using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PublicationCountingHardeningPromoter;

public static class PublicationCountingHardeningContracts
{
    public const string FeatureId = "FEAT-153";
    public const string SourceSchemaVersion = "publication-counting-hardening-source.v1";
    public const string CurrentRegisterId = "RDY-REG-v0.1.5";
    public const string TargetDimensionId = "RDY-DIM-004";
    public const string TargetPackageVersion = "v0.1.0";
    public const string ExpectedTargetPackagePath = "HushVoting-Verifier-Corpus/hushvoting-v1/publication-counting-hardening/v0.1.0/";
    public const string CanonicalizationVersion = "publication-counting-hardening-canonical-json.v1";

    public static readonly string[] RequiredSchemaFiles =
    [
        PublicationCountingHardeningPromotionPaths.SchemaFileName,
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
    ];

    private static readonly HashSet<string> RelativePathProperties = new(StringComparer.Ordinal)
    {
        "publicPath",
        "projectPath",
        "packagePath",
        "expectedResultRef",
        "path",
        "changedArtifact",
        "targetPackagePath",
    };

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static JsonObject LoadSource(
        PublicationCountingHardeningPromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "publication/counting source");
        if (!File.Exists(sourcePath))
        {
            throw new PublicationCountingHardeningPromotionException(
                "FEAT-153 publication/counting source input is missing.",
                [$"Source input was not found: {sourcePath}"]);
        }

        return ReadJsonObject(sourcePath, PublicationCountingHardeningPromotionPaths.SourceFileName);
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
        var errors = ValidateJsonRequired(source, PublicationCountingHardeningPromotionPaths.SourceFileName, [
            "schemaVersion",
            "sourceId",
            "generatedAt",
            "producerFeature",
            "baselineRegister",
            "sourceRelease",
            "protocolRefs",
            "verifierRefs",
            "packageRefs",
            "acceptedToPublishedChecks",
            "tallyReplayChecks",
            "tamperAndStaleMatrix",
            "publicSafety",
            "packageLayout",
            "readinessProposal",
            "downstreamConsumers",
            "residualRisks",
        ]).ToList();

        RequireValue(source, "schemaVersion", SourceSchemaVersion, errors);
        RequireValue(source, "producerFeature", FeatureId, errors);
        ValidateBaselineRegister(source, errors);
        ValidateSourceRelease(source, errors);
        ValidateVerifierRefs(source, errors);
        ValidatePackageRefs(source, errors);
        ValidateBindingChecks(source, "acceptedToPublishedChecks", errors);
        ValidateBindingChecks(source, "tallyReplayChecks", errors);
        ValidateTamperMatrix(source, errors);
        ValidatePublicSafety(source, errors);
        ValidatePackageLayout(source, errors);
        ValidateReadinessProposal(source, errors);
        ValidateRelativePaths(source, errors);

        return errors;
    }

    public static JsonObject ValidateForPromotion(
        PublicationCountingHardeningPromotionPaths paths,
        string? sourceInput = null)
    {
        var schemaErrors = ValidateSchemaSet(paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new PublicationCountingHardeningPromotionException(
                "FEAT-153 publication/counting schema validation failed.",
                schemaErrors);
        }

        var source = LoadSource(paths, sourceInput);
        var sourceErrors = ValidateSource(source).ToList();
        if (sourceErrors.Count > 0)
        {
            throw new PublicationCountingHardeningPromotionException(
                "FEAT-153 publication/counting source validation failed.",
                sourceErrors);
        }

        sourceErrors.AddRange(ValidateCurrentRefs(paths, source));
        if (sourceErrors.Count > 0)
        {
            throw new PublicationCountingHardeningPromotionException(
                "FEAT-153 publication/counting source validation failed.",
                sourceErrors);
        }

        return source;
    }

    public static IReadOnlyList<string> ValidateCurrentRefs(
        PublicationCountingHardeningPromotionPaths paths,
        JsonObject source)
    {
        var errors = new List<string>();
        var sourceRelease = RequireObject(source, "sourceRelease");
        var verifierRefs = RequireObject(source, "verifierRefs");
        var protocolRefs = RequireObject(source, "protocolRefs");
        var packageRefs = RequireObject(source, "packageRefs");

        var publicPath = GetString(sourceRelease, "publicPath");
        var releaseRoot = ResolveWorkspaceRelativePath(paths.WorkspaceRoot, publicPath);
        var manifestPath = Path.Combine(releaseRoot, "corpus-manifest.json");
        var fixtureIndexPath = Path.Combine(releaseRoot, "fixtures", "fixture-index.json");
        var packagePath = ResolveWorkspaceRelativePath(paths.WorkspaceRoot, GetString(packageRefs, "packagePath"));
        var expectedResultPath = ResolveWorkspaceRelativePath(paths.WorkspaceRoot, GetString(packageRefs, "expectedResultRef"));

        RequireFileHash(manifestPath, GetString(sourceRelease, "manifestHash"), "sourceRelease.manifestHash", errors);
        RequireFileHash(fixtureIndexPath, GetString(sourceRelease, "fixtureIndexHash"), "sourceRelease.fixtureIndexHash", errors);
        RequireFileHash(expectedResultPath, GetString(packageRefs, "expectedResultHash"), "packageRefs.expectedResultHash", errors);

        if (!Directory.Exists(packagePath))
        {
            errors.Add($"packageRefs.packagePath does not exist: {GetString(packageRefs, "packagePath")}");
        }

        if (File.Exists(manifestPath))
        {
            var manifest = ReadJsonObject(manifestPath, "corpus-manifest.json");
            var manifestVerifier = RequireObject(manifest, "verifier");
            var manifestProtocol = RequireObject(manifest, "protocolPackage");
            CompareValue(manifestVerifier, "sourceRef", verifierRefs, "sourceRef", "verifierRefs.sourceRef", errors);
            CompareValue(manifestVerifier, "binaryRelease", verifierRefs, "binaryRelease", "verifierRefs.binaryRelease", errors);
            CompareValue(manifestProtocol, "packageVersion", protocolRefs, "packageVersion", "protocolRefs.packageVersion", errors);
        }

        if (!string.Equals(
                GetString(sourceRelease, "goodPackageHash"),
                GetString(packageRefs, "packageHash"),
                StringComparison.Ordinal))
        {
            errors.Add("sourceRelease.goodPackageHash must match packageRefs.packageHash.");
        }

        return errors;
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new PublicationCountingHardeningPromotionException($"{label} is not a JSON object.");
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

        throw new PublicationCountingHardeningPromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new PublicationCountingHardeningPromotionException($"Missing array property: {property}");
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
            throw new PublicationCountingHardeningPromotionException(
                "Publication/counting path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    public static string ResolveSourceInput(
        PublicationCountingHardeningPromotionPaths paths,
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
            ? Path.Combine(fullPath, PublicationCountingHardeningPromotionPaths.SourceFileName)
            : fullPath;
    }

    public static string CanonicalJson(JsonNode node) =>
        NormalizeLineEndings(node.ToJsonString(CanonicalJsonOptions)) + "\n";

    public static string Sha256Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(NormalizeLineEndings(content))))
            .ToLowerInvariant();

    public static string Sha256File(string path) =>
        "sha256:" + Sha256Hex(File.ReadAllText(path));

    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    public static string ResolveWorkspaceRelativePath(string workspaceRoot, string relativePath)
    {
        var combined = Path.Combine(workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var fullPath = Path.GetFullPath(combined);
        EnsurePathUnder(workspaceRoot, fullPath, relativePath);
        return fullPath;
    }

    private static void ValidateBaselineRegister(JsonObject source, List<string> errors)
    {
        var baseline = RequireObject(source, "baselineRegister");
        RequireValue(baseline, "registerVersionId", CurrentRegisterId, errors);
        RequireValue(baseline, "dimensionId", TargetDimensionId, errors);
        RequireValue(baseline, "currentScore", 7, errors);
        RequireValue(baseline, "targetScoreBeforeReviewPilot", 8, errors);
    }

    private static void ValidateSourceRelease(JsonObject source, List<string> errors)
    {
        var release = RequireObject(source, "sourceRelease");
        RequireValue(release, "corpusFamily", "hushvoting-v1", errors);
        RequireValue(release, "corpusVersion", "v0.2.0", errors);
        RequireValue(release, "status", "accepted", errors);
        RequireValue(release, "noSecretScanStatus", "pass", errors);
        RequireSha256(release, "manifestHash", errors);
        RequireSha256(release, "fixtureIndexHash", errors);
        RequireSha256(release, "goodPackageHash", errors);
    }

    private static void ValidateVerifierRefs(JsonObject source, List<string> errors)
    {
        var refs = RequireObject(source, "verifierRefs");
        RequireNonEmpty(refs, "sourceRef", errors);
        RequireNonEmpty(refs, "projectPath", errors);
        RequireValue(refs, "runtime", ".NET 9", errors);
        RequireValue(refs, "profileId", "public_anonymous_v1", errors);
        RequireSha256(refs, "binaryRelease", errors);
    }

    private static void ValidatePackageRefs(JsonObject source, List<string> errors)
    {
        var refs = RequireObject(source, "packageRefs");
        RequireNonEmpty(refs, "fixtureId", errors);
        RequireNonEmpty(refs, "packagePath", errors);
        RequireSha256(refs, "packageHash", errors);
        RequireNonEmpty(refs, "expectedResultRef", errors);
        RequireSha256(refs, "expectedResultHash", errors);
        RequireValue(refs, "expectedOverallStatus", "pass", errors);
        RequireValue(refs, "expectedExitCode", 0, errors);
        if (RequireArray(refs, "requiredArtifacts").Count == 0)
        {
            errors.Add("packageRefs.requiredArtifacts must not be empty.");
        }
    }

    private static void ValidateBindingChecks(JsonObject source, string property, List<string> errors)
    {
        var checks = RequireArray(source, property);
        if (checks.Count == 0)
        {
            errors.Add($"{property} must not be empty.");
            return;
        }

        foreach (var item in checks.OfType<JsonObject>())
        {
            RequireNonEmpty(item, "checkId", errors);
            RequireValue(item, "expectedValidOverallStatus", "pass", errors);
            RequireValue(item, "blocksScoreMovementWhenFailing", true, errors);
            if (RequireArray(item, "validFixtureIds").Count == 0)
            {
                errors.Add($"{property}.validFixtureIds must not be empty.");
            }

            if (RequireArray(item, "tamperFixtureIds").Count == 0)
            {
                errors.Add($"{property}.tamperFixtureIds must not be empty.");
            }
        }
    }

    private static void ValidateTamperMatrix(JsonObject source, List<string> errors)
    {
        var cases = RequireArray(source, "tamperAndStaleMatrix");
        if (cases.Count == 0)
        {
            errors.Add("tamperAndStaleMatrix must not be empty.");
        }

        foreach (var item in cases.OfType<JsonObject>())
        {
            RequireNonEmpty(item, "caseId", errors);
            RequireNonEmpty(item, "fixtureId", errors);
            RequireNonEmpty(item, "expectedPrimaryResultCode", errors);
            RequireValue(item, "blocksScoreMovement", true, errors);
        }
    }

    private static void ValidatePublicSafety(JsonObject source, List<string> errors)
    {
        var safety = RequireObject(source, "publicSafety");
        RequireValue(safety, "visibility", "public_safe", errors);
        RequireValue(safety, "expectedFindingCountInGeneratedPackage", 0, errors);
        var categories = RequireArray(safety, "forbiddenMaterialCategories")
            .Select(item => item?.GetValue<string>() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var required in ForbiddenMaterialCategories)
        {
            if (!categories.Contains(required))
            {
                errors.Add($"publicSafety.forbiddenMaterialCategories is missing {required}.");
            }
        }
    }

    private static void ValidatePackageLayout(JsonObject source, List<string> errors)
    {
        var layout = RequireObject(source, "packageLayout");
        RequireValue(layout, "targetPackagePath", ExpectedTargetPackagePath, errors);
        RequireValue(layout, "immutableVersion", TargetPackageVersion, errors);
        if (RequireArray(layout, "files").Count == 0)
        {
            errors.Add("packageLayout.files must not be empty.");
        }
    }

    private static void ValidateReadinessProposal(JsonObject source, List<string> errors)
    {
        var proposal = RequireObject(source, "readinessProposal");
        RequireValue(proposal, "dimensionId", TargetDimensionId, errors);
        RequireValue(proposal, "proposedScoreFrom", 7, errors);
        RequireValue(proposal, "proposedScoreTo", 8, errors);
        RequireValue(proposal, "doesNotMutateRegister", true, errors);
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
                    errors.Add($"{propertyName} must be workspace-relative and use forward slashes: {path}");
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
        if (!string.Equals(observed, expectedHash, StringComparison.Ordinal))
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
        var observed = GetString(left, leftProperty);
        var expected = GetString(right, rightProperty);
        if (!string.Equals(observed, expected, StringComparison.Ordinal))
        {
            errors.Add($"{label} mismatch: expected {expected}, observed {observed}");
        }
    }

    private static void RequireValue(JsonObject value, string property, string expected, List<string> errors)
    {
        var observed = GetString(value, property);
        if (!string.Equals(observed, expected, StringComparison.Ordinal))
        {
            errors.Add($"{property} must be {expected}; observed {observed}.");
        }
    }

    private static void RequireValue(JsonObject value, string property, int expected, List<string> errors)
    {
        var observed = GetInt(value, property, int.MinValue);
        if (observed != expected)
        {
            errors.Add($"{property} must be {expected}; observed {observed}.");
        }
    }

    private static void RequireValue(JsonObject value, string property, bool expected, List<string> errors)
    {
        var observed = GetBool(value, property, !expected);
        if (observed != expected)
        {
            errors.Add($"{property} must be {expected.ToString().ToLowerInvariant()}.");
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
}
