using System.Text.Json.Nodes;

namespace InternalAudit95ProtocolTraceabilityPromoter;

public static class InternalAudit95ProtocolTraceabilityScoreProjection
{
    public static JsonObject BuildScoreProposal(
        JsonObject source,
        string packageStatus,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> diagnostics,
        DateTimeOffset generatedAt)
    {
        var scorePolicy = InternalAudit95ProtocolTraceabilityContracts.RequireObject(source, "scorePolicy");
        return new JsonObject
        {
            ["schemaVersion"] = "feat157-score-proposal.v1",
            ["featureId"] = InternalAudit95ProtocolTraceabilityContracts.FeatureId,
            ["sourceId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = InternalAudit95ProtocolTraceabilityContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["dimensionId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(scorePolicy, "targetDimensionId"),
            ["proposedScoreFrom"] = InternalAudit95ProtocolTraceabilityContracts.GetInt(scorePolicy, "currentScore"),
            ["proposedScoreTo"] = InternalAudit95ProtocolTraceabilityContracts.GetInt(scorePolicy, "proposedScore"),
            ["targetBlockerId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(scorePolicy, "targetBlockerId"),
            ["blockerResolutionProposed"] = packageStatus == "accepted_candidate",
            ["directRegisterMutation"] = false,
            ["canonicalRegisterMutationOwner"] = InternalAudit95ProtocolTraceabilityContracts.GetString(
                scorePolicy,
                "canonicalRegisterMutationOwner"),
            ["evidenceIds"] = InternalAudit95ProtocolTraceabilityContracts.ToJsonArray([
                "FEAT157-AUDITOR-TRACE-MATRIX",
                "FEAT157-ARTIFACT-INVENTORY",
                "FEAT157-STALE-REFERENCE-VALIDATION",
                "FEAT157-ORPHAN-ARTIFACT-REPORT",
                "FEAT157-PACKAGE-MANIFEST",
            ]),
            ["validationBlockers"] = InternalAudit95ProtocolTraceabilityContracts.ToJsonArray(blockers),
            ["diagnostics"] = InternalAudit95ProtocolTraceabilityContracts.ToJsonArray(diagnostics),
            ["nonClaims"] = InternalAudit95ProtocolTraceabilityContracts.ToJsonArray([
                "This proposal does not mutate the canonical readiness register.",
                "This proposal does not claim external review completion.",
                "This proposal does not claim certification, legal sufficiency, or public/state election readiness.",
            ]),
        };
    }

    public static JsonObject BuildReadinessFragment(
        JsonObject source,
        string packageStatus,
        IReadOnlyList<string> blockers,
        DateTimeOffset generatedAt)
    {
        var scorePolicy = InternalAudit95ProtocolTraceabilityContracts.RequireObject(source, "scorePolicy");
        return new JsonObject
        {
            ["schemaVersion"] = "feat157-readiness-fragment.v1",
            ["featureId"] = InternalAudit95ProtocolTraceabilityContracts.FeatureId,
            ["sourceId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(source, "sourceId"),
            ["generatedAt"] = InternalAudit95ProtocolTraceabilityContracts.FormatTimestamp(generatedAt),
            ["status"] = packageStatus,
            ["dimensionId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(scorePolicy, "targetDimensionId"),
            ["claimLevel"] = "production_organizational_rollout",
            ["evidenceIds"] = InternalAudit95ProtocolTraceabilityContracts.ToJsonArray([
                "FEAT157-SCORE-PROPOSAL",
                "FEAT157-AUDITOR-TRACE-MATRIX",
                "FEAT157-ARTIFACT-INVENTORY",
                "FEAT157-STALE-REFERENCE-VALIDATION",
                "FEAT157-ORPHAN-ARTIFACT-REPORT",
            ]),
            ["scoreProposalRef"] = InternalAudit95ProtocolTraceabilityArtifactGenerator.ScoreProposalPath,
            ["blockerResolutionTargetId"] = InternalAudit95ProtocolTraceabilityContracts.GetString(scorePolicy, "targetBlockerId"),
            ["blockerResolutionProposed"] = packageStatus == "accepted_candidate",
            ["directRegisterMutation"] = false,
            ["remainingBlockers"] = InternalAudit95ProtocolTraceabilityContracts.ToJsonArray(blockers),
        };
    }
}
