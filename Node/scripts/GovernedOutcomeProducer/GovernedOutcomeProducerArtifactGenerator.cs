using System.Text.Json.Nodes;

namespace GovernedOutcomeProducer;

public static class GovernedOutcomeProducerArtifactGenerator
{
    public const string Feat139HandoffPath = "governed-outcome-feat139-handoff.json";
    public const string Feat141HandoffPath = "governed-outcome-feat141-handoff.json";
    public const string PackageHashValidationPath = "governed-outcome-package-hash-validation.json";

    public static GovernedOutcomeGeneratedPackage Generate(
        GovernedOutcomeProducerPaths paths,
        string? sourceInput = null,
        DateTimeOffset? generatedAt = null)
    {
        var source = GovernedOutcomeProducerContracts.LoadSource(paths, sourceInput);
        var validationErrors = GovernedOutcomeProducerContracts.ValidateSource(source);
        if (validationErrors.Count > 0)
        {
            throw new GovernedOutcomeProducerException(
                "FEAT-146 governed outcome producer source validation failed.",
                validationErrors);
        }

        var effectiveGeneratedAt = generatedAt ?? DateTimeOffset.UtcNow;
        var feat139Handoff = BuildFeat139Handoff(source, effectiveGeneratedAt);
        var feat141Handoff = BuildFeat141Handoff(source, effectiveGeneratedAt);

        var publicFindings = GovernedOutcomeProducerContracts.ScanForbiddenPublicMaterial(
            source,
            [
                (Feat139HandoffPath, GovernedOutcomeProducerContracts.CanonicalJson(feat139Handoff)),
                (Feat141HandoffPath, GovernedOutcomeProducerContracts.CanonicalJson(feat141Handoff)),
            ]);
        var blockers = BuildBlockers(source, publicFindings);
        var status = ResolvePackageStatus(source, blockers);
        feat139Handoff["status"] = status;
        feat139Handoff["blockers"] = ToJsonArray(blockers);
        feat141Handoff["status"] = status;
        feat141Handoff["blockers"] = ToJsonArray(blockers);

        var feat139Artifact = JsonArtifact(Feat139HandoffPath, feat139Handoff);
        var feat141Artifact = JsonArtifact(Feat141HandoffPath, feat141Handoff);
        var artifactsForHashValidation = new[]
        {
            feat139Artifact,
            feat141Artifact,
        };
        var hashValidation = BuildPackageHashValidation(source, artifactsForHashValidation, publicFindings, effectiveGeneratedAt);

        var artifacts = artifactsForHashValidation
            .Append(JsonArtifact(PackageHashValidationPath, hashValidation))
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new GovernedOutcomeGeneratedPackage(
            status,
            artifacts,
            publicFindings,
            blockers);
    }

    private static JsonObject BuildFeat139Handoff(JsonObject source, DateTimeOffset generatedAt)
    {
        var outcome = GovernedOutcomeProducerContracts.RequireObject(source, "governedOutcomeEvidence");
        var report = GovernedOutcomeProducerContracts.RequireObject(source, "reportPackageEvidence");
        var verification = GovernedOutcomeProducerContracts.RequireObject(source, "verificationEvidence");
        var policy = GovernedOutcomeProducerContracts.RequireObject(source, "handoffPolicy");

        return new JsonObject
        {
            ["schemaVersion"] = "governed-outcome-feat139-handoff.v1",
            ["handoffId"] = "GOVERNED-OUTCOME-FEAT139-HANDOFF-FEAT-146-001",
            ["producerFeature"] = GovernedOutcomeProducerContracts.FeatureId,
            ["targetFeature"] = "FEAT-139",
            ["status"] = GovernedOutcomeProducerContracts.GetString(source, "status"),
            ["generatedAt"] = GovernedOutcomeProducerContracts.FormatTimestamp(generatedAt),
            ["sourceId"] = GovernedOutcomeProducerContracts.GetString(source, "sourceId"),
            ["blockers"] = new JsonArray(),
            ["governedOutcome"] = new JsonObject
            {
                ["decisionId"] = GovernedOutcomeProducerContracts.GetString(outcome, "decisionId"),
                ["decisionHash"] = GovernedOutcomeProducerContracts.GetString(outcome, "decisionHash"),
                ["outcomeStatus"] = GovernedOutcomeProducerContracts.GetString(outcome, "outcomeStatus"),
                ["cleanFinalization"] = GovernedOutcomeProducerContracts.GetBool(outcome, "cleanFinalization"),
                ["finalizationMode"] = GovernedOutcomeProducerContracts.GetString(outcome, "finalizationMode"),
                ["authorityRole"] = GovernedOutcomeProducerContracts.GetString(outcome, "authorityRole"),
                ["authoritySource"] = GovernedOutcomeProducerContracts.GetString(outcome, "authoritySource"),
                ["feat140HandoffRef"] = GovernedOutcomeProducerContracts.GetString(outcome, "feat140HandoffRef"),
                ["feat140HandoffHash"] = GovernedOutcomeProducerContracts.GetString(outcome, "feat140HandoffHash"),
                ["authorityDecisionRef"] = GovernedOutcomeProducerContracts.GetString(outcome, "authorityDecisionRef"),
                ["authorityDecisionHash"] = GovernedOutcomeProducerContracts.GetString(outcome, "authorityDecisionHash"),
                ["governanceRuleRef"] = GovernedOutcomeProducerContracts.GetString(outcome, "governanceRuleRef"),
                ["finalityRuleRef"] = GovernedOutcomeProducerContracts.GetString(outcome, "finalityRuleRef"),
                ["remedyRuleRef"] = GovernedOutcomeProducerContracts.GetString(outcome, "remedyRuleRef"),
                ["exactCopyStatus"] = GovernedOutcomeProducerContracts.GetString(outcome, "exactCopyStatus"),
                ["fixedUnofficialResultRef"] = GovernedOutcomeProducerContracts.GetString(outcome, "fixedUnofficialResultRef"),
                ["officialResultRef"] = GovernedOutcomeProducerContracts.GetString(outcome, "officialResultRef"),
                ["closeBoundaryRef"] = GovernedOutcomeProducerContracts.GetString(outcome, "closeBoundaryRef"),
                ["tallyReadyBoundaryRef"] = GovernedOutcomeProducerContracts.GetString(outcome, "tallyReadyBoundaryRef"),
                ["finalizeBoundaryRef"] = GovernedOutcomeProducerContracts.GetString(outcome, "finalizeBoundaryRef"),
                ["sourceDecisionRecordRef"] = GovernedOutcomeProducerContracts.GetString(outcome, "sourceDecisionRecordRef"),
                ["abnormalEvidenceArtifactRef"] = GovernedOutcomeProducerContracts.GetString(report, "abnormalEvidenceArtifactRef"),
                ["abnormalEvidenceArtifactHash"] = GovernedOutcomeProducerContracts.GetString(report, "abnormalEvidenceArtifactHash"),
                ["verifierResultRef"] = GovernedOutcomeProducerContracts.GetString(verification, "verifierResultRef"),
                ["verifierResultHash"] = GovernedOutcomeProducerContracts.GetString(verification, "verifierResultHash"),
                ["keyLostEnforcementStatus"] = GovernedOutcomeProducerContracts.GetString(outcome, "keyLostEnforcementStatus"),
                ["keyLostDecisionRefs"] = GovernedOutcomeProducerContracts.Clone(outcome["keyLostDecisionRefs"]) ?? new JsonArray(),
            },
            ["blockerEffect"] = new JsonObject
            {
                ["blockersCleared"] = GovernedOutcomeProducerContracts.Clone(policy["feat139BlockersCleared"]) ?? new JsonArray(),
                ["blockersStillOpen"] = GovernedOutcomeProducerContracts.Clone(policy["feat139BlockersStillOpen"]) ?? new JsonArray(),
                ["acceptedRuntimeEvidence"] = GovernedOutcomeProducerContracts.GetString(source, "status") == "accepted",
                ["consumerInstruction"] = "Clear finalized-with-anomaly only from accepted FEAT-146 runtime evidence; keep failed-finalize blockers until a separate producer exists.",
            },
            ["privacyBoundary"] = BuildPrivacyBoundary(),
        };
    }

    private static JsonObject BuildFeat141Handoff(JsonObject source, DateTimeOffset generatedAt)
    {
        var outcome = GovernedOutcomeProducerContracts.RequireObject(source, "governedOutcomeEvidence");
        var report = GovernedOutcomeProducerContracts.RequireObject(source, "reportPackageEvidence");
        var verification = GovernedOutcomeProducerContracts.RequireObject(source, "verificationEvidence");
        var policy = GovernedOutcomeProducerContracts.RequireObject(source, "handoffPolicy");

        return new JsonObject
        {
            ["schemaVersion"] = "governed-outcome-feat141-handoff.v1",
            ["handoffId"] = "GOVERNED-OUTCOME-FEAT141-HANDOFF-FEAT-146-001",
            ["producerFeature"] = GovernedOutcomeProducerContracts.FeatureId,
            ["targetFeature"] = "FEAT-141",
            ["status"] = GovernedOutcomeProducerContracts.GetString(source, "status"),
            ["generatedAt"] = GovernedOutcomeProducerContracts.FormatTimestamp(generatedAt),
            ["sourceId"] = GovernedOutcomeProducerContracts.GetString(source, "sourceId"),
            ["blockers"] = new JsonArray(),
            ["runtimePackage"] = new JsonObject
            {
                ["reportPackageRef"] = GovernedOutcomeProducerContracts.GetString(report, "reportPackageRef"),
                ["reportPackageHash"] = GovernedOutcomeProducerContracts.GetString(report, "reportPackageHash"),
                ["reportPackageStatus"] = GovernedOutcomeProducerContracts.GetString(report, "reportPackageStatus"),
                ["abnormalEvidenceArtifactRef"] = GovernedOutcomeProducerContracts.GetString(report, "abnormalEvidenceArtifactRef"),
                ["abnormalEvidenceArtifactHash"] = GovernedOutcomeProducerContracts.GetString(report, "abnormalEvidenceArtifactHash"),
            },
            ["governedOutcome"] = new JsonObject
            {
                ["decisionId"] = GovernedOutcomeProducerContracts.GetString(outcome, "decisionId"),
                ["decisionHash"] = GovernedOutcomeProducerContracts.GetString(outcome, "decisionHash"),
                ["outcomeStatus"] = GovernedOutcomeProducerContracts.GetString(outcome, "outcomeStatus"),
                ["cleanFinalization"] = GovernedOutcomeProducerContracts.GetBool(outcome, "cleanFinalization"),
                ["finalizationMode"] = GovernedOutcomeProducerContracts.GetString(outcome, "finalizationMode"),
                ["publicSummary"] = GovernedOutcomeProducerContracts.GetString(outcome, "publicSummary"),
            },
            ["verifierSummary"] = new JsonObject
            {
                ["verifierResultRef"] = GovernedOutcomeProducerContracts.GetString(verification, "verifierResultRef"),
                ["verifierResultHash"] = GovernedOutcomeProducerContracts.GetString(verification, "verifierResultHash"),
                ["verifierStatus"] = GovernedOutcomeProducerContracts.GetString(verification, "verifierStatus"),
                ["verifierResultCode"] = GovernedOutcomeProducerContracts.GetString(verification, "verifierResultCode"),
                ["cleanFinalizationClaim"] = GovernedOutcomeProducerContracts.GetBool(verification, "cleanFinalizationClaim"),
            },
            ["claimStates"] = new JsonArray(GovernedOutcomeProducerContracts
                .RequireArray(policy, "feat141ClaimStates")
                .OfType<JsonObject>()
                .OrderBy(claim => GovernedOutcomeProducerContracts.GetString(claim, "claimId"), StringComparer.Ordinal)
                .Select(claim => claim.DeepClone())
                .ToArray<JsonNode?>()),
            ["publicWordingKeys"] = GovernedOutcomeProducerContracts.Clone(policy["publicWordingKeys"]) ?? new JsonArray(),
            ["residualRisks"] = GovernedOutcomeProducerContracts.Clone(policy["residualRisks"]) ?? new JsonArray(),
            ["restrictedEvidenceAvailability"] = GovernedOutcomeProducerContracts.Clone(policy["restrictedEvidenceAvailability"]) ?? new JsonObject(),
            ["privacyBoundary"] = BuildPrivacyBoundary(),
        };
    }

    private static JsonObject BuildPackageHashValidation(
        JsonObject source,
        IReadOnlyCollection<GovernedOutcomeGeneratedArtifact> generatedArtifacts,
        IReadOnlyList<GovernedOutcomeMaterialFinding> publicFindings,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "governed-outcome-package-hash-validation.v1",
            ["validationId"] = "GOVERNED-OUTCOME-HASH-FEAT-146-001",
            ["generatedAt"] = GovernedOutcomeProducerContracts.FormatTimestamp(generatedAt),
            ["status"] = publicFindings.Count == 0 ? "passed" : "blocked",
            ["canonicalizationVersion"] = GovernedOutcomeProducerContracts.CanonicalizationVersion,
            ["publicForbiddenFindings"] = new JsonArray(publicFindings
                .Select(finding => new JsonObject
                {
                    ["path"] = finding.RelativePath,
                    ["category"] = finding.Category,
                    ["evidence"] = finding.Evidence,
                })
                .ToArray<JsonNode?>()),
            ["sourceEvidenceRefs"] = new JsonArray(GovernedOutcomeProducerContracts
                .RequireArray(source, "evidenceRefs")
                .OfType<JsonObject>()
                .Select(evidence => new JsonObject
                {
                    ["evidenceId"] = GovernedOutcomeProducerContracts.GetString(evidence, "evidenceId"),
                    ["path"] = GovernedOutcomeProducerContracts.GetString(evidence, "path"),
                    ["declaredSha256Hash"] = GovernedOutcomeProducerContracts.GetString(evidence, "sha256Hash"),
                    ["hashFormat"] = "sha256-hex-or-source-controlled",
                })
                .ToArray<JsonNode?>()),
            ["generatedArtifactHashes"] = new JsonArray(generatedArtifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(ArtifactRef)
                .ToArray<JsonNode?>()),
        };

    private static IReadOnlyList<string> BuildBlockers(
        JsonObject source,
        IReadOnlyList<GovernedOutcomeMaterialFinding> publicFindings)
    {
        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        if (GovernedOutcomeProducerContracts.GetString(source, "status") == "blocked")
        {
            blockers.Add("FEAT146-SOURCE-STATUS-BLOCKED");
        }

        if (publicFindings.Count > 0)
        {
            blockers.Add("FEAT146-PUBLIC-FORBIDDEN-MATERIAL");
        }

        return blockers.ToArray();
    }

    private static string ResolvePackageStatus(JsonObject source, IReadOnlyList<string> blockers) =>
        blockers.Count > 0 ? "blocked" : GovernedOutcomeProducerContracts.GetString(source, "status");

    private static JsonObject BuildPrivacyBoundary() =>
        new()
        {
            ["publicHandoffAllowed"] = new JsonArray(
                "artifact ids",
                "artifact hashes",
                "claim states",
                "blocker ids",
                "public status wording",
                "public package refs"),
            ["restrictedPayloadsExcluded"] = true,
            ["excludedPayloads"] = new JsonArray(
                "anomaly bodies",
                "trustee threshold material",
                "secret signing material",
                "voter identities",
                "ballot choices",
                "private legal payloads"),
        };

    private static GovernedOutcomeGeneratedArtifact JsonArtifact(string relativePath, JsonObject content)
    {
        var text = GovernedOutcomeProducerContracts.CanonicalJson(content);
        return new GovernedOutcomeGeneratedArtifact(
            relativePath,
            text,
            GovernedOutcomeProducerContracts.Sha256Hex(text));
    }

    private static JsonObject ArtifactRef(GovernedOutcomeGeneratedArtifact artifact) =>
        new()
        {
            ["path"] = artifact.RelativePath,
            ["sha256Hash"] = artifact.Sha256Hash,
            ["hashFormat"] = "sha256-hex",
        };

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());
}
