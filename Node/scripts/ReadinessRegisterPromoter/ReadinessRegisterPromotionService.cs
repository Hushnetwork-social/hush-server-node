using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ReadinessRegisterPromoter;

public sealed record ReadinessRegisterPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string OutputRoot)
{
    public string SchemaPath => Path.Combine(SourceRoot, "readiness-register.schema.json");
    public string RegisterPath => Path.Combine(SourceRoot, "readiness-register.json");
    public string ExamplePath => Path.Combine(SourceRoot, "readiness-register.example.json");
    public string CatalogPath => Path.Combine(OutputRoot, "readiness-register-catalog.json");

    public static ReadinessRegisterPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        var root = Path.GetFullPath(workspaceRoot);
        return new ReadinessRegisterPromotionPaths(
            root,
            Path.Combine(root, "hush-memory-bank", "Overview", "HushVotingReadiness", "Readiness-Register"),
            Path.Combine(root, "hush-documents", "PrivateServer_ElectronicVoting", "HushVoting-Readiness-Register"));
    }
}

public sealed record ReadinessRegisterPromotionOptions(
    ReadinessRegisterPromotionPaths Paths,
    string RegisterId,
    string? Version,
    string PublicationStatus,
    bool ValidateOnly,
    bool Scaffold,
    DateTimeOffset? GeneratedAt);

public sealed record ReadinessRegisterPromotionResult(
    string RegisterVersion,
    string RegisterVersionId,
    string Status,
    DateTimeOffset GeneratedAt,
    int TotalScore,
    string StrongestAllowedClaim,
    string PublicationStatus,
    string ManifestHash,
    string ArchiveHash,
    string CatalogPath,
    string VersionOutputRoot,
    IReadOnlyList<string> WrittenFiles);

public sealed class ReadinessRegisterPromotionException(
    string message,
    IReadOnlyList<string> details) : InvalidOperationException(message)
{
    public IReadOnlyList<string> Details { get; } = details;
}

public sealed class ReadinessRegisterPromotionService
{
    public const string SchemaFileName = "readiness-register.schema.json";
    public const string RegisterFileName = "readiness-register.json";
    public const string ExampleFileName = "readiness-register.example.json";
    public const string ScorecardFileName = "readiness-scorecard.md";
    public const string RestrictedReviewerExtractFileName = "restricted-reviewer-extract.md";
    public const string PublicSafeSummaryFileName = "public-safe-summary.md";
    public const string ManifestFileName = "readiness-register-manifest.json";
    public const string CatalogFileName = "readiness-register-catalog.json";
    public const string ArchivePrefix = "HushVoting-Readiness-Register";

    private static readonly Regex VersionPattern = new("^v[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex RegisterVersionIdPattern = new("^RDY-REG-v[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex HexSha256Pattern = new("^[a-f0-9]{64}$", RegexOptions.Compiled);
    private static readonly Regex EvidenceIdPattern = new("^RDY-EVID-AT-RDY-[0-9]{3}-FEAT-[0-9]{3}-[0-9]{3}$", RegexOptions.Compiled);
    private static readonly Regex BlockerIdPattern = new("^RDY-BLOCK-[A-Z0-9_]+-[0-9]{3}$", RegexOptions.Compiled);
    private static readonly Regex ScoreChangeIdPattern = new("^RDY-SCORE-[0-9]{8}-[0-9]{3}$", RegexOptions.Compiled);
    private static readonly Regex ExceptionIdPattern = new("^RDY-EXC-[0-9]{8}-[0-9]{3}$", RegexOptions.Compiled);

    private static readonly DateTimeOffset FixedZipTimestamp = new(
        1980,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    private static readonly JsonSerializerOptions ReadableJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.Ordinal)
    {
        "DraftInternal",
        "AcceptedInternal",
        "ReviewerReady",
        "Superseded",
        "Blocked",
    };

    private static readonly string[] DimensionIds =
    [
        "RDY-DIM-001",
        "RDY-DIM-002",
        "RDY-DIM-003",
        "RDY-DIM-004",
        "RDY-DIM-005",
        "RDY-DIM-006",
        "RDY-DIM-007",
        "RDY-DIM-008",
        "RDY-DIM-009",
        "RDY-DIM-010",
    ];

    private static readonly string[] ClaimLevels =
    [
        "internal_development",
        "internal_non_binding_rehearsal",
        "friendly_organization_pilot",
        "production_organizational_rollout",
        "public_or_state_election",
    ];

    private static readonly HashSet<string> EvidenceStates = new(StringComparer.Ordinal)
    {
        "missing",
        "placeholder",
        "draft",
        "observed",
        "accepted",
        "blocked",
        "rejected",
        "superseded",
    };

    private static readonly HashSet<string> ClaimEffects = new(StringComparer.Ordinal)
    {
        "none",
        "score_increase",
        "downgrade",
        "block",
        "unblock",
        "residual_risk_update",
    };

    private static readonly HashSet<string> PublicForbiddenTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "51/100",
        "total score",
        "dimension score",
        "score delta",
        "score history",
        "restricted_reviewer",
        "internal/",
        "sha-256",
        "reviewer-only",
        "client data",
        "raw log",
        "anomaly detail",
        "deployment credential",
    };

    public ReadinessRegisterPromotionResult Promote(ReadinessRegisterPromotionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidatePathConfiguration(options.Paths);

        if (options.Scaffold)
        {
            ScaffoldMissingSourceFiles(options.Paths);
        }

        var missing = new[]
            {
                options.Paths.SchemaPath,
                options.Paths.RegisterPath,
                options.Paths.ExamplePath,
            }
            .Where(path => !File.Exists(path))
            .Select(path => Path.GetRelativePath(options.Paths.SourceRoot, path))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "Readiness register promotion failed because required source files are missing.",
                missing);
        }

        var schema = ReadJsonObject(options.Paths.SchemaPath, SchemaFileName);
        var register = ReadJsonObject(options.Paths.RegisterPath, RegisterFileName);
        var example = ReadJsonObject(options.Paths.ExamplePath, ExampleFileName);
        ApplyCommandOverrides(register, options);

        var validationErrors = new List<string>();
        ValidateSchemaDocument(schema, validationErrors);
        ValidateRegister(register, options, validationErrors);
        ValidateExample(example, options, validationErrors);
        if (validationErrors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "Readiness register promotion failed validation.",
                validationErrors);
        }

        var generatedAt = options.GeneratedAt ?? DateTimeOffset.UtcNow;
        var registerVersion = GetRequiredString(register, "registerVersion");
        var registerVersionId = GetRequiredString(register, "registerVersionId");
        var status = GetRequiredString(register, "status");
        var totalScore = GetRequiredInt(GetRequiredObject(register, "score"), "total");
        var strongestAllowedClaim = GetCurrentStrongestAllowedClaim(register);
        var publicationStatus = GetRequiredString(GetRequiredObject(register, "generatedViews"), "publicSafePublicationStatus");

        var promotedFiles = BuildPromotedFiles(schema, register, example);
        promotedFiles.Add(new PromotedFile(
            ScorecardFileName,
            "restricted",
            EncodingWithoutBom(GetScorecardMarkdown(register, promotedFiles)),
            "text/markdown"));
        promotedFiles.Add(new PromotedFile(
            RestrictedReviewerExtractFileName,
            "restricted",
            EncodingWithoutBom(GetRestrictedReviewerExtractMarkdown(register)),
            "text/markdown"));
        promotedFiles.Add(new PromotedFile(
            PublicSafeSummaryFileName,
            "public-safe",
            EncodingWithoutBom(GetPublicSafeSummaryMarkdown(register)),
            "text/markdown"));

        ValidateGeneratedViews(promotedFiles, validationErrors);
        if (validationErrors.Count > 0)
        {
            throw new ReadinessRegisterPromotionException(
                "Readiness register generated views failed validation.",
                validationErrors);
        }

        var archiveFileName = $"{ArchivePrefix}-{registerVersion}.zip";
        var archiveBytes = BuildDeterministicArchive(promotedFiles);
        var archiveHash = ComputeSha256Hex(archiveBytes);
        var manifestWithoutHash = BuildManifest(
            register,
            generatedAt,
            promotedFiles,
            archiveFileName,
            archiveBytes.Length,
            archiveHash,
            manifestHash: null);
        var manifestHash = ComputeSha256Hex(EncodingWithoutBom(SerializeJson(manifestWithoutHash)));
        var manifest = BuildManifest(
            register,
            generatedAt,
            promotedFiles,
            archiveFileName,
            archiveBytes.Length,
            archiveHash,
            manifestHash);
        var manifestBytes = EncodingWithoutBom(SerializeJson(manifest));
        promotedFiles.Add(new PromotedFile(
            ManifestFileName,
            "restricted",
            manifestBytes,
            "application/json"));

        EnsureCatalogAllowsPromotion(options.Paths.CatalogPath, registerVersionId, manifestHash, archiveHash);

        var versionOutputRoot = Path.Combine(options.Paths.OutputRoot, registerVersion);
        var writtenFiles = new List<string>();
        if (!options.ValidateOnly)
        {
            WritePromotedArtifacts(
                options.Paths,
                versionOutputRoot,
                promotedFiles,
                archiveFileName,
                archiveBytes,
                manifest,
                writtenFiles);
        }

        return new ReadinessRegisterPromotionResult(
            registerVersion,
            registerVersionId,
            status,
            generatedAt,
            totalScore,
            strongestAllowedClaim,
            publicationStatus,
            manifestHash,
            archiveHash,
            options.Paths.CatalogPath,
            versionOutputRoot,
            writtenFiles);
    }

    private static void ValidatePathConfiguration(ReadinessRegisterPromotionPaths paths)
    {
        var workspaceRoot = Path.GetFullPath(paths.WorkspaceRoot);
        if (!Directory.Exists(workspaceRoot))
        {
            throw new ReadinessRegisterPromotionException(
                "Workspace root does not exist.",
                [workspaceRoot]);
        }

        EnsureContained(workspaceRoot, Path.GetFullPath(paths.SourceRoot), "Source root must stay inside workspace root.");
        EnsureContained(workspaceRoot, Path.GetFullPath(paths.OutputRoot), "Output root must stay inside workspace root.");

        if (!Directory.Exists(Path.Combine(workspaceRoot, "hush-memory-bank")) ||
            !Directory.Exists(Path.Combine(workspaceRoot, "hush-documents")) ||
            !Directory.Exists(Path.Combine(workspaceRoot, "hush-server-node")))
        {
            throw new ReadinessRegisterPromotionException(
                "Workspace root must contain hush-memory-bank, hush-documents, and hush-server-node.",
                [workspaceRoot]);
        }
    }

    private static void EnsureContained(string root, string child, string message)
    {
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!child.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !child.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReadinessRegisterPromotionException(message, [child]);
        }
    }

    private static JsonObject ReadJsonObject(string path, string displayName)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new JsonException("Root is not an object.");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new ReadinessRegisterPromotionException(
                $"Could not read {displayName}.",
                [$"{path}: {ex.Message}"]);
        }
    }

    private static void ApplyCommandOverrides(JsonObject register, ReadinessRegisterPromotionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Version))
        {
            register["registerVersion"] = options.Version;
            register["registerVersionId"] = $"RDY-REG-{options.Version}";
        }

        if (!string.IsNullOrWhiteSpace(options.PublicationStatus))
        {
            GetRequiredObject(register, "generatedViews")["publicSafePublicationStatus"] = options.PublicationStatus;
        }
    }

    private static void ValidateSchemaDocument(JsonObject schema, List<string> errors)
    {
        if (GetStringOrDefault(schema, "$schema") != "https://json-schema.org/draft/2020-12/schema")
        {
            errors.Add("readiness-register.schema.json must use JSON Schema draft 2020-12.");
        }

        if (GetStringOrDefault(schema, "$id") != "https://hushnetwork.social/schemas/hushvoting/readiness-register.schema.json")
        {
            errors.Add("readiness-register.schema.json must use the FEAT-130 schema id.");
        }
    }

    private static void ValidateRegister(JsonObject register, ReadinessRegisterPromotionOptions options, List<string> errors)
    {
        RequireFixed(register, "schemaVersion", "1.0", errors);
        RequireFixed(register, "registerId", options.RegisterId, errors);
        RequirePattern(register, "registerVersion", VersionPattern, errors);
        RequirePattern(register, "registerVersionId", RegisterVersionIdPattern, errors);
        RequireEnum(register, "status", ValidStatuses, errors);
        RequireFixed(register, "parentEpic", "EPIC-015", errors);
        RequireNonEmpty(register, "sourceGapRegister", errors);
        RequireNonEmpty(register, "createdAt", errors);
        RequireNonEmpty(register, "sourceCommit", errors);

        var registerVersion = GetStringOrDefault(register, "registerVersion");
        var registerVersionId = GetStringOrDefault(register, "registerVersionId");
        if (!string.IsNullOrWhiteSpace(registerVersion) &&
            registerVersionId != $"RDY-REG-{registerVersion}")
        {
            errors.Add("registerVersionId must be RDY-REG-{registerVersion}.");
        }

        var score = RequireObject(register, "score", errors);
        var dimensions = RequireArray(register, "dimensions", errors);
        var claimLevels = RequireArray(register, "claimLevels", errors);
        var blockers = RequireArray(register, "blockers", errors);
        var evidenceItems = RequireArray(register, "evidenceItems", errors);
        var scoreChanges = RequireArray(register, "scoreChanges", errors);
        var exceptions = RequireArray(register, "exceptions", errors);
        var generatedViews = RequireObject(register, "generatedViews", errors);
        var signoffPolicy = RequireObject(register, "signoffPolicy", errors);
        var claimPolicy = RequireObject(register, "claimPolicy", errors);

        if (score is not null && dimensions is not null)
        {
            ValidateScore(score, dimensions, errors);
        }

        if (claimPolicy is not null)
        {
            ValidateClaimPolicy(claimPolicy, errors);
        }

        if (dimensions is not null)
        {
            ValidateDimensions(dimensions, errors);
        }

        if (claimLevels is not null)
        {
            ValidateClaimLevels(claimLevels, errors);
        }

        if (blockers is not null)
        {
            ValidateBlockers(blockers, errors);
        }

        var evidenceById = evidenceItems is null
            ? new Dictionary<string, JsonObject>(StringComparer.Ordinal)
            : ValidateEvidence(evidenceItems, errors);
        if (scoreChanges is not null)
        {
            ValidateScoreChanges(scoreChanges, evidenceById, errors);
        }

        if (exceptions is not null)
        {
            ValidateExceptions(exceptions, errors);
        }

        if (generatedViews is not null)
        {
            RequireFixed(generatedViews, "scorecardPath", ScorecardFileName, errors);
            RequireFixed(generatedViews, "restrictedReviewerExtractPath", RestrictedReviewerExtractFileName, errors);
            RequireFixed(generatedViews, "publicSafeSummaryPath", PublicSafeSummaryFileName, errors);
            RequireEnum(
                generatedViews,
                "publicSafePublicationStatus",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "not_for_publication",
                    "not_ready_for_public_claim",
                    "pilot_only_with_limitations",
                    "public_claim_blocked",
                },
                errors);
        }

        if (signoffPolicy is not null)
        {
            ValidateSignoffPolicy(signoffPolicy, errors);
        }
    }

    private static void ValidateScore(JsonObject score, JsonArray dimensions, List<string> errors)
    {
        var total = RequireInt(score, "total", errors);
        RequireInt(score, "baselineTotal", errors);
        RequireInt(score, "dimensionCount", errors);
        RequireInt(score, "minimumConfidenceScore", errors);
        RequireInt(score, "strongerTargetScore", errors);
        var dimensionIds = RequireArray(score, "dimensionIds", errors);

        var currentTotal = dimensions
            .Select(x => x?.AsObject())
            .Where(x => x is not null)
            .Sum(x => GetIntOrDefault(x!, "currentScore"));
        if (total != currentTotal)
        {
            errors.Add($"score.total must equal dimension score sum. Expected {currentTotal}, found {total}.");
        }

        if (GetIntOrDefault(score, "baselineTotal") != 51)
        {
            errors.Add("score.baselineTotal must be 51 for RDY-REG-v0.1.0.");
        }

        if (GetIntOrDefault(score, "dimensionCount") != 10)
        {
            errors.Add("score.dimensionCount must be 10.");
        }

        if (dimensionIds is not null)
        {
            var ids = dimensionIds.Select(x => x?.GetValue<string>()).ToArray();
            if (!DimensionIds.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(ids.OrderBy(x => x, StringComparer.Ordinal)))
            {
                errors.Add("score.dimensionIds must contain RDY-DIM-001 through RDY-DIM-010 exactly once.");
            }
        }
    }

    private static void ValidateClaimPolicy(JsonObject claimPolicy, List<string> errors)
    {
        if (GetIntOrDefault(claimPolicy, "minimumConfidenceScore") != 70)
        {
            errors.Add("claimPolicy.minimumConfidenceScore must be 70.");
        }

        if (GetIntOrDefault(claimPolicy, "strongerTargetScore") != 80)
        {
            errors.Add("claimPolicy.strongerTargetScore must be 80.");
        }

        RequireFixed(claimPolicy, "strongestAllowedV1Claim", "friendly_organization_pilot", errors);
        if (claimPolicy["publicScoreAllowed"]?.GetValue<bool>() != false)
        {
            errors.Add("claimPolicy.publicScoreAllowed must be false.");
        }

        var visibility = RequireArray(claimPolicy, "numericScoreVisibility", errors);
        if (visibility is not null)
        {
            var values = visibility.Select(x => x?.GetValue<string>()).ToArray();
            if (!values.Contains("internal", StringComparer.Ordinal) ||
                !values.Contains("restricted_reviewer", StringComparer.Ordinal) ||
                values.Contains("public_safe", StringComparer.Ordinal))
            {
                errors.Add("claimPolicy.numericScoreVisibility must include internal and restricted_reviewer only.");
            }
        }
    }

    private static void ValidateDimensions(JsonArray dimensions, List<string> errors)
    {
        if (dimensions.Count != 10)
        {
            errors.Add("dimensions must contain exactly ten items.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dimension in dimensions.Select((node, index) => (node, index)))
        {
            if (dimension.node is not JsonObject item)
            {
                errors.Add($"dimensions[{dimension.index}] must be an object.");
                continue;
            }

            var dimensionId = GetStringOrDefault(item, "dimensionId");
            if (!DimensionIds.Contains(dimensionId, StringComparer.Ordinal))
            {
                errors.Add($"dimensions[{dimension.index}].dimensionId is not a supported dimension id.");
            }

            if (!string.IsNullOrWhiteSpace(dimensionId) && !seen.Add(dimensionId))
            {
                errors.Add($"Dimension id {dimensionId} is duplicated.");
            }

            RequireNonEmpty(item, "name", errors);
            if (GetIntOrDefault(item, "weight") != 10)
            {
                errors.Add($"{dimensionId}.weight must be 10.");
            }

            var currentScore = RequireInt(item, "currentScore", errors);
            if (currentScore < 0 || currentScore > 10)
            {
                errors.Add($"{dimensionId}.currentScore must be 0..10.");
            }

            var evidenceIds = RequireArray(item, "evidenceIds", errors);
            if (currentScore > 0 && evidenceIds is { Count: 0 })
            {
                errors.Add($"{dimensionId}.evidenceIds cannot be empty when currentScore is above 0.");
            }

            RequireArray(item, "sourceGapRows", errors);
            RequireArray(item, "acceptanceGateIds", errors);
            RequireArray(item, "blockerIds", errors);
            RequireNonEmpty(item, "residualRisk", errors);
            RequireNonEmpty(item, "scoreRationale", errors);
        }
    }

    private static void ValidateClaimLevels(JsonArray claimLevels, List<string> errors)
    {
        if (claimLevels.Count != 5)
        {
            errors.Add("claimLevels must contain exactly five items.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in claimLevels.Select((node, index) => (node, index)))
        {
            if (claim.node is not JsonObject item)
            {
                errors.Add($"claimLevels[{claim.index}] must be an object.");
                continue;
            }

            var claimLevel = GetStringOrDefault(item, "claimLevel");
            if (!ClaimLevels.Contains(claimLevel, StringComparer.Ordinal))
            {
                errors.Add($"Unsupported claim level: {claimLevel}.");
            }

            if (!string.IsNullOrWhiteSpace(claimLevel) && !seen.Add(claimLevel))
            {
                errors.Add($"Claim level {claimLevel} is duplicated.");
            }

            var severity = GetStringOrDefault(item, "blockerSeverity");
            var status = GetStringOrDefault(item, "status");
            if (!new[] { "green", "amber", "red" }.Contains(severity, StringComparer.Ordinal))
            {
                errors.Add($"{claimLevel}.blockerSeverity must be green, amber, or red.");
            }

            if (!new[] { "allowed", "allowed_with_limitations", "blocked", "downgraded" }.Contains(status, StringComparer.Ordinal))
            {
                errors.Add($"{claimLevel}.status is invalid.");
            }

            if (severity == "amber" && string.IsNullOrWhiteSpace(GetStringOrDefault(item, "limitationWording")))
            {
                errors.Add($"{claimLevel} is amber and must include limitation wording.");
            }

            if ((severity == "red" || status == "blocked") &&
                string.IsNullOrWhiteSpace(GetStringOrDefault(item, "blockedWording")))
            {
                errors.Add($"{claimLevel} is blocked/red and must include blocked wording.");
            }

            if (severity == "red" && status is "allowed" or "allowed_with_limitations")
            {
                errors.Add($"{claimLevel} is red and cannot be allowed.");
            }

            RequireArray(item, "blockerIds", errors);
            RequireNonEmpty(item, "publicSafeStatus", errors);
        }
    }

    private static void ValidateBlockers(JsonArray blockers, List<string> errors)
    {
        foreach (var blocker in blockers.Select((node, index) => (node, index)))
        {
            if (blocker.node is not JsonObject item)
            {
                errors.Add($"blockers[{blocker.index}] must be an object.");
                continue;
            }

            RequirePattern(item, "blockerId", BlockerIdPattern, errors);
            RequireNonEmpty(item, "description", errors);
            RequireNonEmpty(item, "featureId", errors);
            RequireArray(item, "acceptanceGateIds", errors);
            RequireArray(item, "dimensionIds", errors);
            RequireEnum(item, "severity", new HashSet<string>(["green", "amber", "red"], StringComparer.Ordinal), errors);
            RequireEnum(item, "status", new HashSet<string>(["open", "resolved", "superseded"], StringComparer.Ordinal), errors);
            if (GetStringOrDefault(item, "severity") is "amber" or "red" &&
                string.IsNullOrWhiteSpace(GetStringOrDefault(item, "resolutionCriteria")))
            {
                errors.Add($"{GetStringOrDefault(item, "blockerId")} must include resolution criteria.");
            }
        }
    }

    private static Dictionary<string, JsonObject> ValidateEvidence(JsonArray evidenceItems, List<string> errors)
    {
        var evidenceById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var evidence in evidenceItems.Select((node, index) => (node, index)))
        {
            if (evidence.node is not JsonObject item)
            {
                errors.Add($"evidenceItems[{evidence.index}] must be an object.");
                continue;
            }

            var evidenceId = GetStringOrDefault(item, "evidenceId");
            RequirePattern(item, "evidenceId", EvidenceIdPattern, errors);
            if (!string.IsNullOrWhiteSpace(evidenceId))
            {
                evidenceById[evidenceId] = item;
            }

            RequireNonEmpty(item, "parentEpic", errors);
            RequireNonEmpty(item, "featureId", errors);
            RequireNonEmpty(item, "sourceGapRow", errors);
            RequireArray(item, "acceptanceGateIds", errors);
            RequireArray(item, "dimensionIds", errors);
            RequireNonEmpty(item, "electionScope", errors);
            RequireNonEmpty(item, "releaseScope", errors);
            RequireEnum(item, "visibility", new HashSet<string>(["internal", "restricted_reviewer", "public_safe"], StringComparer.Ordinal), errors);
            RequireEnum(item, "status", EvidenceStates, errors);
            RequireNonEmpty(item, "owner", errors);
            RequireArray(item, "artifactRefs", errors);
            RequireArray(item, "checkResults", errors);
            RequireObject(item, "freshness", errors);
            RequireEnum(item, "claimEffect", ClaimEffects, errors);
            RequireObject(item, "signoffs", errors);
            RequireArray(item, "relatedExceptionIds", errors);
            RequireArray(item, "relatedBlockerIds", errors);

            var status = GetStringOrDefault(item, "status");
            if (status is "observed" or "accepted" && item["producedAt"] is null)
            {
                errors.Add($"{evidenceId}.producedAt is required for observed or accepted evidence.");
            }

            if (status == "accepted")
            {
                ValidateSignoffs(GetRequiredObject(item, "signoffs"), $"{evidenceId}.signoffs", errors);
            }

            ValidateArtifactRefs(RequireArray(item, "artifactRefs", errors), $"{evidenceId}.artifactRefs", errors);
        }

        return evidenceById;
    }

    private static void ValidateArtifactRefs(JsonArray? artifactRefs, string path, List<string> errors)
    {
        if (artifactRefs is null)
        {
            return;
        }

        foreach (var artifactRef in artifactRefs.Select((node, index) => (node, index)))
        {
            if (artifactRef.node is not JsonObject item)
            {
                errors.Add($"{path}[{artifactRef.index}] must be an object.");
                continue;
            }

            RequireNonEmpty(item, "artifactId", errors);
            RequireNonEmpty(item, "relativePath", errors);
            RequireFixed(item, "hashAlgorithm", "SHA-256", errors);
            RequirePattern(item, "sha256Hash", HexSha256Pattern, errors);
            RequireNonEmpty(item, "mediaType", errors);
            if (RequireInt(item, "sizeBytes", errors) < 0)
            {
                errors.Add($"{path}[{artifactRef.index}].sizeBytes must be positive.");
            }
        }
    }

    private static void ValidateScoreChanges(
        JsonArray scoreChanges,
        IReadOnlyDictionary<string, JsonObject> evidenceById,
        List<string> errors)
    {
        foreach (var scoreChange in scoreChanges.Select((node, index) => (node, index)))
        {
            if (scoreChange.node is not JsonObject item)
            {
                errors.Add($"scoreChanges[{scoreChange.index}] must be an object.");
                continue;
            }

            RequirePattern(item, "scoreChangeId", ScoreChangeIdPattern, errors);
            RequireNonEmpty(item, "dimensionId", errors);
            RequireEnum(item, "direction", new HashSet<string>(["increase", "decrease"], StringComparer.Ordinal), errors);
            RequireInt(item, "previousScore", errors);
            RequireInt(item, "proposedScore", errors);
            RequireInt(item, "acceptedScore", errors);
            var evidenceIds = RequireArray(item, "evidenceIds", errors);
            RequireArray(item, "acceptanceGateIds", errors);
            RequireArray(item, "blockerImpactBefore", errors);
            RequireArray(item, "blockerImpactAfter", errors);
            RequireNonEmpty(item, "claimImpact", errors);
            RequireNonEmpty(item, "reason", errors);
            RequireNonEmpty(item, "generatedDiff", errors);

            if (GetStringOrDefault(item, "direction") == "increase")
            {
                ValidateSignoffs(RequireObject(item, "signoffs", errors), $"{GetStringOrDefault(item, "scoreChangeId")}.signoffs", errors);
                foreach (var evidenceId in evidenceIds?.Select(x => x?.GetValue<string>()) ?? [])
                {
                    if (evidenceId is null ||
                        !evidenceById.TryGetValue(evidenceId, out var evidence) ||
                        GetStringOrDefault(evidence, "status") != "accepted")
                    {
                        errors.Add($"{GetStringOrDefault(item, "scoreChangeId")} increases score using non-accepted evidence {evidenceId}.");
                    }
                }
            }
        }
    }

    private static void ValidateExceptions(JsonArray exceptions, List<string> errors)
    {
        foreach (var exception in exceptions.Select((node, index) => (node, index)))
        {
            if (exception.node is not JsonObject item)
            {
                errors.Add($"exceptions[{exception.index}] must be an object.");
                continue;
            }

            RequirePattern(item, "exceptionId", ExceptionIdPattern, errors);
            RequireEnum(item, "type", new HashSet<string>(["skipped", "unavailable", "deferred", "stale_invalidated", "client_declined"], StringComparer.Ordinal), errors);
            RequireNonEmpty(item, "status", errors);
            RequireNonEmpty(item, "reason", errors);
            RequireNonEmpty(item, "owner", errors);
            RequireEnum(item, "severity", new HashSet<string>(["warn", "downgrade", "block"], StringComparer.Ordinal), errors);
        }
    }

    private static void ValidateSignoffPolicy(JsonObject signoffPolicy, List<string> errors)
    {
        var requiredRoles = RequireArray(signoffPolicy, "requiredRoles", errors);
        if (requiredRoles is not null)
        {
            var roles = requiredRoles.Select(x => x?.GetValue<string>()).ToArray();
            if (!roles.Contains("engineering", StringComparer.Ordinal) ||
                !roles.Contains("operations_product", StringComparer.Ordinal))
            {
                errors.Add("signoffPolicy.requiredRoles must contain engineering and operations_product.");
            }
        }

        if (signoffPolicy["allowSamePersonTwoHat"]?.GetValue<bool>() != true)
        {
            errors.Add("signoffPolicy.allowSamePersonTwoHat must be true for v1.");
        }

        if (signoffPolicy["requiresTwoHatMarkerWhenSameSigner"]?.GetValue<bool>() != true)
        {
            errors.Add("signoffPolicy.requiresTwoHatMarkerWhenSameSigner must be true.");
        }

        if (signoffPolicy["independentDualControlClaimAllowed"]?.GetValue<bool>() != false)
        {
            errors.Add("signoffPolicy.independentDualControlClaimAllowed must be false for v1.");
        }
    }

    private static void ValidateSignoffs(JsonObject? signoffs, string path, List<string> errors)
    {
        if (signoffs is null)
        {
            errors.Add($"{path} is required.");
            return;
        }

        var engineering = RequireObject(signoffs, "engineering", errors);
        var operations = RequireObject(signoffs, "operationsProduct", errors);
        if (engineering is null || operations is null)
        {
            return;
        }

        ValidateSingleSignoff(engineering, $"{path}.engineering", "engineering", errors);
        ValidateSingleSignoff(operations, $"{path}.operationsProduct", "operations_product", errors);
        if (GetStringOrDefault(engineering, "signerId") == GetStringOrDefault(operations, "signerId") &&
            (engineering["samePersonTwoHat"]?.GetValue<bool>() != true ||
             operations["samePersonTwoHat"]?.GetValue<bool>() != true))
        {
            errors.Add($"{path} uses the same signer and must set samePersonTwoHat on both signoffs.");
        }
    }

    private static void ValidateSingleSignoff(JsonObject signoff, string path, string role, List<string> errors)
    {
        RequireFixed(signoff, "role", role, errors);
        RequireNonEmpty(signoff, "signerId", errors);
        RequireNonEmpty(signoff, "signerName", errors);
        RequireNonEmpty(signoff, "basis", errors);
        if (signoff["signedAt"] is null)
        {
            errors.Add($"{path}.signedAt is required.");
        }
    }

    private static void ValidateExample(JsonObject example, ReadinessRegisterPromotionOptions options, List<string> errors)
    {
        ValidateRegister(example, options, errors);

        var evidenceItems = RequireArray(example, "evidenceItems", errors);
        var exceptions = RequireArray(example, "exceptions", errors);
        if (evidenceItems is null)
        {
            return;
        }

        var states = evidenceItems
            .Select(x => x?.AsObject())
            .Where(x => x is not null)
            .Select(x => GetStringOrDefault(x!, "status"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredState in new[] { "accepted", "blocked", "stale", "rejected" })
        {
            if (requiredState == "stale")
            {
                var hasStale = evidenceItems
                    .Select(x => x?.AsObject())
                    .Where(x => x is not null)
                    .Any(x =>
                        x!["freshness"] is JsonObject freshness &&
                        GetStringOrDefault(freshness, "state").StartsWith("stale_", StringComparison.Ordinal));
                if (!hasStale)
                {
                    errors.Add("readiness-register.example.json must include stale evidence.");
                }

                continue;
            }

            if (!states.Contains(requiredState))
            {
                errors.Add($"readiness-register.example.json must include {requiredState} evidence.");
            }
        }

        if (exceptions is { Count: 0 })
        {
            errors.Add("readiness-register.example.json must include at least one exception.");
        }
    }

    private static List<PromotedFile> BuildPromotedFiles(JsonObject schema, JsonObject register, JsonObject example)
    {
        return
        [
            new(SchemaFileName, "restricted", EncodingWithoutBom(SerializeJson(schema)), "application/schema+json"),
            new(RegisterFileName, "internal", EncodingWithoutBom(SerializeJson(register)), "application/json"),
            new(ExampleFileName, "restricted", EncodingWithoutBom(SerializeJson(example)), "application/json"),
        ];
    }

    private static string GetScorecardMarkdown(JsonObject register, IReadOnlyList<PromotedFile> currentFiles)
    {
        var score = GetRequiredObject(register, "score");
        var generatedViews = GetRequiredObject(register, "generatedViews");
        var claimPolicy = GetRequiredObject(register, "claimPolicy");
        var sb = new StringBuilder();
        AppendGeneratedHeader(sb);
        sb.AppendLine("# HushVoting Readiness Scorecard");
        sb.AppendLine();
        AppendMetadataTable(
            sb,
            ("Register Version", GetRequiredString(register, "registerVersionId")),
            ("Status", GetRequiredString(register, "status")),
            ("Generated At", GetRequiredString(register, "promotedAt")),
            ("Source Commit", GetRequiredString(register, "sourceCommit")),
            ("Parent Epic", GetRequiredString(register, "parentEpic")),
            ("Source Gap Register", GetRequiredString(register, "sourceGapRegister")),
            ("Publication Status", GetRequiredString(generatedViews, "publicSafePublicationStatus")));

        sb.AppendLine("## Score Summary");
        sb.AppendLine();
        sb.AppendLine($"Total score: {GetRequiredInt(score, "total")}/100");
        sb.AppendLine($"Minimum confidence threshold: {GetRequiredInt(score, "minimumConfidenceScore")}");
        sb.AppendLine($"Stronger target threshold: {GetRequiredInt(score, "strongerTargetScore")}");
        sb.AppendLine($"Strongest claim allowed by v1 policy ceiling: {GetRequiredString(claimPolicy, "strongestAllowedV1Claim")}");
        sb.AppendLine($"Current strongest allowed claim: {GetCurrentStrongestAllowedClaim(register)}");
        sb.AppendLine("Current go/no-go result: internal non-binding rehearsal is allowed with limitations; pilot and stronger claims are blocked.");
        sb.AppendLine();

        sb.AppendLine("## Dimension Scores");
        AppendTableHeader(sb, "Dimension ID", "Dimension", "Current", "Target", "Delta To Target", "Primary Gates", "Evidence Count", "Blockers");
        foreach (var dimension in GetRequiredArray(register, "dimensions").Select(x => x!.AsObject()))
        {
            var evidenceCount = GetRequiredArray(dimension, "evidenceIds").Count;
            var current = GetRequiredInt(dimension, "currentScore");
            var target = GetRequiredInt(dimension, "targetScoreBeforeReviewPilot");
            AppendTableRow(
                sb,
                GetRequiredString(dimension, "dimensionId"),
                GetRequiredString(dimension, "name"),
                current.ToString(System.Globalization.CultureInfo.InvariantCulture),
                target.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Math.Max(target - current, 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                JoinArray(dimension, "acceptanceGateIds"),
                evidenceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                JoinArray(dimension, "blockerIds"));
        }

        sb.AppendLine();
        sb.AppendLine("## Claim Gates");
        AppendTableHeader(sb, "Claim Level", "Severity", "Status", "Allowed Wording", "Limitation Wording", "Blocked Wording", "Blocker IDs");
        foreach (var claim in GetRequiredArray(register, "claimLevels").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(claim, "claimLevel"),
                GetRequiredString(claim, "blockerSeverity"),
                GetRequiredString(claim, "status"),
                GetRequiredString(claim, "allowedWording"),
                GetRequiredString(claim, "limitationWording"),
                GetRequiredString(claim, "blockedWording"),
                JoinArray(claim, "blockerIds"));
        }

        sb.AppendLine();
        sb.AppendLine("## Active Blockers");
        AppendTableHeader(sb, "Blocker ID", "Claim Level", "Severity", "Status", "Feature", "Gates", "Resolution Criteria");
        foreach (var blocker in GetRequiredArray(register, "blockers").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(blocker, "blockerId"),
                GetRequiredString(blocker, "claimLevel"),
                GetRequiredString(blocker, "severity"),
                GetRequiredString(blocker, "status"),
                GetRequiredString(blocker, "featureId"),
                JoinArray(blocker, "acceptanceGateIds"),
                GetRequiredString(blocker, "resolutionCriteria"));
        }

        sb.AppendLine();
        sb.AppendLine("## Score Changes");
        AppendTableHeader(sb, "Score Change ID", "Dimension ID", "Direction", "Previous", "Proposed", "Accepted", "Evidence IDs", "Reason");
        foreach (var scoreChange in GetRequiredArray(register, "scoreChanges").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(scoreChange, "scoreChangeId"),
                GetRequiredString(scoreChange, "dimensionId"),
                GetRequiredString(scoreChange, "direction"),
                GetRequiredInt(scoreChange, "previousScore").ToString(System.Globalization.CultureInfo.InvariantCulture),
                GetRequiredInt(scoreChange, "proposedScore").ToString(System.Globalization.CultureInfo.InvariantCulture),
                GetRequiredInt(scoreChange, "acceptedScore").ToString(System.Globalization.CultureInfo.InvariantCulture),
                JoinArray(scoreChange, "evidenceIds"),
                GetRequiredString(scoreChange, "reason"));
        }

        sb.AppendLine();
        sb.AppendLine("## Evidence Status");
        AppendTableHeader(sb, "Evidence ID", "Feature", "Gates", "Dimensions", "Status", "Visibility", "Freshness", "Claim Effect");
        foreach (var evidence in GetRequiredArray(register, "evidenceItems").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(evidence, "evidenceId"),
                GetRequiredString(evidence, "featureId"),
                JoinArray(evidence, "acceptanceGateIds"),
                JoinArray(evidence, "dimensionIds"),
                GetRequiredString(evidence, "status"),
                GetRequiredString(evidence, "visibility"),
                GetRequiredString(GetRequiredObject(evidence, "freshness"), "state"),
                GetRequiredString(evidence, "claimEffect"));
        }

        sb.AppendLine();
        sb.AppendLine("## Exceptions And Rejections");
        sb.AppendLine();
        sb.AppendLine("Exceptions:");
        AppendTableHeader(sb, "Exception ID", "Type", "Status", "Severity", "Reason");
        foreach (var exception in GetRequiredArray(register, "exceptions").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(exception, "exceptionId"),
                GetRequiredString(exception, "type"),
                GetRequiredString(exception, "status"),
                GetRequiredString(exception, "severity"),
                GetRequiredString(exception, "reason"));
        }

        sb.AppendLine();
        sb.AppendLine("Rejected evidence:");
        AppendTableHeader(sb, "Evidence ID", "Feature", "Reason");
        foreach (var evidence in GetRequiredArray(register, "evidenceItems").Select(x => x!.AsObject()).Where(x => GetRequiredString(x, "status") == "rejected"))
        {
            AppendTableRow(sb, GetRequiredString(evidence, "evidenceId"), GetRequiredString(evidence, "featureId"), GetRequiredString(evidence, "residualRisk"));
        }

        sb.AppendLine();
        sb.AppendLine("## Residual Risk");
        AppendTableHeader(sb, "Dimension ID", "Residual Risk", "Related Evidence", "Related Blockers");
        foreach (var dimension in GetRequiredArray(register, "dimensions").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(dimension, "dimensionId"),
                GetRequiredString(dimension, "residualRisk"),
                JoinArray(dimension, "evidenceIds"),
                JoinArray(dimension, "blockerIds"));
        }

        sb.AppendLine();
        sb.AppendLine("## Signoff Summary");
        AppendTableHeader(sb, "Evidence/Score Item", "Engineering Signer", "Operations/Product Signer", "Same Person / Two Hats", "Signed At");
        foreach (var evidence in GetRequiredArray(register, "evidenceItems").Select(x => x!.AsObject()))
        {
            AppendSignoffRow(sb, GetRequiredString(evidence, "evidenceId"), GetRequiredObject(evidence, "signoffs"));
        }

        foreach (var scoreChange in GetRequiredArray(register, "scoreChanges").Select(x => x!.AsObject()))
        {
            AppendSignoffRow(sb, GetRequiredString(scoreChange, "scoreChangeId"), GetRequiredObject(scoreChange, "signoffs"));
        }

        sb.AppendLine();
        sb.AppendLine("## Generated Artifacts");
        AppendTableHeader(sb, "Artifact", "Visibility", "SHA-256", "Size Bytes");
        foreach (var file in currentFiles.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            AppendTableRow(sb, file.RelativePath, file.Visibility, ComputeSha256Hex(file.Bytes), file.Bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return NormalizeLineEndings(sb.ToString());
    }

    private static string GetRestrictedReviewerExtractMarkdown(JsonObject register)
    {
        var sb = new StringBuilder();
        AppendGeneratedHeader(sb);
        sb.AppendLine("# HushVoting Restricted Readiness Reviewer Extract");
        sb.AppendLine();
        AppendMetadataTable(
            sb,
            ("Register Version", GetRequiredString(register, "registerVersionId")),
            ("Status", GetRequiredString(register, "status")),
            ("Generated Time", GetRequiredString(register, "promotedAt")),
            ("Source Commit", GetRequiredString(register, "sourceCommit")),
            ("Reviewer Scope", "restricted reviewer navigation; full private artifact access remains controlled"));

        sb.AppendLine("## Readiness Score");
        sb.AppendLine();
        sb.AppendLine($"Total readiness score: {GetRequiredInt(GetRequiredObject(register, "score"), "total")}/100");
        AppendTableHeader(sb, "Dimension ID", "Dimension", "Score", "Target");
        foreach (var dimension in GetRequiredArray(register, "dimensions").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(dimension, "dimensionId"),
                GetRequiredString(dimension, "name"),
                GetRequiredInt(dimension, "currentScore").ToString(System.Globalization.CultureInfo.InvariantCulture),
                GetRequiredInt(dimension, "targetScoreBeforeReviewPilot").ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        sb.AppendLine();
        sb.AppendLine("## Claim And Blocker Status");
        AppendTableHeader(sb, "Claim Level", "Severity", "Status", "Blockers");
        foreach (var claim in GetRequiredArray(register, "claimLevels").Select(x => x!.AsObject()))
        {
            AppendTableRow(sb, GetRequiredString(claim, "claimLevel"), GetRequiredString(claim, "blockerSeverity"), GetRequiredString(claim, "status"), JoinArray(claim, "blockerIds"));
        }

        sb.AppendLine();
        AppendTableHeader(sb, "Blocker ID", "Feature", "Gates", "Resolution Criteria");
        foreach (var blocker in GetRequiredArray(register, "blockers").Select(x => x!.AsObject()))
        {
            AppendTableRow(sb, GetRequiredString(blocker, "blockerId"), GetRequiredString(blocker, "featureId"), JoinArray(blocker, "acceptanceGateIds"), GetRequiredString(blocker, "resolutionCriteria"));
        }

        sb.AppendLine();
        sb.AppendLine("## Evidence Index");
        AppendTableHeader(sb, "Evidence ID", "Feature", "Gate", "Dimension", "Visibility", "Restricted Ref", "SHA-256", "Status", "Freshness");
        foreach (var evidence in GetRequiredArray(register, "evidenceItems").Select(x => x!.AsObject()))
        {
            var firstArtifact = GetRequiredArray(evidence, "artifactRefs").Select(x => x?.AsObject()).FirstOrDefault(x => x is not null);
            AppendTableRow(
                sb,
                GetRequiredString(evidence, "evidenceId"),
                GetRequiredString(evidence, "featureId"),
                JoinArray(evidence, "acceptanceGateIds"),
                JoinArray(evidence, "dimensionIds"),
                GetRequiredString(evidence, "visibility"),
                firstArtifact is null ? "controlled-access-index" : GetRequiredString(firstArtifact, "relativePath"),
                firstArtifact is null ? "not-applicable" : GetRequiredString(firstArtifact, "sha256Hash"),
                GetRequiredString(evidence, "status"),
                GetRequiredString(GetRequiredObject(evidence, "freshness"), "state"));
        }

        sb.AppendLine();
        sb.AppendLine("## Score-Change History");
        AppendTableHeader(sb, "Score Change ID", "Dimension", "Accepted", "Reason");
        foreach (var scoreChange in GetRequiredArray(register, "scoreChanges").Select(x => x!.AsObject()))
        {
            AppendTableRow(
                sb,
                GetRequiredString(scoreChange, "scoreChangeId"),
                GetRequiredString(scoreChange, "dimensionId"),
                GetRequiredInt(scoreChange, "acceptedScore").ToString(System.Globalization.CultureInfo.InvariantCulture),
                GetRequiredString(scoreChange, "reason"));
        }

        sb.AppendLine();
        sb.AppendLine("## Signoff Summary");
        AppendTableHeader(sb, "Item", "Engineering", "Operations/Product", "Same Person / Two Hats", "Signed At");
        foreach (var evidence in GetRequiredArray(register, "evidenceItems").Select(x => x!.AsObject()))
        {
            AppendSignoffRow(sb, GetRequiredString(evidence, "evidenceId"), GetRequiredObject(evidence, "signoffs"));
        }

        sb.AppendLine();
        sb.AppendLine("## Exceptions, Stale Evidence, Rejections, And Superseded Evidence");
        sb.AppendLine();
        sb.AppendLine("Exceptions, stale evidence, rejected evidence, and superseded evidence are listed by stable id for controlled review.");
        sb.AppendLine();

        sb.AppendLine("## Public-Safe Summary Preview");
        sb.AppendLine();
        sb.AppendLine(GetPublicSafeSummaryBody(register));
        sb.AppendLine();

        sb.AppendLine("## Omitted Private Artifacts");
        AppendTableHeader(sb, "Artifact Category", "Reason Omitted", "How Reviewer Requests Access");
        AppendTableRow(sb, "Raw support logs", "May contain private support context", "Request controlled evidence export from the readiness owner");
        AppendTableRow(sb, "Raw anomaly detail", "May expose private election/customer information", "Request EPIC-014 governed evidence package");
        AppendTableRow(sb, "Operational deployment detail", "Could weaken security posture", "Request restricted operations walkthrough");
        sb.AppendLine();

        sb.AppendLine("## Controlled Evidence Access");
        sb.AppendLine();
        sb.AppendLine("Reviewer access is handled by the readiness owner using controlled private artifact paths referenced by the manifest.");
        return NormalizeLineEndings(sb.ToString());
    }

    private static string GetPublicSafeSummaryMarkdown(JsonObject register)
    {
        var sb = new StringBuilder();
        AppendGeneratedHeader(sb);
        sb.AppendLine("# HushVoting Public-Safe Readiness Summary");
        sb.AppendLine();
        AppendMetadataTable(
            sb,
            ("Register Version", GetRequiredString(register, "registerVersionId")),
            ("Generated At", GetRequiredString(register, "promotedAt")),
            ("Publication Status", GetRequiredString(GetRequiredObject(register, "generatedViews"), "publicSafePublicationStatus")));
        sb.Append(GetPublicSafeSummaryBody(register));
        return NormalizeLineEndings(sb.ToString());
    }

    private static string GetPublicSafeSummaryBody(JsonObject register)
    {
        var sb = new StringBuilder();
        var publicationStatus = GetRequiredString(GetRequiredObject(register, "generatedViews"), "publicSafePublicationStatus");
        sb.AppendLine("## Current Public-Safe Status");
        sb.AppendLine();
        sb.AppendLine(publicationStatus);
        sb.AppendLine();
        sb.AppendLine("## Approved Public-Safe Claim Wording");
        sb.AppendLine();
        sb.AppendLine("HushVoting is being prepared for internal non-binding rehearsal use only. Pilot, production, and public election readiness claims remain unavailable until the remaining readiness blockers are resolved and accepted.");
        sb.AppendLine();
        sb.AppendLine("## Known Limitations");
        sb.AppendLine();
        sb.AppendLine("- Internal rehearsal use must be labelled non-binding.");
        sb.AppendLine("- Pilot readiness remains blocked until the minimum confidence band and remaining pilot-critical evidence gates are satisfied.");
        sb.AppendLine("- Production and public/state election readiness are not claimed in this version.");
        sb.AppendLine();
        sb.AppendLine("## Non-Claims");
        sb.AppendLine();
        sb.AppendLine("- This summary is not certification, legal approval, public election authorization, or independent validation.");
        sb.AppendLine("- This summary does not publish private readiness scoring or restricted evidence.");
        sb.AppendLine();
        sb.AppendLine("## Public-Safe Evidence Categories");
        sb.AppendLine();
        sb.AppendLine("- Protocol package and verifier documentation.");
        sb.AppendLine("- Operational readiness categories under active development.");
        sb.AppendLine("- Controlled reviewer extracts available through private review channels.");
        sb.AppendLine();
        sb.AppendLine("## Contact / Review Path");
        sb.AppendLine();
        sb.AppendLine("Contact the HushVoting readiness owner for controlled reviewer access and current readiness package details.");
        return NormalizeLineEndings(sb.ToString());
    }

    private static void AppendGeneratedHeader(StringBuilder sb)
    {
        sb.AppendLine("<!-- Generated by ReadinessRegisterPromoter. Do not edit by hand. -->");
        sb.AppendLine();
    }

    private static void AppendMetadataTable(StringBuilder sb, params (string Label, string Value)[] rows)
    {
        AppendTableHeader(sb, "Field", "Value");
        foreach (var row in rows)
        {
            AppendTableRow(sb, row.Label, row.Value);
        }

        sb.AppendLine();
    }

    private static void AppendTableHeader(StringBuilder sb, params string[] columns)
    {
        sb.AppendLine("| " + string.Join(" | ", columns) + " |");
        sb.AppendLine("| " + string.Join(" | ", columns.Select(_ => "---")) + " |");
    }

    private static void AppendTableRow(StringBuilder sb, params string[] values)
    {
        sb.AppendLine("| " + string.Join(" | ", values.Select(EscapeMarkdownTableValue)) + " |");
    }

    private static void AppendSignoffRow(StringBuilder sb, string itemId, JsonObject signoffs)
    {
        var engineering = GetRequiredObject(signoffs, "engineering");
        var operations = GetRequiredObject(signoffs, "operationsProduct");
        AppendTableRow(
            sb,
            itemId,
            GetRequiredString(engineering, "signerName"),
            GetRequiredString(operations, "signerName"),
            GetBoolOrDefault(engineering, "samePersonTwoHat") || GetBoolOrDefault(operations, "samePersonTwoHat") ? "yes" : "no",
            GetRequiredString(engineering, "signedAt"));
    }

    private static string EscapeMarkdownTableValue(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

    private static string JoinArray(JsonObject item, string propertyName) =>
        string.Join(", ", GetRequiredArray(item, propertyName).Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)));

    private static void ValidateGeneratedViews(IReadOnlyList<PromotedFile> promotedFiles, List<string> errors)
    {
        var publicSummary = Encoding.UTF8.GetString(promotedFiles.Single(x => x.RelativePath == PublicSafeSummaryFileName).Bytes);
        foreach (var forbidden in PublicForbiddenTerms)
        {
            if (publicSummary.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"public-safe-summary.md contains forbidden public-safe term: {forbidden}.");
            }
        }

        if (!publicSummary.Contains("## Current Public-Safe Status", StringComparison.Ordinal) ||
            !publicSummary.Contains("## Non-Claims", StringComparison.Ordinal))
        {
            errors.Add("public-safe-summary.md is missing required sections.");
        }

        var restricted = Encoding.UTF8.GetString(promotedFiles.Single(x => x.RelativePath == RestrictedReviewerExtractFileName).Bytes);
        foreach (var forbidden in new[] { "BEGIN PRIVATE KEY", "password=", "secret=", "credential=" })
        {
            if (restricted.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"restricted-reviewer-extract.md contains forbidden secret marker: {forbidden}.");
            }
        }
    }

    private static JsonObject BuildManifest(
        JsonObject register,
        DateTimeOffset generatedAt,
        IReadOnlyList<PromotedFile> files,
        string archiveFileName,
        long archiveSizeBytes,
        string archiveHash,
        string? manifestHash)
    {
        var fileNodes = new JsonArray();
        foreach (var file in files.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            fileNodes.Add(new JsonObject
            {
                ["relativePath"] = file.RelativePath,
                ["visibility"] = file.Visibility,
                ["sha256Hash"] = ComputeSha256Hex(file.Bytes),
                ["hashAlgorithm"] = "SHA-256",
                ["mediaType"] = file.MediaType,
                ["sizeBytes"] = file.Bytes.Length,
            });
        }

        return new JsonObject
        {
            ["manifestVersion"] = "1.0",
            ["registerId"] = GetRequiredString(register, "registerId"),
            ["registerVersion"] = GetRequiredString(register, "registerVersion"),
            ["registerVersionId"] = GetRequiredString(register, "registerVersionId"),
            ["status"] = GetRequiredString(register, "status"),
            ["generatedAt"] = generatedAt.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["sourceCommit"] = GetRequiredString(register, "sourceCommit"),
            ["totalScore"] = GetRequiredInt(GetRequiredObject(register, "score"), "total"),
            ["strongestAllowedClaim"] = GetCurrentStrongestAllowedClaim(register),
            ["strongestAllowedV1PolicyCeiling"] = GetRequiredString(GetRequiredObject(register, "claimPolicy"), "strongestAllowedV1Claim"),
            ["publicationStatus"] = GetRequiredString(GetRequiredObject(register, "generatedViews"), "publicSafePublicationStatus"),
            ["archive"] = new JsonObject
            {
                ["fileName"] = archiveFileName,
                ["sha256Hash"] = archiveHash,
                ["hashAlgorithm"] = "SHA-256",
                ["sizeBytes"] = archiveSizeBytes,
            },
            ["files"] = fileNodes,
            ["manifestHash"] = manifestHash,
        };
    }

    private static void EnsureCatalogAllowsPromotion(
        string catalogPath,
        string registerVersionId,
        string manifestHash,
        string archiveHash)
    {
        if (!File.Exists(catalogPath))
        {
            return;
        }

        var catalog = ReadJsonObject(catalogPath, CatalogFileName);
        if (catalog["entries"] is not JsonArray entries)
        {
            return;
        }

        foreach (var entry in entries.Select(x => x?.AsObject()).Where(x => x is not null))
        {
            if (GetStringOrDefault(entry!, "registerVersionId") != registerVersionId)
            {
                continue;
            }

            if (GetStringOrDefault(entry!, "manifestHash") != manifestHash ||
                GetStringOrDefault(entry!, "archiveHash") != archiveHash)
            {
                throw new ReadinessRegisterPromotionException(
                    "Readiness register catalog already contains this version with different hashes.",
                    [registerVersionId]);
            }
        }
    }

    private static void WritePromotedArtifacts(
        ReadinessRegisterPromotionPaths paths,
        string versionOutputRoot,
        IReadOnlyList<PromotedFile> files,
        string archiveFileName,
        byte[] archiveBytes,
        JsonObject manifest,
        List<string> writtenFiles)
    {
        Directory.CreateDirectory(versionOutputRoot);
        foreach (var file in files.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            var outputPath = Path.Combine(versionOutputRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, file.Bytes);
            writtenFiles.Add(outputPath);
        }

        var archivePath = Path.Combine(versionOutputRoot, archiveFileName);
        File.WriteAllBytes(archivePath, archiveBytes);
        writtenFiles.Add(archivePath);

        Directory.CreateDirectory(paths.OutputRoot);
        var catalogPath = paths.CatalogPath;
        var catalog = File.Exists(catalogPath)
            ? ReadJsonObject(catalogPath, CatalogFileName)
            : new JsonObject
            {
                ["catalogVersion"] = "1.0",
                ["registerId"] = manifest["registerId"]?.DeepClone(),
                ["entries"] = new JsonArray(),
            };

        var entries = catalog["entries"] as JsonArray ?? [];
        catalog["entries"] = entries;
        var registerVersionId = GetRequiredString(manifest, "registerVersionId");
        var existing = entries
            .Select(x => x?.AsObject())
            .FirstOrDefault(x => x is not null && GetStringOrDefault(x, "registerVersionId") == registerVersionId);
        var entryNode = new JsonObject
        {
            ["registerVersion"] = manifest["registerVersion"]?.DeepClone(),
            ["registerVersionId"] = registerVersionId,
            ["status"] = manifest["status"]?.DeepClone(),
            ["generatedAt"] = manifest["generatedAt"]?.DeepClone(),
            ["totalScore"] = manifest["totalScore"]?.DeepClone(),
            ["strongestAllowedClaim"] = manifest["strongestAllowedClaim"]?.DeepClone(),
            ["strongestAllowedV1PolicyCeiling"] = manifest["strongestAllowedV1PolicyCeiling"]?.DeepClone(),
            ["publicationStatus"] = manifest["publicationStatus"]?.DeepClone(),
            ["manifestHash"] = manifest["manifestHash"]?.DeepClone(),
            ["archiveHash"] = manifest["archive"]?["sha256Hash"]?.DeepClone(),
            ["versionPath"] = Path.GetFileName(versionOutputRoot),
        };

        if (existing is null)
        {
            entries.Add(entryNode);
        }
        else
        {
            var index = entries.IndexOf(existing);
            entries[index] = entryNode;
        }

        if (GetRequiredString(manifest, "status") is "AcceptedInternal" or "ReviewerReady")
        {
            catalog["currentRegisterVersionId"] = registerVersionId;
            catalog["currentRegisterVersion"] = manifest["registerVersion"]?.DeepClone();
            catalog["currentManifestHash"] = manifest["manifestHash"]?.DeepClone();
            catalog["currentArchiveHash"] = manifest["archive"]?["sha256Hash"]?.DeepClone();
        }

        File.WriteAllText(catalogPath, SerializeJson(catalog), new UTF8Encoding(false));
        writtenFiles.Add(catalogPath);
    }

    private static byte[] BuildDeterministicArchive(IReadOnlyList<PromotedFile> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(file.RelativePath.Replace('\\', '/'), CompressionLevel.NoCompression);
                entry.LastWriteTime = FixedZipTimestamp;
                using var entryStream = entry.Open();
                entryStream.Write(file.Bytes, 0, file.Bytes.Length);
            }
        }

        return stream.ToArray();
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SerializeJson(JsonNode node) => NormalizeLineEndings(node.ToJsonString(ReadableJsonOptions)) + "\n";

    private static byte[] EncodingWithoutBom(string value) => new UTF8Encoding(false).GetBytes(NormalizeLineEndings(value));

    private static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static string GetCurrentStrongestAllowedClaim(JsonObject register)
    {
        var allowedStatuses = new HashSet<string>(["allowed", "allowed_with_limitations"], StringComparer.Ordinal);
        var claimRank = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["internal_development"] = 0,
            ["internal_non_binding_rehearsal"] = 1,
            ["friendly_organization_pilot"] = 2,
            ["production_organizational_rollout"] = 3,
            ["public_or_state_election"] = 4,
        };

        return GetRequiredArray(register, "claimLevels")
            .Select(x => x!.AsObject())
            .Where(claim =>
                allowedStatuses.Contains(GetRequiredString(claim, "status")) &&
                GetRequiredString(claim, "blockerSeverity") != "red")
            .OrderByDescending(claim => claimRank.GetValueOrDefault(GetRequiredString(claim, "claimLevel"), -1))
            .Select(claim => GetRequiredString(claim, "claimLevel"))
            .FirstOrDefault() ?? "none";
    }

    private static void ScaffoldMissingSourceFiles(ReadinessRegisterPromotionPaths paths)
    {
        Directory.CreateDirectory(paths.SourceRoot);
        if (!File.Exists(paths.SchemaPath))
        {
            File.WriteAllText(paths.SchemaPath, "{\n  \"$schema\": \"https://json-schema.org/draft/2020-12/schema\"\n}\n", new UTF8Encoding(false));
        }

        if (!File.Exists(paths.RegisterPath))
        {
            File.WriteAllText(paths.RegisterPath, "{\n  \"schemaVersion\": \"1.0\"\n}\n", new UTF8Encoding(false));
        }

        if (!File.Exists(paths.ExamplePath))
        {
            File.WriteAllText(paths.ExamplePath, "{\n  \"evidenceItems\": [],\n  \"exceptions\": []\n}\n", new UTF8Encoding(false));
        }
    }

    private static void RequireFixed(JsonObject item, string propertyName, string expected, List<string> errors)
    {
        if (GetStringOrDefault(item, propertyName) != expected)
        {
            errors.Add($"{propertyName} must be {expected}.");
        }
    }

    private static void RequirePattern(JsonObject item, string propertyName, Regex pattern, List<string> errors)
    {
        var value = GetStringOrDefault(item, propertyName);
        if (string.IsNullOrWhiteSpace(value) || !pattern.IsMatch(value))
        {
            errors.Add($"{propertyName} has invalid format.");
        }
    }

    private static void RequireEnum(JsonObject item, string propertyName, HashSet<string> allowed, List<string> errors)
    {
        var value = GetStringOrDefault(item, propertyName);
        if (string.IsNullOrWhiteSpace(value) || !allowed.Contains(value))
        {
            errors.Add($"{propertyName} has unsupported value {value}.");
        }
    }

    private static void RequireNonEmpty(JsonObject item, string propertyName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(GetStringOrDefault(item, propertyName)))
        {
            errors.Add($"{propertyName} is required.");
        }
    }

    private static JsonObject? RequireObject(JsonObject item, string propertyName, List<string> errors)
    {
        if (item[propertyName] is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"{propertyName} must be an object.");
        return null;
    }

    private static JsonArray? RequireArray(JsonObject item, string propertyName, List<string> errors)
    {
        if (item[propertyName] is JsonArray array)
        {
            return array;
        }

        errors.Add($"{propertyName} must be an array.");
        return null;
    }

    private static int RequireInt(JsonObject item, string propertyName, List<string> errors)
    {
        if (item[propertyName] is JsonValue value &&
            value.TryGetValue<int>(out var result))
        {
            return result;
        }

        errors.Add($"{propertyName} must be an integer.");
        return 0;
    }

    private static JsonObject GetRequiredObject(JsonObject item, string propertyName) => item[propertyName]!.AsObject();
    private static JsonArray GetRequiredArray(JsonObject item, string propertyName) => item[propertyName]!.AsArray();
    private static string GetRequiredString(JsonObject item, string propertyName) => item[propertyName]!.GetValue<string>();
    private static int GetRequiredInt(JsonObject item, string propertyName) => item[propertyName]!.GetValue<int>();

    private static string GetStringOrDefault(JsonObject? item, string propertyName) =>
        item is not null && item[propertyName] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : string.Empty;

    private static int GetIntOrDefault(JsonObject item, string propertyName) =>
        item[propertyName] is JsonValue value && value.TryGetValue<int>(out var result)
            ? result
            : 0;

    private static bool GetBoolOrDefault(JsonObject item, string propertyName) =>
        item[propertyName] is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private sealed record PromotedFile(
        string RelativePath,
        string Visibility,
        byte[] Bytes,
        string MediaType);
}
