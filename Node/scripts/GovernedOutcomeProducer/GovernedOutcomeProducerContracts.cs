using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GovernedOutcomeProducer;

public sealed record GovernedOutcomeProducerPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string OutputRoot)
{
    public const string SourceFolder = "Governed-Outcome-Producer";
    public const string SourceFileName = "governed-outcome-producer-source.json";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);

    public static GovernedOutcomeProducerPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            SourceFolder);
        return new GovernedOutcomeProducerPaths(
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

public sealed record GovernedOutcomeMaterialFinding(
    string RelativePath,
    string Category,
    string Evidence);

public sealed record GovernedOutcomeGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash);

public sealed record GovernedOutcomeGeneratedPackage(
    string Status,
    IReadOnlyList<GovernedOutcomeGeneratedArtifact> Artifacts,
    IReadOnlyList<GovernedOutcomeMaterialFinding> PublicForbiddenFindings,
    IReadOnlyList<string> Blockers);

public sealed class GovernedOutcomeProducerException : Exception
{
    public GovernedOutcomeProducerException(string message, IEnumerable<string>? details = null)
        : base(message)
    {
        Details = details?.ToArray() ?? [];
    }

    public IReadOnlyList<string> Details { get; }
}

public static class GovernedOutcomeProducerContracts
{
    public const string FeatureId = "FEAT-146";
    public const string CanonicalizationVersion = "feat146-governed-outcome-producer-canonical-json-v1";

    public static readonly string[] RequiredSchemaFiles =
    [
        "governed-outcome-producer-source.schema.json",
        "governed-outcome-feat139-handoff.schema.json",
        "governed-outcome-feat141-handoff.schema.json",
        "governed-outcome-package-hash-validation.schema.json",
    ];

    public static readonly string[] RequiredOutputFiles =
    [
        GovernedOutcomeProducerArtifactGenerator.Feat139HandoffPath,
        GovernedOutcomeProducerArtifactGenerator.Feat141HandoffPath,
        GovernedOutcomeProducerArtifactGenerator.PackageHashValidationPath,
    ];

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        "accepted",
        "accepted_with_limitations",
        "blocked",
    };

    public static JsonObject LoadSource(
        GovernedOutcomeProducerPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "governed outcome producer source");
        return ReadJsonObject(sourcePath, GovernedOutcomeProducerPaths.SourceFileName);
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
        var errors = ValidateJsonRequired(source, GovernedOutcomeProducerPaths.SourceFileName, [
            "schemaVersion",
            "sourceId",
            "featureId",
            "status",
            "generatedAt",
            "evidenceRefs",
            "governedOutcomeEvidence",
            "reportPackageEvidence",
            "verificationEvidence",
            "handoffPolicy",
            "publicArtifactSamples",
        ]).ToList();

        RequireValue(source, "featureId", FeatureId, errors);
        var status = GetString(source, "status");
        if (!AllowedStatuses.Contains(status))
        {
            errors.Add($"Unsupported source status: {status}.");
        }

        RequireNonEmptyArray(source, "evidenceRefs", errors);

        if (source.TryGetPropertyValue("governedOutcomeEvidence", out var outcomeNode) &&
            outcomeNode is JsonObject outcome)
        {
            RequireValue(outcome, "outcomeStatus", "finalized_with_anomaly", errors);
            RequireValue(outcome, "finalizationMode", "abnormal_finalization", errors);
            if (GetBool(outcome, "cleanFinalization", defaultValue: true))
            {
                errors.Add("governedOutcomeEvidence.cleanFinalization must be false.");
            }

            RequireValue(outcome, "exactCopyStatus", "passed", errors);
        }

        if (source.TryGetPropertyValue("handoffPolicy", out var policyNode) &&
            policyNode is JsonObject policy)
        {
            RequireNonEmptyArray(policy, "feat141ClaimStates", errors);
            RequireNonEmptyArray(policy, "publicWordingKeys", errors);
        }

        return errors;
    }

    public static IReadOnlyList<GovernedOutcomeMaterialFinding> ScanForbiddenPublicMaterial(
        JsonObject source,
        IEnumerable<(string Path, string Content)> generatedPublicArtifacts)
    {
        var findings = new List<GovernedOutcomeMaterialFinding>();
        foreach (var sample in RequireArray(source, "publicArtifactSamples").OfType<JsonObject>())
        {
            AddForbiddenFindings(GetString(sample, "content"), GetString(sample, "path"), findings);
        }

        foreach (var artifact in generatedPublicArtifacts)
        {
            AddForbiddenFindings(artifact.Content, artifact.Path, findings);
        }

        return findings
            .DistinctBy(x => $"{x.RelativePath}:{x.Category}:{x.Evidence}", StringComparer.Ordinal)
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ThenBy(x => x.Category, StringComparer.Ordinal)
            .ToArray();
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var text = File.ReadAllText(path);
        return JsonNode.Parse(text)?.AsObject()
            ?? throw new GovernedOutcomeProducerException($"{label} is not a JSON object.");
    }

    public static void EnsurePathUnder(string root, string path, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new GovernedOutcomeProducerException($"{label} path escapes the expected root: {path}");
        }
    }

    public static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    public static JsonObject RequireObject(JsonObject source, string property) =>
        source.TryGetPropertyValue(property, out var node) && node is JsonObject value
            ? value
            : throw new GovernedOutcomeProducerException($"Missing object property: {property}");

    public static JsonArray RequireArray(JsonObject source, string property) =>
        source.TryGetPropertyValue(property, out var node) && node is JsonArray value
            ? value
            : throw new GovernedOutcomeProducerException($"Missing array property: {property}");

    public static string GetString(JsonObject source, string property, string fallback = "") =>
        source.TryGetPropertyValue(property, out var node) && node is not null
            ? node.GetValue<string>()
            : fallback;

    public static bool GetBool(JsonObject source, string property, bool defaultValue = false) =>
        source.TryGetPropertyValue(property, out var node) && node is not null
            ? node.GetValue<bool>()
            : defaultValue;

    public static string[] GetStringArray(JsonObject source, string property) =>
        source.TryGetPropertyValue(property, out var node) && node is JsonArray array
            ? array.Select(x => x?.GetValue<string>() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray()
            : [];

    public static string CanonicalJson(JsonNode value) =>
        value.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string ResolveSourceInput(GovernedOutcomeProducerPaths paths, string? sourceInput)
    {
        if (string.IsNullOrWhiteSpace(sourceInput))
        {
            return paths.DefaultSourceInput;
        }

        var fullPath = Path.GetFullPath(sourceInput);
        return Directory.Exists(fullPath)
            ? Path.Combine(fullPath, GovernedOutcomeProducerPaths.SourceFileName)
            : fullPath;
    }

    private static IEnumerable<string> ValidateJsonRequired(
        JsonObject source,
        string label,
        IReadOnlyList<string> required)
    {
        foreach (var property in required)
        {
            if (!source.ContainsKey(property))
            {
                yield return $"{label} is missing required property {property}.";
            }
        }
    }

    private static void RequireValue(JsonObject source, string property, string expected, List<string> errors)
    {
        var actual = GetString(source, property);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            errors.Add($"{property} must be {expected}; found {actual}.");
        }
    }

    private static void RequireNonEmptyArray(JsonObject source, string property, List<string> errors)
    {
        if (!source.TryGetPropertyValue(property, out var node) ||
            node is not JsonArray array ||
            array.Count == 0)
        {
            errors.Add($"{property} must be a non-empty array.");
        }
    }

    private static void AddForbiddenFindings(
        string content,
        string relativePath,
        List<GovernedOutcomeMaterialFinding> findings)
    {
        var checks = new (string Needle, string Category)[]
        {
            ("anomaly body", "anomaly_body"),
            ("trustee share", "trustee_secret_material"),
            ("private key", "trustee_secret_material"),
            ("voter-", "voter_material"),
            ("actor-voter", "voter_material"),
            ("@", "private_contact"),
        };

        foreach (var (needle, category) in checks)
        {
            if (content.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new GovernedOutcomeMaterialFinding(relativePath, category, needle));
            }
        }
    }
}
