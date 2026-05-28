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

public static class ProductionLikeOperationalRunGateChecker
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

    private static void AddRunProfileBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        var profile = ProductionLikeOperationalRunContracts.RequireObject(source, "runProfile");
        if (ProductionLikeOperationalRunContracts.GetBool(profile, "localOnly"))
        {
            AddBlocker(blockers, diagnostics, "FEAT154-RUN-PROFILE-LOCAL-ONLY");
        }

        if (ProductionLikeOperationalRunContracts.GetBool(profile, "privateChainOnly"))
        {
            AddBlocker(blockers, diagnostics, "FEAT154-RUN-PROFILE-PRIVATE-CHAIN-ONLY");
        }

        if (ProductionLikeOperationalRunContracts.GetBool(profile, "uncontrolledProduction") ||
            ProductionLikeOperationalRunContracts.GetString(profile, "environmentClass") != "controlled_hush_managed_staging_aws_like" ||
            ProductionLikeOperationalRunContracts.GetString(profile, "deploymentProfile") != "hush_saas_v1")
        {
            AddBlocker(blockers, diagnostics, "FEAT154-RUN-PROFILE-NOT-PRODUCTION-LIKE");
        }
    }

    private static void AddDataScopeBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        var dataScope = ProductionLikeOperationalRunContracts.RequireObject(source, "dataScope");
        if (ProductionLikeOperationalRunContracts.GetBool(dataScope, "containsRealVoterPersonalData") ||
            ProductionLikeOperationalRunContracts.GetBool(dataScope, "containsVoteChoiceData"))
        {
            AddBlocker(blockers, diagnostics, "FEAT154-DATA-SCOPE-RESTRICTED");
        }
    }

    private static void AddDeploymentProofBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var deploymentProof = ProductionLikeOperationalRunContracts.RequireObject(source, "deploymentProof");
        AddEvidenceStatusBlocker(
            deploymentProof,
            "status",
            "FEAT154-DEPLOYMENT-PROOF-MISSING",
            blockers,
            diagnostics,
            ref hasPlaceholderEvidence);

        if (!ProductionLikeOperationalRunContracts.HasArrayItems(deploymentProof, "evidenceRefs"))
        {
            AddBlocker(blockers, diagnostics, "FEAT154-DEPLOYMENT-PROOF-MISSING");
        }

        foreach (var component in new[] { "serverComponentProof", "webClientComponentProof" })
        {
            var proof = ProductionLikeOperationalRunContracts.RequireObject(deploymentProof, component);
            AddEvidenceStatusBlocker(
                proof,
                "status",
                "FEAT154-DEPLOYMENT-PROOF-MISSING",
                blockers,
                diagnostics,
                ref hasPlaceholderEvidence);
        }

        if (ProductionLikeOperationalRunContracts.GetString(deploymentProof, "classificationStatus") == "unknown_pending_classification")
        {
            AddBlocker(blockers, diagnostics, "FEAT154-DEPLOYMENT-PROOF-MISSING");
        }

        AddSourceBlockerIds(deploymentProof, blockers, diagnostics);
    }

    private static void AddRuntimeBindingBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var runtimeBinding = ProductionLikeOperationalRunContracts.RequireObject(source, "runtimeBinding");
        AddEvidenceGroupBlockers(
            runtimeBinding,
            "FEAT154-RUNTIME-BINDING-MISSING",
            ["ledgerRefs", "evidenceRefs"],
            blockers,
            diagnostics,
            ref hasPlaceholderEvidence);
    }

    private static void AddWebClientObservationBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var webClientObservation = ProductionLikeOperationalRunContracts.RequireObject(source, "webClientObservation");
        AddEvidenceGroupBlockers(
            webClientObservation,
            "FEAT154-WEBCLIENT-PROOF-MISSING",
            ["observedHandshakeRefs", "evidenceRefs"],
            blockers,
            diagnostics,
            ref hasPlaceholderEvidence);
    }

    private static void AddOperationalEvidenceBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var operationalEvidence = ProductionLikeOperationalRunContracts.RequireObject(source, "operationalEvidence");
        AddEvidenceGroupBlockers(
            operationalEvidence,
            "FEAT154-OPERATIONAL-EVIDENCE-MISSING",
            ["opsCheckIds", "evidenceRefs"],
            blockers,
            diagnostics,
            ref hasPlaceholderEvidence);

        var opsSourceStatus = ProductionLikeOperationalRunContracts.GetString(operationalEvidence, "opsSourceStatus");
        if (opsSourceStatus is "accepted_with_warnings" or "blocked" or "missing")
        {
            AddBlocker(blockers, diagnostics, "FEAT154-OPERATIONAL-EVIDENCE-MISSING");
        }
    }

    private static void AddSecurityFreshnessBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var freshness = ProductionLikeOperationalRunContracts.RequireObject(source, "securitySupportFreshness");
        AddEvidenceStatusBlocker(
            freshness,
            "status",
            "FEAT154-SECURITY-FRESHNESS-STALE",
            blockers,
            diagnostics,
            ref hasPlaceholderEvidence);

        if (ProductionLikeOperationalRunContracts.GetString(freshness, "freshness") is "stale" or "superseded" or "blocked" ||
            ProductionLikeOperationalRunContracts.GetInt(freshness, "openCriticalFindings") > 0 ||
            ProductionLikeOperationalRunContracts.GetInt(freshness, "openHighFindings") > 0 ||
            !ProductionLikeOperationalRunContracts.HasArrayItems(freshness, "evidenceRefs"))
        {
            AddBlocker(blockers, diagnostics, "FEAT154-SECURITY-FRESHNESS-STALE");
        }

        AddSourceBlockerIds(freshness, blockers, diagnostics);
    }

    private static void AddMonitoringBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var monitoring = ProductionLikeOperationalRunContracts.RequireObject(source, "monitoring");
        AddEvidenceGroupBlockers(
            monitoring,
            "FEAT154-MONITORING-MISSING",
            ["monitoringEvidenceRefs", "alertingEvidenceRefs", "evidenceRefs"],
            blockers,
            diagnostics,
            ref hasPlaceholderEvidence);
    }

    private static void AddSupportBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var support = ProductionLikeOperationalRunContracts.RequireObject(source, "support");
        AddEvidenceGroupBlockers(
            support,
            "FEAT154-SUPPORT-MISSING",
            ["evidenceRefs"],
            blockers,
            diagnostics,
            ref hasPlaceholderEvidence);

        if (ProductionLikeOperationalRunContracts.GetString(support, "supportRouteStatus") is "blocked" or "missing" ||
            !ProductionLikeOperationalRunContracts.GetBool(support, "operatorEscalationCovered"))
        {
            AddBlocker(blockers, diagnostics, "FEAT154-SUPPORT-MISSING");
        }
    }

    private static void AddBackupRestoreBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var backupRestore = ProductionLikeOperationalRunContracts.RequireObject(source, "backupRestore");
        AddEvidenceStatusBlocker(
            backupRestore,
            "status",
            "FEAT154-BACKUP-RESTORE-MISSING",
            blockers,
            diagnostics,
            ref hasPlaceholderEvidence);

        var acceptedRestore = ProductionLikeOperationalRunContracts.GetBool(backupRestore, "sameProfileRestoreAccepted") ||
            ProductionLikeOperationalRunContracts.GetBool(backupRestore, "runSpecificRestoreAccepted") ||
            ProductionLikeOperationalRunContracts.GetBool(backupRestore, "exceptionAccepted");
        if (!acceptedRestore || !ProductionLikeOperationalRunContracts.HasArrayItems(backupRestore, "evidenceRefs"))
        {
            AddBlocker(blockers, diagnostics, "FEAT154-BACKUP-RESTORE-MISSING");
        }

        AddSourceBlockerIds(backupRestore, blockers, diagnostics);
    }

    private static void AddIncidentDeclarationBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var incident = ProductionLikeOperationalRunContracts.RequireObject(source, "incidentDeclaration");
        AddEvidenceGroupBlockers(
            incident,
            "FEAT154-INCIDENT-DECLARATION-MISSING",
            ["evidenceRefs"],
            blockers,
            diagnostics,
            ref hasPlaceholderEvidence);
    }

    private static void AddOperatorHandoffBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var handoff = ProductionLikeOperationalRunContracts.RequireObject(source, "operatorHandoff");
        AddEvidenceGroupBlockers(
            handoff,
            "FEAT154-OPERATOR-HANDOFF-MISSING",
            ["evidenceRefs"],
            blockers,
            diagnostics,
            ref hasPlaceholderEvidence);

        if (ProductionLikeOperationalRunContracts.GetString(handoff, "handoffStatus") is "blocked" or "missing")
        {
            AddBlocker(blockers, diagnostics, "FEAT154-OPERATOR-HANDOFF-MISSING");
        }
    }

    private static void AddPostmortemBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var postmortem = ProductionLikeOperationalRunContracts.RequireObject(source, "postmortem");
        AddEvidenceGroupBlockers(
            postmortem,
            "FEAT154-POSTMORTEM-MISSING",
            ["evidenceRefs"],
            blockers,
            diagnostics,
            ref hasPlaceholderEvidence);

        if (!ProductionLikeOperationalRunContracts.GetBool(postmortem, "noIncidentStillReviewed") ||
            ProductionLikeOperationalRunContracts.GetInt(postmortem, "unresolvedCriticalFollowUps") > 0)
        {
            AddBlocker(blockers, diagnostics, "FEAT154-POSTMORTEM-MISSING");
        }
    }

    private static void AddReadinessProposalBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        var proposal = ProductionLikeOperationalRunContracts.RequireObject(source, "readinessProposal");
        if (!ProductionLikeOperationalRunContracts.GetBool(proposal, "doesNotMutateRegister") ||
            ProductionLikeOperationalRunContracts.GetBool(proposal, "directRegisterMutation", fallback: true) ||
            ProductionLikeOperationalRunContracts.GetString(proposal, "promotionOwner") != ProductionLikeOperationalRunContracts.PromotionOwner)
        {
            AddBlocker(blockers, diagnostics, "FEAT154-REGISTER-MUTATION-FORBIDDEN");
        }
    }

    private static void AddPublicSafetyBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        var publicSafety = ProductionLikeOperationalRunContracts.RequireObject(source, "publicSafety");
        if (ProductionLikeOperationalRunContracts.GetInt(publicSafety, "expectedFindingCountInGeneratedPublicOutputs") != 0)
        {
            AddBlocker(blockers, diagnostics, "FEAT154-PUBLIC-SAFE-FORBIDDEN-MATERIAL");
        }
    }

    private static void AddDownstreamHandoffBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var downstream = ProductionLikeOperationalRunContracts.RequireObject(source, "downstreamHandoff");
        var targetFeatures = ProductionLikeOperationalRunContracts.GetStringArray(downstream, "targetFeatures")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var feature in new[] { "FEAT-148", "FEAT-155", "FEAT-156" })
        {
            if (!targetFeatures.Contains(feature))
            {
                AddBlocker(blockers, diagnostics, $"FEAT154-DOWNSTREAM-{feature}-HANDOFF-MISSING");
            }
        }

        foreach (var groupName in new[] { "productionRolloutInput", "continuityRunContext", "promotionRegisterInput" })
        {
            var group = ProductionLikeOperationalRunContracts.RequireObject(downstream, groupName);
            AddEvidenceStatusBlocker(
                group,
                "status",
                $"FEAT154-{ToBlockerToken(groupName)}-MISSING",
                blockers,
                diagnostics,
                ref hasPlaceholderEvidence);
            AddSourceBlockerIds(group, blockers, diagnostics);
        }
    }

    private static void AddEvidenceGroupBlockers(
        JsonObject group,
        string blockerCode,
        IReadOnlyList<string> requiredArrayProperties,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        AddEvidenceStatusBlocker(group, "status", blockerCode, blockers, diagnostics, ref hasPlaceholderEvidence);

        foreach (var property in requiredArrayProperties)
        {
            if (!ProductionLikeOperationalRunContracts.HasArrayItems(group, property))
            {
                AddBlocker(blockers, diagnostics, blockerCode);
            }
        }

        AddEvidenceRefBlockers(group, blockerCode, blockers, diagnostics, ref hasPlaceholderEvidence);
        AddSourceBlockerIds(group, blockers, diagnostics);
    }

    private static void AddEvidenceRefBlockers(
        JsonObject group,
        string blockerCode,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        if (!group.TryGetPropertyValue("evidenceRefs", out var node) || node is not JsonArray refs)
        {
            return;
        }

        foreach (var reference in refs.OfType<JsonObject>())
        {
            AddEvidenceStatusBlocker(reference, "status", blockerCode, blockers, diagnostics, ref hasPlaceholderEvidence);
            if (ProductionLikeOperationalRunContracts.GetString(reference, "freshness") is "stale" or "superseded" or "blocked")
            {
                AddBlocker(blockers, diagnostics, blockerCode);
            }
        }
    }

    private static void AddEvidenceStatusBlocker(
        JsonObject group,
        string property,
        string blockerCode,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        ref bool hasPlaceholderEvidence)
    {
        var status = ProductionLikeOperationalRunContracts.GetString(group, property);
        if (status == "placeholder")
        {
            hasPlaceholderEvidence = true;
            diagnostics.Add("FEAT154-PLACEHOLDER-EVIDENCE-BLOCKS-SCORE");
            return;
        }

        if (BlockingEvidenceStatuses.Contains(status))
        {
            AddBlocker(blockers, diagnostics, blockerCode);
        }
    }

    private static void AddSourceBlockerIds(
        JsonObject group,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        foreach (var blockerId in ProductionLikeOperationalRunContracts.GetStringArray(group, "blockerIds"))
        {
            AddBlocker(blockers, diagnostics, blockerId);
        }
    }

    private static void AddBlocker(
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        string code)
    {
        blockers.Add(code);
        diagnostics.Add(code);
    }

    private static string ResolveStatus(
        IReadOnlyCollection<string> blockers,
        IReadOnlyCollection<string> limitations,
        bool hasPlaceholderEvidence)
    {
        if (blockers.Count > 0)
        {
            return "blocked";
        }

        if (hasPlaceholderEvidence)
        {
            return "development_placeholder";
        }

        return limitations.Count > 0 ? "accepted_with_limitations" : "accepted";
    }

    private static string GetGroupStatus(JsonObject parent, string property) =>
        ProductionLikeOperationalRunContracts.GetString(
            ProductionLikeOperationalRunContracts.RequireObject(parent, property),
            "status");

    private static string ToBlockerToken(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_').ToArray();
        var token = new string(chars);
        while (token.Contains("__", StringComparison.Ordinal))
        {
            token = token.Replace("__", "_", StringComparison.Ordinal);
        }

        return token.Trim('_');
    }
}
