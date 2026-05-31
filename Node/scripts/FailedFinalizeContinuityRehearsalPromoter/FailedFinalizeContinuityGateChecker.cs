using System.Text.Json.Nodes;

namespace FailedFinalizeContinuityRehearsalPromoter;

public sealed record FailedFinalizeContinuityGateEvaluation(
    string Status,
    bool ScoreProposalCanBeGenerated,
    bool ScoreChangeAllowed,
    bool DirectRegisterMutation,
    string DownstreamHandoffStatus,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Diagnostics);

public static class FailedFinalizeContinuityGateChecker
{
    public const string MissingDecisionBlocker = "FEAT155-FAILED-FINALIZE-DECISION-MISSING";
    public const string MissingEvidenceBlocker = "FEAT155-FAILED-FINALIZE-EVIDENCE-MISSING";
    public const string CleanResultConflictBlocker = "FEAT155-CLEAN-RESULT-CONFLICT";
    public const string Feat146ReuseBlocker = "FEAT155-FEAT146-EVIDENCE-REUSE";
    public const string Feat154ContextOnlyBlocker = "FEAT155-FEAT154-CONTEXT-ONLY";
    public const string PublicSafetyBlocker = "FEAT155-PUBLIC-SAFETY-FAILED";
    public const string DirectRegisterMutationBlocker = "FEAT155-DIRECT-REGISTER-MUTATION";
    public const string PackageValidationBlocker = "FEAT155-PACKAGE-VALIDATION-BLOCKED";
    public const string DownstreamHandoffBlocker = "FEAT155-DOWNSTREAM-HANDOFF-BLOCKED";

    public static FailedFinalizeContinuityGateEvaluation Evaluate(JsonObject source)
    {
        var validationErrors = FailedFinalizeContinuityContracts.ValidateSource(source);
        var blockers = new SortedSet<string>(
            validationErrors.Select(error => $"VALIDATION: {error}"),
            StringComparer.Ordinal);
        var diagnostics = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var error in validationErrors)
        {
            diagnostics.Add($"VALIDATION: {error}");
        }

        AddGovernedOutcomeBlockers(source, blockers, diagnostics);
        AddNoCleanResultBlockers(source, blockers, diagnostics);
        AddPackageValidationBlockers(source, blockers, diagnostics);
        AddReadinessProposalBlockers(source, blockers, diagnostics);
        AddDownstreamHandoffBlockers(source, blockers, diagnostics);
        AddPublicSafetyBlockers(source, blockers, diagnostics);

        var status = blockers.Count == 0 ? "accepted" : "blocked";
        var scoreAllowed = status == "accepted";
        var readinessProposal = FailedFinalizeContinuityContracts.TryObject(source, "readinessProposal");
        var downstreamHandoff = FailedFinalizeContinuityContracts.TryObject(source, "downstreamHandoff");
        var directRegisterMutation =
            FailedFinalizeContinuityContracts.GetBool(readinessProposal, "directRegisterMutation", fallback: true) ||
            FailedFinalizeContinuityContracts.GetBool(downstreamHandoff, "directRegisterMutation", fallback: true);

        return new FailedFinalizeContinuityGateEvaluation(
            status,
            ScoreProposalCanBeGenerated: scoreAllowed,
            ScoreChangeAllowed: scoreAllowed,
            DirectRegisterMutation: directRegisterMutation,
            DownstreamHandoffStatus: FailedFinalizeContinuityContracts.GetString(downstreamHandoff, "status"),
            blockers.ToArray(),
            diagnostics.ToArray());
    }

    private static void AddGovernedOutcomeBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        var outcome = FailedFinalizeContinuityContracts.TryObject(source, "governedOutcome");
        if (outcome is null)
        {
            AddBlocker(blockers, diagnostics, MissingDecisionBlocker);
            return;
        }

        if (FailedFinalizeContinuityContracts.GetString(outcome, "decisionType") != "record_failed_finalize_continuity" ||
            FailedFinalizeContinuityContracts.GetString(outcome, "outcomeStatus") != "failed_to_finalize" ||
            FailedFinalizeContinuityContracts.GetString(outcome, "finalizationMode") != "failed_finalization" ||
            FailedFinalizeContinuityContracts.GetBool(outcome, "cleanFinalization", fallback: true) ||
            string.IsNullOrWhiteSpace(FailedFinalizeContinuityContracts.GetString(outcome, "authorityDecisionRef")) ||
            string.IsNullOrWhiteSpace(FailedFinalizeContinuityContracts.GetString(outcome, "authorityDecisionHash")) ||
            string.IsNullOrWhiteSpace(FailedFinalizeContinuityContracts.GetString(outcome, "governanceRuleRef")) ||
            string.IsNullOrWhiteSpace(FailedFinalizeContinuityContracts.GetString(outcome, "closeBoundaryRef")))
        {
            AddBlocker(blockers, diagnostics, MissingDecisionBlocker);
        }

        if (!FailedFinalizeContinuityContracts.IsNull(outcome, "officialResultRef") ||
            !FailedFinalizeContinuityContracts.IsNull(outcome, "finalizeBoundaryRef"))
        {
            AddBlocker(blockers, diagnostics, CleanResultConflictBlocker);
        }

        var hasFailedFinalizeEvidence =
            FailedFinalizeContinuityContracts.HasArrayItems(outcome, "missingFinalizeEvidenceRefs") ||
            FailedFinalizeContinuityContracts.HasArrayItems(outcome, "continuityEvidenceRefs") ||
            FailedFinalizeContinuityContracts.HasArrayItems(outcome, "availableTrusteeAcknowledgementRefs");
        if (!hasFailedFinalizeEvidence)
        {
            AddBlocker(blockers, diagnostics, MissingEvidenceBlocker);
            if (FailedFinalizeContinuityContracts.TryObject(source, "productionLikeRunContext") is { } context &&
                FailedFinalizeContinuityContracts.GetString(context, "sourceFeature") == "FEAT-154")
            {
                AddBlocker(blockers, diagnostics, Feat154ContextOnlyBlocker);
            }
        }

        if (ContainsToken(outcome, "FEAT-146") ||
            ContainsToken(outcome, "finalized_with_anomaly") ||
            ContainsToken(outcome, "governed-outcome-producer"))
        {
            AddBlocker(blockers, diagnostics, Feat146ReuseBlocker);
        }
    }

    private static void AddNoCleanResultBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        if (FailedFinalizeContinuityContracts.TryObject(source, "noCleanResult") is not { } noCleanResult)
        {
            AddBlocker(blockers, diagnostics, CleanResultConflictBlocker);
            return;
        }

        if (FailedFinalizeContinuityContracts.GetBool(noCleanResult, "officialResultArtifactPresent") ||
            FailedFinalizeContinuityContracts.GetBool(noCleanResult, "cleanFinalPackagePresent") ||
            FailedFinalizeContinuityContracts.GetBool(noCleanResult, "finalizeBoundaryArtifactPresent"))
        {
            AddBlocker(blockers, diagnostics, CleanResultConflictBlocker);
        }
    }

    private static void AddPackageValidationBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        if (FailedFinalizeContinuityContracts.TryObject(source, "packageValidation") is not { } validation)
        {
            AddBlocker(blockers, diagnostics, PackageValidationBlocker);
            return;
        }

        if (FailedFinalizeContinuityContracts.GetString(validation, "status") != "accepted" ||
            FailedFinalizeContinuityContracts.GetString(validation, "publicSafetyScan") != "passed" ||
            FailedFinalizeContinuityContracts.GetString(validation, "packageHashValidation") != "passed" ||
            FailedFinalizeContinuityContracts.HasArrayItems(validation, "blockedBy"))
        {
            AddBlocker(blockers, diagnostics, PackageValidationBlocker);
        }
    }

    private static void AddReadinessProposalBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        var proposal = FailedFinalizeContinuityContracts.TryObject(source, "readinessProposal");
        if (proposal is null ||
            FailedFinalizeContinuityContracts.GetBool(proposal, "directRegisterMutation", fallback: true))
        {
            AddBlocker(blockers, diagnostics, DirectRegisterMutationBlocker);
        }
    }

    private static void AddDownstreamHandoffBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        var handoff = FailedFinalizeContinuityContracts.TryObject(source, "downstreamHandoff");
        if (handoff is null)
        {
            AddBlocker(blockers, diagnostics, DownstreamHandoffBlocker);
            return;
        }

        if (FailedFinalizeContinuityContracts.GetString(handoff, "status") != "accepted" ||
            FailedFinalizeContinuityContracts.GetString(handoff, "registerPromotionOwner") != FailedFinalizeContinuityContracts.PromotionOwner ||
            FailedFinalizeContinuityContracts.GetBool(handoff, "directRegisterMutation", fallback: true))
        {
            AddBlocker(blockers, diagnostics, DownstreamHandoffBlocker);
        }
    }

    private static void AddPublicSafetyBlockers(
        JsonObject source,
        SortedSet<string> blockers,
        SortedSet<string> diagnostics)
    {
        if (FailedFinalizeContinuityContracts.TryObject(source, "publicSafeStatus") is { } status &&
            FailedFinalizeContinuityContracts.GetBool(status, "containsRestrictedDetails", fallback: true))
        {
            AddBlocker(blockers, diagnostics, PublicSafetyBlocker);
        }

        if (FailedFinalizeContinuityContracts.TryArray(source, "publicArtifactSamples") is not { } samples)
        {
            AddBlocker(blockers, diagnostics, PublicSafetyBlocker);
            return;
        }

        foreach (var sample in samples.OfType<JsonObject>())
        {
            var content = FailedFinalizeContinuityContracts.GetString(sample, "content");
            if (!FailedFinalizeContinuityReviewerOutputGenerator.ScanPublicText(content).Passed)
            {
                AddBlocker(blockers, diagnostics, PublicSafetyBlocker);
            }
        }
    }

    private static bool ContainsToken(JsonNode? node, string token)
    {
        if (node is null)
        {
            return false;
        }

        if (node is JsonValue value)
        {
            return value.ToJsonString().Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        if (node is JsonArray array)
        {
            return array.Any(child => ContainsToken(child, token));
        }

        if (node is JsonObject obj)
        {
            return obj.Any(property => ContainsToken(property.Value, token));
        }

        return false;
    }

    private static void AddBlocker(
        SortedSet<string> blockers,
        SortedSet<string> diagnostics,
        string blocker)
    {
        blockers.Add(blocker);
        diagnostics.Add(blocker);
    }
}
