using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public sealed record ProductionLikeOperationalRunGateEvaluation(
    string Status,
    bool ScoreProposalCanBeGenerated,
    bool ScoreChangeAllowed,
    bool DirectRegisterMutation,
    string ProductionRolloutInputStatus,
    string PromotionRegisterInputStatus,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> Diagnostics);

public static partial class ProductionLikeOperationalRunGateChecker
{
    public const string OneRunOnlyLimitation = "FEAT154-ONE-RUN-ONLY-LIMITATION";

    public static readonly string[] ForbiddenPublicMaterialNeedles =
    [
        "voter identity",
        "vote choice",
        "receipt secret",
        "trustee share",
        "private key",
        "deployment secret",
        "database password",
        "operator contact",
        "support case data",
        "raw log",
        "localhost",
        "c:\\",
    ];

    private static readonly HashSet<string> BlockingEvidenceStatuses = new(StringComparer.Ordinal)
    {
        "blocked",
        "missing",
        "private_only",
        "mismatched",
        "stale",
        "superseded",
    };

    public static ProductionLikeOperationalRunGateEvaluation Evaluate(JsonObject source)
    {
        var validationErrors = ProductionLikeOperationalRunContracts.ValidateSource(source);
        var blockers = new SortedSet<string>(validationErrors.Select(error => $"VALIDATION: {error}"), StringComparer.Ordinal);
        var limitations = new SortedSet<string>(StringComparer.Ordinal);
        var diagnostics = new SortedSet<string>(StringComparer.Ordinal);
        var hasPlaceholderEvidence = false;

        AddRunProfileBlockers(source, blockers, diagnostics);
        AddDataScopeBlockers(source, blockers, diagnostics);
        AddDeploymentProofBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddRuntimeBindingBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddWebClientObservationBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddOperationalEvidenceBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddSecurityFreshnessBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddMonitoringBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddSupportBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddBackupRestoreBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddIncidentDeclarationBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddOperatorHandoffBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddPostmortemBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddReadinessProposalBlockers(source, blockers, diagnostics);
        AddPublicSafetyBlockers(source, blockers, diagnostics);
        AddDownstreamHandoffBlockers(source, blockers, diagnostics, ref hasPlaceholderEvidence);

        var sourceStatus = ProductionLikeOperationalRunContracts.GetString(source, "status");
        if (sourceStatus == "development_placeholder")
        {
            hasPlaceholderEvidence = true;
            diagnostics.Add("FEAT154-PLACEHOLDER-EVIDENCE-BLOCKS-SCORE");
        }
        else if (sourceStatus == "accepted_with_limitations")
        {
            limitations.Add(OneRunOnlyLimitation);
            diagnostics.Add(OneRunOnlyLimitation);
        }

        var status = ResolveStatus(blockers, limitations, hasPlaceholderEvidence);
        var scoreAllowed = status is "accepted" or "accepted_with_limitations";
        var downstreamHandoff = ProductionLikeOperationalRunContracts.RequireObject(source, "downstreamHandoff");

        return new ProductionLikeOperationalRunGateEvaluation(
            status,
            ScoreProposalCanBeGenerated: scoreAllowed,
            ScoreChangeAllowed: scoreAllowed,
            DirectRegisterMutation: ProductionLikeOperationalRunContracts.GetBool(
                ProductionLikeOperationalRunContracts.RequireObject(source, "readinessProposal"),
                "directRegisterMutation",
                fallback: true),
            ProductionRolloutInputStatus: GetGroupStatus(downstreamHandoff, "productionRolloutInput"),
            PromotionRegisterInputStatus: GetGroupStatus(downstreamHandoff, "promotionRegisterInput"),
            blockers.ToArray(),
            limitations.ToArray(),
            diagnostics.ToArray());
    }
}
