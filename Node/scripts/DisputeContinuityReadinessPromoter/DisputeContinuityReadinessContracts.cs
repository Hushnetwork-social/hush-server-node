using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DisputeContinuityReadinessPromoter;

public sealed record DisputeContinuityReadinessPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string OutputRoot)
{
    public const string SourceFolder = "Dispute-Continuity-Readiness";
    public const string SourceFileName = "dispute-continuity-readiness-source.json";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);

    public static DisputeContinuityReadinessPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            SourceFolder);
        return new DisputeContinuityReadinessPromotionPaths(
            fullRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            Path.Combine(sourceRoot, "examples"),
            Path.Combine(
                fullRoot,
                "hush-documents",
                "PrivateServer_ElectronicVoting",
                SourceFolder));
    }
}

public sealed record DisputeContinuityMaterialFinding(
    string RelativePath,
    string Category,
    string Evidence);

public sealed record DisputeContinuityGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash);

public sealed record DisputeContinuityGeneratedPackage(
    string Status,
    IReadOnlyList<DisputeContinuityGeneratedArtifact> Artifacts,
    IReadOnlyList<DisputeContinuityMaterialFinding> PublicForbiddenFindings,
    IReadOnlyList<string> Blockers);

public sealed class DisputeContinuityReadinessPromotionException : Exception
{
    public DisputeContinuityReadinessPromotionException(string message, IEnumerable<string>? details = null)
        : base(message)
    {
        Details = details?.ToArray() ?? [];
    }

    public IReadOnlyList<string> Details { get; }
}

public static class DisputeContinuityReadinessContracts
{
    public const string FeatureId = "FEAT-139";
    public const string AcceptanceGate = "AT-RDY-011";
    public const string ReadinessFragmentId = "RDY-EVID-AT-RDY-011-FEAT-139-001";
    public const string DimensionId = "RDY-DIM-009";
    public const string CanonicalizationVersion = "feat139-dispute-continuity-canonical-json-v1";

    public static readonly string[] RequiredSchemaFiles =
    [
        "dispute-continuity-readiness-source.schema.json",
        "dispute-continuity-readiness-fragment.schema.json",
        "dispute-continuity-evidence-index.schema.json",
        "dispute-continuity-claim-decision-matrix.schema.json",
        "dispute-continuity-downstream-handoff.schema.json",
        "dispute-continuity-package-hash-validation.schema.json",
    ];

    public static readonly string[] RequiredOutputFiles =
    [
        DisputeContinuityReadinessArtifactGenerator.ReadinessFragmentPath,
        DisputeContinuityReadinessArtifactGenerator.EvidenceIndexPath,
        DisputeContinuityReadinessArtifactGenerator.ClaimDecisionMatrixPath,
        DisputeContinuityReadinessArtifactGenerator.PublicSafeSummaryPath,
        DisputeContinuityReadinessArtifactGenerator.DownstreamHandoffPath,
        DisputeContinuityReadinessArtifactGenerator.PackageHashValidationPath,
    ];

    public static readonly string[] RequiredScenarioIds =
    [
        "clean_finalized_zero_anomalies",
        "resolved_non_blocking_anomalies",
        "unresolved_blocking_anomaly",
        "retention_hold_policy_review",
        "voided_election",
        "failed_to_finalize",
        "finalized_with_anomaly",
        "package_artifact_mismatch",
    ];

    private static readonly HashSet<string> AllowedEvidenceStates = new(StringComparer.Ordinal)
    {
        "not_required_zero_anomalies",
        "present",
        "accepted",
        "accepted_with_limitations",
        "missing_required",
        "blocked",
        "stale_or_superseded",
        "not_in_scope",
    };

    private static readonly HashSet<string> AllowedDecisions = new(StringComparer.Ordinal)
    {
        "allow",
        "allow_with_limitations",
        "downgrade",
        "block",
    };

    public static JsonObject LoadSource(
        DisputeContinuityReadinessPromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "dispute continuity readiness source");
        return ReadJsonObject(sourcePath, DisputeContinuityReadinessPromotionPaths.SourceFileName);
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
        var errors = ValidateJsonRequired(source, DisputeContinuityReadinessPromotionPaths.SourceFileName, [
            "schemaVersion",
            "sourceId",
            "featureId",
            "acceptanceGate",
            "sourceGap",
            "status",
            "generatedAt",
            "currentReadinessRegister",
            "evidenceRefs",
            "anomalyEvidence",
            "voidEvidence",
            "governedOutcomeEvidence",
            "scenarioDecisions",
            "publicSummary",
            "publicArtifactSamples",
        ]).ToList();

        RequireValue(source, "featureId", FeatureId, errors);
        RequireValue(source, "acceptanceGate", AcceptanceGate, errors);
        RequireNonEmptyArray(source, "evidenceRefs", errors);
        RequireNonEmptyArray(source, "scenarioDecisions", errors);

        if (source.TryGetPropertyValue("currentReadinessRegister", out var registerNode) &&
            registerNode is JsonObject register)
        {
            RequireValue(register, "dimensionId", DimensionId, errors);
            RequireValue(register, "evidenceId", ReadinessFragmentId, errors);
        }

        var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in RequireArray(source, "scenarioDecisions").OfType<JsonObject>())
        {
            var scenarioId = GetString(scenario, "scenarioId");
            if (!string.IsNullOrWhiteSpace(scenarioId))
            {
                scenarioIds.Add(scenarioId);
            }

            var decision = GetString(scenario, "decision");
            if (!AllowedDecisions.Contains(decision))
            {
                errors.Add($"Scenario {scenarioId} has unsupported decision {decision}.");
            }

            foreach (var state in GetStringArray(scenario, "evidenceStates"))
            {
                if (!AllowedEvidenceStates.Contains(state))
                {
                    errors.Add($"Scenario {scenarioId} has unsupported evidence state {state}.");
                }
            }
        }

        foreach (var requiredScenarioId in RequiredScenarioIds)
        {
            if (!scenarioIds.Contains(requiredScenarioId))
            {
                errors.Add($"Missing required scenario row: {requiredScenarioId}.");
            }
        }

        return errors;
    }

    public static IReadOnlyList<DisputeContinuityMaterialFinding> ScanForbiddenPublicMaterial(
        JsonObject source,
        IEnumerable<(string Path, string Content)> generatedPublicArtifacts)
    {
        var findings = new List<DisputeContinuityMaterialFinding>();
        foreach (var sample in RequireArray(source, "publicArtifactSamples").OfType<JsonObject>())
        {
            AddForbiddenFindings(GetString(sample, "content"), GetString(sample, "path"), findings);
        }

        foreach (var artifact in generatedPublicArtifacts)
        {
            AddForbiddenFindings(artifact.Content, artifact.Path, findings);
        }

        return findings;
    }

    public static string CanonicalJson(JsonNode node)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        return node.ToJsonString(options).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    public static string Sha256Hex(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new DisputeContinuityReadinessPromotionException($"{label} is not a JSON object.");
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

        throw new DisputeContinuityReadinessPromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new DisputeContinuityReadinessPromotionException($"Missing array property: {property}");
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

    public static void EnsurePathUnder(string root, string candidate, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new DisputeContinuityReadinessPromotionException(
                "Dispute continuity readiness path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    public static string ResolveSourceInput(
        DisputeContinuityReadinessPromotionPaths paths,
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
            ? Path.Combine(fullPath, DisputeContinuityReadinessPromotionPaths.SourceFileName)
            : fullPath;
    }

    private static void RequireValue(
        JsonObject value,
        string property,
        string expected,
        List<string> errors)
    {
        if (!string.Equals(GetString(value, property), expected, StringComparison.Ordinal))
        {
            errors.Add($"{property} must be {expected}.");
        }
    }

    private static void RequireNonEmptyArray(JsonObject value, string property, List<string> errors)
    {
        if (!value.TryGetPropertyValue(property, out var node) || node is not JsonArray array || array.Count == 0)
        {
            errors.Add($"{property} must be a non-empty array.");
        }
    }

    private static void AddForbiddenFindings(
        string text,
        string relativePath,
        List<DisputeContinuityMaterialFinding> findings)
    {
        var lower = text.ToLowerInvariant();
        AddIfContains("anomaly body", "anomaly_body");
        AddIfContains("submitter identity", "submitter_identity");
        AddIfContains("person-scope", "person_scope_identifier");
        AddIfContains("raw support log", "support_log");
        AddIfContains("raw attachment payload", "attachment_payload");
        AddIfContains("recipient public key", "recipient_public_key");
        AddIfContains("encrypted content-key", "encrypted_content_key");
        AddIfContains("ballot selection", "ballot_selection");
        AddIfContains("accepted ballot set", "accepted_ballot_set");
        AddIfContains("trustee share", "trustee_share");
        AddIfContains("tally material", "tally_material");
        AddIfContains("vote choice", "vote_choice");
        AddIfContains("voter identity", "voter_identity");
        AddIfContains("begin private key", "private_key");
        AddIfContains("password=", "credential");

        void AddIfContains(string needle, string category)
        {
            if (lower.Contains(needle, StringComparison.Ordinal))
            {
                findings.Add(new DisputeContinuityMaterialFinding(
                    relativePath,
                    category,
                    needle));
            }
        }
    }
}
