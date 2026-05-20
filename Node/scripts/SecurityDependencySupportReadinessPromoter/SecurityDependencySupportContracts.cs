using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SecurityDependencySupportReadinessPromoter;

public sealed record SecurityDependencySupportPromotionPaths(
    string WorkspaceRoot,
    string SourceRoot,
    string SchemasRoot,
    string ExamplesRoot,
    string OutputRoot)
{
    public const string ReadinessSourceFolder = "Security-Dependency-Support-Readiness";
    public const string PackageFileName = "security-dependency-support-package.json";
    public const string CatalogFileName = "security-dependency-support-catalog.json";

    public string DefaultSourceInput => Path.Combine(ExamplesRoot, "release-baseline", PackageFileName);

    public string CatalogPath => Path.Combine(OutputRoot, CatalogFileName);

    public string ArchivesRoot => Path.Combine(OutputRoot, "archives");

    public static SecurityDependencySupportPromotionPaths FromWorkspaceRoot(string workspaceRoot)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var sourceRoot = Path.Combine(
            fullRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            ReadinessSourceFolder);
        return new SecurityDependencySupportPromotionPaths(
            fullRoot,
            sourceRoot,
            Path.Combine(sourceRoot, "schemas"),
            Path.Combine(sourceRoot, "examples"),
            Path.Combine(
                fullRoot,
                "hush-documents",
                "PrivateServer_ElectronicVoting",
                ReadinessSourceFolder));
    }
}

public sealed record SecurityDependencySupportSourceSet(
    JsonObject Package,
    JsonObject DependencyInventory,
    JsonObject LicenseScan,
    JsonObject VulnerabilityScan,
    JsonObject DisclosureProcess,
    JsonObject AccessibilityEvidence,
    JsonObject VoterClientGuidance,
    JsonObject SupportReadiness,
    JsonObject SupportExportPrivacyProof,
    JsonObject Exceptions);

public sealed record SecurityDependencySupportCheck(
    string CheckId,
    string Name,
    string Status,
    string Reason);

public sealed record SecurityDependencySupportMaterialFinding(
    string Boundary,
    string RelativePath,
    string Category,
    string Evidence);

public sealed record SecurityDependencySupportCheckSet(
    string Status,
    IReadOnlyList<SecurityDependencySupportCheck> Checks,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> NotApplicable,
    IReadOnlyList<SecurityDependencySupportMaterialFinding> ForbiddenMaterialFindings)
{
    public bool BlocksAcceptedEvidence => Blockers.Count > 0 || ForbiddenMaterialFindings.Count > 0;
}

public sealed record SecurityDependencySupportGeneratedArtifact(
    string RelativePath,
    string Content,
    string Sha256Hash);

public sealed record SecurityDependencySupportGeneratedPackage(
    string Status,
    SecurityDependencySupportCheckSet CheckResult,
    IReadOnlyList<SecurityDependencySupportGeneratedArtifact> Artifacts,
    IReadOnlyList<SecurityDependencySupportMaterialFinding> ScanFindings);

public sealed class SecurityDependencySupportPromotionException : Exception
{
    public SecurityDependencySupportPromotionException(string message, IEnumerable<string>? details = null)
        : base(message)
    {
        Details = details?.ToArray() ?? [];
    }

    public IReadOnlyList<string> Details { get; }
}

public static class SecurityDependencySupportContracts
{
    public const string FeatureId = "FEAT-134";
    public const string AcceptanceGate = "AT-RDY-014";
    public const string ReadinessFragmentId = "RDY-EVID-AT-RDY-014-FEAT-134-001";
    public const string CanonicalizationVersion = "feat134-canonical-json-v1";

    public static readonly string[] RequiredSchemaFiles =
    [
        "security-dependency-support-package.schema.json",
        "dependency-inventory.schema.json",
        "license-scan-normalized.schema.json",
        "vulnerability-scan-normalized.schema.json",
        "disclosure-process.schema.json",
        "accessibility-evidence-index.schema.json",
        "voter-client-integrity-guidance.schema.json",
        "support-readiness-index.schema.json",
        "support-export-privacy-proof.schema.json",
        "security-dependency-support-exceptions.schema.json",
        "security-dependency-support-readiness-fragment.schema.json",
        "security-support-handoff.schema.json",
        "security-dependency-support-manifest.schema.json",
        "security-dependency-support-catalog.schema.json",
    ];

    public static readonly string[] RequiredSdsCheckIds =
    [
        "SDS-000",
        "SDS-001",
        "SDS-002",
        "SDS-003",
        "SDS-004",
        "SDS-005",
        "SDS-006",
        "SDS-007",
        "SDS-008",
        "SDS-009",
        "SDS-010",
    ];

    public static readonly string[] RequiredSourceRefKeys =
    [
        "dependencyInventory",
        "licenseScan",
        "vulnerabilityScan",
        "disclosureProcess",
        "accessibilityEvidence",
        "voterClientGuidance",
        "supportReadiness",
        "supportExportPrivacyProof",
        "exceptions",
    ];

    public static readonly string[] RequiredOutputFiles =
    [
        SecurityDependencySupportPromotionPaths.PackageFileName,
        "dependency-inventory.json",
        "license-scan-normalized.json",
        "vulnerability-scan-normalized.json",
        "disclosure-process.json",
        "accessibility-evidence-index.json",
        "voter-client-integrity-guidance.md",
        "support-readiness-index.json",
        "support-export-privacy-proof.json",
        "security-dependency-support-exceptions.json",
        "security-dependency-support-readiness-fragment.json",
        "security-support-handoff.json",
        "restricted-security-dependency-support-evidence-index.md",
        "public-safe-security-dependency-support-summary.md",
        "security-dependency-support-manifest.json",
    ];

    public static readonly string[] RequiredSupportRunbooks =
    [
        "runbooks/support-access-login.md",
        "runbooks/support-voter-client.md",
        "runbooks/support-receipt-verification.md",
        "runbooks/support-trustee-key-ceremony.md",
        "runbooks/support-publication-counting.md",
        "runbooks/support-deployment-runtime.md",
        "runbooks/support-security-report.md",
        "runbooks/support-accessibility.md",
        "runbooks/support-dispute-anomaly-escalation.md",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

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

            JsonObject schema;
            try
            {
                schema = ReadJsonObject(path, schemaFile);
            }
            catch (Exception ex)
            {
                errors.Add($"Schema {schemaFile} is not loadable JSON: {ex.Message}");
                continue;
            }

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

    public static IReadOnlyList<string> ValidateSourceFixtureSet(
        SecurityDependencySupportPromotionPaths paths,
        string? sourceInput = null,
        DateTimeOffset? generatedAt = null)
    {
        var errors = new List<string>();

        try
        {
            var sources = LoadSources(paths, sourceInput);
            errors.AddRange(ValidatePackage(sources.Package));
            errors.AddRange(ValidateJsonRequired(sources.DependencyInventory, "dependency-inventory.json", [
                "inventoryId",
                "releaseScopeId",
                "components",
                "lockfileRefs",
                "scannerProvenance",
                "knownOmissions",
                "generatedAt",
                "claimImpact",
            ]));
            errors.AddRange(ValidateJsonRequired(sources.LicenseScan, "license-scan-normalized.json", [
                "scanId",
                "scannerProvenance",
                "policyVersion",
                "licenseFindings",
                "unknownLicenses",
                "restrictedLicenses",
                "rejectedLicenses",
                "noticeObligations",
                "exceptionRefs",
                "claimImpact",
            ]));
            errors.AddRange(ValidateJsonRequired(sources.VulnerabilityScan, "vulnerability-scan-normalized.json", [
                "scanId",
                "scannerProvenance",
                "severityThresholds",
                "findings",
                "openCriticalHigh",
                "warnings",
                "acceptedExceptions",
                "freshness",
                "claimImpact",
            ]));
            errors.AddRange(ValidateJsonRequired(sources.DisclosureProcess, "disclosure-process.json", [
                "processId",
                "intakeChannel",
                "triageOwner",
                "severityRubric",
                "embargoRule",
                "customerNotificationRule",
                "publicPrivateBoundary",
                "pilotReadinessStatus",
                "claimImpact",
            ]));
            errors.AddRange(ValidateJsonRequired(sources.AccessibilityEvidence, "accessibility-evidence-index.json", [
                "indexId",
                "feat103EvidenceRefs",
                "workflowCoverage",
                "staleCoverage",
                "missingCoverage",
                "blockingWorkflows",
                "claimImpact",
            ]));
            errors.AddRange(ValidateJsonRequired(sources.VoterClientGuidance, "voter-client-integrity-guidance.json", [
                "guidanceId",
                "claimBearingPaths",
                "supportedPaths",
                "conditionalPaths",
                "unsupportedPaths",
                "notAvailableV1Paths",
                "mobileEvidenceRefs",
                "sameDeviceLimitations",
                "fallbackWarnings",
                "claimImpact",
            ]));
            errors.AddRange(ValidateJsonRequired(sources.SupportReadiness, "support-readiness-index.json", [
                "indexId",
                "supportCategories",
                "runbookRefs",
                "escalationRefs",
                "privacyRules",
                "ownerStatus",
                "missingRunbooks",
                "claimImpact",
            ]));
            errors.AddRange(ValidateJsonRequired(sources.SupportExportPrivacyProof, "support-export-privacy-proof.json", [
                "proofId",
                "exportSchemaRef",
                "fixtureRefs",
                "allowedFields",
                "restrictedFields",
                "forbiddenFields",
                "scanResults",
                "privacyResult",
                "claimImpact",
            ]));
            errors.AddRange(ValidateJsonRequired(sources.Exceptions, "security-dependency-support-exceptions.json", [
                "schemaVersion",
                "exceptions",
            ]));

            var checkSet = SecurityDependencySupportChecker.Evaluate(paths, sources, generatedAt);
            if (checkSet.Checks.Select(check => check.CheckId).Except(RequiredSdsCheckIds, StringComparer.Ordinal).Any() ||
                RequiredSdsCheckIds.Except(checkSet.Checks.Select(check => check.CheckId), StringComparer.Ordinal).Any())
            {
                errors.Add("SDS check set is incomplete.");
            }
        }
        catch (SecurityDependencySupportPromotionException ex)
        {
            errors.Add(ex.Message);
            errors.AddRange(ex.Details);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        return errors;
    }

    public static SecurityDependencySupportSourceSet LoadSources(
        SecurityDependencySupportPromotionPaths paths,
        string? sourceInput = null)
    {
        var packagePath = ResolveSourceInput(paths, sourceInput);
        EnsurePathUnder(paths.SourceRoot, packagePath, "source package");
        var package = ReadJsonObject(packagePath, SecurityDependencySupportPromotionPaths.PackageFileName);
        var sourceRefs = RequireObject(package, "sourceRefs");

        foreach (var key in RequiredSourceRefKeys)
        {
            if (!sourceRefs.ContainsKey(key))
            {
                throw new SecurityDependencySupportPromotionException(
                    "Security dependency package sourceRefs are incomplete.",
                    [$"Missing sourceRefs.{key}"]);
            }
        }

        return new SecurityDependencySupportSourceSet(
            package,
            ReadJsonObject(ResolveSourceRef(paths, package, "dependencyInventory"), "dependency-inventory.json"),
            ReadJsonObject(ResolveSourceRef(paths, package, "licenseScan"), "license-scan-normalized.json"),
            ReadJsonObject(ResolveSourceRef(paths, package, "vulnerabilityScan"), "vulnerability-scan-normalized.json"),
            ReadJsonObject(ResolveSourceRef(paths, package, "disclosureProcess"), "disclosure-process.json"),
            ReadJsonObject(ResolveSourceRef(paths, package, "accessibilityEvidence"), "accessibility-evidence-index.json"),
            ReadJsonObject(ResolveSourceRef(paths, package, "voterClientGuidance"), "voter-client-integrity-guidance.json"),
            ReadJsonObject(ResolveSourceRef(paths, package, "supportReadiness"), "support-readiness-index.json"),
            ReadJsonObject(ResolveSourceRef(paths, package, "supportExportPrivacyProof"), "support-export-privacy-proof.json"),
            ReadJsonObject(ResolveSourceRef(paths, package, "exceptions"), "security-dependency-support-exceptions.json"));
    }

    public static IReadOnlyList<string> ValidatePackage(JsonObject package)
    {
        var errors = ValidateJsonRequired(package, SecurityDependencySupportPromotionPaths.PackageFileName, [
            "schemaVersion",
            "packageId",
            "featureId",
            "sourceGap",
            "acceptanceGate",
            "releaseScope",
            "claimLevel",
            "generatedAt",
            "generatedBy",
            "status",
            "sourceRefs",
            "componentScopes",
            "claimBearingClientPaths",
            "scannerProvenanceRefs",
            "dependencyInventoryRef",
            "licenseScanRef",
            "vulnerabilityScanRef",
            "disclosureProcessRef",
            "accessibilityEvidenceRef",
            "voterClientGuidanceRef",
            "supportReadinessRef",
            "supportExportPrivacyProofRef",
            "exceptionRefs",
            "freshness",
            "claimImpact",
            "generatedViews",
            "handoffRef",
            "manifestRef",
        ]).ToList();

        if (GetString(package, "featureId") != FeatureId)
        {
            errors.Add("security-dependency-support-package.json featureId must be FEAT-134.");
        }

        if (GetString(package, "acceptanceGate") != AcceptanceGate)
        {
            errors.Add("security-dependency-support-package.json acceptanceGate must be AT-RDY-014.");
        }

        var claimBearingPaths = GetStringSet(package, "claimBearingClientPaths");
        if (!claimBearingPaths.Contains("desktop_web") || !claimBearingPaths.Contains("mobile_web"))
        {
            errors.Add("claimBearingClientPaths must include desktop_web and mobile_web.");
        }

        return errors;
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

    public static JsonObject ReadJsonObject(string path, string label)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ??
            throw new SecurityDependencySupportPromotionException($"{label} is not a JSON object.");
    }

    public static string CanonicalJson(JsonNode node)
    {
        return node.ToJsonString(JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    public static string Sha256Hex(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Sha256FileHex(string path)
    {
        using var stream = File.OpenRead(path);
        var bytes = SHA256.HashData(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string GetString(JsonObject value, string property, string fallback = "") =>
        value.TryGetPropertyValue(property, out var node) && node is not null
            ? node.GetValue<string>()
            : fallback;

    public static bool GetBool(JsonObject value, string property, bool fallback = false) =>
        value.TryGetPropertyValue(property, out var node) && node is not null &&
        node.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? node.GetValue<bool>()
            : fallback;

    public static JsonObject RequireObject(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonObject obj)
        {
            return obj;
        }

        throw new SecurityDependencySupportPromotionException($"Missing object property: {property}");
    }

    public static JsonArray RequireArray(JsonObject value, string property)
    {
        if (value.TryGetPropertyValue(property, out var node) && node is JsonArray array)
        {
            return array;
        }

        throw new SecurityDependencySupportPromotionException($"Missing array property: {property}");
    }

    public static HashSet<string> GetStringSet(JsonObject value, string property)
    {
        if (!value.TryGetPropertyValue(property, out var node) || node is not JsonArray array)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return array
            .OfType<JsonValue>()
            .Select(item => item.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
    }

    public static string ResolveSourceRef(
        SecurityDependencySupportPromotionPaths paths,
        JsonObject package,
        string key)
    {
        var sourceRefs = RequireObject(package, "sourceRefs");
        if (!sourceRefs.TryGetPropertyValue(key, out var node) || node is null)
        {
            throw new SecurityDependencySupportPromotionException($"Missing sourceRefs.{key}");
        }

        var relativePath = node.GetValue<string>();
        var fullPath = Path.GetFullPath(Path.Combine(paths.SourceRoot, relativePath));
        EnsurePathUnder(paths.SourceRoot, fullPath, $"sourceRefs.{key}");
        if (!File.Exists(fullPath))
        {
            throw new SecurityDependencySupportPromotionException(
                "Security dependency source ref does not exist.",
                [$"{key}: {relativePath}"]);
        }

        return fullPath;
    }

    public static string ResolveSourceInput(
        SecurityDependencySupportPromotionPaths paths,
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
            ? Path.Combine(fullPath, SecurityDependencySupportPromotionPaths.PackageFileName)
            : fullPath;
    }

    public static void EnsurePathUnder(string root, string candidate, string label)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityDependencySupportPromotionException(
                "Security dependency support path failed containment checks.",
                [$"{label}: {candidate}"]);
        }
    }

    public static IReadOnlyList<SecurityDependencySupportMaterialFinding> ScanForbiddenMaterial(
        JsonNode node,
        string boundary,
        string relativePath)
    {
        var findings = new List<SecurityDependencySupportMaterialFinding>();
        ScanNode(node, boundary, relativePath, "$", findings);
        return findings;
    }

    public static IReadOnlyList<SecurityDependencySupportMaterialFinding> ScanForbiddenMaterial(
        string content,
        string boundary,
        string relativePath)
    {
        var findings = new List<SecurityDependencySupportMaterialFinding>();
        AddForbiddenFindings(content, boundary, relativePath, findings);
        return findings;
    }

    private static void ScanNode(
        JsonNode? node,
        string boundary,
        string relativePath,
        string path,
        List<SecurityDependencySupportMaterialFinding> findings)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj)
                {
                    ScanNode(child, boundary, relativePath, $"{path}.{key}", findings);
                }

                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    ScanNode(array[index], boundary, relativePath, $"{path}[{index}]", findings);
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                AddForbiddenFindings(text, boundary, relativePath, findings);
                break;
        }
    }

    private static void AddForbiddenFindings(
        string text,
        string boundary,
        string relativePath,
        List<SecurityDependencySupportMaterialFinding> findings)
    {
        var lower = text.ToLowerInvariant();
        AddIfContains(lower, "begin private key", "private_key");
        AddIfContains(lower, "aws_secret_access_key", "credential");
        AddIfContains(lower, "password=", "credential");
        AddIfContains(lower, "raw auth token", "credential");
        AddIfContains(lower, "arn:aws:kms", "provider_kms_identifier");
        AddIfContains(lower, "receipt secret", "receipt_secret");
        AddIfContains(lower, "vote choice", "vote_choice");
        AddIfContains(lower, "named voter identity joined to ballot", "voter_identity_ballot_join");
        AddIfContains(lower, "voteridentityreceiptjoin=true", "voter_identity_receipt_join");
        AddIfContains(lower, "raw trustee share", "trustee_raw_share");

        void AddIfContains(string source, string needle, string category)
        {
            if (source.Contains(needle, StringComparison.Ordinal))
            {
                findings.Add(new SecurityDependencySupportMaterialFinding(
                    boundary,
                    relativePath,
                    category,
                    needle));
            }
        }
    }
}
