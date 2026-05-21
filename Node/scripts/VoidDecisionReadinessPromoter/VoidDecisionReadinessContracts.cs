using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VoidDecisionReadinessPromoter;

public sealed record VoidDecisionReadinessPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string OutputRoot)
{
    public const string SourceFolder = "Void-Decision-Publication-Replacement";
    public const string SourceFileName = "void-readiness-source.json";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);

    public static VoidDecisionReadinessPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            SourceFolder);
        return new VoidDecisionReadinessPromotionPaths(
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

public sealed record VoidDecisionReadinessMaterialFinding(
    string RelativePath,
    string Category,
    string Evidence);

public sealed record VoidDecisionReadinessGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash);

public sealed record VoidDecisionReadinessGeneratedPackage(
    string Status,
    IReadOnlyList<VoidDecisionReadinessGeneratedArtifact> Artifacts,
    IReadOnlyList<VoidDecisionReadinessMaterialFinding> PublicForbiddenFindings,
    IReadOnlyList<string> Blockers);

public sealed class VoidDecisionReadinessPromotionException : Exception
{
    public VoidDecisionReadinessPromotionException(string message, IEnumerable<string>? details = null)
        : base(message)
    {
        Details = details?.ToArray() ?? [];
    }

    public IReadOnlyList<string> Details { get; }
}

public static class VoidDecisionReadinessContracts
{
    public const string FeatureId = "FEAT-138";
    public const string AcceptanceGate = "AT-RDY-015";
    public const string ReadinessFragmentId = "RDY-EVID-AT-RDY-015-FEAT-138-001";
    public const string CanonicalizationVersion = "feat138-void-readiness-canonical-json-v1";

    public static readonly string[] RequiredSchemaFiles =
    [
        "void-readiness-source.schema.json",
        "void-readiness-fragment.schema.json",
        "void-downstream-handoff.schema.json",
        "void-public-artifact-scan.schema.json",
        "void-package-hash-validation.schema.json",
    ];

    public static readonly string[] RequiredOutputFiles =
    [
        VoidDecisionReadinessArtifactGenerator.ReadinessFragmentPath,
        VoidDecisionReadinessArtifactGenerator.DownstreamHandoffPath,
        VoidDecisionReadinessArtifactGenerator.PublicArtifactScanPath,
        VoidDecisionReadinessArtifactGenerator.PackageHashValidationPath,
    ];

    public static JsonObject LoadSource(
        VoidDecisionReadinessPromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "void readiness source");
        return ReadJsonObject(sourcePath, VoidDecisionReadinessPromotionPaths.SourceFileName);
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
        var errors = ValidateJsonRequired(source, VoidDecisionReadinessPromotionPaths.SourceFileName, [
            "schemaVersion",
            "sourceId",
            "featureId",
            "acceptanceGate",
            "sourceGap",
            "status",
            "generatedAt",
            "evidenceRefs",
            "focusedVerification",
            "publicArtifactSamples",
            "scoreEffect",
            "claimEffect",
            "residualRisk",
            "pba013Handoff",
            "voidEvidenceContract",
        ]).ToList();

        RequireValue(source, "featureId", FeatureId, errors);
        RequireValue(source, "acceptanceGate", AcceptanceGate, errors);
        RequireNonEmptyArray(source, "evidenceRefs", errors);
        RequireNonEmptyArray(source, "focusedVerification", errors);
        RequireNonEmptyArray(source, "publicArtifactSamples", errors);
        return errors;
    }

    public static IReadOnlyList<VoidDecisionReadinessMaterialFinding> ScanForbiddenPublicMaterial(JsonObject source)
    {
        var findings = new List<VoidDecisionReadinessMaterialFinding>();
        foreach (var sample in RequireArray(source, "publicArtifactSamples").OfType<JsonObject>())
        {
            var path = GetString(sample, "path");
            var content = GetString(sample, "content");
            AddForbiddenFindings(content, path, findings);
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
            throw new VoidDecisionReadinessPromotionException($"{label} is not a JSON object.");
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

        throw new VoidDecisionReadinessPromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new VoidDecisionReadinessPromotionException($"Missing array property: {property}");
    }

    public static string GetString(JsonObject? value, string property, string fallback = "")
    {
        if (value is null || !value.TryGetPropertyValue(property, out var node) || node is null)
        {
            return fallback;
        }

        return node.GetValue<string>();
    }

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    public static void EnsurePathUnder(string root, string candidate, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new VoidDecisionReadinessPromotionException(
                "Void readiness path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    public static string ResolveSourceInput(
        VoidDecisionReadinessPromotionPaths paths,
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
            ? Path.Combine(fullPath, VoidDecisionReadinessPromotionPaths.SourceFileName)
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
        List<VoidDecisionReadinessMaterialFinding> findings)
    {
        var lower = text.ToLowerInvariant();
        AddIfContains("begin private key", "private_key");
        AddIfContains("aws_secret_access_key", "credential");
        AddIfContains("password=", "credential");
        AddIfContains("arn:aws:kms", "provider_kms_identifier");
        AddIfContains("raw support log", "support_log");
        AddIfContains("voter identity", "voter_identity");
        AddIfContains("vote choice", "vote_choice");
        AddIfContains("accepted ballot set", "accepted_ballot_set");
        AddIfContains("tally total", "tally_total");
        AddIfContains("raw trustee share", "trustee_raw_share");

        void AddIfContains(string needle, string category)
        {
            if (lower.Contains(needle, StringComparison.Ordinal))
            {
                findings.Add(new VoidDecisionReadinessMaterialFinding(
                    relativePath,
                    category,
                    needle));
            }
        }
    }
}
