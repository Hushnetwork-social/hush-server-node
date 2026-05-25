using System.Text;
using System.Text.Json.Nodes;

namespace LegalGovernanceBoundaryPromoter;

public static class LegalGovernanceBoundaryArtifactGenerator
{
    public const string ReadinessFragmentPath = "legal-governance-boundary-readiness-fragment.json";
    public const string PackagePath = "legal-governance-boundary-package.json";
    public const string ClaimImpactMatrixPath = "legal-governance-boundary-claim-impact-matrix.json";
    public const string RestrictedIndexPath = "legal-governance-boundary-restricted-index.json";
    public const string PublicSafeSummaryPath = "legal-governance-boundary-public-safe-summary.md";
    public const string Feat139HandoffPath = "legal-governance-boundary-feat139-handoff.json";
    public const string Feat146HandoffPath = "legal-governance-boundary-feat146-handoff.json";
    public const string DownstreamHandoffPath = "legal-governance-boundary-downstream-handoff.json";
    public const string PackageHashValidationPath = "legal-governance-boundary-package-hash-validation.json";

    public static LegalGovernanceBoundaryGeneratedPackage Generate(
        LegalGovernanceBoundaryPromotionPaths paths,
        string? sourceInput = null,
        DateTimeOffset? generatedAt = null)
    {
        var source = LegalGovernanceBoundaryContracts.LoadSource(paths, sourceInput);
        var validationErrors = LegalGovernanceBoundaryContracts.ValidateSource(source);
        if (validationErrors.Count > 0)
        {
            throw new LegalGovernanceBoundaryPromotionException(
                "FEAT-140 legal governance boundary source validation failed.",
                validationErrors);
        }

        var effectiveGeneratedAt = generatedAt ?? DateTimeOffset.UtcNow;
        var claimMatrix = BuildClaimImpactMatrix(source, effectiveGeneratedAt);
        var restrictedIndex = BuildRestrictedIndex(source, effectiveGeneratedAt);
        var feat139Handoff = BuildFeat139Handoff(source, effectiveGeneratedAt);
        var feat146Handoff = BuildFeat146Handoff(source, effectiveGeneratedAt);

        var initialBlockers = BuildBlockers(source, claimMatrix, []);
        var initialDowngrades = BuildDowngrades(source, claimMatrix);
        var initialStatus = ResolvePackageStatus(claimMatrix, initialBlockers, initialDowngrades);
        var publicSummary = BuildPublicSafeSummary(source, claimMatrix, initialStatus, effectiveGeneratedAt);
        var publicFindings = LegalGovernanceBoundaryContracts.ScanForbiddenPublicMaterial(
            source,
            [(PublicSafeSummaryPath, publicSummary)]);
        var blockers = BuildBlockers(source, claimMatrix, publicFindings);
        var downgrades = BuildDowngrades(source, claimMatrix);
        var status = ResolvePackageStatus(claimMatrix, blockers, downgrades);
        publicSummary = BuildPublicSafeSummary(source, claimMatrix, status, effectiveGeneratedAt);
        publicFindings = LegalGovernanceBoundaryContracts.ScanForbiddenPublicMaterial(
            source,
            [(PublicSafeSummaryPath, publicSummary)]);
        blockers = BuildBlockers(source, claimMatrix, publicFindings);
        status = ResolvePackageStatus(claimMatrix, blockers, downgrades);

        var claimMatrixArtifact = JsonArtifact(ClaimImpactMatrixPath, claimMatrix);
        var restrictedIndexArtifact = JsonArtifact(RestrictedIndexPath, restrictedIndex);
        var feat139HandoffArtifact = JsonArtifact(Feat139HandoffPath, feat139Handoff);
        var feat146HandoffArtifact = JsonArtifact(Feat146HandoffPath, feat146Handoff);
        var publicSummaryArtifact = TextArtifact(PublicSafeSummaryPath, publicSummary);
        var readiness = BuildReadinessFragment(
            source,
            status,
            blockers,
            downgrades,
            claimMatrixArtifact,
            restrictedIndexArtifact,
            feat139HandoffArtifact,
            feat146HandoffArtifact,
            publicSummaryArtifact,
            effectiveGeneratedAt);
        var readinessArtifact = JsonArtifact(ReadinessFragmentPath, readiness);
        var downstreamHandoff = BuildDownstreamHandoff(
            source,
            status,
            blockers,
            downgrades,
            readinessArtifact,
            claimMatrixArtifact,
            restrictedIndexArtifact,
            feat139HandoffArtifact,
            feat146HandoffArtifact,
            publicSummaryArtifact,
            effectiveGeneratedAt);
        var downstreamHandoffArtifact = JsonArtifact(DownstreamHandoffPath, downstreamHandoff);
        var package = BuildPackage(
            source,
            status,
            blockers,
            downgrades,
            [
                readinessArtifact,
                claimMatrixArtifact,
                restrictedIndexArtifact,
                feat139HandoffArtifact,
                feat146HandoffArtifact,
                downstreamHandoffArtifact,
                publicSummaryArtifact,
            ],
            effectiveGeneratedAt);
        var packageArtifact = JsonArtifact(PackagePath, package);
        var artifactsForHashValidation = new[]
        {
            claimMatrixArtifact,
            downstreamHandoffArtifact,
            feat139HandoffArtifact,
            feat146HandoffArtifact,
            packageArtifact,
            publicSummaryArtifact,
            readinessArtifact,
            restrictedIndexArtifact,
        };
        var hashValidation = BuildPackageHashValidation(source, artifactsForHashValidation, blockers, effectiveGeneratedAt);

        var artifacts = artifactsForHashValidation
            .Append(JsonArtifact(PackageHashValidationPath, hashValidation))
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new LegalGovernanceBoundaryGeneratedPackage(
            status,
            artifacts,
            publicFindings,
            blockers,
            downgrades);
    }

    private static JsonObject BuildClaimImpactMatrix(JsonObject source, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "legal-governance-boundary-claim-impact-matrix.v1",
            ["matrixId"] = "LEGAL-GOVERNANCE-CLAIM-MATRIX-FEAT-140-001",
            ["featureSlice"] = LegalGovernanceBoundaryContracts.FeatureId,
            ["generatedAt"] = LegalGovernanceBoundaryContracts.FormatTimestamp(generatedAt),
            ["governanceInputs"] = new JsonArray(LegalGovernanceBoundaryContracts
                .RequireArray(source, "governanceInputs")
                .OfType<JsonObject>()
                .OrderBy(input => LegalGovernanceBoundaryContracts.GetString(input, "inputId"), StringComparer.Ordinal)
                .Select(BuildGovernanceInputRow)
                .ToArray<JsonNode?>()),
            ["claimImpactScenarios"] = new JsonArray(LegalGovernanceBoundaryContracts
                .RequireArray(source, "claimImpactScenarios")
                .OfType<JsonObject>()
                .OrderBy(scenario => LegalGovernanceBoundaryContracts.GetString(scenario, "scenarioId"), StringComparer.Ordinal)
                .Select(BuildClaimScenario)
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildGovernanceInputRow(JsonObject input)
    {
        var status = LegalGovernanceBoundaryContracts.GetString(input, "status");
        return new JsonObject
        {
            ["inputId"] = LegalGovernanceBoundaryContracts.GetString(input, "inputId"),
            ["label"] = LegalGovernanceBoundaryContracts.GetString(input, "label"),
            ["status"] = status,
            ["owner"] = LegalGovernanceBoundaryContracts.GetString(input, "owner"),
            ["statusReason"] = LegalGovernanceBoundaryContracts.GetString(input, "statusReason"),
            ["publicSafeSummary"] = LegalGovernanceBoundaryContracts.GetString(input, "publicSafeSummary"),
            ["hushConfigurationImpact"] = LegalGovernanceBoundaryContracts.GetString(input, "hushConfigurationImpact"),
            ["affectedClaims"] = LegalGovernanceBoundaryContracts.Clone(input["affectedClaims"]),
            ["blockerIds"] = LegalGovernanceBoundaryContracts.Clone(input["blockerIds"]) ?? new JsonArray(),
            ["downgradeIds"] = LegalGovernanceBoundaryContracts.Clone(input["downgradeIds"]) ?? new JsonArray(),
            ["staleTriggers"] = LegalGovernanceBoundaryContracts.Clone(input["staleTriggers"]),
            ["effectiveDecision"] = ResolveInputDecision(input),
        };
    }

    private static JsonObject BuildClaimScenario(JsonObject scenario)
    {
        var effectiveDecision = ResolveScenarioDecision(scenario);
        return new JsonObject
        {
            ["scenarioId"] = LegalGovernanceBoundaryContracts.GetString(scenario, "scenarioId"),
            ["label"] = LegalGovernanceBoundaryContracts.GetString(scenario, "label"),
            ["sourceDecision"] = LegalGovernanceBoundaryContracts.GetString(scenario, "decision"),
            ["effectiveDecision"] = effectiveDecision,
            ["evidenceStates"] = LegalGovernanceBoundaryContracts.Clone(scenario["evidenceStates"]),
            ["affectedClaims"] = LegalGovernanceBoundaryContracts.Clone(scenario["affectedClaims"]),
            ["blockerIds"] = LegalGovernanceBoundaryContracts.Clone(scenario["blockerIds"]) ?? new JsonArray(),
            ["downgradeIds"] = LegalGovernanceBoundaryContracts.Clone(scenario["downgradeIds"]) ?? new JsonArray(),
            ["publicWordingKey"] = LegalGovernanceBoundaryContracts.GetString(scenario, "publicWordingKey"),
            ["residualRisk"] = LegalGovernanceBoundaryContracts.GetString(scenario, "residualRisk"),
            ["decisionRule"] = "Active missing, stale, or blocked governance evidence blocks claims; customer-deferred inputs downgrade or block according to source impact.",
        };
    }

    private static JsonObject BuildRestrictedIndex(JsonObject source, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "legal-governance-boundary-restricted-index.v1",
            ["indexId"] = "LEGAL-GOVERNANCE-RESTRICTED-INDEX-FEAT-140-001",
            ["featureSlice"] = LegalGovernanceBoundaryContracts.FeatureId,
            ["generatedAt"] = LegalGovernanceBoundaryContracts.FormatTimestamp(generatedAt),
            ["customerScope"] = LegalGovernanceBoundaryContracts.Clone(source["customerScope"]),
            ["authorityBoundaryRefs"] = new JsonObject
            {
                ["authorityActorRef"] = LegalGovernanceBoundaryContracts.GetString(
                    LegalGovernanceBoundaryContracts.RequireObject(source, "authorityBoundary"),
                    "authorityActorRef"),
                ["setupSignerRef"] = LegalGovernanceBoundaryContracts.GetString(
                    LegalGovernanceBoundaryContracts.RequireObject(source, "authorityBoundary"),
                    "setupSignerRef"),
                ["acknowledgementRef"] = LegalGovernanceBoundaryContracts.GetString(
                    LegalGovernanceBoundaryContracts.RequireObject(source, "authorityBoundary"),
                    "acknowledgementRef"),
                ["acknowledgementHash"] = LegalGovernanceBoundaryContracts.GetString(
                    LegalGovernanceBoundaryContracts.RequireObject(source, "authorityBoundary"),
                    "acknowledgementHash"),
            },
            ["sourceEvidenceRefs"] = LegalGovernanceBoundaryContracts.Clone(source["evidenceRefs"]),
            ["governanceInputRestrictedRefs"] = new JsonArray(LegalGovernanceBoundaryContracts
                .RequireArray(source, "governanceInputs")
                .OfType<JsonObject>()
                .OrderBy(input => LegalGovernanceBoundaryContracts.GetString(input, "inputId"), StringComparer.Ordinal)
                .Select(input => new JsonObject
                {
                    ["inputId"] = LegalGovernanceBoundaryContracts.GetString(input, "inputId"),
                    ["status"] = LegalGovernanceBoundaryContracts.GetString(input, "status"),
                    ["evidenceRef"] = LegalGovernanceBoundaryContracts.GetString(input, "evidenceRef"),
                    ["restrictedEvidenceRefs"] = LegalGovernanceBoundaryContracts.Clone(input["restrictedEvidenceRefs"]) ?? new JsonArray(),
                    ["staleTriggers"] = LegalGovernanceBoundaryContracts.Clone(input["staleTriggers"]) ?? new JsonArray(),
                })
                .ToArray<JsonNode?>()),
            ["redactionPolicy"] = new JsonObject
            {
                ["publicOutputPolicy"] = "Public outputs are generated from allowlisted summaries and hashes only.",
                ["restrictedPayloadPolicy"] = "Restricted output carries refs and hashes only; private customer/legal payload bodies are not embedded.",
            },
        };

    private static JsonObject BuildFeat139Handoff(JsonObject source, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "legal-governance-boundary-feat139-handoff.v1",
            ["handoffId"] = "LEGAL-GOVERNANCE-FEAT139-HANDOFF-FEAT-140-001",
            ["producerFeature"] = LegalGovernanceBoundaryContracts.FeatureId,
            ["targetFeature"] = "FEAT-139",
            ["generatedAt"] = LegalGovernanceBoundaryContracts.FormatTimestamp(generatedAt),
            ["blockerMappings"] = new JsonArray(LegalGovernanceBoundaryContracts
                .RequireArray(source, "feat139BlockerMappings")
                .OfType<JsonObject>()
                .OrderBy(mapping => LegalGovernanceBoundaryContracts.GetString(mapping, "blockerId"), StringComparer.Ordinal)
                .Select(mapping => new JsonObject
                {
                    ["blockerId"] = LegalGovernanceBoundaryContracts.GetString(mapping, "blockerId"),
                    ["classification"] = LegalGovernanceBoundaryContracts.GetString(mapping, "classification"),
                    ["governanceInputIds"] = LegalGovernanceBoundaryContracts.Clone(mapping["governanceInputIds"]),
                    ["evidenceState"] = LegalGovernanceBoundaryContracts.GetString(mapping, "evidenceState"),
                    ["runtimeProducer"] = LegalGovernanceBoundaryContracts.GetString(mapping, "runtimeProducer"),
                    ["publicWordingKey"] = LegalGovernanceBoundaryContracts.GetString(mapping, "publicWordingKey"),
                    ["residualRisk"] = LegalGovernanceBoundaryContracts.GetString(mapping, "residualRisk"),
                })
                .ToArray<JsonNode?>()),
            ["consumerInstruction"] = "FEAT-139 can clear governance-boundary prerequisites where evidenceState is governance_boundary_cleared, but must keep FEAT-146 runtime blockers until accepted outcome evidence exists.",
        };

    private static JsonObject BuildFeat146Handoff(JsonObject source, DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "legal-governance-boundary-feat146-handoff.v1",
            ["handoffId"] = "LEGAL-GOVERNANCE-FEAT146-HANDOFF-FEAT-140-001",
            ["producerFeature"] = LegalGovernanceBoundaryContracts.FeatureId,
            ["targetFeature"] = "FEAT-146",
            ["generatedAt"] = LegalGovernanceBoundaryContracts.FormatTimestamp(generatedAt),
            ["authorityInputs"] = new JsonArray(LegalGovernanceBoundaryContracts
                .RequireArray(source, "feat146AuthorityInputs")
                .OfType<JsonObject>()
                .OrderBy(input => LegalGovernanceBoundaryContracts.GetString(input, "outcome"), StringComparer.Ordinal)
                .Select(input => new JsonObject
                {
                    ["outcome"] = LegalGovernanceBoundaryContracts.GetString(input, "outcome"),
                    ["authorityRole"] = LegalGovernanceBoundaryContracts.GetString(input, "authorityRole"),
                    ["authorityActorRef"] = LegalGovernanceBoundaryContracts.GetString(input, "authorityActorRef"),
                    ["governingRuleRef"] = LegalGovernanceBoundaryContracts.GetString(input, "governingRuleRef"),
                    ["finalityRuleStatus"] = LegalGovernanceBoundaryContracts.GetString(input, "finalityRuleStatus"),
                    ["remedyAuthorityStatus"] = LegalGovernanceBoundaryContracts.GetString(input, "remedyAuthorityStatus"),
                    ["challengeProcessStatus"] = LegalGovernanceBoundaryContracts.GetString(input, "challengeProcessStatus"),
                    ["publicSafeLimitationWording"] = LegalGovernanceBoundaryContracts.GetString(input, "publicSafeLimitationWording"),
                    ["blockerIds"] = LegalGovernanceBoundaryContracts.Clone(input["blockerIds"]) ?? new JsonArray(),
                })
                .ToArray<JsonNode?>()),
            ["producerBoundary"] = "FEAT-140 provides authority/rule inputs only. FEAT-146 must validate state and produce signed governed outcome decisions.",
        };

    private static JsonObject BuildReadinessFragment(
        JsonObject source,
        string status,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> downgrades,
        params object[] artifactArgs)
    {
        var generatedAt = (DateTimeOffset)artifactArgs[^1];
        var artifacts = artifactArgs[..^1].Cast<LegalGovernanceBoundaryGeneratedArtifact>().ToArray();
        var register = LegalGovernanceBoundaryContracts.RequireObject(source, "currentReadinessRegister");
        var currentScore = LegalGovernanceBoundaryContracts.GetInt(register, "currentScore");
        var candidateScore = LegalGovernanceBoundaryContracts.GetInt(register, "candidateScoreWhenAccepted", currentScore);
        return new JsonObject
        {
            ["schemaVersion"] = "legal-governance-boundary-readiness-fragment.v1",
            ["fragmentId"] = LegalGovernanceBoundaryContracts.ReadinessFragmentId,
            ["featureSlice"] = LegalGovernanceBoundaryContracts.FeatureId,
            ["sourceGap"] = LegalGovernanceBoundaryContracts.GetString(source, "sourceGap"),
            ["acceptanceGate"] = LegalGovernanceBoundaryContracts.AcceptanceGate,
            ["dimensionId"] = LegalGovernanceBoundaryContracts.GetString(register, "dimensionId"),
            ["status"] = status,
            ["directRegisterMutation"] = false,
            ["doesNotMutateRegister"] = true,
            ["registerPromotionOwner"] = "FEAT-130",
            ["generatedAt"] = LegalGovernanceBoundaryContracts.FormatTimestamp(generatedAt),
            ["scoreEffect"] = new JsonObject
            {
                ["currentScore"] = currentScore,
                ["candidateScoreWhenAccepted"] = candidateScore,
                ["appliedScore"] = status == "blocked" ? currentScore : candidateScore,
                ["targetScoreBeforeReviewPilot"] = LegalGovernanceBoundaryContracts.GetInt(register, "targetScoreBeforeReviewPilot"),
                ["scoreIncreaseRequiresFeat130Promotion"] = true,
            },
            ["blockers"] = ToJsonArray(blockers),
            ["downgrades"] = ToJsonArray(downgrades),
            ["claimEffect"] = BuildClaimEffect(artifacts.Single(artifact => artifact.RelativePath == ClaimImpactMatrixPath)),
            ["artifactRefs"] = new JsonArray(artifacts.Select(ArtifactRef).ToArray<JsonNode?>()),
            ["nonLegalValidationWording"] = LegalGovernanceBoundaryContracts.RequiredDisclaimer,
            ["promotionInstructions"] = "FEAT-130 may promote this fragment later. FEAT-140 does not mutate the canonical readiness register directly.",
        };
    }

    private static JsonObject BuildDownstreamHandoff(
        JsonObject source,
        string status,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> downgrades,
        LegalGovernanceBoundaryGeneratedArtifact readinessArtifact,
        LegalGovernanceBoundaryGeneratedArtifact claimMatrixArtifact,
        LegalGovernanceBoundaryGeneratedArtifact restrictedIndexArtifact,
        LegalGovernanceBoundaryGeneratedArtifact feat139HandoffArtifact,
        LegalGovernanceBoundaryGeneratedArtifact feat146HandoffArtifact,
        LegalGovernanceBoundaryGeneratedArtifact publicSummaryArtifact,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "legal-governance-boundary-downstream-handoff.v1",
            ["handoffId"] = "LEGAL-GOVERNANCE-DOWNSTREAM-HANDOFF-FEAT-140-001",
            ["producerFeature"] = LegalGovernanceBoundaryContracts.FeatureId,
            ["status"] = status,
            ["generatedAt"] = LegalGovernanceBoundaryContracts.FormatTimestamp(generatedAt),
            ["blockers"] = ToJsonArray(blockers),
            ["downgrades"] = ToJsonArray(downgrades),
            ["feat130Handoff"] = new JsonObject
            {
                ["targetFeature"] = "FEAT-130",
                ["readinessFragmentRef"] = readinessArtifact.RelativePath,
                ["readinessFragmentHash"] = readinessArtifact.Sha256Hash,
                ["acceptanceGate"] = LegalGovernanceBoundaryContracts.AcceptanceGate,
                ["dimensionId"] = LegalGovernanceBoundaryContracts.DimensionId,
                ["directRegisterMutation"] = false,
            },
            ["feat139Handoff"] = ArtifactRef(feat139HandoffArtifact),
            ["feat141Handoff"] = new JsonObject
            {
                ["targetFeature"] = "FEAT-141",
                ["publicSafeSummaryRef"] = publicSummaryArtifact.RelativePath,
                ["publicSafeSummaryHash"] = publicSummaryArtifact.Sha256Hash,
                ["claimImpactMatrixRef"] = claimMatrixArtifact.RelativePath,
                ["claimImpactMatrixHash"] = claimMatrixArtifact.Sha256Hash,
                ["restrictedIndexRef"] = restrictedIndexArtifact.RelativePath,
                ["restrictedIndexHash"] = restrictedIndexArtifact.Sha256Hash,
                ["feat146HandoffRef"] = feat146HandoffArtifact.RelativePath,
                ["feat146HandoffHash"] = feat146HandoffArtifact.Sha256Hash,
            },
            ["privacyBoundary"] = new JsonObject
            {
                ["publicHandoffAllowed"] = new JsonArray("artifact hashes", "claim status", "blocker ids", "residual risk wording", "public package refs"),
                ["restrictedPayloadsExcluded"] = true,
            },
            ["nonLegalValidationWording"] = LegalGovernanceBoundaryContracts.RequiredDisclaimer,
            ["customerScope"] = LegalGovernanceBoundaryContracts.Clone(source["customerScope"]),
        };

    private static JsonObject BuildPackage(
        JsonObject source,
        string status,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> downgrades,
        IReadOnlyCollection<LegalGovernanceBoundaryGeneratedArtifact> generatedArtifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "legal-governance-boundary-package.v1",
            ["packageId"] = "LEGAL-GOVERNANCE-BOUNDARY-PACKAGE-FEAT-140-001",
            ["featureSlice"] = LegalGovernanceBoundaryContracts.FeatureId,
            ["acceptanceGate"] = LegalGovernanceBoundaryContracts.AcceptanceGate,
            ["generatedAt"] = LegalGovernanceBoundaryContracts.FormatTimestamp(generatedAt),
            ["status"] = status,
            ["blockers"] = ToJsonArray(blockers),
            ["downgrades"] = ToJsonArray(downgrades),
            ["customerScope"] = LegalGovernanceBoundaryContracts.Clone(source["customerScope"]),
            ["nonLegalValidationWording"] = LegalGovernanceBoundaryContracts.RequiredDisclaimer,
            ["artifactRefs"] = new JsonArray(generatedArtifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(ArtifactRef)
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildPackageHashValidation(
        JsonObject source,
        IReadOnlyCollection<LegalGovernanceBoundaryGeneratedArtifact> generatedArtifacts,
        IReadOnlyList<string> blockers,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "legal-governance-boundary-package-hash-validation.v1",
            ["validationId"] = "LEGAL-GOVERNANCE-HASH-FEAT-140-001",
            ["generatedAt"] = LegalGovernanceBoundaryContracts.FormatTimestamp(generatedAt),
            ["status"] = blockers.Count == 0 ? "passed" : "blocked",
            ["canonicalizationVersion"] = LegalGovernanceBoundaryContracts.CanonicalizationVersion,
            ["sourceEvidenceRefs"] = new JsonArray(LegalGovernanceBoundaryContracts
                .RequireArray(source, "evidenceRefs")
                .OfType<JsonObject>()
                .Select(evidence => new JsonObject
                {
                    ["evidenceId"] = LegalGovernanceBoundaryContracts.GetString(evidence, "evidenceId"),
                    ["path"] = LegalGovernanceBoundaryContracts.GetString(evidence, "path"),
                    ["declaredSha256Hash"] = LegalGovernanceBoundaryContracts.GetString(evidence, "sha256Hash"),
                    ["hashFormat"] = "sha256-hex-or-source-controlled",
                })
                .ToArray<JsonNode?>()),
            ["generatedArtifactHashes"] = new JsonArray(generatedArtifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(ArtifactRef)
                .ToArray<JsonNode?>()),
        };

    private static string BuildPublicSafeSummary(
        JsonObject source,
        JsonObject claimMatrix,
        string status,
        DateTimeOffset generatedAt)
    {
        var publicSummary = LegalGovernanceBoundaryContracts.RequireObject(source, "publicSummary");
        var inputs = LegalGovernanceBoundaryContracts.RequireArray(claimMatrix, "governanceInputs")
            .OfType<JsonObject>()
            .ToArray();
        var scenarios = LegalGovernanceBoundaryContracts.RequireArray(claimMatrix, "claimImpactScenarios")
            .OfType<JsonObject>()
            .Where(scenario => LegalGovernanceBoundaryContracts.GetString(scenario, "effectiveDecision") != "not_in_scope")
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine($"# {LegalGovernanceBoundaryContracts.GetString(publicSummary, "title")}");
        builder.AppendLine();
        builder.AppendLine($"Generated: {LegalGovernanceBoundaryContracts.FormatTimestamp(generatedAt)}");
        builder.AppendLine($"Status: {status}");
        builder.AppendLine();
        builder.AppendLine(LegalGovernanceBoundaryContracts.GetString(publicSummary, "statusWording"));
        builder.AppendLine();
        builder.AppendLine("## Governance Inputs");
        foreach (var input in inputs)
        {
            builder.Append("- ");
            builder.Append(LegalGovernanceBoundaryContracts.GetString(input, "label"));
            builder.Append(": ");
            builder.AppendLine(LegalGovernanceBoundaryContracts.GetString(input, "status"));
        }

        builder.AppendLine();
        builder.AppendLine("## Claim Decisions");
        foreach (var scenario in scenarios)
        {
            builder.Append("- ");
            builder.Append(LegalGovernanceBoundaryContracts.GetString(scenario, "label"));
            builder.Append(": ");
            builder.AppendLine(LegalGovernanceBoundaryContracts.GetString(scenario, "effectiveDecision"));
        }

        builder.AppendLine();
        builder.AppendLine("## Allowed Claims");
        foreach (var claim in LegalGovernanceBoundaryContracts.GetStringArray(publicSummary, "allowedClaims"))
        {
            builder.Append("- ");
            builder.AppendLine(claim);
        }

        builder.AppendLine();
        builder.AppendLine("## Blocked Claims");
        foreach (var claim in LegalGovernanceBoundaryContracts.GetStringArray(publicSummary, "blockedClaims"))
        {
            builder.Append("- ");
            builder.AppendLine(claim);
        }

        builder.AppendLine();
        builder.AppendLine("## Residual Risks");
        foreach (var risk in LegalGovernanceBoundaryContracts.GetStringArray(publicSummary, "residualRisks"))
        {
            builder.Append("- ");
            builder.AppendLine(risk);
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> BuildBlockers(
        JsonObject source,
        JsonObject claimMatrix,
        IReadOnlyList<LegalGovernanceBoundaryMaterialFinding> publicFindings)
    {
        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var input in LegalGovernanceBoundaryContracts
            .RequireArray(claimMatrix, "governanceInputs")
            .OfType<JsonObject>())
        {
            if (LegalGovernanceBoundaryContracts.GetString(input, "effectiveDecision") != "block")
            {
                continue;
            }

            AddBlockersFrom(input, LegalGovernanceBoundaryContracts.GetString(input, "inputId"));
        }

        foreach (var scenario in LegalGovernanceBoundaryContracts
            .RequireArray(claimMatrix, "claimImpactScenarios")
            .OfType<JsonObject>())
        {
            if (LegalGovernanceBoundaryContracts.GetString(scenario, "effectiveDecision") != "block")
            {
                continue;
            }

            AddBlockersFrom(scenario, LegalGovernanceBoundaryContracts.GetString(scenario, "scenarioId"));
        }

        foreach (var authorityInput in LegalGovernanceBoundaryContracts
            .RequireArray(source, "feat146AuthorityInputs")
            .OfType<JsonObject>())
        {
            foreach (var blocker in LegalGovernanceBoundaryContracts.GetStringArray(authorityInput, "blockerIds"))
            {
                blockers.Add(blocker);
            }
        }

        if (publicFindings.Count > 0)
        {
            blockers.Add("FEAT140-PUBLIC-FORBIDDEN-MATERIAL");
        }

        return blockers.ToArray();

        void AddBlockersFrom(JsonObject value, string id)
        {
            var ids = LegalGovernanceBoundaryContracts.GetStringArray(value, "blockerIds");
            if (ids.Count == 0)
            {
                blockers.Add($"FEAT140-{id.ToUpperInvariant()}");
            }
            else
            {
                foreach (var blocker in ids)
                {
                    blockers.Add(blocker);
                }
            }
        }
    }

    private static IReadOnlyList<string> BuildDowngrades(JsonObject source, JsonObject claimMatrix)
    {
        var downgrades = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var input in LegalGovernanceBoundaryContracts
            .RequireArray(claimMatrix, "governanceInputs")
            .OfType<JsonObject>())
        {
            if (LegalGovernanceBoundaryContracts.GetString(input, "effectiveDecision") is "downgrade" or "allow_with_limitations")
            {
                foreach (var downgrade in LegalGovernanceBoundaryContracts.GetStringArray(input, "downgradeIds"))
                {
                    downgrades.Add(downgrade);
                }
            }
        }

        foreach (var scenario in LegalGovernanceBoundaryContracts
            .RequireArray(claimMatrix, "claimImpactScenarios")
            .OfType<JsonObject>())
        {
            if (LegalGovernanceBoundaryContracts.GetString(scenario, "effectiveDecision") is "downgrade" or "allow_with_limitations")
            {
                foreach (var downgrade in LegalGovernanceBoundaryContracts.GetStringArray(scenario, "downgradeIds"))
                {
                    downgrades.Add(downgrade);
                }
            }
        }

        if (downgrades.Count == 0 && LegalGovernanceBoundaryContracts.GetString(source, "status") == "accepted_with_limitations")
        {
            downgrades.Add("FEAT140-NON-LEGAL-VALIDATION-LIMITATION");
        }

        return downgrades.ToArray();
    }

    private static string ResolvePackageStatus(
        JsonObject claimMatrix,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> downgrades)
    {
        if (blockers.Count > 0)
        {
            return "blocked";
        }

        var scenarioDecisions = LegalGovernanceBoundaryContracts
            .RequireArray(claimMatrix, "claimImpactScenarios")
            .OfType<JsonObject>()
            .Select(scenario => LegalGovernanceBoundaryContracts.GetString(scenario, "effectiveDecision"))
            .Where(decision => decision != "not_in_scope")
            .ToArray();
        var inputDecisions = LegalGovernanceBoundaryContracts
            .RequireArray(claimMatrix, "governanceInputs")
            .OfType<JsonObject>()
            .Select(input => LegalGovernanceBoundaryContracts.GetString(input, "effectiveDecision"))
            .ToArray();

        return scenarioDecisions.Concat(inputDecisions).Any(decision => decision is "allow_with_limitations" or "downgrade") ||
            downgrades.Count > 0
            ? "accepted_with_limitations"
            : "accepted";
    }

    private static string ResolveInputDecision(JsonObject input) =>
        LegalGovernanceBoundaryContracts.GetString(input, "status") switch
        {
            "provided" => "allow",
            "not_applicable" => "allow",
            "customer_deferred" => LegalGovernanceBoundaryContracts.GetStringArray(input, "blockerIds").Count > 0 ? "block" : "downgrade",
            "not_provided" or "stale_or_superseded" => "block",
            _ => "block",
        };

    private static string ResolveScenarioDecision(JsonObject scenario)
    {
        var states = LegalGovernanceBoundaryContracts.GetStringArray(scenario, "evidenceStates");
        if (states.Contains("not_in_scope", StringComparer.Ordinal))
        {
            return "not_in_scope";
        }

        if (states.Any(state => state is "missing_required" or "blocked" or "stale_or_superseded"))
        {
            return "block";
        }

        var decision = LegalGovernanceBoundaryContracts.GetString(scenario, "decision");
        return decision == "allow_with_limitations" ||
            states.Contains("non_legal_validation_limitation", StringComparer.Ordinal)
            ? "allow_with_limitations"
            : decision;
    }

    private static JsonObject BuildClaimEffect(LegalGovernanceBoundaryGeneratedArtifact claimMatrixArtifact)
    {
        var claimMatrix = JsonNode.Parse(claimMatrixArtifact.Content)?.AsObject() ??
            throw new InvalidOperationException("Claim impact matrix artifact is not a JSON object.");
        var decisions = LegalGovernanceBoundaryContracts
            .RequireArray(claimMatrix, "claimImpactScenarios")
            .OfType<JsonObject>()
            .Select(scenario => LegalGovernanceBoundaryContracts.GetString(scenario, "effectiveDecision"))
            .ToArray();
        return new JsonObject
        {
            ["allowed"] = decisions.Count(decision => decision == "allow"),
            ["limited"] = decisions.Count(decision => decision == "allow_with_limitations"),
            ["downgraded"] = decisions.Count(decision => decision == "downgrade"),
            ["blocked"] = decisions.Count(decision => decision == "block"),
            ["notInScope"] = decisions.Count(decision => decision == "not_in_scope"),
        };
    }

    private static LegalGovernanceBoundaryGeneratedArtifact JsonArtifact(string relativePath, JsonObject content)
    {
        var text = LegalGovernanceBoundaryContracts.CanonicalJson(content);
        return new LegalGovernanceBoundaryGeneratedArtifact(
            relativePath,
            text,
            LegalGovernanceBoundaryContracts.Sha256Hex(text));
    }

    private static LegalGovernanceBoundaryGeneratedArtifact TextArtifact(string relativePath, string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.EndsWith('\n'))
        {
            normalized += "\n";
        }

        return new LegalGovernanceBoundaryGeneratedArtifact(
            relativePath,
            normalized,
            LegalGovernanceBoundaryContracts.Sha256Hex(normalized));
    }

    private static JsonObject ArtifactRef(LegalGovernanceBoundaryGeneratedArtifact artifact) =>
        new()
        {
            ["path"] = artifact.RelativePath,
            ["sha256Hash"] = artifact.Sha256Hash,
            ["hashFormat"] = "sha256-hex",
        };

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());
}
