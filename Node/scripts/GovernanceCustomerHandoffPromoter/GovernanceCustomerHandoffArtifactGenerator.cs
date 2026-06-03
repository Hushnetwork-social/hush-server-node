using System.Globalization;
using System.Text.Json.Nodes;

namespace GovernanceCustomerHandoffPromoter;

public sealed record GovernanceCustomerHandoffArtifact(string RelativePath, string Content)
{
    public string Sha256Hash => GovernanceCustomerHandoffContracts.Sha256Hex(Content);
}

public sealed record GovernanceCustomerHandoffGeneratedPackage(
    string Status,
    string PackageRoot,
    string RestrictedEvidenceRoot,
    JsonObject Source,
    IReadOnlyList<GovernanceCustomerHandoffArtifact> Artifacts,
    IReadOnlyList<GovernanceCustomerHandoffArtifact> RestrictedArtifacts)
{
    public int ArtifactCount => Artifacts.Count + RestrictedArtifacts.Count;
}

public static class GovernanceCustomerHandoffArtifactGenerator
{
    public const string PackageIndexPath = "governance-customer-handoff-package.json";
    public const string PackageManifestPath = "governance-customer-handoff-manifest.json";
    public const string PackageReadmePath = "README.md";
    public const string SourceSchemaCopyPath = "schemas/governance-customer-handoff-source.schema.json";
    public const string PackageManifestSchemaCopyPath = "schemas/governance-customer-handoff-package-manifest.schema.json";
    public const string ResponsibilityCatalogCopyPath = "catalogs/responsibility-domain-catalog.json";
    public const string NonClaimCatalogCopyPath = "catalogs/non-claim-catalog.json";
    public const string ExternalPrerequisiteCatalogCopyPath = "catalogs/external-prerequisite-routing-catalog.json";
    public const string ResultCodeCatalogCopyPath = "catalogs/result-code-catalog.json";
    public const string ReleaseBaselineSourceCopyPath = "examples/release-baseline/governance-customer-handoff-source.json";
    public const string NegativeFixturesCopyPath = "examples/negative/governance-customer-handoff-negative-fixtures.json";
    public const string ReadinessBaselineCurrentnessSummaryPath = "validation/readiness-baseline-currentness-summary.json";
    public const string UpstreamCurrentnessSummaryPath = "validation/upstream-evidence-currentness-summary.json";
    public const string ResponsibilityMatrixSummaryPath = "validation/responsibility-matrix-summary.json";
    public const string NonClaimBoundarySummaryPath = "validation/non-claim-boundary-summary.json";
    public const string ExternalPrerequisiteRoutingSummaryPath = "validation/external-prerequisite-routing-summary.json";
    public const string CustomerChecklistBoundarySummaryPath = "validation/customer-checklist-boundary-summary.json";
    public const string ReadinessFragmentPath = "readiness/governance-customer-handoff-readiness-fragment.json";
    public const string ScoreProposalPath = "readiness/governance-customer-handoff-score-proposal.json";
    public const string DownstreamHandoffPath = "handoff/governance-customer-handoff-downstream-handoff.json";
    public const string PublicOnlyValidationSummaryPath = "validation/public-only-validation-summary.json";
    public const string NoSecretScanResultPath = "validation/no-secret-scan-result.json";
    public const string RestrictedEvidenceIndexSchemaNotePath = "restricted/restricted-evidence-index.schema-note.md";
    public const string PrivateRestrictedEvidenceIndexPath = "restricted-evidence-index.json";

    public static readonly string[] RequiredArtifactPaths =
    [
        PackageReadmePath,
        SourceSchemaCopyPath,
        PackageManifestSchemaCopyPath,
        ResponsibilityCatalogCopyPath,
        NonClaimCatalogCopyPath,
        ExternalPrerequisiteCatalogCopyPath,
        ResultCodeCatalogCopyPath,
        ReleaseBaselineSourceCopyPath,
        NegativeFixturesCopyPath,
        ReadinessBaselineCurrentnessSummaryPath,
        UpstreamCurrentnessSummaryPath,
        ResponsibilityMatrixSummaryPath,
        NonClaimBoundarySummaryPath,
        ExternalPrerequisiteRoutingSummaryPath,
        CustomerChecklistBoundarySummaryPath,
        ReadinessFragmentPath,
        ScoreProposalPath,
        DownstreamHandoffPath,
        PublicOnlyValidationSummaryPath,
        NoSecretScanResultPath,
        RestrictedEvidenceIndexSchemaNotePath,
        PackageIndexPath,
        PackageManifestPath,
    ];

    public static GovernanceCustomerHandoffGeneratedPackage Generate(
        GovernanceCustomerHandoffPromotionPaths paths,
        string? sourceInput = null,
        string? outputRoot = null,
        DateTimeOffset? generatedAt = null,
        bool publicOnly = false)
    {
        var source = GovernanceCustomerHandoffContracts.ValidateForPromotion(paths, sourceInput, publicOnly);
        var generatedAtText = (generatedAt ?? DateTimeOffset.Parse("2026-06-03T12:00:00Z", CultureInfo.InvariantCulture))
            .UtcDateTime
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var packageRoot = ResolvePackageRoot(paths, outputRoot);
        var restrictedEvidenceRoot = ResolveRestrictedEvidenceRoot(paths);
        var artifacts = new List<GovernanceCustomerHandoffArtifact>
        {
            TextArtifact(PackageReadmePath, BuildPackageReadme(generatedAtText)),
            JsonFileArtifact(SourceSchemaCopyPath, Path.Combine(paths.SchemasRoot, GovernanceCustomerHandoffPromotionPaths.SourceSchemaFileName), "source schema"),
            JsonFileArtifact(PackageManifestSchemaCopyPath, Path.Combine(paths.SchemasRoot, GovernanceCustomerHandoffPromotionPaths.PackageManifestSchemaFileName), "package manifest schema"),
            JsonFileArtifact(ResponsibilityCatalogCopyPath, Path.Combine(paths.CatalogsRoot, GovernanceCustomerHandoffPromotionPaths.ResponsibilityDomainCatalogFileName), "responsibility-domain catalog"),
            JsonFileArtifact(NonClaimCatalogCopyPath, Path.Combine(paths.CatalogsRoot, GovernanceCustomerHandoffPromotionPaths.NonClaimCatalogFileName), "non-claim catalog"),
            JsonFileArtifact(ExternalPrerequisiteCatalogCopyPath, Path.Combine(paths.CatalogsRoot, GovernanceCustomerHandoffPromotionPaths.ExternalPrerequisiteRoutingCatalogFileName), "external-prerequisite catalog"),
            JsonFileArtifact(ResultCodeCatalogCopyPath, Path.Combine(paths.CatalogsRoot, GovernanceCustomerHandoffPromotionPaths.ResultCodeCatalogFileName), "result-code catalog"),
            JsonFileArtifact(ReleaseBaselineSourceCopyPath, paths.DefaultSourcePath, "release-baseline source"),
            JsonFileArtifact(NegativeFixturesCopyPath, Path.Combine(paths.ExamplesRoot, "negative", GovernanceCustomerHandoffPromotionPaths.NegativeFixtureCatalogFileName), "negative fixtures"),
            JsonArtifact(ReadinessBaselineCurrentnessSummaryPath, BuildReadinessBaselineCurrentnessSummary(source, generatedAtText, publicOnly: true)),
            JsonArtifact(UpstreamCurrentnessSummaryPath, BuildUpstreamCurrentnessSummary(source, generatedAtText, publicOnly: true)),
            JsonArtifact(ResponsibilityMatrixSummaryPath, BuildResponsibilityMatrixSummary(source, generatedAtText, publicOnly: true)),
            JsonArtifact(NonClaimBoundarySummaryPath, BuildNonClaimBoundarySummary(source, generatedAtText, publicOnly: true)),
            JsonArtifact(ExternalPrerequisiteRoutingSummaryPath, BuildExternalPrerequisiteRoutingSummary(source, generatedAtText, publicOnly: true)),
            JsonArtifact(CustomerChecklistBoundarySummaryPath, BuildCustomerChecklistBoundarySummary(source, generatedAtText, publicOnly: true)),
        };
        artifacts.Add(JsonArtifact(ReadinessFragmentPath, BuildReadinessFragment(source, generatedAtText, artifacts)));
        artifacts.Add(JsonArtifact(ScoreProposalPath, BuildScoreProposal(source, generatedAtText, artifacts)));
        artifacts.Add(JsonArtifact(DownstreamHandoffPath, BuildDownstreamHandoff(source, generatedAtText, artifacts)));
        artifacts.Add(JsonArtifact(PackageIndexPath, BuildPackageIndex(source, generatedAtText, artifacts)));
        artifacts.Add(JsonArtifact(PublicOnlyValidationSummaryPath, BuildPublicOnlyValidationSummary(source, generatedAtText, artifacts)));
        artifacts.Add(TextArtifact(RestrictedEvidenceIndexSchemaNotePath, BuildRestrictedEvidenceIndexSchemaNote()));
        artifacts.Add(JsonArtifact(NoSecretScanResultPath, BuildNoSecretScanResult(generatedAtText, artifacts)));
        artifacts.Add(JsonArtifact(PackageManifestPath, BuildPackageManifest(source, generatedAtText, artifacts)));

        var restrictedArtifacts = publicOnly
            ? []
            : new List<GovernanceCustomerHandoffArtifact>
            {
                JsonArtifact(PrivateRestrictedEvidenceIndexPath, BuildPrivateRestrictedEvidenceIndex(source, generatedAtText)),
            };

        return new GovernanceCustomerHandoffGeneratedPackage(
            "accepted",
            packageRoot,
            restrictedEvidenceRoot,
            source,
            artifacts,
            restrictedArtifacts);
    }

    public static string ResolvePackageRoot(
        GovernanceCustomerHandoffPromotionPaths paths,
        string? outputRoot = null)
    {
        var root = Path.GetFullPath(outputRoot ?? paths.PackagesRoot);
        GovernanceCustomerHandoffContracts.EnsurePathUnder(paths.WorkspaceRoot, root, "FEAT-166 package output root");
        var packageRoot = Path.GetFullPath(Path.Combine(
            root,
            GovernanceCustomerHandoffPromotionPaths.PackageFamilyFolder,
            GovernanceCustomerHandoffPromotionPaths.DefaultHandoffRunId));
        GovernanceCustomerHandoffContracts.EnsurePathUnder(root, packageRoot, "FEAT-166 package root");
        return packageRoot;
    }

    public static string ResolveRestrictedEvidenceRoot(GovernanceCustomerHandoffPromotionPaths paths)
    {
        var root = Path.GetFullPath(paths.RestrictedEvidenceRoot);
        GovernanceCustomerHandoffContracts.EnsurePathUnder(paths.WorkspaceRoot, root, "FEAT-166 restricted evidence root");
        return root;
    }

    private static JsonObject BuildReadinessBaselineCurrentnessSummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var baseline = GovernanceCustomerHandoffContracts.RequireObject(source, "readinessBaseline");
        var proposal = GovernanceCustomerHandoffContracts.RequireObject(source, "scoreProposal");
        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-readiness-baseline-currentness-summary/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["status"] = "accepted",
            ["generatedAt"] = generatedAt,
            ["publicOnlyValidation"] = publicOnly,
            ["readinessBaseline"] = new JsonObject
            {
                ["registerVersion"] = GovernanceCustomerHandoffContracts.CurrentRegisterVersionId,
                ["dimension"] = GovernanceCustomerHandoffContracts.TargetDimensionId,
                ["blocker"] = GovernanceCustomerHandoffContracts.TargetBlockerId,
                ["currentScore"] = baseline["currentScore"]?.DeepClone(),
                ["targetScore"] = baseline["targetScore"]?.DeepClone(),
                ["registerManifestRef"] = baseline["registerManifestRef"]?.DeepClone(),
            },
            ["scoreProposal"] = new JsonObject
            {
                ["dimension"] = proposal["dimension"]?.DeepClone(),
                ["movement"] = proposal["movement"]?.DeepClone(),
                ["directRegisterMutation"] = proposal["directRegisterMutation"]?.DeepClone(),
                ["proposalOnly"] = true,
                ["forbiddenReplayMovements"] = GovernanceCustomerHandoffContracts.ToStringArray([
                    "FEAT-156 production rollout 7 -> 8",
                    "any RDY-DIM-010 movement other than 8 -> 9",
                ]),
            },
            ["accepted"] = true,
        };
    }

    private static JsonObject BuildUpstreamCurrentnessSummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var upstream = GovernanceCustomerHandoffContracts.RequireArray(source, "upstreamEvidence");
        var summaries = new JsonArray();
        foreach (var item in upstream.OfType<JsonObject>())
        {
            var refs = item["evidenceRefs"] as JsonArray ?? [];
            summaries.Add(new JsonObject
            {
                ["featureId"] = GovernanceCustomerHandoffContracts.GetString(item, "featureId"),
                ["role"] = GovernanceCustomerHandoffContracts.GetString(item, "role"),
                ["status"] = GovernanceCustomerHandoffContracts.GetString(item, "status"),
                ["freshness"] = GovernanceCustomerHandoffContracts.GetString(item, "freshness"),
                ["evidenceRefCount"] = refs.Count,
                ["allRefsHashBound"] = refs.OfType<JsonObject>().All(reference =>
                    (reference["hash"]?.GetValue<string>() ?? string.Empty).Length == 64),
                ["allRefsPublicSafe"] = refs.OfType<JsonObject>().All(reference =>
                    (reference["visibility"]?.GetValue<string>() ?? string.Empty) is "public" or "restricted-ref-only"),
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-upstream-evidence-currentness-summary/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["status"] = "accepted",
            ["generatedAt"] = generatedAt,
            ["publicOnlyValidation"] = publicOnly,
            ["requiredFeatureCount"] = GovernanceCustomerHandoffContracts.RequiredUpstreamFeatures.Length,
            ["observedFeatureCount"] = summaries.Count,
            ["missingFeatureIds"] = new JsonArray(),
            ["staleOrBlockedFeatureIds"] = new JsonArray(),
            ["upstreamEvidence"] = summaries,
            ["currentnessGate"] = "all required upstream refs accepted-current or accepted-input, hash-bound, and public-safe",
        };
    }

    private static JsonObject BuildResponsibilityMatrixSummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var domains = GovernanceCustomerHandoffContracts.RequireArray(source, "responsibilityDomains");
        var observedDomains = domains
            .OfType<JsonObject>()
            .Select(domain => domain["domain"]?.GetValue<string>() ?? string.Empty)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .ToHashSet(StringComparer.Ordinal);
        var missingRows = GovernanceCustomerHandoffContracts.RequiredResponsibilityDomains
            .Where(required => !observedDomains.Contains(required))
            .ToArray();

        var rows = new JsonArray();
        var allRowsHaveEvidenceRefs = true;
        var allRefsHashBound = true;
        var allRefsPublicSafe = true;
        var missingInputFailsClosed = true;
        foreach (var domain in domains.OfType<JsonObject>())
        {
            var refs = domain["evidenceRefs"] as JsonArray ?? [];
            var refsHashBound = refs.OfType<JsonObject>().All(reference =>
                (reference["hash"]?.GetValue<string>() ?? string.Empty).Length == 64);
            var refsPublicSafe = refs.OfType<JsonObject>().All(reference =>
                (reference["visibility"]?.GetValue<string>() ?? string.Empty) is "public" or "restricted-ref-only");

            allRowsHaveEvidenceRefs &= refs.Count > 0;
            allRefsHashBound &= refs.Count > 0 && refsHashBound;
            allRefsPublicSafe &= refs.Count > 0 && refsPublicSafe;
            missingInputFailsClosed &= (domain["missingInputBehavior"]?.GetValue<string>() ?? string.Empty) == "fail-closed";

            rows.Add(new JsonObject
            {
                ["domain"] = domain["domain"]?.DeepClone(),
                ["ownerClass"] = domain["ownerClass"]?.DeepClone(),
                ["visibility"] = domain["visibility"]?.DeepClone(),
                ["nonClaimBoundary"] = domain["nonClaimBoundary"]?.DeepClone(),
                ["missingInputBehavior"] = domain["missingInputBehavior"]?.DeepClone(),
                ["claimEffect"] = domain["claimEffect"]?.DeepClone(),
                ["evidenceRefCount"] = refs.Count,
                ["allRefsHashBound"] = refsHashBound,
                ["allRefsPublicSafe"] = refsPublicSafe,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-responsibility-matrix-summary/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["status"] = "accepted",
            ["generatedAt"] = generatedAt,
            ["publicOnlyValidation"] = publicOnly,
            ["requiredRowCount"] = GovernanceCustomerHandoffContracts.RequiredResponsibilityDomains.Length,
            ["observedRowCount"] = observedDomains.Count,
            ["missingRows"] = GovernanceCustomerHandoffContracts.ToStringArray(missingRows),
            ["allRowsHaveEvidenceRefs"] = allRowsHaveEvidenceRefs,
            ["allRefsHashBound"] = allRefsHashBound,
            ["allRefsPublicSafe"] = allRefsPublicSafe,
            ["missingInputFailsClosed"] = missingInputFailsClosed,
            ["ownerSeparationAccepted"] = true,
            ["claimEffectGate"] = "technical evidence, customer decisions, external prerequisites, auditor review, and promotion-owner action remain separated",
            ["rows"] = rows,
        };
    }

    private static JsonObject BuildNonClaimBoundarySummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var boundaries = GovernanceCustomerHandoffContracts.RequireArray(source, "nonClaimBoundaries");
        var observedCategories = boundaries
            .OfType<JsonObject>()
            .Select(boundary => boundary["category"]?.GetValue<string>() ?? string.Empty)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .ToHashSet(StringComparer.Ordinal);
        var missingCategories = GovernanceCustomerHandoffContracts.RequiredNonClaimCategories
            .Where(required => !observedCategories.Contains(required))
            .ToArray();

        var rows = new JsonArray();
        foreach (var boundary in boundaries.OfType<JsonObject>())
        {
            rows.Add(new JsonObject
            {
                ["category"] = boundary["category"]?.DeepClone(),
                ["blockingResultCode"] = boundary["blockingResultCode"]?.DeepClone(),
                ["forbiddenClaimRejected"] = true,
                ["allowedPublicWordingAccepted"] = true,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-non-claim-boundary-summary/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["status"] = "accepted",
            ["generatedAt"] = generatedAt,
            ["publicOnlyValidation"] = publicOnly,
            ["requiredBoundaryCount"] = GovernanceCustomerHandoffContracts.RequiredNonClaimCategories.Length,
            ["observedBoundaryCount"] = observedCategories.Count,
            ["missingBoundaryCategories"] = GovernanceCustomerHandoffContracts.ToStringArray(missingCategories),
            ["forbiddenClaimsRejected"] = true,
            ["legalSufficiencyClaimed"] = false,
            ["agmManagementClaimed"] = false,
            ["certificationClaimed"] = false,
            ["externalAuditAcceptanceClaimed"] = false,
            ["publicStateReadinessClaimed"] = false,
            ["productionRolloutApprovalClaimed"] = false,
            ["customerGovernanceDecisionClaimed"] = false,
            ["directRegisterMutationClaimed"] = false,
            ["boundaryGate"] = "all forbidden claims remain non-claims; FEAT-166 emits public-safe evidence and proposal artifacts only",
            ["boundaries"] = rows,
        };
    }

    private static JsonObject BuildExternalPrerequisiteRoutingSummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var routing = GovernanceCustomerHandoffContracts.RequireObject(source, "externalPrerequisiteRouting");
        var routes = GovernanceCustomerHandoffContracts.RequireArray(routing, "routes");
        var observedRoutes = routes
            .OfType<JsonObject>()
            .Select(route => route["routeId"]?.GetValue<string>() ?? string.Empty)
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .ToHashSet(StringComparer.Ordinal);
        var missingRoutes = GovernanceCustomerHandoffContracts.RequiredExternalPrerequisiteRoutes
            .Where(required => !observedRoutes.Contains(required))
            .ToArray();

        var rows = new JsonArray();
        var allRoutesFailClosed = true;
        var allRoutesExternalBoundaryOnly = true;
        var allRefsHashBound = true;
        var allRefsPublicSafe = true;
        foreach (var route in routes.OfType<JsonObject>())
        {
            var refs = route["evidenceRefs"] as JsonArray ?? [];
            var refsHashBound = refs.OfType<JsonObject>().All(reference =>
                (reference["hash"]?.GetValue<string>() ?? string.Empty).Length == 64);
            var refsPublicSafe = refs.OfType<JsonObject>().All(reference =>
                (reference["visibility"]?.GetValue<string>() ?? string.Empty) is "public" or "restricted-ref-only");

            allRoutesFailClosed &= (route["missingInputBehavior"]?.GetValue<string>() ?? string.Empty) == "fail-closed";
            allRoutesExternalBoundaryOnly &= (route["publicStateReadinessEffect"]?.GetValue<string>() ?? string.Empty) == "external-boundary-only";
            allRefsHashBound &= refs.Count > 0 && refsHashBound;
            allRefsPublicSafe &= refs.Count > 0 && refsPublicSafe;

            rows.Add(new JsonObject
            {
                ["routeId"] = route["routeId"]?.DeepClone(),
                ["ownerClass"] = route["ownerClass"]?.DeepClone(),
                ["sourceFeature"] = route["sourceFeature"]?.DeepClone(),
                ["publicStateReadinessEffect"] = route["publicStateReadinessEffect"]?.DeepClone(),
                ["evidenceVisibility"] = route["evidenceVisibility"]?.DeepClone(),
                ["routeStatus"] = route["routeStatus"]?.DeepClone(),
                ["evidenceRefType"] = route["evidenceRefType"]?.DeepClone(),
                ["missingInputBehavior"] = route["missingInputBehavior"]?.DeepClone(),
                ["evidenceRefCount"] = refs.Count,
                ["allRefsHashBound"] = refsHashBound,
                ["allRefsPublicSafe"] = refsPublicSafe,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-external-prerequisite-routing-summary/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["status"] = "accepted",
            ["generatedAt"] = generatedAt,
            ["publicOnlyValidation"] = publicOnly,
            ["feat149Alignment"] = routing["feat149Alignment"]?.DeepClone(),
            ["publicStateReadinessResolvedByFeat166"] = routing["publicStateReadinessResolvedByFeat166"]?.DeepClone(),
            ["requiredRouteCount"] = GovernanceCustomerHandoffContracts.RequiredExternalPrerequisiteRoutes.Length,
            ["observedRouteCount"] = observedRoutes.Count,
            ["missingRoutes"] = GovernanceCustomerHandoffContracts.ToStringArray(missingRoutes),
            ["allRoutesFailClosed"] = allRoutesFailClosed,
            ["allRoutesExternalBoundaryOnly"] = allRoutesExternalBoundaryOnly,
            ["allRefsHashBound"] = allRefsHashBound,
            ["allRefsPublicSafe"] = allRefsPublicSafe,
            ["publicStateReadinessClaimed"] = false,
            ["privatePayloadPublished"] = false,
            ["routingGate"] = "customer and external prerequisites remain fail-closed external-boundary inputs; FEAT-166 does not resolve public/state readiness",
            ["routes"] = rows,
        };
    }

    private static JsonObject BuildCustomerChecklistBoundarySummary(JsonObject source, string generatedAt, bool publicOnly)
    {
        var checklist = GovernanceCustomerHandoffContracts.RequireObject(source, "customerChecklist");
        var sections = GovernanceCustomerHandoffContracts.RequireArray(checklist, "sections");
        var observedSections = sections
            .OfType<JsonObject>()
            .Select(section => section["sectionId"]?.GetValue<string>() ?? string.Empty)
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .ToHashSet(StringComparer.Ordinal);
        var missingSections = GovernanceCustomerHandoffContracts.RequiredCustomerChecklistSections
            .Where(required => !observedSections.Contains(required))
            .ToArray();

        var rows = new JsonArray();
        var allSectionsFailClosed = true;
        var allAnswersPrivate = true;
        var allRefsHashBound = true;
        var allRefsPublicSafe = true;
        foreach (var section in sections.OfType<JsonObject>())
        {
            var refs = section["evidenceRefs"] as JsonArray ?? [];
            var refsHashBound = refs.OfType<JsonObject>().All(reference =>
                (reference["hash"]?.GetValue<string>() ?? string.Empty).Length == 64);
            var refsPublicSafe = refs.OfType<JsonObject>().All(reference =>
                (reference["visibility"]?.GetValue<string>() ?? string.Empty) is "public" or "restricted-ref-only");

            allSectionsFailClosed &= (section["missingInputBehavior"]?.GetValue<string>() ?? string.Empty) == "fail-closed";
            allAnswersPrivate &= section["answerPublished"]?.GetValue<bool>() == false;
            allRefsHashBound &= refs.Count > 0 && refsHashBound;
            allRefsPublicSafe &= refs.Count > 0 && refsPublicSafe;

            rows.Add(new JsonObject
            {
                ["sectionId"] = section["sectionId"]?.DeepClone(),
                ["questionClass"] = section["questionClass"]?.DeepClone(),
                ["ownerClass"] = section["ownerClass"]?.DeepClone(),
                ["status"] = section["status"]?.DeepClone(),
                ["answerPublished"] = section["answerPublished"]?.DeepClone(),
                ["evidenceRefType"] = section["evidenceRefType"]?.DeepClone(),
                ["evidenceVisibility"] = section["evidenceVisibility"]?.DeepClone(),
                ["missingInputBehavior"] = section["missingInputBehavior"]?.DeepClone(),
                ["evidenceRefCount"] = refs.Count,
                ["allRefsHashBound"] = refsHashBound,
                ["allRefsPublicSafe"] = refsPublicSafe,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-customer-checklist-boundary-summary/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["status"] = "accepted",
            ["generatedAt"] = generatedAt,
            ["publicOnlyValidation"] = publicOnly,
            ["genericQuestionsOnly"] = checklist["genericQuestionsOnly"]?.DeepClone(),
            ["customerAnswersPublished"] = checklist["customerAnswersPublished"]?.DeepClone(),
            ["requiredSectionCount"] = GovernanceCustomerHandoffContracts.RequiredCustomerChecklistSections.Length,
            ["observedSectionCount"] = observedSections.Count,
            ["missingSections"] = GovernanceCustomerHandoffContracts.ToStringArray(missingSections),
            ["allSectionsFailClosed"] = allSectionsFailClosed,
            ["allAnswersPrivate"] = allAnswersPrivate,
            ["allRefsHashBound"] = allRefsHashBound,
            ["allRefsPublicSafe"] = allRefsPublicSafe,
            ["privateAnswerPayloadPublished"] = false,
            ["checklistGate"] = "public checklist content is generic; customer answers and authority details remain private or no-payload restricted refs",
            ["sections"] = rows,
        };
    }

    private static JsonObject BuildPackageIndex(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<GovernanceCustomerHandoffArtifact> artifacts)
    {
        var baseline = GovernanceCustomerHandoffContracts.RequireObject(source, "readinessBaseline");
        var proposal = GovernanceCustomerHandoffContracts.RequireObject(source, "scoreProposal");
        var validationArtifacts = artifacts
            .Where(artifact => artifact.RelativePath.StartsWith("validation/", StringComparison.Ordinal))
            .Select(artifact => artifact.RelativePath);
        var readinessArtifacts = artifacts
            .Where(artifact => artifact.RelativePath.StartsWith("readiness/", StringComparison.Ordinal))
            .Select(artifact => artifact.RelativePath);
        var handoffArtifacts = artifacts
            .Where(artifact => artifact.RelativePath.StartsWith("handoff/", StringComparison.Ordinal))
            .Select(artifact => artifact.RelativePath);

        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-package/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["packageId"] = GovernanceCustomerHandoffPromotionPaths.DefaultHandoffRunId,
            ["status"] = "accepted",
            ["generatedAt"] = generatedAt,
            ["publicOnlyValidation"] = true,
            ["readinessBaseline"] = new JsonObject
            {
                ["registerVersion"] = baseline["registerVersion"]?.DeepClone(),
                ["dimension"] = baseline["dimension"]?.DeepClone(),
                ["blocker"] = baseline["blocker"]?.DeepClone(),
                ["currentScore"] = baseline["currentScore"]?.DeepClone(),
                ["targetScore"] = baseline["targetScore"]?.DeepClone(),
            },
            ["scoreProposal"] = new JsonObject
            {
                ["dimension"] = proposal["dimension"]?.DeepClone(),
                ["movement"] = proposal["movement"]?.DeepClone(),
                ["directRegisterMutation"] = proposal["directRegisterMutation"]?.DeepClone(),
                ["proposalOnly"] = true,
            },
            ["validationArtifacts"] = GovernanceCustomerHandoffContracts.ToStringArray(validationArtifacts),
            ["readinessArtifacts"] = GovernanceCustomerHandoffContracts.ToStringArray(readinessArtifacts),
            ["handoffArtifacts"] = GovernanceCustomerHandoffContracts.ToStringArray(handoffArtifacts),
            ["restrictedEvidence"] = new JsonObject
            {
                ["payloadsPublishedHere"] = false,
                ["publicRefsOnly"] = true,
                ["restrictedRefCount"] = CollectRestrictedEvidenceRefs(source).Count,
            },
            ["claimBoundary"] = "technical evidence and proposal-only readiness package; customer governance, legal sufficiency, certification, public/state readiness, auditor acceptance, and register mutation remain outside FEAT-166",
        };
    }

    private static JsonObject BuildReadinessFragment(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<GovernanceCustomerHandoffArtifact> artifacts)
    {
        var baseline = GovernanceCustomerHandoffContracts.RequireObject(source, "readinessBaseline");
        var proposal = GovernanceCustomerHandoffContracts.RequireObject(source, "scoreProposal");
        var supportingValidationRefs = artifacts
            .Where(artifact => artifact.RelativePath.StartsWith("validation/", StringComparison.Ordinal))
            .ToArray();

        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-readiness-fragment/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["packageId"] = GovernanceCustomerHandoffPromotionPaths.DefaultHandoffRunId,
            ["status"] = "accepted_candidate",
            ["generatedAt"] = generatedAt,
            ["readinessRegisterVersion"] = GovernanceCustomerHandoffContracts.CurrentRegisterVersionId,
            ["dimension"] = GovernanceCustomerHandoffContracts.TargetDimensionId,
            ["blocker"] = GovernanceCustomerHandoffContracts.TargetBlockerId,
            ["currentScore"] = baseline["currentScore"]?.DeepClone(),
            ["proposedScore"] = baseline["targetScore"]?.DeepClone(),
            ["movement"] = proposal["movement"]?.DeepClone(),
            ["proposalOnly"] = true,
            ["directRegisterMutation"] = false,
            ["promotionOwnerRequired"] = true,
            ["publicOnlyValidation"] = true,
            ["evidenceRefs"] = ArtifactRefs(supportingValidationRefs),
            ["acceptedGates"] = GovernanceCustomerHandoffContracts.ToStringArray([
                "readiness-baseline-current",
                "upstream-evidence-current",
                "responsibility-matrix-complete",
                "non-claim-boundaries-enforced",
                "external-prerequisite-routing-fail-closed",
                "customer-checklist-public-boundary-accepted",
            ]),
            ["nonClaims"] = BuildPhase7NonClaims(),
            ["residualLimitations"] = GovernanceCustomerHandoffContracts.ToStringArray([
                "Customer governance authority, notices, quorum, proxy, minutes, and remedy decisions remain customer-owned.",
                "Legal sufficiency remains customer or external legal authority scope.",
                "Independent certification and external auditor acceptance are not granted by this package.",
                "Public/state election readiness remains routed through FEAT-149 and external authority prerequisites.",
                "Canonical readiness-register movement requires later promotion-owner action.",
            ]),
        };
    }

    private static JsonObject BuildScoreProposal(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<GovernanceCustomerHandoffArtifact> artifacts)
    {
        var baseline = GovernanceCustomerHandoffContracts.RequireObject(source, "readinessBaseline");
        var proposal = GovernanceCustomerHandoffContracts.RequireObject(source, "scoreProposal");
        var readinessFragment = artifacts.First(artifact => artifact.RelativePath == ReadinessFragmentPath);
        var supportingRefs = artifacts
            .Where(artifact =>
                artifact.RelativePath.StartsWith("validation/", StringComparison.Ordinal) ||
                artifact.RelativePath == ReadinessFragmentPath)
            .ToArray();

        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-score-proposal/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["packageId"] = GovernanceCustomerHandoffPromotionPaths.DefaultHandoffRunId,
            ["status"] = "proposal_only",
            ["generatedAt"] = generatedAt,
            ["readinessRegisterVersion"] = baseline["registerVersion"]?.DeepClone(),
            ["dimension"] = proposal["dimension"]?.DeepClone(),
            ["blocker"] = baseline["blocker"]?.DeepClone(),
            ["currentScore"] = baseline["currentScore"]?.DeepClone(),
            ["proposedScore"] = baseline["targetScore"]?.DeepClone(),
            ["movement"] = proposal["movement"]?.DeepClone(),
            ["proposalOnly"] = true,
            ["directRegisterMutation"] = proposal["directRegisterMutation"]?.DeepClone(),
            ["canonicalRegisterMutationPerformed"] = false,
            ["promotionOwnerRequired"] = true,
            ["readinessFragmentRef"] = ArtifactRef(readinessFragment, "FEAT-130-compatible proposal-only readiness fragment."),
            ["supportingRefs"] = ArtifactRefs(supportingRefs),
            ["claimEffect"] = "proposes RDY-DIM-010 8 -> 9 for later promotion-owner review only",
            ["nonClaims"] = BuildPhase7NonClaims(),
        };
    }

    private static JsonObject BuildDownstreamHandoff(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<GovernanceCustomerHandoffArtifact> artifacts)
    {
        var readinessFragment = artifacts.First(artifact => artifact.RelativePath == ReadinessFragmentPath);
        var scoreProposal = artifacts.First(artifact => artifact.RelativePath == ScoreProposalPath);
        var validationRefs = artifacts
            .Where(artifact => artifact.RelativePath.StartsWith("validation/", StringComparison.Ordinal))
            .ToArray();
        var packageRefs = new[]
        {
            readinessFragment,
            scoreProposal,
        };

        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-downstream-handoff/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["packageId"] = GovernanceCustomerHandoffPromotionPaths.DefaultHandoffRunId,
            ["status"] = "handoff_ready",
            ["generatedAt"] = generatedAt,
            ["publicOnlyValidation"] = true,
            ["packageRefs"] = ArtifactRefs(packageRefs),
            ["validationRefs"] = ArtifactRefs(validationRefs),
            ["restrictedEvidenceRefs"] = CollectRestrictedEvidenceRefs(source),
            ["downstreamTargets"] = new JsonArray
            {
                DownstreamTarget(
                    "local-rehearsal",
                    "Use the public package and no-payload restricted refs as reviewer inputs before any controlled rehearsal claim is expanded.",
                    "does-not-start-rehearsal"),
                DownstreamTarget(
                    "binding-internal-election-proof-validation",
                    "Consume the handoff package as static governance/customer context only; live proof binding remains FEAT-143/FEAT-144 scope.",
                    "does-not-bind-live-election"),
                DownstreamTarget(
                    "external-auditor-review",
                    "Provide public-safe package refs for auditor review without publishing auditor notes or implying acceptance.",
                    "review-consumer-only"),
                DownstreamTarget(
                    "promotion-owner-review",
                    "Review the proposal-only RDY-DIM-010 8 -> 9 score package before any canonical register movement.",
                    "promotion-owner-action-required"),
            },
            ["residualLimitations"] = GovernanceCustomerHandoffContracts.ToStringArray([
                "Legal sufficiency remains outside FEAT-166.",
                "Independent certification remains outside FEAT-166.",
                "AGM management and customer governance decisions remain customer-owned.",
                "Public/state readiness remains blocked unless external prerequisites are accepted through the appropriate track.",
                "External auditor acceptance is not represented by this handoff.",
                "Production rollout approval is not granted by this handoff.",
            ]),
            ["nonClaims"] = BuildPhase7NonClaims(),
        };
    }

    private static JsonObject DownstreamTarget(string targetId, string requiredAction, string claimEffect) =>
        new()
        {
            ["targetId"] = targetId,
            ["requiredAction"] = requiredAction,
            ["claimEffect"] = claimEffect,
            ["directRegisterMutationAllowed"] = false,
            ["payloadPublished"] = false,
        };

    private static JsonObject BuildPhase7NonClaims() =>
        new()
        {
            ["legalSufficiencyClaimed"] = false,
            ["agmManagementClaimed"] = false,
            ["certificationClaimed"] = false,
            ["externalAuditAcceptanceClaimed"] = false,
            ["publicStateReadinessClaimed"] = false,
            ["productionRolloutApprovalClaimed"] = false,
            ["customerGovernanceDecisionClaimed"] = false,
            ["directReadinessRegisterMutationClaimed"] = false,
        };

    private static JsonObject BuildPublicOnlyValidationSummary(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<GovernanceCustomerHandoffArtifact> artifacts)
    {
        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-public-only-validation-summary/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["status"] = "accepted",
            ["generatedAt"] = generatedAt,
            ["publicOnlyValidation"] = true,
            ["privateCheckoutRequired"] = false,
            ["credentialRequired"] = false,
            ["privatePayloadPublished"] = false,
            ["customerAnswersPublished"] = GovernanceCustomerHandoffContracts.RequireObject(source, "customerChecklist")["customerAnswersPublished"]?.DeepClone(),
            ["directRegisterMutation"] = GovernanceCustomerHandoffContracts.RequireObject(source, "scoreProposal")["directRegisterMutation"]?.DeepClone(),
            ["publicArtifactCount"] = artifacts.Count,
            ["publicOnlyGate"] = "package generation and check-only replay use public schemas, catalogs, sanitized fixtures, public-safe summaries, and no-payload restricted refs only",
        };
    }

    private static JsonObject BuildNoSecretScanResult(
        string generatedAt,
        IReadOnlyList<GovernanceCustomerHandoffArtifact> artifacts)
    {
        var findings = new JsonArray();
        foreach (var artifact in artifacts)
        {
            foreach (var pattern in ForbiddenPayloadPatterns())
            {
                if (artifact.Content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new JsonObject
                    {
                        ["path"] = artifact.RelativePath,
                        ["pattern"] = pattern,
                    });
                }
            }
        }

        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-no-secret-scan-result/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["status"] = findings.Count == 0 ? "accepted" : "blocked",
            ["generatedAt"] = generatedAt,
            ["scannedArtifactCount"] = artifacts.Count,
            ["findingCount"] = findings.Count,
            ["privatePathFindingCount"] = findings.Count,
            ["payloadFindingCount"] = findings.Count,
            ["schemaAndCatalogDenyListTermsAllowed"] = true,
            ["findings"] = findings,
        };
    }

    private static JsonObject BuildPackageManifest(
        JsonObject source,
        string generatedAt,
        IReadOnlyList<GovernanceCustomerHandoffArtifact> artifacts)
    {
        var sourceArtifact = artifacts.First(artifact => artifact.RelativePath == ReleaseBaselineSourceCopyPath);
        var validationArtifacts = artifacts
            .Where(artifact => artifact.RelativePath.StartsWith("validation/", StringComparison.Ordinal))
            .ToArray();
        var publicArtifacts = artifacts
            .Where(artifact => !artifact.RelativePath.StartsWith("validation/", StringComparison.Ordinal))
            .Where(artifact => artifact.RelativePath != PackageManifestPath)
            .ToArray();
        var readinessProposalRef = artifacts.First(artifact => artifact.RelativePath == ScoreProposalPath);
        var handoffArtifacts = artifacts
            .Where(artifact => artifact.RelativePath.StartsWith("handoff/", StringComparison.Ordinal))
            .ToArray();

        return new JsonObject
        {
            ["schemaVersion"] = "governance-customer-handoff-package-manifest/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["packageId"] = GovernanceCustomerHandoffPromotionPaths.DefaultHandoffRunId,
            ["generatedAt"] = generatedAt,
            ["sourceRef"] = ArtifactRef(sourceArtifact, "Sanitized public release-baseline source."),
            ["artifactRefs"] = ArtifactRefs(publicArtifacts),
            ["validationRefs"] = ArtifactRefs(validationArtifacts),
            ["readinessProposal"] = new JsonObject
            {
                ["dimension"] = GovernanceCustomerHandoffContracts.TargetDimensionId,
                ["movement"] = GovernanceCustomerHandoffContracts.AllowedScoreMovement,
                ["directRegisterMutation"] = false,
                ["proposalRef"] = ArtifactRef(readinessProposalRef, "Proposal-only readiness score package."),
            },
            ["handoffRefs"] = ArtifactRefs(handoffArtifacts),
            ["restrictedEvidenceRefs"] = CollectRestrictedEvidenceRefs(source),
        };
    }

    private static JsonObject BuildPrivateRestrictedEvidenceIndex(JsonObject source, string generatedAt) =>
        new()
        {
            ["schemaVersion"] = "governance-customer-handoff-restricted-evidence-index/v1",
            ["featureId"] = GovernanceCustomerHandoffContracts.FeatureId,
            ["packageId"] = GovernanceCustomerHandoffPromotionPaths.DefaultHandoffRunId,
            ["generatedAt"] = generatedAt,
            ["payloadsPublished"] = false,
            ["publicOnlyValidationRequiresPrivatePayload"] = false,
            ["restrictedRefs"] = CollectRestrictedEvidenceRefs(source),
            ["indexBoundary"] = "No customer, legal, auditor, voter, trustee, credential, or private payload is stored in this index; it records ids and expected hashes only.",
        };

    private static string BuildPackageReadme(string generatedAt) =>
        $"""
        # FEAT-166 Governance Customer Handoff Package

        Package id: `{GovernanceCustomerHandoffPromotionPaths.DefaultHandoffRunId}`
        Generated at: `{generatedAt}`

        This package is public-safe. It contains schemas, catalogs, sanitized fixtures, validation
        summaries, package manifest refs, and no-payload restricted evidence references for the
        governance/customer handoff readiness slice.

        The package does not publish customer legal documents, checklist answers, authority names,
        signoff records, external auditor notes, voter material, trustee material, credentials,
        private paths, or restricted reviewer payloads.

        FEAT-166 proposes `RDY-DIM-010 8 -> 9` only. It does not mutate the readiness register.
        """;

    private static string BuildRestrictedEvidenceIndexSchemaNote() =>
        """
        # Restricted Evidence Index Schema Note

        The private restricted evidence index for this package is a no-payload JSON index. Public
        outputs may reference opaque restricted ids, expected hashes, visibility markers, and
        no-payload notes only.

        Customer legal material, checklist answers, authority names, signoff records, external
        auditor notes, voter material, trustee material, credentials, private paths, and restricted
        reviewer payloads must not be copied into the public repository.
        """;

    private static JsonArray ArtifactRefs(IEnumerable<GovernanceCustomerHandoffArtifact> artifacts)
    {
        var refs = new JsonArray();
        foreach (var artifact in artifacts)
        {
            refs.Add(ArtifactRef(artifact));
        }

        return refs;
    }

    private static JsonObject ArtifactRef(GovernanceCustomerHandoffArtifact artifact, string? description = null)
    {
        var result = new JsonObject
        {
            ["path"] = artifact.RelativePath,
            ["sha256"] = artifact.Sha256Hash,
            ["visibility"] = "public",
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            result["description"] = description;
        }

        return result;
    }

    private static JsonArray CollectRestrictedEvidenceRefs(JsonObject source)
    {
        var refs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (source.TryGetPropertyValue("restrictedEvidencePolicy", out var policyNode) &&
            policyNode is JsonObject policy &&
            policy.TryGetPropertyValue("restrictedEvidenceRefs", out var policyRefsNode) &&
            policyRefsNode is JsonArray policyRefs)
        {
            foreach (var item in policyRefs.OfType<JsonObject>())
            {
                var id = item["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                refs[id] = NoPayloadRestrictedRef(
                    id,
                    item["expectedHash"]?.GetValue<string>(),
                    item["note"]?.GetValue<string>() ?? "No-payload restricted ref from restricted evidence policy.");
            }
        }

        CollectRestrictedEvidenceRefs(source, refs);

        var array = new JsonArray();
        foreach (var item in refs.Values.OrderBy(item => item["id"]?.GetValue<string>(), StringComparer.Ordinal))
        {
            array.Add(item);
        }

        return array;
    }

    private static void CollectRestrictedEvidenceRefs(JsonNode? node, Dictionary<string, JsonObject> refs)
    {
        switch (node)
        {
            case JsonObject obj:
                if ((obj["visibility"]?.GetValue<string>() ?? string.Empty) == "restricted-ref-only")
                {
                    var id = obj["id"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        refs.TryAdd(
                            id,
                            NoPayloadRestrictedRef(
                                id,
                                obj["hash"]?.GetValue<string>() ?? obj["expectedHash"]?.GetValue<string>(),
                                "No-payload restricted ref collected from public-safe FEAT-166 source."));
                    }
                }

                foreach (var child in obj.Select(property => property.Value))
                {
                    CollectRestrictedEvidenceRefs(child, refs);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    CollectRestrictedEvidenceRefs(child, refs);
                }

                break;
        }
    }

    private static JsonObject NoPayloadRestrictedRef(string id, string? expectedHash, string note)
    {
        var result = new JsonObject
        {
            ["id"] = id,
            ["visibility"] = "restricted-ref-only",
            ["payloadPublished"] = false,
            ["note"] = note,
        };

        if (!string.IsNullOrWhiteSpace(expectedHash))
        {
            result["expectedHash"] = expectedHash;
        }

        return result;
    }

    private static string[] ForbiddenPayloadPatterns() =>
    [
        "PrivateServer_ElectronicVoting",
        @"C:\",
        "/Users/",
        "/home/",
        "BEGIN PRIVATE KEY",
        "aws_secret_access_key=",
        "raw restricted payload",
    ];

    private static GovernanceCustomerHandoffArtifact JsonArtifact(string relativePath, JsonObject content) =>
        new(relativePath, GovernanceCustomerHandoffContracts.CanonicalJson(content));

    private static GovernanceCustomerHandoffArtifact JsonFileArtifact(string relativePath, string path, string description) =>
        JsonArtifact(relativePath, GovernanceCustomerHandoffContracts.ReadJsonObject(path, description));

    private static GovernanceCustomerHandoffArtifact TextArtifact(string relativePath, string content) =>
        new(relativePath, GovernanceCustomerHandoffContracts.NormalizeLineEndings(content));
}
