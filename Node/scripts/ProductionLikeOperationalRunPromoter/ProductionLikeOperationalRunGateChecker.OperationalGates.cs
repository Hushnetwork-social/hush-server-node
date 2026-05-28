using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static partial class ProductionLikeOperationalRunGateChecker
{
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
}
