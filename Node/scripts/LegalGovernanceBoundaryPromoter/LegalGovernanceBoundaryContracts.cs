using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LegalGovernanceBoundaryPromoter;

public sealed record LegalGovernanceBoundaryPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string OutputRoot)
{
    public const string SourceFolder = "Legal-Governance-Boundary";
    public const string SourceFileName = "legal-governance-boundary-source.json";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);

    public static LegalGovernanceBoundaryPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            SourceFolder);
        return new LegalGovernanceBoundaryPromotionPaths(
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

public sealed record LegalGovernanceBoundaryMaterialFinding(
    string RelativePath,
    string Category,
    string Evidence);

public sealed record LegalGovernanceBoundaryGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash);

public sealed record LegalGovernanceBoundaryGeneratedPackage(
    string Status,
    IReadOnlyList<LegalGovernanceBoundaryGeneratedArtifact> Artifacts,
    IReadOnlyList<LegalGovernanceBoundaryMaterialFinding> PublicForbiddenFindings,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Downgrades);

public sealed class LegalGovernanceBoundaryPromotionException : Exception
{
    public LegalGovernanceBoundaryPromotionException(string message, IEnumerable<string>? details = null)
        : base(message)
    {
        Details = details?.ToArray() ?? [];
    }

    public IReadOnlyList<string> Details { get; }
}

public static class LegalGovernanceBoundaryContracts
{
    public const string FeatureId = "FEAT-140";
    public const string AcceptanceGate = "AT-RDY-012";
    public const string ReadinessFragmentId = "RDY-EVID-AT-RDY-012-FEAT-140-001";
    public const string DimensionId = "RDY-DIM-010";
    public const string CanonicalizationVersion = "feat140-legal-governance-boundary-canonical-json-v1";
    public const string RequiredDisclaimer =
        "Customer-supplied governance boundary recorded. HushVoting can serve as the voting subsystem for binding customer decisions when customer-supplied authority and rules support that use. HushVoting has not validated legal sufficiency. Missing or deferred customer-owned inputs may block or downgrade readiness claims.";

    public static readonly string[] RequiredSchemaFiles =
    [
        "legal-governance-boundary-source.schema.json",
        "legal-governance-boundary-package.schema.json",
        "legal-governance-boundary-readiness-fragment.schema.json",
        "legal-governance-boundary-claim-impact-matrix.schema.json",
        "legal-governance-boundary-restricted-index.schema.json",
        "legal-governance-boundary-feat139-handoff.schema.json",
        "legal-governance-boundary-feat146-handoff.schema.json",
        "legal-governance-boundary-downstream-handoff.schema.json",
        "legal-governance-boundary-package-hash-validation.schema.json",
    ];

    public static readonly string[] RequiredOutputFiles =
    [
        LegalGovernanceBoundaryArtifactGenerator.ReadinessFragmentPath,
        LegalGovernanceBoundaryArtifactGenerator.PackagePath,
        LegalGovernanceBoundaryArtifactGenerator.ClaimImpactMatrixPath,
        LegalGovernanceBoundaryArtifactGenerator.RestrictedIndexPath,
        LegalGovernanceBoundaryArtifactGenerator.PublicSafeSummaryPath,
        LegalGovernanceBoundaryArtifactGenerator.Feat139HandoffPath,
        LegalGovernanceBoundaryArtifactGenerator.Feat146HandoffPath,
        LegalGovernanceBoundaryArtifactGenerator.DownstreamHandoffPath,
        LegalGovernanceBoundaryArtifactGenerator.PackageHashValidationPath,
    ];

    public static readonly string[] RequiredInputIds =
    [
        "customer_organization",
        "election_authority",
        "setup_signer",
        "governing_rule_ref",
        "notice_rule",
        "eligibility_rule",
        "quorum_rule",
        "proxy_delegation_rule",
        "minutes_reporting_rule",
        "challenge_window",
        "challenge_process",
        "remedy_authority",
        "finality_rule",
    ];

    public static readonly string[] RequiredScenarioIds =
    [
        "all_required_governance_inputs_provided",
        "authority_missing",
        "governing_rule_ref_missing",
        "quorum_deferred",
        "proxy_delegation_not_applicable",
        "challenge_window_missing",
        "remedy_authority_missing",
        "finality_rule_missing",
        "governance_evidence_stale",
    ];

    public static readonly string[] RequiredFeat139BlockerIds =
    [
        "FEAT139-GOVERNED-FAILED-FINALIZE-MISSING",
        "FEAT139-GOVERNED-FINALIZED-WITH-ANOMALY-MISSING",
        "FEAT139-UNRESOLVED-BLOCKING-ANOMALY",
        "FEAT139-PACKAGE-ARTIFACT-MISMATCH",
    ];

    private static readonly HashSet<string> AllowedItemStatuses = new(StringComparer.Ordinal)
    {
        "provided",
        "not_provided",
        "not_applicable",
        "customer_deferred",
        "stale_or_superseded",
    };

    private static readonly HashSet<string> AllowedOwners = new(StringComparer.Ordinal)
    {
        "customer",
        "hush",
        "shared_boundary",
    };

    private static readonly HashSet<string> AllowedDecisions = new(StringComparer.Ordinal)
    {
        "allow",
        "allow_with_limitations",
        "downgrade",
        "block",
        "not_in_scope",
    };

    private static readonly HashSet<string> AllowedFeat139Classifications = new(StringComparer.Ordinal)
    {
        "governance_boundary_cleared",
        "governance_boundary_missing",
        "runtime_outcome_evidence_required",
        "package_regeneration_required",
        "not_in_scope",
    };

    public static JsonObject LoadSource(
        LegalGovernanceBoundaryPromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "legal governance boundary source");
        return ReadJsonObject(sourcePath, LegalGovernanceBoundaryPromotionPaths.SourceFileName);
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
        var errors = ValidateJsonRequired(source, LegalGovernanceBoundaryPromotionPaths.SourceFileName, [
            "schemaVersion",
            "sourceId",
            "featureId",
            "acceptanceGate",
            "sourceGap",
            "status",
            "generatedAt",
            "currentReadinessRegister",
            "customerScope",
            "authorityBoundary",
            "evidenceRefs",
            "governanceInputs",
            "claimImpactScenarios",
            "feat139BlockerMappings",
            "feat146AuthorityInputs",
            "publicSummary",
            "publicArtifactSamples",
        ]).ToList();

        RequireValue(source, "featureId", FeatureId, errors);
        RequireValue(source, "acceptanceGate", AcceptanceGate, errors);
        RequireNonEmptyArray(source, "evidenceRefs", errors);
        RequireNonEmptyArray(source, "governanceInputs", errors);
        RequireNonEmptyArray(source, "claimImpactScenarios", errors);
        RequireNonEmptyArray(source, "feat139BlockerMappings", errors);
        RequireNonEmptyArray(source, "feat146AuthorityInputs", errors);

        if (source.TryGetPropertyValue("currentReadinessRegister", out var registerNode) &&
            registerNode is JsonObject register)
        {
            RequireValue(register, "dimensionId", DimensionId, errors);
            RequireValue(register, "evidenceId", ReadinessFragmentId, errors);
        }

        ValidateAuthorityBoundary(source, errors);
        ValidateGovernanceInputs(source, errors);
        ValidateClaimImpactScenarios(source, errors);
        ValidateFeat139Mappings(source, errors);
        ValidateFeat146Inputs(source, errors);
        ValidatePublicSummary(source, errors);

        return errors;
    }

    public static IReadOnlyList<LegalGovernanceBoundaryMaterialFinding> ScanForbiddenPublicMaterial(
        JsonObject source,
        IEnumerable<(string Path, string Content)> generatedPublicArtifacts)
    {
        var findings = new List<LegalGovernanceBoundaryMaterialFinding>();
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
            throw new LegalGovernanceBoundaryPromotionException($"{label} is not a JSON object.");
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

        throw new LegalGovernanceBoundaryPromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new LegalGovernanceBoundaryPromotionException($"Missing array property: {property}");
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
            throw new LegalGovernanceBoundaryPromotionException(
                "Legal governance boundary path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    public static string ResolveSourceInput(
        LegalGovernanceBoundaryPromotionPaths paths,
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
            ? Path.Combine(fullPath, LegalGovernanceBoundaryPromotionPaths.SourceFileName)
            : fullPath;
    }

    private static void ValidateAuthorityBoundary(JsonObject source, List<string> errors)
    {
        var boundary = source.TryGetPropertyValue("authorityBoundary", out var node)
            ? node as JsonObject
            : null;
        if (boundary is null)
        {
            errors.Add("authorityBoundary must be an object.");
            return;
        }

        foreach (var property in new[]
        {
            "authorityActorRef",
            "authorityRole",
            "setupSignerRef",
            "acknowledgementRef",
            "publicSafeSummary",
            "nonLegalValidationWording",
        })
        {
            if (string.IsNullOrWhiteSpace(GetString(boundary, property)))
            {
                errors.Add($"authorityBoundary.{property} is required.");
            }
        }

        if (!GetString(boundary, "nonLegalValidationWording").Contains(
                "HushVoting has not validated legal sufficiency",
                StringComparison.Ordinal))
        {
            errors.Add("authorityBoundary.nonLegalValidationWording must include the legal sufficiency disclaimer.");
        }
    }

    private static void ValidateGovernanceInputs(JsonObject source, List<string> errors)
    {
        var inputIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in RequireArray(source, "governanceInputs").OfType<JsonObject>())
        {
            var inputId = GetString(input, "inputId");
            if (!string.IsNullOrWhiteSpace(inputId))
            {
                inputIds.Add(inputId);
            }

            foreach (var property in new[]
            {
                "inputId",
                "label",
                "status",
                "statusReason",
                "owner",
                "evidenceRef",
                "publicSafeSummary",
                "hushConfigurationImpact",
            })
            {
                if (string.IsNullOrWhiteSpace(GetString(input, property)))
                {
                    errors.Add($"Governance input {inputId} is missing {property}.");
                }
            }

            var status = GetString(input, "status");
            if (!AllowedItemStatuses.Contains(status))
            {
                errors.Add($"Governance input {inputId} has unsupported status {status}.");
            }

            var owner = GetString(input, "owner");
            if (!AllowedOwners.Contains(owner))
            {
                errors.Add($"Governance input {inputId} has unsupported owner {owner}.");
            }

            if (status == "not_applicable" && string.IsNullOrWhiteSpace(GetString(input, "statusReason")))
            {
                errors.Add($"Governance input {inputId} is not_applicable without a reason.");
            }

            if (GetStringArray(input, "affectedClaims").Count == 0)
            {
                errors.Add($"Governance input {inputId} must define affectedClaims.");
            }

            if (GetStringArray(input, "staleTriggers").Count == 0)
            {
                errors.Add($"Governance input {inputId} must define staleTriggers.");
            }

            if (status is "not_provided" or "stale_or_superseded" &&
                GetStringArray(input, "blockerIds").Count == 0)
            {
                errors.Add($"Governance input {inputId} with status {status} must define blockerIds.");
            }

            if (status == "customer_deferred" &&
                GetStringArray(input, "downgradeIds").Count == 0 &&
                GetStringArray(input, "blockerIds").Count == 0)
            {
                errors.Add($"Governance input {inputId} with status customer_deferred must define downgradeIds or blockerIds.");
            }
        }

        foreach (var requiredInputId in RequiredInputIds)
        {
            if (!inputIds.Contains(requiredInputId))
            {
                errors.Add($"Missing required governance input: {requiredInputId}.");
            }
        }
    }

    private static void ValidateClaimImpactScenarios(JsonObject source, List<string> errors)
    {
        var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in RequireArray(source, "claimImpactScenarios").OfType<JsonObject>())
        {
            var scenarioId = GetString(scenario, "scenarioId");
            if (!string.IsNullOrWhiteSpace(scenarioId))
            {
                scenarioIds.Add(scenarioId);
            }

            var decision = GetString(scenario, "decision");
            if (!AllowedDecisions.Contains(decision))
            {
                errors.Add($"Claim scenario {scenarioId} has unsupported decision {decision}.");
            }

            foreach (var property in new[] { "label", "publicWordingKey", "residualRisk" })
            {
                if (string.IsNullOrWhiteSpace(GetString(scenario, property)))
                {
                    errors.Add($"Claim scenario {scenarioId} is missing {property}.");
                }
            }

            if (GetStringArray(scenario, "affectedClaims").Count == 0)
            {
                errors.Add($"Claim scenario {scenarioId} must define affectedClaims.");
            }
        }

        foreach (var requiredScenarioId in RequiredScenarioIds)
        {
            if (!scenarioIds.Contains(requiredScenarioId))
            {
                errors.Add($"Missing required claim-impact scenario: {requiredScenarioId}.");
            }
        }
    }

    private static void ValidateFeat139Mappings(JsonObject source, List<string> errors)
    {
        var blockerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in RequireArray(source, "feat139BlockerMappings").OfType<JsonObject>())
        {
            var blockerId = GetString(mapping, "blockerId");
            if (!string.IsNullOrWhiteSpace(blockerId))
            {
                blockerIds.Add(blockerId);
            }

            var classification = GetString(mapping, "classification");
            if (!AllowedFeat139Classifications.Contains(classification))
            {
                errors.Add($"FEAT-139 blocker {blockerId} has unsupported classification {classification}.");
            }

            if (GetStringArray(mapping, "governanceInputIds").Count == 0)
            {
                errors.Add($"FEAT-139 blocker {blockerId} must list governanceInputIds.");
            }
        }

        foreach (var requiredBlocker in RequiredFeat139BlockerIds)
        {
            if (!blockerIds.Contains(requiredBlocker))
            {
                errors.Add($"Missing FEAT-139 blocker mapping: {requiredBlocker}.");
            }
        }
    }

    private static void ValidateFeat146Inputs(JsonObject source, List<string> errors)
    {
        foreach (var input in RequireArray(source, "feat146AuthorityInputs").OfType<JsonObject>())
        {
            var outcome = GetString(input, "outcome");
            foreach (var property in new[]
            {
                "outcome",
                "authorityRole",
                "authorityActorRef",
                "governingRuleRef",
                "finalityRuleStatus",
                "remedyAuthorityStatus",
                "challengeProcessStatus",
                "publicSafeLimitationWording",
            })
            {
                if (string.IsNullOrWhiteSpace(GetString(input, property)))
                {
                    errors.Add($"FEAT-146 authority input {outcome} is missing {property}.");
                }
            }
        }
    }

    private static void ValidatePublicSummary(JsonObject source, List<string> errors)
    {
        var publicSummary = source.TryGetPropertyValue("publicSummary", out var node)
            ? node as JsonObject
            : null;
        if (publicSummary is null)
        {
            errors.Add("publicSummary must be an object.");
            return;
        }

        if (!GetString(publicSummary, "statusWording").Contains(
                "HushVoting has not validated legal sufficiency",
                StringComparison.Ordinal))
        {
            errors.Add("publicSummary.statusWording must include the legal sufficiency disclaimer.");
        }
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
        List<LegalGovernanceBoundaryMaterialFinding> findings)
    {
        var lower = text.ToLowerInvariant();
        AddIfContains("private contact", "private_contact");
        AddIfContains("@", "email_or_private_contact");
        AddIfContains("legal note", "legal_note");
        AddIfContains("private bylaw", "private_governing_rule_body");
        AddIfContains("support ref", "support_reference");
        AddIfContains("raw acknowledgement", "raw_acknowledgement");
        AddIfContains("anomaly body", "anomaly_body");
        AddIfContains("voter identity", "voter_identity");
        AddIfContains("ballot selection", "ballot_selection");
        AddIfContains("trustee share", "trustee_share");
        AddIfContains("tally material", "tally_material");
        AddIfContains("begin private key", "private_key");
        AddIfContains("password=", "credential");

        void AddIfContains(string needle, string category)
        {
            if (lower.Contains(needle, StringComparison.Ordinal))
            {
                findings.Add(new LegalGovernanceBoundaryMaterialFinding(
                    relativePath,
                    category,
                    needle));
            }
        }
    }
}
