using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GovernanceCustomerHandoffPromoter;

public static class GovernanceCustomerHandoffContracts
{
    public const string FeatureId = "FEAT-166";
    public const string SourceSchemaVersion = "governance-customer-handoff-source/v1";
    public const string CurrentRegisterVersionId = "RDY-REG-v0.1.7";
    public const string TargetDimensionId = "RDY-DIM-010";
    public const string TargetBlockerId = "RDY-BLOCK-INTERNAL_AUDIT_95_DIM010-001";
    public const string AllowedScoreMovement = "8 -> 9";

    public static readonly string[] RequiredSchemaFiles =
    [
        GovernanceCustomerHandoffPromotionPaths.SourceSchemaFileName,
        GovernanceCustomerHandoffPromotionPaths.PackageManifestSchemaFileName,
    ];

    public static readonly string[] RequiredCatalogFiles =
    [
        GovernanceCustomerHandoffPromotionPaths.ResponsibilityDomainCatalogFileName,
        GovernanceCustomerHandoffPromotionPaths.NonClaimCatalogFileName,
        GovernanceCustomerHandoffPromotionPaths.ExternalPrerequisiteRoutingCatalogFileName,
        GovernanceCustomerHandoffPromotionPaths.ResultCodeCatalogFileName,
    ];

    public static readonly string[] RequiredExampleFiles =
    [
        Path.Combine("release-baseline", GovernanceCustomerHandoffPromotionPaths.SourceFileName),
        Path.Combine("negative", GovernanceCustomerHandoffPromotionPaths.NegativeFixtureCatalogFileName),
    ];

    public static readonly string[] RequiredUpstreamFeatures =
    [
        "FEAT-140",
        "FEAT-149",
        "FEAT-156",
        "FEAT-157",
        "FEAT-158",
        "FEAT-159",
        "FEAT-160",
        "FEAT-161",
        "FEAT-162",
        "FEAT-163",
        "FEAT-164",
        "FEAT-165",
    ];

    private static readonly string[] RequiredResultCodeSeverities =
    [
        "accepted",
        "warning",
        "limited",
        "blocking",
    ];

    public static readonly string[] RequiredResponsibilityDomains =
    [
        "hush-technical-proof",
        "hush-operational-evidence",
        "customer-governance",
        "external-legal-authority",
        "public-state-prerequisite",
        "independent-certification",
        "external-auditor-review",
        "promotion-owner-action",
    ];

    public static readonly string[] RequiredNonClaimCategories =
    [
        "legal-sufficiency",
        "agm-management",
        "certification",
        "external-audit-acceptance",
        "public-state-readiness",
        "production-rollout-approval",
        "customer-governance-decision",
        "readiness-register-mutation",
    ];

    public static readonly string[] RequiredExternalPrerequisiteRoutes =
    [
        "GCH-ROUTE-CUSTOMER-AUTHORITY-REVIEW",
        "GCH-ROUTE-EXTERNAL-LEGAL-REVIEW",
        "GCH-ROUTE-AUDITOR-PACKAGE-REVIEW",
        "GCH-ROUTE-PROMOTION-OWNER-REVIEW",
    ];

    public static readonly string[] RequiredCustomerChecklistSections =
    [
        "GCH-CHECKLIST-AUTHORITY",
        "GCH-CHECKLIST-GOVERNANCE-RULES",
        "GCH-CHECKLIST-EXTERNAL-PREREQUISITES",
        "GCH-CHECKLIST-AUDITOR-ROUTING",
        "GCH-CHECKLIST-PROMOTION-OWNER",
    ];

    private sealed record ResponsibilityDomainProfile(
        string OwnerClass,
        string Visibility,
        string NonClaimBoundary,
        string ClaimEffect);

    private static readonly IReadOnlyDictionary<string, ResponsibilityDomainProfile> RequiredResponsibilityProfiles =
        new Dictionary<string, ResponsibilityDomainProfile>(StringComparer.Ordinal)
        {
            ["hush-technical-proof"] = new("hush", "public", "legal-sufficiency", "technical-evidence-only"),
            ["hush-operational-evidence"] = new("hush", "public", "production-rollout-approval", "technical-evidence-only"),
            ["customer-governance"] = new("customer", "restricted-ref-only", "customer-governance-decision", "customer-owned-decision"),
            ["external-legal-authority"] = new("external-legal-authority", "restricted-ref-only", "legal-sufficiency", "external-boundary-only"),
            ["public-state-prerequisite"] = new("public-state-authority", "restricted-ref-only", "public-state-readiness", "external-boundary-only"),
            ["independent-certification"] = new("independent-certifier", "restricted-ref-only", "certification", "external-boundary-only"),
            ["external-auditor-review"] = new("external-auditor", "restricted-ref-only", "external-audit-acceptance", "review-consumer-only"),
            ["promotion-owner-action"] = new("promotion-owner", "public", "readiness-register-mutation", "proposal-only"),
        };

    private static readonly IReadOnlyDictionary<string, string> RequiredNonClaimResultCodes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["legal-sufficiency"] = "GCH-LEGAL-SUFFICIENCY-OVERCLAIM",
            ["agm-management"] = "GCH-AGM-MANAGEMENT-OVERCLAIM",
            ["certification"] = "GCH-CERTIFICATION-OVERCLAIM",
            ["external-audit-acceptance"] = "GCH-AUDITOR-ACCEPTANCE-OVERCLAIM",
            ["public-state-readiness"] = "GCH-PUBLIC-STATE-READINESS-OVERCLAIM",
            ["production-rollout-approval"] = "GCH-PRODUCTION-APPROVAL-OVERCLAIM",
            ["customer-governance-decision"] = "GCH-CUSTOMER-DECISION-OVERCLAIM",
            ["readiness-register-mutation"] = "GCH-DIRECT-REGISTER-MUTATION",
        };

    private sealed record ExternalPrerequisiteRouteProfile(
        string OwnerClass,
        string SourceFeature,
        string EvidenceVisibility,
        string RouteStatus,
        string EvidenceRefType);

    private static readonly IReadOnlyDictionary<string, ExternalPrerequisiteRouteProfile> RequiredExternalPrerequisiteRouteProfiles =
        new Dictionary<string, ExternalPrerequisiteRouteProfile>(StringComparer.Ordinal)
        {
            ["GCH-ROUTE-CUSTOMER-AUTHORITY-REVIEW"] = new("customer", "FEAT-149", "private-only", "blocked-pending-customer-input", "no-payload-restricted-ref"),
            ["GCH-ROUTE-EXTERNAL-LEGAL-REVIEW"] = new("external-authority", "FEAT-149", "restricted-ref-only", "blocked-pending-external-input", "no-payload-restricted-ref"),
            ["GCH-ROUTE-AUDITOR-PACKAGE-REVIEW"] = new("external-auditor", "FEAT-166", "public", "public-package-ready", "public-package-ref"),
            ["GCH-ROUTE-PROMOTION-OWNER-REVIEW"] = new("promotion-owner", "FEAT-156", "public", "proposal-review-ready", "promotion-owner-ref"),
        };

    private sealed record CustomerChecklistSectionProfile(
        string OwnerClass,
        string Status,
        string EvidenceRefType,
        string EvidenceVisibility);

    private static readonly IReadOnlyDictionary<string, CustomerChecklistSectionProfile> RequiredCustomerChecklistSectionProfiles =
        new Dictionary<string, CustomerChecklistSectionProfile>(StringComparer.Ordinal)
        {
            ["GCH-CHECKLIST-AUTHORITY"] = new("customer", "required-private-answer", "no-payload-restricted-ref", "restricted-ref-only"),
            ["GCH-CHECKLIST-GOVERNANCE-RULES"] = new("customer", "required-private-answer", "no-payload-restricted-ref", "restricted-ref-only"),
            ["GCH-CHECKLIST-EXTERNAL-PREREQUISITES"] = new("external-authority", "required-external-input", "no-payload-restricted-ref", "restricted-ref-only"),
            ["GCH-CHECKLIST-AUDITOR-ROUTING"] = new("external-auditor", "public-package-review", "public-package-ref", "public"),
            ["GCH-CHECKLIST-PROMOTION-OWNER"] = new("promotion-owner", "promotion-owner-review", "promotion-owner-ref", "public"),
        };

    public static JsonObject ValidateForPromotion(
        GovernanceCustomerHandoffPromotionPaths paths,
        string? sourceInput,
        bool publicOnly)
    {
        var errors = new List<string>();
        errors.AddRange(ValidateSchemaSet(paths.SchemasRoot));
        errors.AddRange(ValidatePublicRepositorySet(paths));

        var resultCodes = LoadResultCodeSeverities(paths);
        errors.AddRange(ValidateResultCodeCatalog(resultCodes));
        errors.AddRange(ValidateResponsibilityCatalog(paths));
        errors.AddRange(ValidateNonClaimCatalog(paths));
        errors.AddRange(ValidateExternalPrerequisiteCatalog(paths));
        errors.AddRange(ValidateNegativeFixtures(paths, resultCodes.Keys.ToHashSet(StringComparer.Ordinal)));

        var source = LoadSource(paths, sourceInput);
        errors.AddRange(ValidateSource(source, publicOnly));

        if (errors.Count > 0)
        {
            throw new GovernanceCustomerHandoffPromotionException(
                "FEAT-166 governance customer handoff validation failed.",
                errors.Distinct(StringComparer.Ordinal).ToArray());
        }

        return source;
    }

    public static IReadOnlyList<string> ValidateSchemaSet(string schemasRoot)
    {
        var errors = new List<string>();
        foreach (var fileName in RequiredSchemaFiles)
        {
            var path = Path.Combine(schemasRoot, fileName);
            if (!File.Exists(path))
            {
                errors.Add($"GCH-SCHEMA-MISSING: required schema is missing: {fileName}");
                continue;
            }

            try
            {
                var schema = ReadJsonObject(path, $"schema {fileName}");
                if (!schema.ContainsKey("$schema"))
                {
                    errors.Add($"GCH-SCHEMA-INVALID: schema has no $schema field: {fileName}");
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"GCH-SCHEMA-INVALID: schema {fileName} is not valid JSON: {ex.Message}");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidatePublicRepositorySet(GovernanceCustomerHandoffPromotionPaths paths)
    {
        var errors = new List<string>();
        if (!Directory.Exists(paths.PublicRepositoryRoot))
        {
            errors.Add($"GCH-PUBLIC-REPOSITORY-MISSING: {paths.PublicRepositoryRoot}");
            return errors;
        }

        foreach (var fileName in RequiredCatalogFiles)
        {
            var path = Path.Combine(paths.CatalogsRoot, fileName);
            if (!File.Exists(path))
            {
                errors.Add($"GCH-CATALOG-MISSING: {fileName}");
            }
            else
            {
                _ = ReadJsonObject(path, $"catalog {fileName}");
            }
        }

        foreach (var fileName in RequiredExampleFiles)
        {
            var path = Path.Combine(paths.ExamplesRoot, fileName);
            if (!File.Exists(path))
            {
                errors.Add($"GCH-EXAMPLE-MISSING: {fileName}");
            }
            else
            {
                _ = ReadJsonObject(path, $"example {fileName}");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSource(JsonObject source, bool publicOnly = false)
    {
        var errors = new List<string>();
        if (GetStringOrNull(source, "schemaVersion") != SourceSchemaVersion)
        {
            errors.Add("GCH-SOURCE-SCHEMA-INVALID: source schemaVersion is not supported.");
        }

        if (GetStringOrNull(source, "featureId") != FeatureId)
        {
            errors.Add("GCH-FEATURE-ID-INVALID: source featureId must be FEAT-166.");
        }

        ValidateReadinessBaseline(source, errors);
        ValidateScoreProposal(source, errors);
        ValidateUpstreamEvidence(source, errors);
        ValidateResponsibilityDomains(source, errors);
        ValidateNonClaimBoundaries(source, errors);
        ValidateExternalPrerequisiteRouting(source, errors);
        ValidateCustomerChecklist(source, errors);
        ValidatePublicBoundary(source, publicOnly, errors);
        ValidateRestrictedEvidencePolicy(source, errors);
        ScanForbiddenMaterial(source, "$", errors);

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static JsonObject LoadSource(GovernanceCustomerHandoffPromotionPaths paths, string? sourceInput = null)
    {
        var path = string.IsNullOrWhiteSpace(sourceInput) ? paths.DefaultSourcePath : sourceInput;
        return ReadJsonObject(path, "FEAT-166 source fixture");
    }

    public static JsonObject ReadJsonObject(string path, string description)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        if (node is not JsonObject obj)
        {
            throw new GovernanceCustomerHandoffPromotionException(
                $"Expected {description} to be a JSON object.",
                [$"Path: {path}"]);
        }

        return obj;
    }

    public static string GetString(JsonObject obj, string propertyName)
    {
        var value = GetStringOrNull(obj, propertyName);
        if (value is null)
        {
            throw new GovernanceCustomerHandoffPromotionException(
                "FEAT-166 generated package construction failed.",
                [$"Missing string property: {propertyName}"]);
        }

        return value;
    }

    public static JsonObject RequireObject(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonObject child)
        {
            return child;
        }

        throw new GovernanceCustomerHandoffPromotionException(
            "FEAT-166 generated package construction failed.",
            [$"Missing object property: {propertyName}"]);
    }

    public static JsonArray RequireArray(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonArray child)
        {
            return child;
        }

        throw new GovernanceCustomerHandoffPromotionException(
            "FEAT-166 generated package construction failed.",
            [$"Missing array property: {propertyName}"]);
    }

    public static string CanonicalJson(JsonNode node) => GovernanceCustomerHandoffCanonicalJson.Serialize(node);

    public static string NormalizeLineEndings(string value) => GovernanceCustomerHandoffCanonicalJson.NormalizeLineEndings(value);

    public static string Sha256Hex(string content) => GovernanceCustomerHandoffCanonicalJson.ComputeSha256(content);

    public static string FileSha256Hex(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public static void EnsurePathUnder(string root, string candidate, string description)
    {
        var rootFullPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidateFullPath = Path.GetFullPath(candidate);
        if (!candidateFullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new GovernanceCustomerHandoffPromotionException(
                "FEAT-166 path containment check failed.",
                [$"{description}: {candidateFullPath} is outside {rootFullPath}"]);
        }
    }

    public static JsonArray ToStringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static void ValidateReadinessBaseline(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "readinessBaseline", out var baseline))
        {
            errors.Add("GCH-STALE-READINESS-BASELINE: readinessBaseline is missing.");
            return;
        }

        if (GetStringOrNull(baseline, "registerVersion") != CurrentRegisterVersionId ||
            GetStringOrNull(baseline, "dimension") != TargetDimensionId ||
            GetStringOrNull(baseline, "blocker") != TargetBlockerId ||
            GetIntOrNull(baseline, "currentScore") != 8 ||
            GetIntOrNull(baseline, "targetScore") != 9)
        {
            errors.Add("GCH-STALE-READINESS-BASELINE: source must bind RDY-REG-v0.1.7 RDY-DIM-010 8 -> 9.");
        }

        if (TryGetObject(baseline, "registerManifestRef", out var registerRef))
        {
            ValidatePublicRef(registerRef, "readinessBaseline.registerManifestRef", errors);
        }
    }

    private static void ValidateScoreProposal(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "scoreProposal", out var proposal))
        {
            errors.Add("GCH-SCORE-PROPOSAL-MISMATCH: scoreProposal is missing.");
            return;
        }

        if (GetStringOrNull(proposal, "dimension") != TargetDimensionId ||
            GetStringOrNull(proposal, "movement") != AllowedScoreMovement)
        {
            errors.Add("GCH-SCORE-PROPOSAL-MISMATCH: FEAT-166 can only propose RDY-DIM-010 8 -> 9.");
        }

        if (GetBoolOrNull(proposal, "directRegisterMutation") != false)
        {
            errors.Add("GCH-DIRECT-REGISTER-MUTATION: FEAT-166 must not mutate the readiness register directly.");
        }
    }

    private static void ValidateUpstreamEvidence(JsonObject source, List<string> errors)
    {
        if (!TryGetArray(source, "upstreamEvidence", out var evidence) || evidence.Count == 0)
        {
            errors.Add("GCH-UPSTREAM-REF-STALE: upstreamEvidence is missing.");
            return;
        }

        var byFeature = evidence
            .OfType<JsonObject>()
            .Where(item => !string.IsNullOrWhiteSpace(GetStringOrNull(item, "featureId")))
            .GroupBy(item => GetString(item, "featureId"), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var feature in RequiredUpstreamFeatures)
        {
            if (!byFeature.TryGetValue(feature, out var items) || items.Length == 0)
            {
                errors.Add($"GCH-UPSTREAM-REF-STALE: required upstream evidence is missing: {feature}");
                continue;
            }

            foreach (var item in items)
            {
                var status = GetStringOrNull(item, "status");
                if (feature == "FEAT-156")
                {
                    if (status != "accepted-current")
                    {
                        errors.Add("GCH-UPSTREAM-REF-STALE: FEAT-156 production rollout wording must be accepted-current.");
                    }
                }
                else if (status is not ("accepted-current" or "accepted-input"))
                {
                    errors.Add($"GCH-UPSTREAM-REF-STALE: {feature} evidence must be accepted-current or accepted-input.");
                }

                var freshness = GetStringOrNull(item, "freshness") ?? string.Empty;
                if (!freshness.Contains("current-for-rdy-reg-v0.1.7", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"GCH-UPSTREAM-REF-STALE: {feature} freshness must bind to RDY-REG-v0.1.7.");
                }

                if (!TryGetArray(item, "evidenceRefs", out var refs) || refs.Count == 0)
                {
                    errors.Add($"GCH-UPSTREAM-REF-STALE: {feature} must include at least one evidence ref.");
                    continue;
                }

                foreach (var reference in refs.OfType<JsonObject>())
                {
                    ValidatePublicRef(reference, $"upstreamEvidence.{feature}", errors);
                }
            }
        }
    }

    private static void ValidateResponsibilityDomains(JsonObject source, List<string> errors)
    {
        if (!TryGetArray(source, "responsibilityDomains", out var domains))
        {
            errors.Add("GCH-RESPONSIBILITY-DOMAIN-MISSING: responsibilityDomains is missing.");
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < domains.Count; index++)
        {
            if (domains[index] is not JsonObject domain)
            {
                errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: responsibilityDomains[{index}] must be an object.");
                continue;
            }

            var domainName = GetStringOrNull(domain, "domain");
            if (!string.IsNullOrWhiteSpace(domainName) && !names.Add(domainName))
            {
                duplicates.Add(domainName);
            }

            ValidateResponsibilityDomainRow(
                domain,
                $"responsibilityDomains[{index}]",
                requireEvidenceRefs: true,
                requireBlockingResultCodes: false,
                errors);
        }

        foreach (var duplicate in duplicates)
        {
            errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: responsibility domain is duplicated: {duplicate}");
        }

        foreach (var required in RequiredResponsibilityDomains)
        {
            if (!names.Contains(required))
            {
                errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: required responsibility domain is missing: {required}");
            }
        }
    }

    private static void ValidateNonClaimBoundaries(JsonObject source, List<string> errors)
    {
        if (!TryGetArray(source, "nonClaimBoundaries", out var boundaries))
        {
            errors.Add("GCH-NON-CLAIM-MISSING: nonClaimBoundaries is missing.");
            return;
        }

        var categories = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < boundaries.Count; index++)
        {
            if (boundaries[index] is not JsonObject boundary)
            {
                errors.Add($"GCH-NON-CLAIM-MISSING: nonClaimBoundaries[{index}] must be an object.");
                continue;
            }

            var category = GetStringOrNull(boundary, "category");
            if (!string.IsNullOrWhiteSpace(category) && !categories.Add(category))
            {
                duplicates.Add(category);
            }

            ValidateNonClaimBoundaryRow(boundary, $"nonClaimBoundaries[{index}]", errors);
        }

        foreach (var duplicate in duplicates)
        {
            errors.Add($"GCH-NON-CLAIM-MISSING: non-claim category is duplicated: {duplicate}");
        }

        foreach (var required in RequiredNonClaimCategories)
        {
            if (!categories.Contains(required))
            {
                errors.Add($"GCH-NON-CLAIM-MISSING: required non-claim category is missing: {required}");
            }
        }
    }

    private static void ValidateResponsibilityDomainRow(
        JsonObject domain,
        string context,
        bool requireEvidenceRefs,
        bool requireBlockingResultCodes,
        List<string> errors)
    {
        var domainName = GetStringOrNull(domain, "domain");
        if (string.IsNullOrWhiteSpace(domainName))
        {
            errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: {context} domain is missing.");
            return;
        }

        if (!RequiredResponsibilityProfiles.TryGetValue(domainName, out var profile))
        {
            errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: {context} uses unsupported responsibility domain: {domainName}");
            return;
        }

        ValidateExpectedResponsibilityField(domain, domainName, "ownerClass", profile.OwnerClass, errors);
        ValidateExpectedResponsibilityField(domain, domainName, "visibility", profile.Visibility, errors);
        ValidateExpectedResponsibilityField(domain, domainName, "nonClaimBoundary", profile.NonClaimBoundary, errors);
        ValidateExpectedResponsibilityField(domain, domainName, "missingInputBehavior", "fail-closed", errors);
        ValidateExpectedResponsibilityField(domain, domainName, "claimEffect", profile.ClaimEffect, errors);

        if (!TryGetArray(domain, "requiredEvidence", out var requiredEvidence) || requiredEvidence.Count == 0)
        {
            errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: {domainName} requires requiredEvidence.");
        }

        if (!TryGetArray(domain, "mayClaim", out var mayClaim) || mayClaim.Count == 0)
        {
            errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: {domainName} requires at least one mayClaim boundary.");
        }

        if (!TryGetArray(domain, "mustNotClaim", out var mustNotClaim) || mustNotClaim.Count == 0)
        {
            errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: {domainName} requires at least one mustNotClaim boundary.");
        }

        if (requireEvidenceRefs)
        {
            if (!TryGetArray(domain, "evidenceRefs", out var evidenceRefs) || evidenceRefs.Count == 0)
            {
                errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: {domainName} requires at least one evidence ref.");
            }
            else
            {
                for (var index = 0; index < evidenceRefs.Count; index++)
                {
                    if (evidenceRefs[index] is JsonObject evidenceRef)
                    {
                        ValidatePublicRef(evidenceRef, $"responsibilityDomains.{domainName}.evidenceRefs[{index}]", errors);
                    }
                    else
                    {
                        errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: {domainName} evidenceRefs[{index}] must be an object.");
                    }
                }
            }
        }

        if (requireBlockingResultCodes &&
            (!TryGetArray(domain, "blockingResultCodes", out var blockingResultCodes) || blockingResultCodes.Count == 0))
        {
            errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: {domainName} requires blockingResultCodes.");
        }
    }

    private static void ValidateExpectedResponsibilityField(
        JsonObject domain,
        string domainName,
        string propertyName,
        string expected,
        List<string> errors)
    {
        var actual = GetStringOrNull(domain, propertyName);
        if (actual == "private-only")
        {
            errors.Add($"GCH-PRIVATE-MATERIAL-PUBLISHED: {domainName} uses private-only {propertyName}.");
        }

        if (actual != expected)
        {
            errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: {domainName} must set {propertyName} to {expected}.");
        }
    }

    private static void ValidateNonClaimBoundaryRow(JsonObject boundary, string context, List<string> errors)
    {
        var category = GetStringOrNull(boundary, "category");
        if (string.IsNullOrWhiteSpace(category))
        {
            errors.Add($"GCH-NON-CLAIM-MISSING: {context} category is missing.");
            return;
        }

        if (!RequiredNonClaimResultCodes.TryGetValue(category, out var expectedResultCode))
        {
            errors.Add($"GCH-NON-CLAIM-MISSING: {context} uses unsupported non-claim category: {category}");
            return;
        }

        if (string.IsNullOrWhiteSpace(GetStringOrNull(boundary, "forbiddenClaim")))
        {
            errors.Add($"GCH-NON-CLAIM-MISSING: {category} requires forbiddenClaim.");
        }

        if (string.IsNullOrWhiteSpace(GetStringOrNull(boundary, "allowedPublicWording")))
        {
            errors.Add($"GCH-NON-CLAIM-MISSING: {category} requires allowedPublicWording.");
        }

        var blockingResultCode = GetStringOrNull(boundary, "blockingResultCode");
        if (blockingResultCode != expectedResultCode)
        {
            errors.Add($"GCH-NON-CLAIM-MISSING: {category} must use blockingResultCode {expectedResultCode}.");
        }
    }

    private static void ValidateExternalPrerequisiteRouting(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "externalPrerequisiteRouting", out var routing))
        {
            errors.Add("GCH-EXTERNAL-PREREQUISITE-OVERCLAIM: externalPrerequisiteRouting is missing.");
            return;
        }

        if (GetBoolOrNull(routing, "feat149Alignment") != true)
        {
            errors.Add("GCH-EXTERNAL-PREREQUISITE-OVERCLAIM: external routing must align to FEAT-149.");
        }

        if (GetBoolOrNull(routing, "publicStateReadinessResolvedByFeat166") != false)
        {
            errors.Add("GCH-PUBLIC-STATE-READINESS-OVERCLAIM: FEAT-166 must not resolve public/state readiness.");
        }

        ValidateExternalPrerequisiteRoutes(routing, requireEvidenceRefs: true, errors);
    }

    private static void ValidateCustomerChecklist(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "customerChecklist", out var checklist))
        {
            errors.Add("GCH-CUSTOMER-ANSWER-PUBLISHED: customerChecklist is missing.");
            return;
        }

        if (GetBoolOrNull(checklist, "genericQuestionsOnly") != true ||
            GetBoolOrNull(checklist, "customerAnswersPublished") != false)
        {
            errors.Add("GCH-CUSTOMER-ANSWER-PUBLISHED: customer checklist must publish generic questions only and no answers.");
        }

        ValidateCustomerChecklistSections(checklist, errors);
    }

    private static void ValidateExternalPrerequisiteRoutes(JsonObject routing, bool requireEvidenceRefs, List<string> errors)
    {
        if (!TryGetArray(routing, "routes", out var routes))
        {
            errors.Add("GCH-EXTERNAL-PREREQUISITE-ROUTE-MISSING: external prerequisite routes are missing.");
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < routes.Count; index++)
        {
            if (routes[index] is not JsonObject route)
            {
                errors.Add($"GCH-EXTERNAL-PREREQUISITE-ROUTE-MISSING: routes[{index}] must be an object.");
                continue;
            }

            var routeId = GetStringOrNull(route, "routeId");
            if (!string.IsNullOrWhiteSpace(routeId) && !names.Add(routeId))
            {
                duplicates.Add(routeId);
            }

            ValidateExternalPrerequisiteRouteRow(route, $"routes[{index}]", requireEvidenceRefs, errors);
        }

        foreach (var duplicate in duplicates)
        {
            errors.Add($"GCH-EXTERNAL-PREREQUISITE-ROUTE-MISSING: external prerequisite route is duplicated: {duplicate}");
        }

        foreach (var required in RequiredExternalPrerequisiteRoutes)
        {
            if (!names.Contains(required))
            {
                errors.Add($"GCH-EXTERNAL-PREREQUISITE-ROUTE-MISSING: required external prerequisite route is missing: {required}");
            }
        }
    }

    private static void ValidateExternalPrerequisiteRouteRow(
        JsonObject route,
        string context,
        bool requireEvidenceRefs,
        List<string> errors)
    {
        var routeId = GetStringOrNull(route, "routeId");
        if (string.IsNullOrWhiteSpace(routeId))
        {
            errors.Add($"GCH-EXTERNAL-PREREQUISITE-ROUTE-MISSING: {context} routeId is missing.");
            return;
        }

        if (!RequiredExternalPrerequisiteRouteProfiles.TryGetValue(routeId, out var profile))
        {
            errors.Add($"GCH-EXTERNAL-PREREQUISITE-ROUTE-MISSING: unsupported external prerequisite route: {routeId}");
            return;
        }

        ValidateExpectedRouteField(route, routeId, "ownerClass", profile.OwnerClass, errors);
        ValidateExpectedRouteField(route, routeId, "sourceFeature", profile.SourceFeature, errors);
        ValidateExpectedRouteField(route, routeId, "publicStateReadinessEffect", "external-boundary-only", errors);
        ValidateExpectedRouteField(route, routeId, "evidenceVisibility", profile.EvidenceVisibility, errors);
        ValidateExpectedRouteField(route, routeId, "routeStatus", profile.RouteStatus, errors);
        ValidateExpectedRouteField(route, routeId, "evidenceRefType", profile.EvidenceRefType, errors);
        ValidateExpectedRouteField(route, routeId, "missingInputBehavior", "fail-closed", errors);

        if (requireEvidenceRefs)
        {
            ValidateEvidenceRefs(
                route,
                "evidenceRefs",
                $"externalPrerequisiteRouting.{routeId}",
                profile.EvidenceVisibility == "private-only" ? "restricted-ref-only" : profile.EvidenceVisibility,
                "GCH-EXTERNAL-PREREQUISITE-ROUTE-MISSING",
                errors);
        }
    }

    private static void ValidateCustomerChecklistSections(JsonObject checklist, List<string> errors)
    {
        if (!TryGetArray(checklist, "sections", out var sections))
        {
            errors.Add("GCH-CUSTOMER-CHECKLIST-BOUNDARY-MISSING: customer checklist sections are missing.");
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < sections.Count; index++)
        {
            if (sections[index] is not JsonObject section)
            {
                errors.Add($"GCH-CUSTOMER-CHECKLIST-BOUNDARY-MISSING: customerChecklist.sections[{index}] must be an object.");
                continue;
            }

            var sectionId = GetStringOrNull(section, "sectionId");
            if (!string.IsNullOrWhiteSpace(sectionId) && !names.Add(sectionId))
            {
                duplicates.Add(sectionId);
            }

            ValidateCustomerChecklistSectionRow(section, $"customerChecklist.sections[{index}]", errors);
        }

        foreach (var duplicate in duplicates)
        {
            errors.Add($"GCH-CUSTOMER-CHECKLIST-BOUNDARY-MISSING: customer checklist section is duplicated: {duplicate}");
        }

        foreach (var required in RequiredCustomerChecklistSections)
        {
            if (!names.Contains(required))
            {
                errors.Add($"GCH-CUSTOMER-CHECKLIST-BOUNDARY-MISSING: required customer checklist section is missing: {required}");
            }
        }
    }

    private static void ValidateCustomerChecklistSectionRow(JsonObject section, string context, List<string> errors)
    {
        var sectionId = GetStringOrNull(section, "sectionId");
        if (string.IsNullOrWhiteSpace(sectionId))
        {
            errors.Add($"GCH-CUSTOMER-CHECKLIST-BOUNDARY-MISSING: {context} sectionId is missing.");
            return;
        }

        if (!RequiredCustomerChecklistSectionProfiles.TryGetValue(sectionId, out var profile))
        {
            errors.Add($"GCH-CUSTOMER-CHECKLIST-BOUNDARY-MISSING: unsupported customer checklist section: {sectionId}");
            return;
        }

        if (GetBoolOrNull(section, "answerPublished") != false)
        {
            errors.Add("GCH-CUSTOMER-ANSWER-PUBLISHED: customer checklist answers must not be published.");
        }

        ValidateExpectedChecklistField(section, sectionId, "ownerClass", profile.OwnerClass, errors);
        ValidateExpectedChecklistField(section, sectionId, "status", profile.Status, errors);
        ValidateExpectedChecklistField(section, sectionId, "evidenceRefType", profile.EvidenceRefType, errors);
        ValidateExpectedChecklistField(section, sectionId, "evidenceVisibility", profile.EvidenceVisibility, errors);
        ValidateExpectedChecklistField(section, sectionId, "missingInputBehavior", "fail-closed", errors);

        ValidateEvidenceRefs(
            section,
            "evidenceRefs",
            $"customerChecklist.{sectionId}",
            profile.EvidenceVisibility,
            "GCH-CUSTOMER-CHECKLIST-BOUNDARY-MISSING",
            errors);
    }

    private static void ValidateExpectedRouteField(
        JsonObject route,
        string routeId,
        string propertyName,
        string expected,
        List<string> errors)
    {
        if (GetStringOrNull(route, propertyName) != expected)
        {
            errors.Add($"GCH-EXTERNAL-PREREQUISITE-ROUTE-MISSING: {routeId} must set {propertyName} to {expected}.");
        }
    }

    private static void ValidateExpectedChecklistField(
        JsonObject section,
        string sectionId,
        string propertyName,
        string expected,
        List<string> errors)
    {
        if (GetStringOrNull(section, propertyName) != expected)
        {
            errors.Add($"GCH-CUSTOMER-CHECKLIST-BOUNDARY-MISSING: {sectionId} must set {propertyName} to {expected}.");
        }
    }

    private static void ValidateEvidenceRefs(
        JsonObject owner,
        string propertyName,
        string context,
        string expectedVisibility,
        string missingResultCode,
        List<string> errors)
    {
        if (!TryGetArray(owner, propertyName, out var refs) || refs.Count == 0)
        {
            errors.Add($"{missingResultCode}: {context} requires at least one evidence ref.");
            return;
        }

        for (var index = 0; index < refs.Count; index++)
        {
            if (refs[index] is not JsonObject reference)
            {
                errors.Add($"{missingResultCode}: {context}.{propertyName}[{index}] must be an object.");
                continue;
            }

            ValidatePublicRef(reference, $"{context}.{propertyName}[{index}]", errors);
            if (GetStringOrNull(reference, "visibility") != expectedVisibility)
            {
                errors.Add($"{missingResultCode}: {context}.{propertyName}[{index}] must use {expectedVisibility} visibility.");
            }
        }
    }

    private static void ValidatePublicBoundary(JsonObject source, bool publicOnly, List<string> errors)
    {
        if (!TryGetObject(source, "publicBoundary", out var boundary))
        {
            errors.Add("GCH-PUBLIC-ONLY-PRIVATE-DEPENDENCY: publicBoundary is missing.");
            return;
        }

        if (GetBoolOrNull(boundary, "publicOnlyValidation") != true)
        {
            errors.Add("GCH-PUBLIC-ONLY-PRIVATE-DEPENDENCY: public-only validation must be enabled.");
        }

        if (publicOnly && GetBoolOrNull(boundary, "publicOnlyValidation") != true)
        {
            errors.Add("GCH-PUBLIC-ONLY-PRIVATE-DEPENDENCY: -PublicOnly cannot depend on private checkouts or credentials.");
        }
    }

    private static void ValidateRestrictedEvidencePolicy(JsonObject source, List<string> errors)
    {
        if (!TryGetObject(source, "restrictedEvidencePolicy", out var policy))
        {
            errors.Add("GCH-RESTRICTED-PAYLOAD-PUBLISHED: restrictedEvidencePolicy is missing.");
            return;
        }

        if (GetBoolOrNull(policy, "publicRefsOnly") != true || GetBoolOrNull(policy, "payloadsPublishedHere") != false)
        {
            errors.Add("GCH-RESTRICTED-PAYLOAD-PUBLISHED: restricted payloads must not be published.");
        }

        if (TryGetArray(policy, "restrictedEvidenceRefs", out var refs))
        {
            foreach (var reference in refs.OfType<JsonObject>())
            {
                if (GetStringOrNull(reference, "visibility") != "restricted-ref-only" ||
                    GetBoolOrNull(reference, "payloadPublished") != false)
                {
                    errors.Add("GCH-RESTRICTED-PAYLOAD-PUBLISHED: restricted refs must be no-payload refs only.");
                }
            }
        }
    }

    public static IReadOnlyDictionary<string, string> LoadResultCodeSeverities(GovernanceCustomerHandoffPromotionPaths paths)
    {
        var path = Path.Combine(paths.CatalogsRoot, GovernanceCustomerHandoffPromotionPaths.ResultCodeCatalogFileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var catalog = ReadJsonObject(path, "result-code catalog");
        if (!TryGetArray(catalog, "resultCodes", out var codes))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in codes.OfType<JsonObject>())
        {
            var code = GetStringOrNull(item, "code");
            if (!string.IsNullOrWhiteSpace(code))
            {
                result[code] = GetStringOrNull(item, "severity") ?? string.Empty;
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ValidateResultCodeCatalog(IReadOnlyDictionary<string, string> resultCodes)
    {
        var errors = new List<string>();
        if (resultCodes.Count == 0)
        {
            errors.Add("GCH-RESULT-CODE-CATALOG-INVALID: result code catalog is missing or empty.");
            return errors;
        }

        var severities = resultCodes.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var severity in RequiredResultCodeSeverities)
        {
            if (!severities.Contains(severity))
            {
                errors.Add($"GCH-RESULT-CODE-CATALOG-INVALID: result-code catalog must include {severity} severity.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateResponsibilityCatalog(GovernanceCustomerHandoffPromotionPaths paths)
    {
        var errors = new List<string>();
        var path = Path.Combine(paths.CatalogsRoot, GovernanceCustomerHandoffPromotionPaths.ResponsibilityDomainCatalogFileName);
        if (!File.Exists(path))
        {
            errors.Add($"GCH-CATALOG-MISSING: {GovernanceCustomerHandoffPromotionPaths.ResponsibilityDomainCatalogFileName}");
            return errors;
        }

        var catalog = ReadJsonObject(path, "responsibility-domain catalog");
        if (!TryGetArray(catalog, "domains", out var domains))
        {
            errors.Add("GCH-RESPONSIBILITY-DOMAIN-MISSING: responsibility-domain catalog has no domains.");
            return errors;
        }

        var domainNames = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < domains.Count; index++)
        {
            if (domains[index] is not JsonObject domain)
            {
                errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: responsibility-domain catalog domains[{index}] must be an object.");
                continue;
            }

            var domainName = GetStringOrNull(domain, "domain");
            if (!string.IsNullOrWhiteSpace(domainName) && !domainNames.Add(domainName))
            {
                duplicates.Add(domainName);
            }

            ValidateResponsibilityDomainRow(
                domain,
                $"responsibility-domain catalog domains[{index}]",
                requireEvidenceRefs: false,
                requireBlockingResultCodes: true,
                errors);
        }

        foreach (var duplicate in duplicates)
        {
            errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: responsibility-domain catalog duplicates {duplicate}.");
        }

        foreach (var required in RequiredResponsibilityDomains)
        {
            if (!domainNames.Contains(required))
            {
                errors.Add($"GCH-RESPONSIBILITY-DOMAIN-MISSING: responsibility-domain catalog missing {required}.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateNonClaimCatalog(GovernanceCustomerHandoffPromotionPaths paths)
    {
        var errors = new List<string>();
        var path = Path.Combine(paths.CatalogsRoot, GovernanceCustomerHandoffPromotionPaths.NonClaimCatalogFileName);
        if (!File.Exists(path))
        {
            errors.Add($"GCH-CATALOG-MISSING: {GovernanceCustomerHandoffPromotionPaths.NonClaimCatalogFileName}");
            return errors;
        }

        var catalog = ReadJsonObject(path, "non-claim catalog");
        if (!TryGetArray(catalog, "boundaries", out var boundaries))
        {
            errors.Add("GCH-NON-CLAIM-MISSING: non-claim catalog has no boundaries.");
            return errors;
        }

        var categories = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < boundaries.Count; index++)
        {
            if (boundaries[index] is not JsonObject boundary)
            {
                errors.Add($"GCH-NON-CLAIM-MISSING: non-claim catalog boundaries[{index}] must be an object.");
                continue;
            }

            var category = GetStringOrNull(boundary, "category");
            if (!string.IsNullOrWhiteSpace(category) && !categories.Add(category))
            {
                duplicates.Add(category);
            }

            ValidateNonClaimBoundaryRow(boundary, $"non-claim catalog boundaries[{index}]", errors);
        }

        foreach (var duplicate in duplicates)
        {
            errors.Add($"GCH-NON-CLAIM-MISSING: non-claim catalog duplicates {duplicate}.");
        }

        foreach (var required in RequiredNonClaimCategories)
        {
            if (!categories.Contains(required))
            {
                errors.Add($"GCH-NON-CLAIM-MISSING: non-claim catalog missing {required}.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateExternalPrerequisiteCatalog(GovernanceCustomerHandoffPromotionPaths paths)
    {
        var errors = new List<string>();
        var path = Path.Combine(paths.CatalogsRoot, GovernanceCustomerHandoffPromotionPaths.ExternalPrerequisiteRoutingCatalogFileName);
        if (!File.Exists(path))
        {
            errors.Add($"GCH-CATALOG-MISSING: {GovernanceCustomerHandoffPromotionPaths.ExternalPrerequisiteRoutingCatalogFileName}");
            return errors;
        }

        var catalog = ReadJsonObject(path, "external prerequisite routing catalog");
        if (GetBoolOrNull(catalog, "feat149Alignment") != true ||
            GetBoolOrNull(catalog, "publicStateReadinessResolvedByFeat166") != false)
        {
            errors.Add("GCH-EXTERNAL-PREREQUISITE-OVERCLAIM: external-prerequisite catalog must align to FEAT-149 without resolving public/state readiness.");
        }

        ValidateExternalPrerequisiteRoutes(catalog, requireEvidenceRefs: false, errors);
        if (TryGetArray(catalog, "routes", out var routes))
        {
            foreach (var route in routes.OfType<JsonObject>())
            {
                var routeId = GetStringOrNull(route, "routeId") ?? "<missing>";
                if (string.IsNullOrWhiteSpace(GetStringOrNull(route, "publicSafeSummary")))
                {
                    errors.Add($"GCH-EXTERNAL-PREREQUISITE-ROUTE-MISSING: external-prerequisite catalog route {routeId} requires publicSafeSummary.");
                }
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateNegativeFixtures(GovernanceCustomerHandoffPromotionPaths paths, IReadOnlySet<string> resultCodes)
    {
        var errors = new List<string>();
        var path = Path.Combine(paths.ExamplesRoot, "negative", GovernanceCustomerHandoffPromotionPaths.NegativeFixtureCatalogFileName);
        if (!File.Exists(path))
        {
            errors.Add($"GCH-EXAMPLE-MISSING: {GovernanceCustomerHandoffPromotionPaths.NegativeFixtureCatalogFileName}");
            return errors;
        }

        var catalog = ReadJsonObject(path, "negative fixture catalog");
        if (!TryGetArray(catalog, "fixtures", out var fixtures) || fixtures.Count == 0)
        {
            errors.Add("GCH-NEGATIVE-FIXTURE-CATALOG-INVALID: fixtures are missing.");
            return errors;
        }

        foreach (var item in fixtures.OfType<JsonObject>())
        {
            var code = GetStringOrNull(item, "expectedResultCode");
            if (string.IsNullOrWhiteSpace(code) || !resultCodes.Contains(code))
            {
                errors.Add($"GCH-RESULT-CODE-CATALOG-INVALID: negative fixture references unknown result code: {code ?? "<missing>"}");
            }
        }

        return errors;
    }

    private static void ValidatePublicRef(JsonObject publicRef, string context, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(GetStringOrNull(publicRef, "id")) ||
            string.IsNullOrWhiteSpace(GetStringOrNull(publicRef, "source")) ||
            string.IsNullOrWhiteSpace(GetStringOrNull(publicRef, "ref")))
        {
            errors.Add($"GCH-UPSTREAM-REF-STALE: {context} must use id, source, and ref.");
        }

        if (!IsSha256(GetStringOrNull(publicRef, "hash")))
        {
            errors.Add($"GCH-UPSTREAM-REF-STALE: {context} hash is not a SHA-256 hex value.");
        }

        var visibility = GetStringOrNull(publicRef, "visibility");
        if (visibility == "private-only")
        {
            errors.Add($"GCH-PRIVATE-MATERIAL-PUBLISHED: {context} uses private-only visibility in a public source ref.");
        }
        else if (visibility is not ("public" or "restricted-ref-only"))
        {
            errors.Add($"GCH-UPSTREAM-REF-STALE: {context} visibility must be public or restricted-ref-only.");
        }
    }

    private static void ScanForbiddenMaterial(JsonNode? node, string path, List<string> errors)
    {
        if (ShouldSkipForbiddenScan(path))
        {
            return;
        }

        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    AddForbiddenNameFinding(name, errors);
                    ScanForbiddenMaterial(child, $"{path}.{name}", errors);
                }

                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    ScanForbiddenMaterial(array[index], $"{path}[{index}]", errors);
                }

                break;
            case JsonValue value:
                AddForbiddenValueFinding(value.ToJsonString().Trim('"'), errors);
                break;
        }
    }

    private static bool ShouldSkipForbiddenScan(string path) =>
        path.Contains(".forbiddenPublicMaterials", StringComparison.Ordinal) ||
        path.Contains(".mustNotClaim", StringComparison.Ordinal) ||
        path.Contains(".forbiddenClaim", StringComparison.Ordinal) ||
        path.Contains(".allowedPublicWording", StringComparison.Ordinal);

    private static void AddForbiddenNameFinding(string name, List<string> errors)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("customerlegaldocument", StringComparison.Ordinal) ||
            lower.Contains("customerchecklistanswer", StringComparison.Ordinal) ||
            lower.Contains("rawrestrictedpayload", StringComparison.Ordinal) ||
            lower.Contains("authorityname", StringComparison.Ordinal) ||
            lower.Contains("signoffrecord", StringComparison.Ordinal) ||
            lower.Contains("externalauditornote", StringComparison.Ordinal))
        {
            errors.Add("GCH-PRIVATE-MATERIAL-PUBLISHED: source contains restricted customer/governance material.");
        }
        else if (lower.Contains("privatepath", StringComparison.Ordinal) ||
            lower.Contains("localpath", StringComparison.Ordinal))
        {
            errors.Add("GCH-PRIVATE-MATERIAL-PUBLISHED: source contains private path material.");
        }
        else if (lower.Contains("secret", StringComparison.Ordinal) ||
            lower.Contains("credential", StringComparison.Ordinal) ||
            lower.Contains("token", StringComparison.Ordinal) ||
            lower.Contains("privatekey", StringComparison.Ordinal))
        {
            errors.Add("GCH-PRIVATE-MATERIAL-PUBLISHED: source contains secret or credential material.");
        }
    }

    private static void AddForbiddenValueFinding(string value, List<string> errors)
    {
        if (ContainsPrivatePath(value))
        {
            errors.Add("GCH-PRIVATE-MATERIAL-PUBLISHED: source contains a private or local path.");
        }

        var lower = value.ToLowerInvariant();
        if (lower.Contains("7 -> 8", StringComparison.Ordinal))
        {
            errors.Add("GCH-SCORE-PROPOSAL-MISMATCH: source contains forbidden replay or overclaim wording.");
        }

        if (lower.Contains("certifies legal sufficiency", StringComparison.Ordinal) ||
            lower.Contains("legal sufficiency is accepted", StringComparison.Ordinal) ||
            lower.Contains("legal sufficiency is certified", StringComparison.Ordinal) ||
            lower.Contains("provides legal advice", StringComparison.Ordinal))
        {
            errors.Add("GCH-LEGAL-SUFFICIENCY-OVERCLAIM: source claims legal sufficiency or legal advice.");
        }

        if (lower.Contains("manages the agm", StringComparison.Ordinal) ||
            lower.Contains("agm management is accepted", StringComparison.Ordinal) ||
            lower.Contains("meeting governance completion", StringComparison.Ordinal))
        {
            errors.Add("GCH-AGM-MANAGEMENT-OVERCLAIM: source claims AGM management completion.");
        }

        if (lower.Contains("certifies the election system", StringComparison.Ordinal) ||
            lower.Contains("certifies the election", StringComparison.Ordinal) ||
            lower.Contains("certification is accepted", StringComparison.Ordinal) ||
            lower.Contains("independent certification is granted", StringComparison.Ordinal) ||
            lower.Contains("system is certified by this public package", StringComparison.Ordinal))
        {
            errors.Add("GCH-CERTIFICATION-OVERCLAIM: source claims certification.");
        }

        if (lower.Contains("external audit acceptance is accepted", StringComparison.Ordinal) ||
            lower.Contains("external audit acceptance is granted", StringComparison.Ordinal) ||
            lower.Contains("records external auditor acceptance", StringComparison.Ordinal))
        {
            errors.Add("GCH-AUDITOR-ACCEPTANCE-OVERCLAIM: source claims external auditor acceptance.");
        }

        if (lower.Contains("public/state election readiness is accepted", StringComparison.Ordinal) ||
            lower.Contains("public/state election readiness is resolved", StringComparison.Ordinal) ||
            lower.Contains("public or state election readiness is resolved", StringComparison.Ordinal))
        {
            errors.Add("GCH-PUBLIC-STATE-READINESS-OVERCLAIM: source claims public/state election readiness.");
        }

        if (lower.Contains("production rollout approval is accepted", StringComparison.Ordinal) ||
            lower.Contains("production rollout accepted", StringComparison.Ordinal) ||
            lower.Contains("production rollout approval is granted", StringComparison.Ordinal) ||
            lower.Contains("grants production rollout approval", StringComparison.Ordinal))
        {
            errors.Add("GCH-PRODUCTION-APPROVAL-OVERCLAIM: source claims production rollout approval.");
        }

        if (lower.Contains("customer governance decisions are made by feat-166", StringComparison.Ordinal) ||
            lower.Contains("makes or publishes customer governance decisions", StringComparison.Ordinal))
        {
            errors.Add("GCH-CUSTOMER-DECISION-OVERCLAIM: source claims customer governance decision authority.");
        }

        if (lower.Contains("mutates the readiness register", StringComparison.Ordinal) ||
            lower.Contains("readiness-register mutation has happened", StringComparison.Ordinal) ||
            lower.Contains("claims final 95+ acceptance", StringComparison.Ordinal))
        {
            errors.Add("GCH-DIRECT-REGISTER-MUTATION: source claims readiness-register mutation.");
        }
    }

    private static bool ContainsPrivatePath(string? value) =>
        value is not null &&
        (value.Contains("PrivateServer_ElectronicVoting", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("hush-documents/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(@"C:\", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("/Users/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("/home/", StringComparison.OrdinalIgnoreCase));

    private static bool IsSha256(string? value) =>
        value is not null &&
        value.Length == 64 &&
        value.All(ch => (ch >= 'a' && ch <= 'f') || (ch >= '0' && ch <= '9'));

    private static bool TryGetObject(JsonObject obj, string propertyName, out JsonObject value)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonObject objectValue)
        {
            value = objectValue;
            return true;
        }

        value = new JsonObject();
        return false;
    }

    private static bool TryGetArray(JsonObject obj, string propertyName, out JsonArray value)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonArray arrayValue)
        {
            value = arrayValue;
            return true;
        }

        value = new JsonArray();
        return false;
    }

    private static string? GetStringOrNull(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is not null)
        {
            return node.GetValue<string>();
        }

        return null;
    }

    private static int? GetIntOrNull(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is not null)
        {
            return node.GetValue<int>();
        }

        return null;
    }

    private static bool? GetBoolOrNull(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is not null)
        {
            return node.GetValue<bool>();
        }

        return null;
    }
}
