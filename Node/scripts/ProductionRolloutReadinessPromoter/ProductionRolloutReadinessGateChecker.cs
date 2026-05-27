using System.Text.Json.Nodes;

namespace ProductionRolloutReadinessPromoter;

public sealed record ProductionRolloutBlockerDecision(
    string BlockerId,
    string Severity,
    string Status,
    string Decision,
    string Reason);

public sealed record ProductionRolloutGateEvaluation(
    string Status,
    ProductionRolloutBlockerDecision ProductionDecision,
    ProductionRolloutBlockerDecision PublicStateDecision,
    bool GreenAllowed,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> Diagnostics);

public static class ProductionRolloutReadinessGateChecker
{
    public static readonly string[] ForbiddenPublicMaterialNeedles =
    [
        "voter identity",
        "vote choice",
        "receipt secret",
        "trustee share",
        "private key",
        "deployment secret",
        "database password",
    ];

    private static readonly string[] EvidenceGroupNames =
    [
        "runEvidence",
        "operationalEvidence",
        "deploymentProofEvidence",
        "webClientProofEvidence",
        "governedOutcomeEvidence",
    ];

    private static readonly HashSet<string> ScoreBlockingEvidenceStatuses = new(StringComparer.Ordinal)
    {
        "missing",
        "stale",
        "placeholder",
        "private_only",
        "mismatched",
        "blocked",
    };

    public static ProductionRolloutGateEvaluation Evaluate(JsonObject source)
    {
        var validationErrors = ProductionRolloutReadinessContracts.ValidateSource(source);
        var blockers = new SortedSet<string>(validationErrors.Select(error => $"VALIDATION: {error}"), StringComparer.Ordinal);
        var limitations = new SortedSet<string>(StringComparer.Ordinal);
        var diagnostics = new SortedSet<string>(StringComparer.Ordinal);

        var scorePolicy = ProductionRolloutReadinessContracts.RequireObject(source, "scorePolicy");
        var candidateScore = ProductionRolloutReadinessContracts.GetInt(scorePolicy, "candidateTotalScoreWhenAccepted");
        var scoreMeetsAmber = candidateScore >= ProductionRolloutReadinessContracts.AmberMinimumScore;
        if (!scoreMeetsAmber)
        {
            blockers.Add("FEAT148-SCORE-BELOW-80");
            diagnostics.Add("Candidate score is below the hard 80 threshold for production allowed_with_limitations.");
        }

        var runEvidence = ProductionRolloutReadinessContracts.RequireObject(source, "runEvidence");
        var hasAcceptedRunEvidence =
            ProductionRolloutReadinessContracts.GetString(runEvidence, "status") == "accepted" &&
            ProductionRolloutReadinessContracts.RequireArray(runEvidence, "evidenceRefs").Count > 0;
        if (!hasAcceptedRunEvidence)
        {
            blockers.Add("FEAT148-PRODUCTION-LIKE-RUN-MISSING");
            diagnostics.Add("Accepted production-like run evidence is required before production rollout can move.");
        }

        foreach (var groupName in EvidenceGroupNames)
        {
            var group = ProductionRolloutReadinessContracts.RequireObject(source, groupName);
            var status = ProductionRolloutReadinessContracts.GetString(group, "status");
            if (ScoreBlockingEvidenceStatuses.Contains(status))
            {
                blockers.Add($"FEAT148-{ToBlockerToken(groupName)}-{status.ToUpperInvariant()}");
                diagnostics.Add($"{groupName} has score-blocking status {status}.");
            }

            foreach (var blockerId in ProductionRolloutReadinessContracts.GetStringArray(group, "blockerIds"))
            {
                if (string.Equals(blockerId, "FEAT148-FEAT139-FAILED-FINALIZE-PRODUCTION-LIMITATION", StringComparison.Ordinal))
                {
                    limitations.Add(blockerId);
                }
                else
                {
                    blockers.Add(blockerId);
                }
            }
        }

        foreach (var evidence in ProductionRolloutReadinessContracts.RequireArray(source, "upstreamEvidence").OfType<JsonObject>())
        {
            var feature = ProductionRolloutReadinessContracts.GetString(evidence, "featureSlice");
            var status = ProductionRolloutReadinessContracts.GetString(evidence, "status");
            var freshness = ProductionRolloutReadinessContracts.GetString(evidence, "freshness");
            if (ScoreBlockingEvidenceStatuses.Contains(status) || freshness is "stale" or "superseded" or "blocked")
            {
                blockers.Add($"FEAT148-UPSTREAM-{feature}-{status.ToUpperInvariant()}-{freshness.ToUpperInvariant()}");
                diagnostics.Add($"Upstream evidence {feature} cannot support score movement because status={status}, freshness={freshness}.");
            }
        }

        foreach (var sample in ProductionRolloutReadinessContracts.RequireArray(source, "publicArtifactSamples").OfType<JsonObject>())
        {
            var content = ProductionRolloutReadinessContracts.GetString(sample, "content");
            foreach (var forbidden in ForbiddenPublicMaterialNeedles)
            {
                if (content.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add("FEAT148-PUBLIC-FORBIDDEN-MATERIAL");
                    diagnostics.Add($"Public artifact sample contains forbidden material '{forbidden}'.");
                }
            }
        }

        var claimPolicy = ProductionRolloutReadinessContracts.RequireObject(source, "claimPolicy");
        if (ProductionRolloutReadinessContracts.GetString(claimPolicy, "publicStateClaimState") != "blocked")
        {
            blockers.Add(ProductionRolloutReadinessContracts.PublicStateBlockerId);
            diagnostics.Add("Public/state election readiness must remain blocked and owned by FEAT-149.");
        }

        var productionDecision = BuildProductionDecision(blockers, scoreMeetsAmber, hasAcceptedRunEvidence);
        var publicStateDecision = new ProductionRolloutBlockerDecision(
            ProductionRolloutReadinessContracts.PublicStateBlockerId,
            "red",
            "open",
            "keep_policy_blocked",
            "Public/state election readiness is owned by FEAT-149 and remains outside FEAT-148.");

        var statusResult = productionDecision.Status == "allowed_with_limitations"
            ? "allowed_with_limitations_candidate"
            : "blocked";

        if (statusResult == "allowed_with_limitations_candidate")
        {
            limitations.Add("FEAT148-REPEATED-OPERATING-HISTORY-REQUIRED-FOR-GREEN");
            diagnostics.Add("Only an amber production candidate is possible; green/full allowed requires repeated production-like evidence.");
        }

        return new ProductionRolloutGateEvaluation(
            statusResult,
            productionDecision,
            publicStateDecision,
            GreenAllowed: false,
            blockers.ToArray(),
            limitations.ToArray(),
            diagnostics.ToArray());
    }

    private static ProductionRolloutBlockerDecision BuildProductionDecision(
        IReadOnlyCollection<string> blockers,
        bool scoreMeetsAmber,
        bool hasAcceptedRunEvidence)
    {
        if (blockers.Count == 0 && scoreMeetsAmber && hasAcceptedRunEvidence)
        {
            return new ProductionRolloutBlockerDecision(
                ProductionRolloutReadinessContracts.ProductionBlockerId,
                "amber",
                "allowed_with_limitations",
                "propose_allowed_with_limitations",
                "Accepted production-like run evidence exists and candidate score reaches the hard 80 threshold.");
        }

        return new ProductionRolloutBlockerDecision(
            ProductionRolloutReadinessContracts.ProductionBlockerId,
            "red",
            "open",
            "keep_open",
            "Production rollout remains blocked until accepted production-like run evidence, score, and production-critical gates pass.");
    }

    private static string ToBlockerToken(string groupName) =>
        string.Concat(groupName.Select(ch => char.IsUpper(ch) ? $"_{ch}" : ch.ToString()))
            .TrimStart('_')
            .ToUpperInvariant();
}
