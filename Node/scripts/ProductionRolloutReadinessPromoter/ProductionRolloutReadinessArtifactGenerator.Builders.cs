using System.Text;
using System.Text.Json.Nodes;

namespace ProductionRolloutReadinessPromoter;

public static partial class ProductionRolloutReadinessArtifactGenerator
{
    private static JsonObject BuildCheckResults(
        JsonObject source,
        ProductionRolloutGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        IReadOnlyList<ProductionRolloutPublicOutputFinding> publicFindings,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "production-rollout-check-results.v1",
            ["checkResultId"] = "FEAT148-PRODUCTION-ROLLOUT-CHECK-RESULTS-001",
            ["sourceId"] = ProductionRolloutReadinessContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionRolloutReadinessContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["gateStatus"] = gate.Status,
            ["greenAllowed"] = gate.GreenAllowed,
            ["productionDecision"] = DecisionToJson(gate.ProductionDecision),
            ["publicStateDecision"] = DecisionToJson(gate.PublicStateDecision),
            ["blockers"] = ProductionRolloutReadinessContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
            ["limitations"] = ProductionRolloutReadinessContracts.ToJsonArray(gate.Limitations),
            ["diagnostics"] = ProductionRolloutReadinessContracts.ToJsonArray(gate.Diagnostics),
            ["packageFailures"] = ProductionRolloutReadinessContracts.ToJsonArray(packageFailures),
            ["publicOutputFindings"] = new JsonArray(publicFindings
                .Select(finding => new JsonObject
                {
                    ["path"] = finding.RelativePath,
                    ["category"] = finding.Category,
                    ["evidence"] = finding.Evidence,
                })
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildDecisionLedger(
        JsonObject source,
        ProductionRolloutGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt)
    {
        var decisions = new JsonArray();
        foreach (var decision in ProductionRolloutReadinessContracts.RequireArray(source, "blockerDecisions").OfType<JsonObject>())
        {
            var blockerId = ProductionRolloutReadinessContracts.GetString(decision, "blockerId");
            var runtimeDecision = blockerId == gate.ProductionDecision.BlockerId
                ? gate.ProductionDecision
                : blockerId == gate.PublicStateDecision.BlockerId
                    ? gate.PublicStateDecision
                    : null;
            decisions.Add(new JsonObject
            {
                ["blockerId"] = blockerId,
                ["currentSeverity"] = ProductionRolloutReadinessContracts.GetString(decision, "currentSeverity"),
                ["currentStatus"] = ProductionRolloutReadinessContracts.GetString(decision, "currentStatus"),
                ["proposedSeverity"] = runtimeDecision?.Severity ?? ProductionRolloutReadinessContracts.GetString(decision, "proposedSeverity"),
                ["proposedStatus"] = runtimeDecision?.Status ?? ProductionRolloutReadinessContracts.GetString(decision, "proposedStatus"),
                ["decision"] = runtimeDecision?.Decision ?? ProductionRolloutReadinessContracts.GetString(decision, "decision"),
                ["decisionReason"] = runtimeDecision?.Reason ?? ProductionRolloutReadinessContracts.GetString(decision, "decisionReason"),
                ["evidenceRefs"] = ProductionRolloutReadinessContracts.Clone(decision["evidenceRefs"]) ?? new JsonArray(),
                ["scoreImpact"] = BuildScoreImpact(source, packageStatus),
                ["claimImpact"] = ProductionRolloutReadinessContracts.GetString(decision, "claimImpact"),
                ["residualRisk"] = ProductionRolloutReadinessContracts.GetString(decision, "residualRisk"),
                ["packageBlockers"] = blockerId == gate.ProductionDecision.BlockerId
                    ? ProductionRolloutReadinessContracts.ToJsonArray(MergeBlockers(gate, packageFailures))
                    : new JsonArray(),
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = "production-rollout-blocker-resolution-decision-ledger.v1",
            ["ledgerId"] = "FEAT148-PRODUCTION-ROLLOUT-DECISION-LEDGER-001",
            ["sourceId"] = ProductionRolloutReadinessContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionRolloutReadinessContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["baselineRegister"] = ProductionRolloutReadinessContracts.Clone(source["baselineRegister"]),
            ["decisions"] = decisions,
        };
    }

    private static JsonObject BuildReadinessFragment(
        JsonObject source,
        ProductionRolloutGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt)
    {
        var scorePolicy = ProductionRolloutReadinessContracts.RequireObject(source, "scorePolicy");
        var currentScore = ProductionRolloutReadinessContracts.GetInt(scorePolicy, "currentTotalScore");
        var candidateScore = ProductionRolloutReadinessContracts.GetInt(scorePolicy, "candidateTotalScoreWhenAccepted");
        var scoreMovementAllowed = packageStatus != "blocked";
        return new JsonObject
        {
            ["schemaVersion"] = "production-rollout-readiness-fragment.v1",
            ["fragmentId"] = "FEAT148-PRODUCTION-ROLLOUT-READINESS-FRAGMENT-001",
            ["featureSlice"] = ProductionRolloutReadinessContracts.FeatureId,
            ["sourceId"] = ProductionRolloutReadinessContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionRolloutReadinessContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["directRegisterMutation"] = false,
            ["doesNotMutateRegister"] = true,
            ["registerPromotionOwner"] = "FEAT-130",
            ["scoreEffect"] = new JsonObject
            {
                ["currentTotalScore"] = currentScore,
                ["candidateTotalScoreWhenAccepted"] = candidateScore,
                ["appliedTotalScore"] = scoreMovementAllowed ? candidateScore : currentScore,
                ["scoreChangeAllowed"] = scoreMovementAllowed,
                ["scoreChangeBlockedBy"] = ProductionRolloutReadinessContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
            },
            ["claimEffect"] = new JsonObject
            {
                ["productionOrganizationalRollout"] = packageStatus == "blocked"
                    ? "blocked"
                    : "allowed_with_limitations_candidate",
                ["publicOrStateElection"] = "blocked",
                ["greenAllowed"] = false,
            },
            ["limitations"] = ProductionRolloutReadinessContracts.ToJsonArray(gate.Limitations),
            ["promotionInstructions"] = "FEAT-130 may ingest this fragment after reviewer acceptance. FEAT-148 does not mutate the canonical readiness register directly.",
        };
    }

    private static JsonObject BuildEvidencePackage(
        JsonObject source,
        ProductionRolloutGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        IReadOnlyList<ProductionRolloutPublicOutputFinding> publicFindings,
        IReadOnlyCollection<ProductionRolloutGeneratedArtifact> artifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "production-rollout-evidence-package.v1",
            ["packageId"] = "FEAT148-PRODUCTION-ROLLOUT-EVIDENCE-PACKAGE-001",
            ["sourceId"] = ProductionRolloutReadinessContracts.GetString(source, "sourceId"),
            ["featureSlice"] = ProductionRolloutReadinessContracts.FeatureId,
            ["generatedAt"] = ProductionRolloutReadinessContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["canonicalizationVersion"] = ProductionRolloutReadinessContracts.CanonicalizationVersion,
            ["baselineRegister"] = ProductionRolloutReadinessContracts.Clone(source["baselineRegister"]),
            ["rolloutProfile"] = ProductionRolloutReadinessContracts.Clone(source["rolloutProfile"]),
            ["scorePolicy"] = ProductionRolloutReadinessContracts.Clone(source["scorePolicy"]),
            ["gateResult"] = BuildCheckResults(source, gate, packageFailures, packageStatus, publicFindings, generatedAt),
            ["evidence"] = new JsonObject
            {
                ["runEvidence"] = ProductionRolloutReadinessContracts.Clone(source["runEvidence"]),
                ["operationalEvidence"] = ProductionRolloutReadinessContracts.Clone(source["operationalEvidence"]),
                ["deploymentProofEvidence"] = ProductionRolloutReadinessContracts.Clone(source["deploymentProofEvidence"]),
                ["webClientProofEvidence"] = ProductionRolloutReadinessContracts.Clone(source["webClientProofEvidence"]),
                ["governedOutcomeEvidence"] = ProductionRolloutReadinessContracts.Clone(source["governedOutcomeEvidence"]),
                ["upstreamEvidence"] = ProductionRolloutReadinessContracts.Clone(source["upstreamEvidence"]),
            },
            ["claimPolicy"] = ProductionRolloutReadinessContracts.Clone(source["claimPolicy"]),
            ["signoff"] = ProductionRolloutReadinessContracts.Clone(source["signoff"]),
            ["artifactRefs"] = ArtifactRefs(artifacts),
        };

    private static JsonObject BuildPackageHashValidation(
        JsonObject source,
        string packageStatus,
        IReadOnlyCollection<ProductionRolloutGeneratedArtifact> artifacts,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "production-rollout-package-hash-validation.v1",
            ["validationId"] = "FEAT148-PRODUCTION-ROLLOUT-PACKAGE-HASH-VALIDATION-001",
            ["sourceId"] = ProductionRolloutReadinessContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionRolloutReadinessContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["canonicalizationVersion"] = ProductionRolloutReadinessContracts.CanonicalizationVersion,
            ["generatedArtifactHashes"] = ArtifactRefs(artifacts),
            ["selfHashPolicy"] = "This validation artifact records every generated artifact except itself to avoid circular hashes.",
        };

    private static JsonArray ArtifactRefs(IReadOnlyCollection<ProductionRolloutGeneratedArtifact> artifacts) =>
        new(artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(ArtifactRef)
            .ToArray<JsonNode?>());

    private static JsonObject DecisionToJson(ProductionRolloutBlockerDecision decision) =>
        new()
        {
            ["blockerId"] = decision.BlockerId,
            ["severity"] = decision.Severity,
            ["status"] = decision.Status,
            ["decision"] = decision.Decision,
            ["reason"] = decision.Reason,
        };

    private static IReadOnlyList<string> MergeBlockers(
        ProductionRolloutGateEvaluation gate,
        IReadOnlyList<string> auditFailures) =>
        gate.Blockers
            .Concat(auditFailures)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static JsonObject BuildScoreImpact(JsonObject source, string packageStatus)
    {
        var policy = ProductionRolloutReadinessContracts.RequireObject(source, "scorePolicy");
        var currentScore = ProductionRolloutReadinessContracts.GetInt(policy, "currentTotalScore");
        var candidateScore = ProductionRolloutReadinessContracts.GetInt(policy, "candidateTotalScoreWhenAccepted");
        return new JsonObject
        {
            ["currentTotalScore"] = currentScore,
            ["proposedTotalScore"] = packageStatus == "blocked" ? currentScore : candidateScore,
            ["scoreChangeAllowed"] = packageStatus != "blocked",
        };
    }

    private static string BuildPublicSafeSummary(
        JsonObject source,
        ProductionRolloutGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt)
    {
        var claimPolicy = ProductionRolloutReadinessContracts.RequireObject(source, "claimPolicy");
        var builder = new StringBuilder();
        builder.AppendLine("# Production Organizational Rollout Readiness Summary");
        builder.AppendLine();
        builder.AppendLine($"Generated: {ProductionRolloutReadinessContracts.FormatTimestamp(generatedAt)}");
        builder.AppendLine($"Source: {ProductionRolloutReadinessContracts.GetString(source, "sourceId")}");
        builder.AppendLine($"Status: {packageStatus}");
        builder.AppendLine();
        builder.AppendLine(packageStatus == "blocked"
            ? ProductionRolloutReadinessContracts.GetString(claimPolicy, "blockedWording")
            : ProductionRolloutReadinessContracts.GetString(claimPolicy, "allowedWithLimitationsWording"));
        builder.AppendLine();
        builder.AppendLine("## Non-Claims");
        foreach (var nonClaim in ProductionRolloutReadinessContracts.GetStringArray(claimPolicy, "nonClaims"))
        {
            builder.Append("- ");
            builder.AppendLine(nonClaim);
        }

        builder.AppendLine();
        builder.AppendLine("## Active Blockers");
        foreach (var blocker in MergeBlockers(gate, packageFailures))
        {
            builder.Append("- ");
            builder.AppendLine(blocker);
        }

        builder.AppendLine();
        builder.AppendLine("Public/state election readiness remains blocked and outside FEAT-148.");
        return builder.ToString();
    }
}
