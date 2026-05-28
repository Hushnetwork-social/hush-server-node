using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static partial class ProductionLikeOperationalRunArtifactGenerator
{
    private static JsonObject BuildRunProfileSummary(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt) =>
        BuildGroupSummary(source, gate, packageFailures, packageStatus, "run-profile-summary.v1", "FEAT154-RUN-PROFILE-SUMMARY", "runProfile", generatedAt);

    private static JsonObject BuildDeploymentProofBindingSummary(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "deployment-proof-binding-summary.v1",
            ["summaryId"] = "FEAT154-DEPLOYMENT-PROOF-BINDING-SUMMARY",
            ["sourceId"] = ProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["gateStatus"] = gate.Status,
            ["deploymentProof"] = ProductionLikeOperationalRunContracts.Clone(source["deploymentProof"]),
            ["runtimeBinding"] = ProductionLikeOperationalRunContracts.Clone(source["runtimeBinding"]),
            ["webClientObservation"] = ProductionLikeOperationalRunContracts.Clone(source["webClientObservation"]),
            ["blockers"] = ProductionLikeOperationalRunContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
        };

    private static JsonObject BuildMonitoringAlertingSummary(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt) =>
        BuildGroupSummary(source, gate, packageFailures, packageStatus, "monitoring-alerting-summary.v1", "FEAT154-MONITORING-ALERTING-SUMMARY", "monitoring", generatedAt);

    private static JsonObject BuildSupportOperatorHandoffSummary(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "support-operator-handoff-summary.v1",
            ["summaryId"] = "FEAT154-SUPPORT-OPERATOR-HANDOFF-SUMMARY",
            ["sourceId"] = ProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["gateStatus"] = gate.Status,
            ["support"] = ProductionLikeOperationalRunContracts.Clone(source["support"]),
            ["operatorHandoff"] = ProductionLikeOperationalRunContracts.Clone(source["operatorHandoff"]),
            ["blockers"] = ProductionLikeOperationalRunContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
        };

    private static JsonObject BuildBackupRestoreSummary(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt) =>
        BuildGroupSummary(source, gate, packageFailures, packageStatus, "backup-restore-summary.v1", "FEAT154-BACKUP-RESTORE-SUMMARY", "backupRestore", generatedAt);

    private static JsonObject BuildIncidentNoIncidentSummary(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt) =>
        BuildGroupSummary(source, gate, packageFailures, packageStatus, "incident-no-incident-summary.v1", "FEAT154-INCIDENT-NO-INCIDENT-SUMMARY", "incidentDeclaration", generatedAt);

    private static JsonObject BuildSecuritySupportFreshnessSummary(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt) =>
        BuildGroupSummary(source, gate, packageFailures, packageStatus, "security-support-freshness-summary.v1", "FEAT154-SECURITY-SUPPORT-FRESHNESS-SUMMARY", "securitySupportFreshness", generatedAt);

    private static JsonObject BuildPostmortemSummary(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt) =>
        BuildGroupSummary(source, gate, packageFailures, packageStatus, "postmortem-summary.v1", "FEAT154-POSTMORTEM-SUMMARY", "postmortem", generatedAt);

    private static JsonObject BuildNoSecretScanResult(
        JsonObject source,
        IReadOnlyList<ProductionLikeOperationalRunPublicOutputFinding> publicFindings,
        string packageStatus,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "no-secret-scan-result.v1",
            ["scanId"] = "FEAT154-PUBLIC-SAFE-NO-SECRET-SCAN",
            ["sourceId"] = ProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt),
            ["status"] = publicFindings.Count == 0 ? "passed" : "blocked",
            ["packageStatus"] = packageStatus,
            ["publicOutputsChecked"] = ProductionLikeOperationalRunContracts.ToJsonArray([PublicSafeSummaryPath, ReadmePath]),
            ["findings"] = new JsonArray(publicFindings
                .Select(finding => new JsonObject
                {
                    ["path"] = finding.RelativePath,
                    ["category"] = finding.Category,
                    ["evidence"] = finding.Evidence,
                })
                .ToArray<JsonNode?>()),
        };

    private static JsonObject BuildReadinessFragment(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt)
    {
        var register = ProductionLikeOperationalRunContracts.RequireObject(source, "baselineRegister");
        var currentScore = ProductionLikeOperationalRunContracts.GetInt(register, "currentScore");
        var targetScore = ProductionLikeOperationalRunContracts.GetInt(register, "targetScore");
        var scoreAllowed = IsScoreMovementAllowed(gate, packageStatus);

        return new JsonObject
        {
            ["schemaVersion"] = "production-like-operational-run-readiness-fragment.v1",
            ["fragmentId"] = "FEAT154-PRODUCTION-LIKE-RUN-READINESS-FRAGMENT",
            ["featureSlice"] = ProductionLikeOperationalRunContracts.FeatureId,
            ["sourceId"] = ProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["directRegisterMutation"] = false,
            ["doesNotMutateRegister"] = true,
            ["promotionOwner"] = ProductionLikeOperationalRunContracts.PromotionOwner,
            ["scoreEffect"] = new JsonObject
            {
                ["dimensionId"] = ProductionLikeOperationalRunContracts.DimensionId,
                ["currentDimensionScore"] = currentScore,
                ["targetDimensionScore"] = targetScore,
                ["appliedDimensionScore"] = scoreAllowed ? targetScore : currentScore,
                ["scoreChangeAllowed"] = scoreAllowed,
                ["scoreChangeBlockedBy"] = ProductionLikeOperationalRunContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
            },
            ["claimLimitations"] = ProductionLikeOperationalRunContracts.ToJsonArray(gate.Limitations),
            ["residualRisks"] = ProductionLikeOperationalRunContracts.Clone(source["residualRisks"]),
        };
    }

    private static JsonObject BuildScoreProposal(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt)
    {
        var proposal = ProductionLikeOperationalRunContracts.RequireObject(source, "readinessProposal");
        var fromScore = ProductionLikeOperationalRunContracts.GetInt(proposal, "proposedScoreFrom");
        var requestedToScore = ProductionLikeOperationalRunContracts.GetInt(proposal, "proposedScoreTo");
        var scoreAllowed = IsScoreMovementAllowed(gate, packageStatus);

        return new JsonObject
        {
            ["schemaVersion"] = "production-like-operational-run-score-proposal.v1",
            ["proposalId"] = "FEAT154-RDY-DIM-007-SCORE-PROPOSAL",
            ["sourceId"] = ProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["dimensionId"] = ProductionLikeOperationalRunContracts.DimensionId,
            ["proposedScoreFrom"] = fromScore,
            ["proposedScoreTo"] = scoreAllowed ? requestedToScore : fromScore,
            ["requestedScoreTo"] = requestedToScore,
            ["scoreChangeAllowed"] = scoreAllowed,
            ["doesNotMutateRegister"] = true,
            ["directRegisterMutation"] = false,
            ["promotionOwner"] = ProductionLikeOperationalRunContracts.PromotionOwner,
            ["blockedBy"] = ProductionLikeOperationalRunContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
            ["residualRiskSummary"] = ProductionLikeOperationalRunContracts.GetString(proposal, "residualRiskSummary"),
        };
    }

    private static JsonObject BuildDownstreamHandoff(
        JsonObject source,
        ProductionLikeOperationalRunGateEvaluation gate,
        IReadOnlyList<string> packageFailures,
        string packageStatus,
        DateTimeOffset generatedAt) =>
        new()
        {
            ["schemaVersion"] = "production-like-operational-run-downstream-handoff.v1",
            ["handoffId"] = "FEAT154-DOWNSTREAM-HANDOFF",
            ["sourceId"] = ProductionLikeOperationalRunContracts.GetString(source, "sourceId"),
            ["generatedAt"] = ProductionLikeOperationalRunContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["targetFeatures"] = ProductionLikeOperationalRunContracts.Clone(
                ProductionLikeOperationalRunContracts.RequireObject(source, "downstreamHandoff")["targetFeatures"]),
            ["productionRolloutInputStatus"] = gate.ProductionRolloutInputStatus,
            ["promotionRegisterInputStatus"] = gate.PromotionRegisterInputStatus,
            ["scoreProposalPath"] = ScoreProposalPath,
            ["readinessFragmentPath"] = ReadinessFragmentPath,
            ["blockedBy"] = ProductionLikeOperationalRunContracts.ToJsonArray(MergeBlockers(gate, packageFailures)),
            ["sourceHandoff"] = ProductionLikeOperationalRunContracts.Clone(source["downstreamHandoff"]),
        };
}
