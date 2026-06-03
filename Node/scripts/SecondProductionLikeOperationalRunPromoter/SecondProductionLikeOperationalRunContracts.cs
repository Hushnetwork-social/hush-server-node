using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SecondProductionLikeOperationalRunPromoter;

public static class SecondProductionLikeOperationalRunContracts
{
    public const string FeatureId = "FEAT-163";
    public const string SourceSchemaVersion = "second-production-like-run-source.v1";
    public const string PackageManifestSchemaVersion = "second-production-like-run-package-manifest.v1";
    public const string CurrentRegisterId = "RDY-REG-v0.1.7";
    public const string TargetDimensionId = "RDY-DIM-007";
    public const string TargetBlockerId = "RDY-BLOCK-INTERNAL_AUDIT_95_DIM007-001";
    public const string AcceptedFeat154ManifestHash = "62b2c9afb605bb6e0d26876629b7df122b7da566df37f536b4790a9398ecb410";
    public const string CanonicalizationVersion = "second-production-like-run-canonical-json.v1";

    public static readonly string[] RequiredSchemaFiles =
    [
        SecondProductionLikeOperationalRunPromotionPaths.SourceSchemaFileName,
        SecondProductionLikeOperationalRunPromotionPaths.PackageManifestSchemaFileName,
    ];

    private static readonly string[] RequiredSourceProperties =
    [
        "schemaVersion",
        "featureId",
        "sourceId",
        "generatedAt",
        "readinessBaseline",
        "scoreProposal",
        "firstRunBaseline",
        "secondRunProfile",
        "upstreamRefs",
        "evidenceGates",
        "operationalEvidence",
        "restrictedEvidencePolicy",
        "publicSafety",
    ];

    private static readonly UpstreamPolicy[] RequiredUpstreamPolicies =
    [
        new("feat134", "FEAT-134", "requires_currentness_check_at_package_time", RequiredForScore: true, RequiresSha256: true, RequiresCommit: false, "FEAT163_STALE_FEAT134_REF"),
        new("feat143", "FEAT-143", "requires_current_runtime_binding_ref", RequiredForScore: true, RequiresSha256: true, RequiresCommit: false, "FEAT163_STALE_FEAT143_REF"),
        new("feat144", "FEAT-144", "requires_current_webclient_observed_proof_ref", RequiredForScore: true, RequiresSha256: true, RequiresCommit: false, "FEAT163_STALE_FEAT144_REF"),
        new("feat154", "FEAT-154", "accepted_first_run_baseline_only", RequiredForScore: true, RequiresSha256: true, RequiresCommit: false, "FEAT163_STALE_FEAT154_REF"),
        new("feat161", "FEAT-161", "consume_when_custody_profile_is_in_scope", RequiredForScore: false, RequiresSha256: true, RequiresCommit: true, "FEAT163_STALE_FEAT161_REF"),
        new("feat162", "FEAT-162", "consume_when_deployment_variance_is_claimed", RequiredForScore: false, RequiresSha256: true, RequiresCommit: true, "FEAT163_STALE_FEAT162_REF"),
    ];

    private static readonly string[] RequiredGateKeys =
    [
        "feat154BaselineCurrentness",
        "runtimeProofBinding",
        "monitoringAlerting",
        "backupRestore",
        "supportOperatorHandoff",
        "securitySupportFreshness",
        "incidentResponseWalkthrough",
        "postmortem",
        "noSecretScan",
    ];

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
        "incident_payload_raw",
        "private screenshot",
        "voter_data=",
        "kms_key=",
        "kms_alias=",
        @"c:\mywork\hushnetworkorg\hush-documents",
    ];

    private static readonly HashSet<string> ForbiddenNeedleAllowListProperties = new(StringComparer.Ordinal)
    {
        "allowedPublicFields",
        "forbiddenMaterialClasses",
        "restrictedMaterialClasses",
    };

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static JsonObject LoadSource(
        SecondProductionLikeOperationalRunPromotionPaths paths,
        string? sourceInput = null)
    {
        var sourcePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, sourcePath, "FEAT-163 second production-like run source");
        if (!File.Exists(sourcePath))
        {
            throw new SecondProductionLikeOperationalRunPromotionException(
                "FEAT-163 second production-like run source input is missing.",
                [$"Source input was not found: {sourcePath}"]);
        }

        return ReadJsonObject(sourcePath, SecondProductionLikeOperationalRunPromotionPaths.SourceFileName);
    }

    public static JsonObject ValidateForPromotion(
        SecondProductionLikeOperationalRunPromotionPaths paths,
        string? sourceInput = null,
        bool publicOnly = false)
    {
        var schemaErrors = ValidateSchemaSet(paths.SchemasRoot);
        if (schemaErrors.Count > 0)
        {
            throw new SecondProductionLikeOperationalRunPromotionException(
                "FEAT-163 second production-like run schema validation failed.",
                schemaErrors);
        }

        var source = LoadSource(paths, sourceInput);
        var errors = ValidateSource(source).ToList();
        errors.AddRange(ValidateCurrentRefs(paths, source, publicOnly));
        if (errors.Count > 0)
        {
            throw new SecondProductionLikeOperationalRunPromotionException(
                "FEAT-163 second production-like run source validation failed.",
                errors);
        }

        return source;
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
        var errors = ValidateJsonRequired(
            source,
            SecondProductionLikeOperationalRunPromotionPaths.SourceFileName,
            RequiredSourceProperties).ToList();

        RequireValue(source, "schemaVersion", SourceSchemaVersion, errors, "FEAT163_SCHEMA_VERSION_INVALID");
        RequireValue(source, "featureId", FeatureId, errors, "FEAT163_FEATURE_ID_INVALID");
        ValidateReadinessBaseline(source, errors);
        ValidateScoreProposal(source, errors);
        ValidateFirstRunBaseline(source, errors);
        ValidateSecondRunProfile(source, errors);
        ValidateUpstreamRefs(source, errors);
        ValidateEvidenceGates(source, errors);
        ValidateOperationalEvidence(source, errors);
        ValidateRestrictedEvidencePolicy(source, errors);
        ValidatePublicSafety(source, errors);
        ValidateForbiddenNeedles(source, errors);

        return errors;
    }

    public static IReadOnlyList<string> ValidateCurrentRefs(
        SecondProductionLikeOperationalRunPromotionPaths paths,
        JsonObject source,
        bool publicOnly = false)
    {
        var errors = new List<string>();
        var upstream = RequireObject(source, "upstreamRefs");
        var firstRunBaseline = RequireObject(source, "firstRunBaseline");
        RequireValue(firstRunBaseline, "manifestSha256Hash", AcceptedFeat154ManifestHash, errors, "FEAT163_STALE_FEAT154_REF");

        if (publicOnly)
        {
            return errors;
        }

        RequireFileHash(
            Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-134-security-dependency-support-readiness", "feature-completion-report.md"),
            GetString(RequireObject(upstream, "feat134"), "sha256Hash"),
            "upstreamRefs.feat134.sha256Hash",
            errors);
        RequireFileHash(
            Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-143-runtime-deployment-proof-binding-ledger", "readiness-handoff-20260526.md"),
            GetString(RequireObject(upstream, "feat143"), "sha256Hash"),
            "upstreamRefs.feat143.sha256Hash",
            errors);
        RequireFileHash(
            Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-144-hushwebclient-deployment-proof-exposure-handshake", "FeatureDescription.md"),
            GetString(RequireObject(upstream, "feat144"), "sha256Hash"),
            "upstreamRefs.feat144.sha256Hash",
            errors);
        RequireFileHash(
            Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-161-kms-custody-drift-rotation-recovery-rehearsal", "feature-completion-report.md"),
            GetString(RequireObject(upstream, "feat161"), "sha256Hash"),
            "upstreamRefs.feat161.sha256Hash",
            errors);
        RequireFileHash(
            Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED", "FEAT-162-trusted-deployment-rollback-emergency-change-rehearsal", "feature-completion-report.md"),
            GetString(RequireObject(upstream, "feat162"), "sha256Hash"),
            "upstreamRefs.feat162.sha256Hash",
            errors);

        RequireGitHead(
            Path.Combine(paths.WorkspaceRoot, "Kms-Custody-Rehearsal"),
            GetString(RequireObject(upstream, "feat161"), "commitHash"),
            "upstreamRefs.feat161.commitHash",
            errors);
        RequireGitHead(
            Path.Combine(paths.WorkspaceRoot, "Deployment-Rollback-Rehearsal"),
            GetString(RequireObject(upstream, "feat162"), "commitHash"),
            "upstreamRefs.feat162.commitHash",
            errors);

        return errors;
    }

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new SecondProductionLikeOperationalRunPromotionException($"{label} is not a JSON object.");
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

        throw new SecondProductionLikeOperationalRunPromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new SecondProductionLikeOperationalRunPromotionException($"Missing array property: {property}");
    }

    public static string GetString(JsonObject value, string property, string fallback = "")
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var text))
        {
            return text;
        }

        return fallback;
    }

    public static int GetInt(JsonObject value, string property, int fallback = 0)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonValue jsonValue &&
            jsonValue.TryGetValue<int>(out var number))
        {
            return number;
        }

        return fallback;
    }

    public static bool GetBool(JsonObject value, string property, bool fallback = false)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonValue jsonValue &&
            jsonValue.TryGetValue<bool>(out var flag))
        {
            return flag;
        }

        return fallback;
    }

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    public static string CanonicalJson(JsonNode node) =>
        node.ToJsonString(CanonicalJsonOptions) + Environment.NewLine;

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
            throw new SecondProductionLikeOperationalRunPromotionException(
                "FEAT-163 second production-like run path escaped the expected root.",
                [$"{label}: {fullPath}", $"Root: {fullRoot}"]);
        }
    }

    private static string ResolveSourceInput(
        SecondProductionLikeOperationalRunPromotionPaths paths,
        string? sourceInput)
    {
        if (string.IsNullOrWhiteSpace(sourceInput))
        {
            return Path.GetFullPath(paths.DefaultSourceInput);
        }

        var combined = Path.IsPathRooted(sourceInput)
            ? sourceInput
            : Path.Combine(paths.SourceRoot, sourceInput);
        var fullPath = Path.GetFullPath(combined);
        return Directory.Exists(fullPath)
            ? Path.Combine(fullPath, SecondProductionLikeOperationalRunPromotionPaths.SourceFileName)
            : fullPath;
    }

    private static void ValidateReadinessBaseline(JsonObject source, List<string> errors)
    {
        var baseline = RequireObject(source, "readinessBaseline");
        RequireValue(baseline, "registerId", CurrentRegisterId, errors, "FEAT163_STALE_READINESS_BASELINE");
        RequireInt(baseline, "totalScore", 80, errors, "FEAT163_STALE_READINESS_BASELINE");
        RequireSha256(baseline, "registerManifestSha256Hash", errors, "FEAT163_STALE_READINESS_BASELINE");
        RequireSha256(baseline, "scorecardSha256Hash", errors, "FEAT163_STALE_READINESS_BASELINE");

        var dimension = RequireObject(baseline, "dimension");
        RequireValue(dimension, "dimensionId", TargetDimensionId, errors, "FEAT163_TARGET_DIMENSION_MISMATCH");
        RequireInt(dimension, "currentScore", 8, errors, "FEAT163_SCORE_BASELINE_INVALID");
        RequireInt(dimension, "targetScore", 10, errors, "FEAT163_SCORE_TARGET_INVALID");
        RequireValue(dimension, "blockerId", TargetBlockerId, errors, "FEAT163_TARGET_BLOCKER_MISMATCH");
    }

    private static void ValidateScoreProposal(JsonObject source, List<string> errors)
    {
        var proposal = RequireObject(source, "scoreProposal");
        RequireValue(proposal, "dimensionId", TargetDimensionId, errors, "FEAT163_SCORE_PROPOSAL_INVALID");
        RequireInt(proposal, "fromScore", 8, errors, "FEAT163_SCORE_PROPOSAL_INVALID");
        RequireInt(proposal, "toScore", 10, errors, "FEAT163_SCORE_PROPOSAL_INVALID");
        RequireBool(proposal, "proposalOnly", true, errors, "FEAT163_SCORE_PROPOSAL_INVALID");
        RequireBool(proposal, "directRegisterMutation", false, errors, "FEAT163_DIRECT_REGISTER_MUTATION_FORBIDDEN");
        RequireBool(proposal, "blockedUnlessAllGatesPass", true, errors, "FEAT163_SCORE_PROPOSAL_INVALID");
    }

    private static void ValidateFirstRunBaseline(JsonObject source, List<string> errors)
    {
        var baseline = RequireObject(source, "firstRunBaseline");
        RequireValue(baseline, "featureId", "FEAT-154", errors, "FEAT163_STALE_FEAT154_REF");
        RequireValue(baseline, "status", "accepted", errors, "FEAT163_STALE_FEAT154_REF");
        RequireValue(baseline, "manifestSha256Hash", AcceptedFeat154ManifestHash, errors, "FEAT163_STALE_FEAT154_REF");
        RequireValue(baseline, "baselineUse", "baseline_currentness_only", errors, "FEAT163_FEAT154_REUSE_FORBIDDEN");
        if (GetString(baseline, "sourceId", "").StartsWith("FEAT163-", StringComparison.Ordinal))
        {
            errors.Add("FEAT163_FEAT154_REUSE_FORBIDDEN: firstRunBaseline.sourceId must remain FEAT-154 baseline evidence only.");
        }
    }

    private static void ValidateSecondRunProfile(JsonObject source, List<string> errors)
    {
        var profile = RequireObject(source, "secondRunProfile");
        var runId = GetString(profile, "runId", "");
        if (!runId.StartsWith("FEAT163-SECOND-RUN-", StringComparison.Ordinal))
        {
            errors.Add("FEAT163_SECOND_RUN_PROFILE_INVALID: secondRunProfile.runId must identify a FEAT-163 second run.");
        }

        if (runId.Contains("FEAT154", StringComparison.OrdinalIgnoreCase) ||
            !GetBool(profile, "distinctFromFirstRun"))
        {
            errors.Add("FEAT163_FEAT154_REUSE_FORBIDDEN: second run must be distinct from FEAT-154.");
        }

        var environmentProfile = GetString(profile, "environmentProfile", "");
        if (environmentProfile.Contains("local_only", StringComparison.OrdinalIgnoreCase) ||
            environmentProfile.Contains("private_chain_only", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("FEAT163_SECOND_RUN_PROFILE_INVALID: run profile cannot be local-only or private-chain-only.");
        }

        var dataScope = GetString(profile, "dataScope", "");
        if (dataScope is not ("synthetic" or "non_confidential" or "synthetic_or_non_confidential"))
        {
            errors.Add("FEAT163_DATA_SCOPE_INVALID: data scope must be synthetic or non-confidential.");
        }

        RequireValue(profile, "evidenceMode", "sanitized_public_fixture_with_restricted_refs", errors, "FEAT163_SECOND_RUN_PROFILE_INVALID");
        var runWindow = RequireObject(profile, "runWindow");
        foreach (var property in new[] { "plannedStart", "plannedEnd", "timeBasis" })
        {
            if (string.IsNullOrWhiteSpace(GetString(runWindow, property, "")))
            {
                errors.Add($"FEAT163_SECOND_RUN_PROFILE_INVALID: runWindow.{property} is required.");
            }
        }
    }

    private static void ValidateUpstreamRefs(JsonObject source, List<string> errors)
    {
        var upstream = RequireObject(source, "upstreamRefs");
        foreach (var policy in RequiredUpstreamPolicies)
        {
            if (!upstream.ContainsKey(policy.Key))
            {
                errors.Add($"{policy.Diagnostic}: upstreamRefs.{policy.Key} is required.");
                continue;
            }

            var value = RequireObject(upstream, policy.Key);
            RequireValue(value, "featureId", policy.FeatureId, errors, policy.Diagnostic);
            RequireValue(value, "status", policy.RequiredStatus, errors, policy.Diagnostic);
            RequireBool(value, "requiredForScoreMovement", policy.RequiredForScore, errors, policy.Diagnostic);
            if (policy.RequiresSha256)
            {
                RequireSha256(value, "sha256Hash", errors, policy.Diagnostic);
            }

            if (policy.RequiresCommit)
            {
                RequireCommitHash(value, "commitHash", errors, policy.Diagnostic);
            }
        }

        var feat154 = RequireObject(upstream, "feat154");
        RequireValue(feat154, "sha256Hash", AcceptedFeat154ManifestHash, errors, "FEAT163_STALE_FEAT154_REF");
    }

    private static void ValidateEvidenceGates(JsonObject source, List<string> errors)
    {
        var gates = RequireObject(source, "evidenceGates");
        foreach (var gateKey in RequiredGateKeys)
        {
            if (!gates.ContainsKey(gateKey))
            {
                errors.Add($"FEAT163_REQUIRED_GATE_MISSING: evidenceGates.{gateKey} is required.");
                continue;
            }

            var gate = RequireObject(gates, gateKey);
            RequireBool(gate, "required", true, errors, "FEAT163_REQUIRED_GATE_INVALID");
            if (string.IsNullOrWhiteSpace(GetString(gate, "gateId", "")) ||
                string.IsNullOrWhiteSpace(GetString(gate, "resultCode", "")) ||
                string.IsNullOrWhiteSpace(GetString(gate, "publicSummaryRef", "")))
            {
                errors.Add($"FEAT163_REQUIRED_GATE_INVALID: evidenceGates.{gateKey} is missing gate id, result code, or summary ref.");
            }
        }
    }

    private static void ValidateOperationalEvidence(JsonObject source, List<string> errors)
    {
        var evidence = RequireObject(source, "operationalEvidence");
        var profile = RequireObject(source, "secondRunProfile");
        var runWindow = RequireObject(profile, "runWindow");
        var plannedStart = ParseDateTime(GetString(runWindow, "plannedStart", ""), "secondRunProfile.runWindow.plannedStart", errors);
        var plannedEnd = ParseDateTime(GetString(runWindow, "plannedEnd", ""), "secondRunProfile.runWindow.plannedEnd", errors);

        ValidateMonitoringAlerting(evidence, plannedStart, plannedEnd, errors);
        ValidateBackupRestore(evidence, errors);
        ValidateSupportOperatorHandoff(evidence, errors);
        ValidateSecuritySupportFreshness(evidence, errors);
        ValidateIncidentResponseWalkthrough(evidence, errors);
        ValidatePostmortem(evidence, errors);
        ValidateRestrictedBoundary(evidence, errors);
    }

    private static void ValidateMonitoringAlerting(
        JsonObject evidence,
        DateTimeOffset? plannedStart,
        DateTimeOffset? plannedEnd,
        List<string> errors)
    {
        var gate = RequireOperationalGate(evidence, "monitoringAlerting", "FEAT163_MONITORING_ALERTING_ACCEPTED", "FEAT163_MONITORING_ALERTING_BLOCKED", errors);
        var windowStart = ParseDateTime(GetString(gate, "windowStart", ""), "operationalEvidence.monitoringAlerting.windowStart", errors);
        var windowEnd = ParseDateTime(GetString(gate, "windowEnd", ""), "operationalEvidence.monitoringAlerting.windowEnd", errors);
        if (plannedStart is not null && plannedEnd is not null && windowStart is not null && windowEnd is not null &&
            (windowStart > plannedStart || windowEnd < plannedEnd))
        {
            errors.Add("FEAT163_MONITORING_ALERTING_BLOCKED: monitoring window must cover the second-run window.");
        }

        RequireStringArrayContains(gate, "alertResultCodes", "FEAT163_MONITORING_ALERTING_ACCEPTED", errors, "FEAT163_MONITORING_ALERTING_BLOCKED");
        ValidateRestrictedRefs(gate, "monitoringAlerting", errors);
        RequireSignoffRoles(gate, "monitoringAlerting", errors);
    }

    private static void ValidateBackupRestore(JsonObject evidence, List<string> errors)
    {
        var gate = RequireOperationalGate(evidence, "backupRestore", "FEAT163_BACKUP_RESTORE_ACCEPTED", "FEAT163_BACKUP_RESTORE_BLOCKED", errors);
        var mode = GetString(gate, "restoreEvidenceMode", "");
        if (mode is not ("run_specific" or "same_profile_current"))
        {
            errors.Add("FEAT163_BACKUP_RESTORE_BLOCKED: restoreEvidenceMode must be run_specific or same_profile_current.");
        }

        var compatibility = GetString(gate, "profileCompatibility", "");
        if (compatibility is not ("run_specific" or "same_profile_current"))
        {
            errors.Add("FEAT163_BACKUP_RESTORE_BLOCKED: profileCompatibility must be run_specific or same_profile_current.");
        }

        ValidateRestrictedRefs(gate, "backupRestore", errors);
        RequireSignoffRoles(gate, "backupRestore", errors);
    }

    private static void ValidateSupportOperatorHandoff(JsonObject evidence, List<string> errors)
    {
        var gate = RequireOperationalGate(evidence, "supportOperatorHandoff", "FEAT163_SUPPORT_OPERATOR_HANDOFF_ACCEPTED", "FEAT163_SUPPORT_OPERATOR_HANDOFF_BLOCKED", errors);
        RequireNonEmptyArray(gate, "supportCategories", errors, "FEAT163_SUPPORT_OPERATOR_HANDOFF_BLOCKED");
        RequireNonEmptyArray(gate, "escalationPathRefs", errors, "FEAT163_SUPPORT_OPERATOR_HANDOFF_BLOCKED");
        RequireBool(gate, "privateIdentityPublished", false, errors, "FEAT163_SUPPORT_OPERATOR_HANDOFF_BLOCKED");
        ValidateRestrictedRefs(gate, "supportOperatorHandoff", errors);
        RequireSignoffRoles(gate, "supportOperatorHandoff", errors);
    }

    private static void ValidateSecuritySupportFreshness(JsonObject evidence, List<string> errors)
    {
        var gate = RequireOperationalGate(evidence, "securitySupportFreshness", "FEAT163_SECURITY_SUPPORT_FRESHNESS_ACCEPTED", "FEAT163_SECURITY_SUPPORT_FRESHNESS_BLOCKED", errors);
        RequireValue(gate, "feat134Currentness", "current", errors, "FEAT163_SECURITY_SUPPORT_FRESHNESS_BLOCKED");
        ParseDateTime(GetString(gate, "freshnessCheckedAt", ""), "operationalEvidence.securitySupportFreshness.freshnessCheckedAt", errors);
        var maxAgeDays = GetInt(gate, "maxAgeDays", 0);
        if (maxAgeDays is <= 0 or > 30)
        {
            errors.Add("FEAT163_SECURITY_SUPPORT_FRESHNESS_BLOCKED: maxAgeDays must be between 1 and 30.");
        }

        ValidateRestrictedRefs(gate, "securitySupportFreshness", errors);
        RequireSignoffRoles(gate, "securitySupportFreshness", errors);
    }

    private static void ValidateIncidentResponseWalkthrough(JsonObject evidence, List<string> errors)
    {
        var gate = RequireOperationalGate(evidence, "incidentResponseWalkthrough", "FEAT163_INCIDENT_RESPONSE_ACCEPTED", "FEAT163_INCIDENT_RESPONSE_BLOCKED", errors);
        var noIncident = RequireNestedObject(gate, "noIncidentDeclaration", "FEAT163_INCIDENT_RESPONSE_BLOCKED", errors);
        foreach (var property in new[] { "monitoringWindowRef", "incidentRegisterRef" })
        {
            if (string.IsNullOrWhiteSpace(GetString(noIncident, property, "")))
            {
                errors.Add($"FEAT163_INCIDENT_RESPONSE_BLOCKED: noIncidentDeclaration.{property} is required.");
            }
        }

        RequireValue(noIncident, "resultCode", "FEAT163_INCIDENT_RESPONSE_ACCEPTED", errors, "FEAT163_INCIDENT_RESPONSE_BLOCKED");

        var simulated = RequireNestedObject(gate, "simulatedIncident", "FEAT163_INCIDENT_RESPONSE_BLOCKED", errors);
        foreach (var property in new[] { "reasonCode", "accountabilityRole", "timelineSummary" })
        {
            if (string.IsNullOrWhiteSpace(GetString(simulated, property, "")))
            {
                errors.Add($"FEAT163_INCIDENT_RESPONSE_BLOCKED: simulatedIncident.{property} is required.");
            }
        }

        RequireValue(simulated, "resultCode", "FEAT163_INCIDENT_RESPONSE_ACCEPTED", errors, "FEAT163_INCIDENT_RESPONSE_BLOCKED");
        RequireValue(simulated, "status", "accepted", errors, "FEAT163_INCIDENT_RESPONSE_BLOCKED");
        ValidateRestrictedRefs(gate, "incidentResponseWalkthrough", errors);
        RequireSignoffRoles(gate, "incidentResponseWalkthrough", errors);
    }

    private static void ValidatePostmortem(JsonObject evidence, List<string> errors)
    {
        var gate = RequireOperationalGate(evidence, "postmortem", "FEAT163_POSTMORTEM_ACCEPTED", "FEAT163_POSTMORTEM_BLOCKED", errors);
        RequireNonEmptyArray(gate, "findingsCategories", errors, "FEAT163_POSTMORTEM_BLOCKED");
        RequireNonEmptyArray(gate, "followUpRefs", errors, "FEAT163_POSTMORTEM_BLOCKED");
        ValidateRestrictedRefs(gate, "postmortem", errors);
        RequireSignoffRoles(gate, "postmortem", errors);
    }

    private static void ValidateRestrictedBoundary(JsonObject evidence, List<string> errors)
    {
        var gate = RequireOperationalGate(evidence, "restrictedBoundary", "FEAT163_NO_SECRET_SCAN_ACCEPTED", "FEAT163_NO_SECRET_SCAN_BLOCKED", errors);
        RequireBool(gate, "payloadPublished", false, errors, "FEAT163_NO_SECRET_SCAN_BLOCKED");
        RequireBool(gate, "restrictedRefsOnly", true, errors, "FEAT163_NO_SECRET_SCAN_BLOCKED");
        RequireNonEmptyArray(gate, "scannerFamilies", errors, "FEAT163_NO_SECRET_SCAN_BLOCKED");
        ValidateRestrictedRefs(gate, "restrictedBoundary", errors);
        RequireSignoffRoles(gate, "restrictedBoundary", errors);
    }

    private static JsonObject RequireOperationalGate(
        JsonObject evidence,
        string key,
        string expectedResultCode,
        string diagnostic,
        List<string> errors)
    {
        if (!evidence.TryGetPropertyValue(key, out var node) || node is not JsonObject gate)
        {
            errors.Add($"{diagnostic}: operationalEvidence.{key} is required.");
            return new JsonObject();
        }

        RequireValue(gate, "status", "accepted", errors, diagnostic);
        RequireValue(gate, "resultCode", expectedResultCode, errors, diagnostic);
        if (string.IsNullOrWhiteSpace(GetString(gate, "publicSummaryRef", "")))
        {
            errors.Add($"{diagnostic}: operationalEvidence.{key}.publicSummaryRef is required.");
        }

        return gate;
    }

    private static JsonObject RequireNestedObject(JsonObject value, string property, string diagnostic, List<string> errors)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonObject obj)
        {
            return obj;
        }

        errors.Add($"{diagnostic}: {property} is required.");
        return new JsonObject();
    }

    private static void ValidateRestrictedRefs(JsonObject gate, string gateName, List<string> errors)
    {
        if (!gate.TryGetPropertyValue("restrictedEvidenceRefs", out var node) || node is not JsonArray refs)
        {
            errors.Add($"FEAT163_RESTRICTED_REFS_INVALID: operationalEvidence.{gateName}.restrictedEvidenceRefs is required.");
            return;
        }

        foreach (var refNode in refs.OfType<JsonObject>())
        {
            if (string.IsNullOrWhiteSpace(GetString(refNode, "refId", "")))
            {
                errors.Add($"FEAT163_RESTRICTED_REFS_INVALID: operationalEvidence.{gateName} restricted ref id is required.");
            }

            RequireSha256(refNode, "sha256Hash", errors, "FEAT163_RESTRICTED_REFS_INVALID");
            RequireBool(refNode, "payloadPublished", false, errors, "FEAT163_RESTRICTED_PAYLOAD_FORBIDDEN");
        }
    }

    private static void RequireSignoffRoles(JsonObject gate, string gateName, List<string> errors) =>
        RequireNonEmptyArray(gate, "signoffRoles", errors, $"FEAT163_SIGNOFF_ROLE_MISSING: operationalEvidence.{gateName}");

    private static void RequireStringArrayContains(
        JsonObject value,
        string property,
        string expected,
        List<string> errors,
        string diagnostic)
    {
        if (!value.TryGetPropertyValue(property, out var node) || node is not JsonArray array ||
            !array.Any(item => item?.GetValue<string>() == expected))
        {
            errors.Add($"{diagnostic}: {property} must include {expected}.");
        }
    }

    private static void RequireNonEmptyArray(JsonObject value, string property, List<string> errors, string diagnostic)
    {
        if (!value.TryGetPropertyValue(property, out var node) || node is not JsonArray array || array.Count == 0)
        {
            errors.Add($"{diagnostic}: {property} must contain at least one item.");
        }
    }

    private static DateTimeOffset? ParseDateTime(string value, string label, List<string> errors)
    {
        if (DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return parsed;
        }

        errors.Add($"FEAT163_OPERATIONAL_TIME_INVALID: {label} must be a valid timestamp.");
        return null;
    }

    private static void ValidateRestrictedEvidencePolicy(JsonObject source, List<string> errors)
    {
        var policy = RequireObject(source, "restrictedEvidencePolicy");
        RequireBool(policy, "payloadPublished", false, errors, "FEAT163_RESTRICTED_PAYLOAD_FORBIDDEN");
    }

    private static void ValidatePublicSafety(JsonObject source, List<string> errors)
    {
        var safety = RequireObject(source, "publicSafety");
        RequireBool(safety, "noSecretScanRequired", true, errors, "FEAT163_NO_SECRET_SCAN_REQUIRED");
        RequireBool(safety, "allowPrivatePathStrings", false, errors, "FEAT163_PRIVATE_PATH_FORBIDDEN");
    }

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
                    errors.Add($"FEAT163_PRIVATE_MATERIAL_FORBIDDEN: {property} contains forbidden marker {forbidden}.");
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

    private static void RequireValue(JsonObject value, string property, string expected, List<string> errors, string diagnostic)
    {
        var actual = GetString(value, property, "");
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            errors.Add($"{diagnostic}: {property} expected {expected} but found {actual}.");
        }
    }

    private static void RequireInt(JsonObject value, string property, int expected, List<string> errors, string diagnostic)
    {
        if (GetInt(value, property, int.MinValue) != expected)
        {
            errors.Add($"{diagnostic}: {property} expected {expected}.");
        }
    }

    private static void RequireBool(JsonObject value, string property, bool expected, List<string> errors, string diagnostic)
    {
        if (GetBool(value, property, !expected) != expected)
        {
            errors.Add($"{diagnostic}: {property} expected {expected}.");
        }
    }

    private static void RequireSha256(JsonObject value, string property, List<string> errors, string diagnostic)
    {
        var observed = NormalizeHash(GetString(value, property, ""));
        if (observed.Length != 64 || observed.Any(c => !Uri.IsHexDigit(c)))
        {
            errors.Add($"{diagnostic}: {property} must be a SHA-256 hash.");
        }
    }

    private static void RequireCommitHash(JsonObject value, string property, List<string> errors, string diagnostic)
    {
        var observed = GetString(value, property, "");
        if (observed.Length != 40 || observed.Any(c => !Uri.IsHexDigit(c)))
        {
            errors.Add($"{diagnostic}: {property} must be a git commit hash.");
        }
    }

    private static string NormalizeHash(string value) =>
        value.StartsWith("sha256:", StringComparison.Ordinal) ? value[7..] : value;

    private static void RequireFileHash(string path, string expectedHash, string label, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"{label} file missing: {path}");
            return;
        }

        var expected = NormalizeHash(expectedHash);
        var observed = FileSha256Hex(path);
        if (!string.Equals(observed, expected, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{label} mismatch. Expected {expected}, observed {observed}.");
        }
    }

    private static void RequireGitHead(string repoRoot, string expectedCommit, string label, List<string> errors)
    {
        var observed = TryReadGitHead(repoRoot);
        if (string.IsNullOrWhiteSpace(observed))
        {
            errors.Add($"{label} git HEAD missing: {repoRoot}");
            return;
        }

        if (!string.Equals(observed, expectedCommit, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{label} mismatch. Expected {expectedCommit}, observed {observed}.");
        }
    }

    private static string? TryReadGitHead(string repoRoot)
    {
        var gitPath = Path.Combine(repoRoot, ".git");
        if (!Directory.Exists(gitPath) && !File.Exists(gitPath))
        {
            return null;
        }

        var gitDir = gitPath;
        if (File.Exists(gitPath))
        {
            var marker = File.ReadAllText(gitPath).Trim();
            const string prefix = "gitdir:";
            if (!marker.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relative = marker[prefix.Length..].Trim();
            gitDir = Path.GetFullPath(Path.IsPathRooted(relative) ? relative : Path.Combine(repoRoot, relative));
        }

        var headPath = Path.Combine(gitDir, "HEAD");
        if (!File.Exists(headPath))
        {
            return null;
        }

        var head = File.ReadAllText(headPath).Trim();
        const string refPrefix = "ref:";
        if (!head.StartsWith(refPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return head;
        }

        var refPath = Path.Combine(gitDir, head[refPrefix.Length..].Trim().Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : null;
    }

    private sealed record UpstreamPolicy(
        string Key,
        string FeatureId,
        string RequiredStatus,
        bool RequiredForScore,
        bool RequiresSha256,
        bool RequiresCommit,
        string Diagnostic);
}
