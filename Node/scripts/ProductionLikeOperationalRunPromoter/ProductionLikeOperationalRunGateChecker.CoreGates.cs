using System.Text.Json.Nodes;

namespace ProductionLikeOperationalRunPromoter;

public static partial class ProductionLikeOperationalRunGateChecker
{
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
}
