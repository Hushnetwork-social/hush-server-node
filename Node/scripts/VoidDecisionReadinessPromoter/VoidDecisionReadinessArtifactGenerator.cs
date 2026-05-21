using System.Text.Json.Nodes;

namespace VoidDecisionReadinessPromoter;

public static class VoidDecisionReadinessArtifactGenerator
{
    public const string ReadinessFragmentPath = "void-readiness-fragment.json";
    public const string DownstreamHandoffPath = "void-downstream-handoff.json";
    public const string PublicArtifactScanPath = "void-public-artifact-scan.json";
    public const string PackageHashValidationPath = "void-package-hash-validation.json";

    public static VoidDecisionReadinessGeneratedPackage Generate(
        VoidDecisionReadinessPromotionPaths paths,
        string? sourceInput = null,
        DateTimeOffset? generatedAt = null)
    {
        var source = VoidDecisionReadinessContracts.LoadSource(paths, sourceInput);
        var validationErrors = VoidDecisionReadinessContracts.ValidateSource(source);
        if (validationErrors.Count > 0)
        {
            throw new VoidDecisionReadinessPromotionException(
                "FEAT-138 void readiness source validation failed.",
                validationErrors);
        }

        var effectiveGeneratedAt = generatedAt ?? DateTimeOffset.UtcNow;
        var publicFindings = VoidDecisionReadinessContracts.ScanForbiddenPublicMaterial(source);
        var blockers = BuildBlockers(source, publicFindings);
        var status = blockers.Count == 0 ? "accepted" : "blocked";
        var publicScan = BuildPublicArtifactScan(source, publicFindings, effectiveGeneratedAt);
        var readiness = BuildReadinessFragment(source, status, blockers, publicFindings, effectiveGeneratedAt);
        var readinessArtifact = JsonArtifact(ReadinessFragmentPath, readiness);
        var handoff = BuildDownstreamHandoff(source, status, readinessArtifact, publicFindings, effectiveGeneratedAt);
        var publicScanArtifact = JsonArtifact(PublicArtifactScanPath, publicScan);
        var handoffArtifact = JsonArtifact(DownstreamHandoffPath, handoff);
        var artifactsForHashValidation = new[]
        {
            readinessArtifact,
            handoffArtifact,
            publicScanArtifact,
        };
        var hashValidation = BuildPackageHashValidation(source, artifactsForHashValidation, effectiveGeneratedAt);

        var artifacts = new[]
        {
            readinessArtifact,
            handoffArtifact,
            publicScanArtifact,
            JsonArtifact(PackageHashValidationPath, hashValidation),
        };

        return new VoidDecisionReadinessGeneratedPackage(
            status,
            artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray(),
            publicFindings,
            blockers);
    }

    private static JsonObject BuildReadinessFragment(
        JsonObject source,
        string status,
        IReadOnlyList<string> blockers,
        IReadOnlyList<VoidDecisionReadinessMaterialFinding> publicFindings,
        DateTimeOffset generatedAt)
    {
        var scoreEffect = VoidDecisionReadinessContracts.RequireObject(source, "scoreEffect");
        return new JsonObject
        {
            ["schemaVersion"] = "void-readiness-fragment.v1",
            ["fragmentId"] = VoidDecisionReadinessContracts.ReadinessFragmentId,
            ["featureSlice"] = VoidDecisionReadinessContracts.FeatureId,
            ["sourceGap"] = VoidDecisionReadinessContracts.GetString(source, "sourceGap"),
            ["acceptanceGate"] = VoidDecisionReadinessContracts.AcceptanceGate,
            ["status"] = status,
            ["doesNotMutateRegister"] = true,
            ["registerPromotionOwner"] = "FEAT-130",
            ["evidenceRefs"] = VoidDecisionReadinessContracts.Clone(source["evidenceRefs"]),
            ["focusedVerification"] = VoidDecisionReadinessContracts.Clone(source["focusedVerification"]),
            ["blockers"] = ToJsonArray(blockers),
            ["forbiddenMaterialFindings"] = ToJsonArray(publicFindings.Select(FormatFinding)),
            ["dimensionScoreChange"] = new JsonObject
            {
                ["dimensionId"] = VoidDecisionReadinessContracts.GetString(scoreEffect, "dimensionId"),
                ["previousScore"] = scoreEffect["previousScore"]?.DeepClone(),
                ["acceptedScore"] = scoreEffect["acceptedScore"]?.DeepClone(),
                ["appliedScore"] = status == "accepted" ? scoreEffect["acceptedScore"]?.DeepClone() : scoreEffect["previousScore"]?.DeepClone(),
                ["targetGapAfterAcceptedEvidence"] = scoreEffect["targetGapAfterAcceptedEvidence"]?.DeepClone(),
            },
            ["claimEffect"] = VoidDecisionReadinessContracts.Clone(source["claimEffect"]),
            ["residualRisk"] = VoidDecisionReadinessContracts.Clone(source["residualRisk"]),
            ["signoff"] = new JsonObject
            {
                ["generatedAt"] = VoidDecisionReadinessContracts.FormatTimestamp(generatedAt),
                ["owner"] = "AboimPinto Consulting readiness maintainer",
                ["status"] = "pending_owner_review",
            },
            ["promotionInstructions"] = "FEAT-130 may promote this fragment only when status is accepted and public forbidden-material findings are empty.",
        };
    }

    private static JsonObject BuildDownstreamHandoff(
        JsonObject source,
        string status,
        VoidDecisionReadinessGeneratedArtifact readinessArtifact,
        IReadOnlyList<VoidDecisionReadinessMaterialFinding> publicFindings,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "void-downstream-handoff.v1",
            ["handoffId"] = "VOID-HANDOFF-FEAT-138-001",
            ["producerFeature"] = VoidDecisionReadinessContracts.FeatureId,
            ["status"] = status,
            ["generatedAt"] = VoidDecisionReadinessContracts.FormatTimestamp(generatedAt),
            ["readinessRegisterHandoff"] = new JsonObject
            {
                ["targetFeature"] = "FEAT-130",
                ["readinessFragmentRef"] = ReadinessFragmentPath,
                ["readinessFragmentHash"] = readinessArtifact.Sha256Hash,
                ["acceptanceGate"] = VoidDecisionReadinessContracts.AcceptanceGate,
                ["directRegisterMutation"] = false,
                ["promotionPreconditions"] = new JsonArray(
                    "status accepted",
                    "focused FEAT-138 unit/TwinTest/E2E gates passed",
                    "public forbidden-material findings empty",
                    "package/hash validation passed"),
            },
            ["feat139Handoff"] = new JsonObject
            {
                ["targetFeature"] = "FEAT-139",
                ["consumerAction"] = "Consume void decision id, justification hash, lifecycle transition refs, superseded package refs, and verifier result when a voided election is in scope.",
                ["missingEvidencePolicy"] = "Missing FEAT-138 evidence for a voided election must block or downgrade dispute/continuity readiness claims.",
            },
            ["feat141Handoff"] = new JsonObject
            {
                ["targetFeature"] = "FEAT-141",
                ["consumerAction"] = "Use public VOID package/status refs and restricted evidence index ids when assembling pilot evidence. Do not copy restricted anomaly bodies, support logs, voter identities, or tally material into public outputs.",
                ["proofRequestFlow"] = "Customer-safe proof requests should receive public VOID package refs plus restricted evidence ids for owner/auditor review.",
            },
            ["pba013Handoff"] = VoidDecisionReadinessContracts.Clone(source["pba013Handoff"]),
            ["voidEvidenceContract"] = VoidDecisionReadinessContracts.Clone(source["voidEvidenceContract"]),
            ["privacyBoundary"] = new JsonObject
            {
                ["publicArtifacts"] = new JsonArray(
                    "void-decision id/hash only",
                    "public justification",
                    "public status/package refs",
                    "election_voided verifier result",
                    "superseded artifact ids/hashes"),
                ["restrictedArtifacts"] = new JsonArray(
                    "restricted void evidence index",
                    "historical unofficial result when present",
                    "anomaly bodies and support records"),
                ["publicForbiddenMaterialFindings"] = ToJsonArray(publicFindings.Select(FormatFinding)),
            },
            ["consumerInstructions"] = new JsonObject
            {
                ["FEAT-130"] = "Promote only after blockers clear; this producer never mutates the canonical readiness register directly.",
                ["FEAT-139"] = "Use this handoff to bind void evidence into dispute/continuity readiness checks.",
                ["FEAT-141"] = "Use this handoff to assemble pilot evidence without exposing restricted material.",
                ["PBA-013"] = "Treat missing FEAT-143/FEAT-144 runtime binding refs as a readiness downgrade, not as a blocker for the owner void decision itself.",
            },
        };

    private static JsonObject BuildPublicArtifactScan(
        JsonObject source,
        IReadOnlyList<VoidDecisionReadinessMaterialFinding> findings,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "void-public-artifact-scan.v1",
            ["scanId"] = "VOID-PUBLIC-SCAN-FEAT-138-001",
            ["generatedAt"] = VoidDecisionReadinessContracts.FormatTimestamp(generatedAt),
            ["status"] = findings.Count == 0 ? "passed" : "blocked",
            ["scannedArtifacts"] = new JsonArray(VoidDecisionReadinessContracts
                .RequireArray(source, "publicArtifactSamples")
                .OfType<JsonObject>()
                .Select(sample => new JsonObject
                {
                    ["path"] = VoidDecisionReadinessContracts.GetString(sample, "path"),
                    ["sha256Hash"] = VoidDecisionReadinessContracts.Sha256Hex(VoidDecisionReadinessContracts.GetString(sample, "content")),
                })
                .ToArray<JsonNode?>()),
            ["forbiddenMaterialFindings"] = ToJsonArray(findings.Select(FormatFinding)),
        };

    private static JsonObject BuildPackageHashValidation(
        JsonObject source,
        IReadOnlyCollection<VoidDecisionReadinessGeneratedArtifact> generatedArtifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "void-package-hash-validation.v1",
            ["validationId"] = "VOID-HASH-FEAT-138-001",
            ["generatedAt"] = VoidDecisionReadinessContracts.FormatTimestamp(generatedAt),
            ["status"] = "passed",
            ["canonicalizationVersion"] = VoidDecisionReadinessContracts.CanonicalizationVersion,
            ["sourceEvidenceRefs"] = new JsonArray(VoidDecisionReadinessContracts
                .RequireArray(source, "evidenceRefs")
                .OfType<JsonObject>()
                .Select(evidence => new JsonObject
                {
                    ["evidenceId"] = VoidDecisionReadinessContracts.GetString(evidence, "evidenceId"),
                    ["path"] = VoidDecisionReadinessContracts.GetString(evidence, "path"),
                    ["declaredSha256Hash"] = VoidDecisionReadinessContracts.GetString(evidence, "sha256Hash"),
                    ["hashFormat"] = "sha256-hex",
                })
                .ToArray<JsonNode?>()),
            ["generatedArtifactHashes"] = new JsonArray(generatedArtifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(artifact => new JsonObject
                {
                    ["path"] = artifact.RelativePath,
                    ["sha256Hash"] = artifact.Sha256Hash,
                    ["hashFormat"] = "sha256-hex",
                })
                .ToArray<JsonNode?>()),
        };

    private static IReadOnlyList<string> BuildBlockers(
        JsonObject source,
        IReadOnlyList<VoidDecisionReadinessMaterialFinding> publicFindings)
    {
        var blockers = VoidDecisionReadinessContracts
            .RequireArray(source, "focusedVerification")
            .OfType<JsonObject>()
            .Where(check => VoidDecisionReadinessContracts.GetString(check, "status") is "blocked" or "failed" or "missing")
            .Select(check => VoidDecisionReadinessContracts.GetString(check, "checkId"))
            .Where(checkId => !string.IsNullOrWhiteSpace(checkId))
            .ToList();

        if (publicFindings.Count > 0)
        {
            blockers.Add("FEAT138-PUBLIC-FORBIDDEN-MATERIAL");
        }

        return blockers;
    }

    private static VoidDecisionReadinessGeneratedArtifact JsonArtifact(string relativePath, JsonObject content)
    {
        var text = VoidDecisionReadinessContracts.CanonicalJson(content);
        return new VoidDecisionReadinessGeneratedArtifact(
            relativePath,
            text,
            VoidDecisionReadinessContracts.Sha256Hex(text));
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());

    private static string FormatFinding(VoidDecisionReadinessMaterialFinding finding) =>
        $"{finding.RelativePath}:{finding.Category}:{finding.Evidence}";
}
