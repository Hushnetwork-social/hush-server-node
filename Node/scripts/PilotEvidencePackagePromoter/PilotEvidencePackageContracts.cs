using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PilotEvidencePackagePromoter;

public sealed record PilotEvidencePackagePromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string OutputRoot)
{
    public const string SourceFolder = "Pilot-Evidence-Package";
    public const string SourceFileName = "pilot-evidence-source.json";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", SourceFileName);

    public static PilotEvidencePackagePromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            SourceFolder);

        return new PilotEvidencePackagePromotionPaths(
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

public sealed record PilotEvidenceMaterialFinding(
    string RelativePath,
    string Category,
    string Evidence);

public sealed record PilotEvidenceGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash);

public sealed record PilotEvidenceGeneratedPackage(
    string Status,
    IReadOnlyList<PilotEvidenceGeneratedArtifact> Artifacts,
    IReadOnlyList<PilotEvidenceMaterialFinding> PublicForbiddenFindings,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Downgrades);

public sealed class PilotEvidencePackagePromotionException : Exception
{
    public PilotEvidencePackagePromotionException(string message, IEnumerable<string>? details = null)
        : base(message)
    {
        Details = details?.ToArray() ?? [];
    }

    public IReadOnlyList<string> Details { get; }
}

public static class PilotEvidencePackageContracts
{
    public const string FeatureId = "FEAT-141";
    public const string AcceptanceGate = "AT-RDY-013";
    public const string SourceGap = "Controlled pilot evidence";
    public const string ReadinessFragmentId = "RDY-EVID-AT-RDY-013-FEAT-141-001";
    public const string DimensionId = "RDY-DIM-010";
    public const string CurrentRegisterId = "RDY-REG-v0.1.3";
    public const string CurrentRegisterManifestHash = "e0a62370459c875602820e728fd6bb0dd006d302b8855d077a8205033677b055";
    public const string CurrentRegisterArchiveHash = "a8a4eb01c496620c598ea3c97c92033098c43ecb6245797382625f26d2dc2ae0";
    public const string CanonicalizationVersion = "feat141-pilot-evidence-package-canonical-json-v1";
    public const string RequiredInternalRehearsalWording =
        "This package supports an internal non-binding rehearsal only. It is not certification, legal approval, production rollout approval, public election authorization, or independent validation. Friendly organization pilot claims remain blocked until FEAT-130 promotes the required evidence and all pilot-critical blockers are resolved.";

    public static readonly string[] RequiredSchemaFiles =
    [
        "pilot-evidence-source.schema.json",
        "pilot-evidence-package.schema.json",
        "pilot-evidence-package-manifest.schema.json",
        "pilot-evidence-readiness-fragment.schema.json",
        "pilot-evidence-restricted-index.schema.json",
        "pilot-evidence-downstream-handoff.schema.json",
        "pilot-evidence-exception-records.schema.json",
        "pilot-evidence-public-artifact-scan.schema.json",
        "pilot-evidence-package-hash-validation.schema.json",
    ];

    public static readonly string[] RequiredOutputFiles =
    [
        PilotEvidencePackageArtifactGenerator.PackagePath,
        PilotEvidencePackageArtifactGenerator.PackageManifestPath,
        PilotEvidencePackageArtifactGenerator.ReadinessFragmentPath,
        PilotEvidencePackageArtifactGenerator.PublicSafeSummaryPath,
        PilotEvidencePackageArtifactGenerator.RestrictedIndexPath,
        PilotEvidencePackageArtifactGenerator.DownstreamHandoffPath,
        PilotEvidencePackageArtifactGenerator.ExceptionRecordsPath,
        PilotEvidencePackageArtifactGenerator.PublicArtifactScanPath,
        PilotEvidencePackageArtifactGenerator.PackageHashValidationPath,
    ];

    public static readonly string[] RequiredUpstreamFeatureSlices =
    [
        "FEAT-130",
        "FEAT-131",
        "FEAT-132",
        "FEAT-133",
        "FEAT-134",
        "FEAT-135",
        "FEAT-136",
        "FEAT-137",
        "FEAT-138",
        "FEAT-139",
        "FEAT-140",
        "FEAT-142",
        "FEAT-143",
        "FEAT-144",
        "FEAT-146",
    ];

    public static readonly IReadOnlyDictionary<string, string> RequiredFeat138Hashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["void-readiness-fragment.json"] = "ec4000765257c8973b2ff21f0a41a3ec4eded1427eba2696017e7eff2b4fe9ba",
            ["void-downstream-handoff.json"] = "f501cf09ccec7cbb18f5857b9dd97c432844bd5515efae67e3fa5c0e82d4eb14",
            ["void-public-artifact-scan.json"] = "3542aa184ae55f2d0c9dac0b054d6f7aeb32a1356b18a02609edb6005ba6dc9b",
            ["void-package-hash-validation.json"] = "4e01c3927b2b8dbdf9500ff754af6155d33253fdfa15da4e245b9085597ea117",
        };

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        "accepted",
        "accepted_with_limitations",
        "blocked",
    };

    private static readonly HashSet<string> AllowedProfiles = new(StringComparer.Ordinal)
    {
        "internal_non_binding_rehearsal",
        "friendly_organization_pilot",
        "exception_only",
    };

    private static readonly HashSet<string> AllowedEvidenceStatuses = new(StringComparer.Ordinal)
    {
        "accepted",
        "accepted_with_limitations",
        "observed",
        "blocked",
        "downgraded",
        "missing",
        "stale",
        "superseded",
        "not_in_scope",
    };

    private static readonly HashSet<string> AllowedClaimStates = new(StringComparer.Ordinal)
    {
        "allowed",
        "allowed_with_limitations",
        "downgraded",
        "blocked",
        "not_in_scope",
    };

    public static JsonObject LoadSource(
        PilotEvidencePackagePromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "pilot evidence source");
        return ReadJsonObject(sourcePath, PilotEvidencePackagePromotionPaths.SourceFileName);
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
        var errors = ValidateJsonRequired(source, PilotEvidencePackagePromotionPaths.SourceFileName, [
            "schemaVersion",
            "sourceId",
            "packageId",
            "featureId",
            "acceptanceGate",
            "sourceGap",
            "status",
            "profile",
            "scenario",
            "organizationScope",
            "participantModel",
            "timeline",
            "rehearsalDecision",
            "beforeRegister",
            "afterRegister",
            "upstreamEvidence",
            "observedRunEvidence",
            "runtimeEvidence",
            "claimDecisions",
            "exceptions",
            "redactionPolicy",
            "signoff",
            "promotionPolicy",
            "publicSummary",
            "publicArtifactSamples",
        ]).ToList();

        RequireValue(source, "schemaVersion", "pilot-evidence-source.v1", errors);
        RequireValue(source, "featureId", FeatureId, errors);
        RequireValue(source, "acceptanceGate", AcceptanceGate, errors);
        RequireValue(source, "sourceGap", SourceGap, errors);

        var status = GetString(source, "status");
        if (!AllowedStatuses.Contains(status))
        {
            errors.Add($"Unsupported source status: {status}.");
        }

        var profile = GetString(source, "profile");
        if (!AllowedProfiles.Contains(profile))
        {
            errors.Add($"Unsupported profile: {profile}.");
        }

        ValidateRegisterBinding(source, errors);
        ValidateRehearsalDecision(source, errors);
        ValidateUpstreamEvidence(source, errors);
        ValidateObservedRunEvidence(source, errors);
        ValidateRuntimeEvidence(source, errors);
        ValidateClaimDecisions(source, errors);
        ValidateExceptions(source, errors);
        ValidatePromotionPolicy(source, errors);
        ValidatePublicSummary(source, errors);

        return errors;
    }

    public static IReadOnlyList<PilotEvidenceMaterialFinding> ScanForbiddenPublicMaterial(
        JsonObject source,
        IEnumerable<(string Path, string Content)> generatedPublicArtifacts)
    {
        var findings = new List<PilotEvidenceMaterialFinding>();
        foreach (var sample in RequireArray(source, "publicArtifactSamples").OfType<JsonObject>())
        {
            AddForbiddenFindings(GetString(sample, "content"), GetString(sample, "path"), findings);
        }

        foreach (var artifact in generatedPublicArtifacts)
        {
            AddForbiddenFindings(artifact.Content, artifact.Path, findings);
        }

        return findings
            .DistinctBy(finding => $"{finding.RelativePath}:{finding.Category}:{finding.Evidence}", StringComparer.Ordinal)
            .OrderBy(finding => finding.RelativePath, StringComparer.Ordinal)
            .ThenBy(finding => finding.Category, StringComparer.Ordinal)
            .ToArray();
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new PilotEvidencePackagePromotionException($"{label} is not a JSON object.");
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

        throw new PilotEvidencePackagePromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new PilotEvidencePackagePromotionException($"Missing array property: {property}");
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

    public static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());

    public static void EnsurePathUnder(string root, string candidate, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new PilotEvidencePackagePromotionException(
                "Pilot evidence package path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    public static string ResolveSourceInput(
        PilotEvidencePackagePromotionPaths paths,
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
            ? Path.Combine(fullPath, PilotEvidencePackagePromotionPaths.SourceFileName)
            : fullPath;
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

    private static void ValidateRegisterBinding(JsonObject source, List<string> errors)
    {
        foreach (var property in new[] { "beforeRegister", "afterRegister" })
        {
            var register = source.TryGetPropertyValue(property, out var node) ? node as JsonObject : null;
            if (register is null)
            {
                errors.Add($"{property} must be an object.");
                continue;
            }

            RequireValue(register, "registerId", CurrentRegisterId, errors, property);
            RequireValue(register, "manifestHash", CurrentRegisterManifestHash, errors, property);
            RequireValue(register, "archiveHash", CurrentRegisterArchiveHash, errors, property);
            foreach (var required in new[] { "version", "path", "status", "strongestAllowedClaim", "publicationStatus" })
            {
                if (string.IsNullOrWhiteSpace(GetString(register, required)))
                {
                    errors.Add($"{property}.{required} is required.");
                }
            }

            if (GetInt(register, "totalScore") <= 0)
            {
                errors.Add($"{property}.totalScore must be positive.");
            }
        }
    }

    private static void ValidateRehearsalDecision(JsonObject source, List<string> errors)
    {
        var decision = TryObject(source, "rehearsalDecision", errors);
        if (decision is null)
        {
            return;
        }

        var status = GetString(decision, "status");
        if (status is not ("completed" or "offered" or "declined_by_client" or "skipped" or "blocked" or "not_applicable"))
        {
            errors.Add($"rehearsalDecision.status has unsupported value {status}.");
        }

        foreach (var property in new[] { "decidedBy", "decisionRef", "readinessImpact" })
        {
            if (string.IsNullOrWhiteSpace(GetString(decision, property)))
            {
                errors.Add($"rehearsalDecision.{property} is required.");
            }
        }
    }

    private static void ValidateUpstreamEvidence(JsonObject source, List<string> errors)
    {
        var features = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var evidence in RequireArray(source, "upstreamEvidence").OfType<JsonObject>())
        {
            var feature = GetString(evidence, "featureSlice");
            if (!string.IsNullOrWhiteSpace(feature))
            {
                features[feature] = evidence;
            }

            foreach (var property in new[] { "featureSlice", "acceptanceGate", "status", "publicRef", "restrictedRef", "sha256Hash", "freshness", "claimEffect" })
            {
                if (string.IsNullOrWhiteSpace(GetString(evidence, property)))
                {
                    errors.Add($"Upstream evidence {feature} is missing {property}.");
                }
            }

            var status = GetString(evidence, "status");
            if (!AllowedEvidenceStatuses.Contains(status))
            {
                errors.Add($"Upstream evidence {feature} has unsupported status {status}.");
            }
        }

        foreach (var requiredFeature in RequiredUpstreamFeatureSlices)
        {
            if (!features.ContainsKey(requiredFeature))
            {
                errors.Add($"Missing upstream evidence for {requiredFeature}.");
            }
        }

        ValidateFeat138Hashes(features, errors);
        ValidateFeat139State(features, errors);
    }

    private static void ValidateFeat138Hashes(
        IReadOnlyDictionary<string, JsonObject> features,
        List<string> errors)
    {
        if (!features.TryGetValue("FEAT-138", out var feat138))
        {
            return;
        }

        var artifacts = feat138.TryGetPropertyValue("artifacts", out var node) && node is JsonArray array
            ? array.OfType<JsonObject>().ToArray()
            : [];
        foreach (var (path, hash) in RequiredFeat138Hashes)
        {
            var artifact = artifacts.SingleOrDefault(item =>
                string.Equals(GetString(item, "path"), path, StringComparison.Ordinal));
            if (artifact is null)
            {
                errors.Add($"FEAT-138 evidence is missing required artifact {path}.");
                continue;
            }

            if (!string.Equals(GetString(artifact, "sha256Hash"), hash, StringComparison.Ordinal))
            {
                errors.Add($"FEAT-138 artifact {path} has stale or unexpected hash.");
            }
        }
    }

    private static void ValidateFeat139State(
        IReadOnlyDictionary<string, JsonObject> features,
        List<string> errors)
    {
        if (!features.TryGetValue("FEAT-139", out var feat139))
        {
            return;
        }

        if (GetString(feat139, "status") == "blocked" &&
            GetStringArray(feat139, "blockerIds").Count == 0)
        {
            errors.Add("FEAT-139 blocked state must list blockerIds.");
        }
    }

    private static void ValidateObservedRunEvidence(JsonObject source, List<string> errors)
    {
        var observed = TryObject(source, "observedRunEvidence", errors);
        if (observed is null)
        {
            return;
        }

        var rehearsalDecisionStatus = GetString(RequireObject(source, "rehearsalDecision"), "status");
        var observedStatus = GetString(observed, "status");
        if (rehearsalDecisionStatus == "completed" && observedStatus != "completed")
        {
            errors.Add("Completed rehearsal decision requires observedRunEvidence.status completed.");
        }

        if (observedStatus == "completed")
        {
            foreach (var property in new[]
            {
                "exportPackage",
                "verifierOutput",
                "supportOrNoIncidentStatement",
                "acceptanceNotes",
                "postmortem",
            })
            {
                var item = TryObject(observed, property, errors);
                if (item is null)
                {
                    continue;
                }

                foreach (var required in new[] { "evidenceId", "publicRef", "restrictedRef", "sha256Hash" })
                {
                    if (string.IsNullOrWhiteSpace(GetString(item, required)))
                    {
                        errors.Add($"observedRunEvidence.{property}.{required} is required for a completed rehearsal.");
                    }
                }
            }
        }
        else if (!HasPackageBlockingException(source))
        {
            errors.Add("Skipped, declined, blocked, or non-completed rehearsal evidence requires a packageBlocking exception.");
        }
    }

    private static void ValidateRuntimeEvidence(JsonObject source, List<string> errors)
    {
        var runtime = TryObject(source, "runtimeEvidence", errors);
        if (runtime is null)
        {
            return;
        }

        var deployment = TryObject(runtime, "deploymentProofBinding", errors);
        if (deployment is not null && string.IsNullOrWhiteSpace(GetString(deployment, "ledgerRef")))
        {
            errors.Add("runtimeEvidence.deploymentProofBinding.ledgerRef is required.");
        }

        var webClient = TryObject(runtime, "webClientObservedProof", errors);
        if (webClient is not null && GetStringArray(webClient, "limitationIds").Count == 0)
        {
            errors.Add("runtimeEvidence.webClientObservedProof must carry limitationIds.");
        }

        var governed = TryObject(runtime, "governedOutcome", errors);
        if (governed is not null)
        {
            if (GetString(governed, "finalizedWithAnomalyStatus") != "accepted")
            {
                errors.Add("runtimeEvidence.governedOutcome.finalizedWithAnomalyStatus must be accepted for the baseline.");
            }

            if (GetString(governed, "failedToFinalizeStatus") == "accepted")
            {
                errors.Add("runtimeEvidence.governedOutcome.failedToFinalizeStatus cannot be accepted without future evidence.");
            }
        }
    }

    private static void ValidateClaimDecisions(JsonObject source, List<string> errors)
    {
        var claims = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var claim in RequireArray(source, "claimDecisions").OfType<JsonObject>())
        {
            var claimId = GetString(claim, "claimId");
            if (!string.IsNullOrWhiteSpace(claimId))
            {
                claims[claimId] = claim;
            }

            var state = GetString(claim, "state");
            if (!AllowedClaimStates.Contains(state))
            {
                errors.Add($"Claim {claimId} has unsupported state {state}.");
            }

            foreach (var property in new[] { "wordingKey", "residualRisk" })
            {
                if (string.IsNullOrWhiteSpace(GetString(claim, property)))
                {
                    errors.Add($"Claim {claimId} is missing {property}.");
                }
            }

            if (state == "blocked" && GetStringArray(claim, "blockerIds").Count == 0)
            {
                errors.Add($"Blocked claim {claimId} must define blockerIds.");
            }
        }

        if (!claims.TryGetValue("internal_non_binding_rehearsal", out var internalClaim) ||
            GetString(internalClaim, "state") is not ("allowed" or "allowed_with_limitations"))
        {
            errors.Add("internal_non_binding_rehearsal claim must be allowed or allowed_with_limitations.");
        }

        if (!claims.TryGetValue("friendly_organization_pilot", out var friendlyPilot) ||
            GetString(friendlyPilot, "state") != "blocked")
        {
            errors.Add("friendly_organization_pilot claim must remain blocked under RDY-REG-v0.1.3.");
        }
    }

    private static void ValidateExceptions(JsonObject source, List<string> errors)
    {
        foreach (var exception in RequireArray(source, "exceptions").OfType<JsonObject>())
        {
            var exceptionId = GetString(exception, "exceptionId");
            foreach (var property in new[]
            {
                "exceptionId",
                "sourceGap",
                "acceptanceGate",
                "featureSlice",
                "affectedClaim",
                "reason",
                "compensatingEvidence",
                "scoreImpact",
                "claimImpact",
                "reviewDue",
                "signoff",
            })
            {
                if (string.IsNullOrWhiteSpace(GetString(exception, property)))
                {
                    errors.Add($"Exception {exceptionId} is missing {property}.");
                }
            }

            if (GetString(exception, "scoreImpact") is not ("no_score_increase" or "limited_claim_only" or "downgrade" or "block"))
            {
                errors.Add($"Exception {exceptionId} has unsupported scoreImpact.");
            }
        }
    }

    private static void ValidatePromotionPolicy(JsonObject source, List<string> errors)
    {
        var policy = TryObject(source, "promotionPolicy", errors);
        if (policy is null)
        {
            return;
        }

        RequireValue(policy, "dimensionId", DimensionId, errors, "promotionPolicy");
        RequireValue(policy, "registerPromotionOwner", "FEAT-130", errors, "promotionPolicy");
        if (GetBool(policy, "directRegisterMutation", true))
        {
            errors.Add("promotionPolicy.directRegisterMutation must be false.");
        }

        if (GetInt(policy, "currentTotalScore") != 60)
        {
            errors.Add("promotionPolicy.currentTotalScore must match RDY-REG-v0.1.3 score 60.");
        }
    }

    private static void ValidatePublicSummary(JsonObject source, List<string> errors)
    {
        var summary = TryObject(source, "publicSummary", errors);
        if (summary is null)
        {
            return;
        }

        var wording = GetString(summary, "statusWording");
        if (!wording.Contains("internal non-binding rehearsal only", StringComparison.Ordinal) ||
            !wording.Contains("not certification", StringComparison.Ordinal) ||
            !wording.Contains("Friendly organization pilot claims remain blocked", StringComparison.Ordinal))
        {
            errors.Add("publicSummary.statusWording must include required internal non-binding and non-claim wording.");
        }
    }

    private static JsonObject? TryObject(JsonObject source, string property, List<string> errors)
    {
        if (source.TryGetPropertyValue(property, out var node) && node is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"{property} must be an object.");
        return null;
    }

    private static bool HasPackageBlockingException(JsonObject source) =>
        RequireArray(source, "exceptions")
            .OfType<JsonObject>()
            .Any(exception => GetBool(exception, "packageBlocking"));

    private static void RequireValue(
        JsonObject value,
        string property,
        string expected,
        List<string> errors,
        string? prefix = null)
    {
        if (!string.Equals(GetString(value, property), expected, StringComparison.Ordinal))
        {
            errors.Add($"{(prefix is null ? property : $"{prefix}.{property}")} must be {expected}.");
        }
    }

    private static void AddForbiddenFindings(
        string text,
        string relativePath,
        List<PilotEvidenceMaterialFinding> findings)
    {
        var checks = new (string Needle, string Category)[]
        {
            ("hush-documents/private", "private_authoring_path"),
            ("support log body", "support_log_body"),
            ("raw support log", "support_log_body"),
            ("anomaly body", "anomaly_body"),
            ("voter identity", "voter_identity"),
            ("vote choice", "vote_choice"),
            ("receipt secret", "receipt_secret"),
            ("ballot id", "ballot_id"),
            ("nullifier", "nullifier"),
            ("credential=", "credential"),
            ("kms key", "kms_provider_internal"),
            ("kms arn", "kms_provider_internal"),
            ("trustee share", "trustee_secret_material"),
            ("private legal payload", "private_legal_payload"),
            ("private contact", "private_contact"),
            ("@", "email_or_private_contact"),
            ("ci secret", "ci_secret"),
            ("deployment secret", "deployment_secret"),
            ("database password", "database_credential"),
            ("begin private key", "private_key"),
        };

        foreach (var (needle, category) in checks)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new PilotEvidenceMaterialFinding(relativePath, category, needle));
            }
        }
    }
}
