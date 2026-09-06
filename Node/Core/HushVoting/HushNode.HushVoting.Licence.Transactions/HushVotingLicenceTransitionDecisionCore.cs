// FEAT-015 Task 3.3 — licence transition decision core (pure, server-derived).
//
// Mirrors FullIdentityValidator's pure-decision shape: given the closed payload, the
// immutable FEAT-012 catalogue snapshot, and the caller-resolved indexed current state
// (never client-authored), decide the transition and derive the server-owned operative
// facts (plan semantics, rank, term, cap, governance, lifecycle intent). Identity and
// catalogue resolution are dependency-safe ports resolved by the host adapter (Phase 6);
// this core never performs I/O, never consults wall clocks for authority, and never
// writes. All expected failures are typed data with the stable LICENCE_* codes.

using HushShared.HushVoting.Licensing.Model;

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>Caller-resolved indexed current state at the validation point (never client-authored).</summary>
public abstract record HushVotingLicenceCurrentState
{
    /// <summary>No indexed assignment is currently effective (verified absence).</summary>
    public sealed record NoActive() : HushVotingLicenceCurrentState;

    /// <summary>An indexed assignment is effective now.</summary>
    public sealed record Active(
        HushVotingLicencePlanId CurrentPlanId,
        Guid? CurrentLicenceTransactionId,
        string CurrentCatalogueVersion,
        DateTime EffectiveFromUtc,
        DateTime? ExpiresAtUtc) : HushVotingLicenceCurrentState;
}

/// <summary>Server-derived operative facts the index writer persists (never client-authored).</summary>
public sealed record HushVotingLicenceOperativeFacts(
    HushVotingLicencePlanId PlanId,
    string PlanFamily,
    int UpgradeRank,
    int? EligibleVoterCap,
    bool UnlimitedElections,
    HushVotingLicenceTerm Term,
    string TermKind,
    int TermYears,
    IReadOnlyList<string> GovernanceOptionIds,
    string AssignedCatalogueVersion,
    string TransitionIntent);

/// <summary>Typed licence-transition decision. Expected rejections are data, never exceptions.</summary>
public sealed record HushVotingLicenceTransitionDecision(
    bool IsValid,
    string? ValidationCode,
    string? Message,
    HushVotingLicenceOperativeFacts? OperativeFacts)
{
    public static HushVotingLicenceTransitionDecision Allow(HushVotingLicenceOperativeFacts facts) =>
        new(true, null, null, facts);

    public static HushVotingLicenceTransitionDecision Reject(string code, string message) =>
        new(false, code, message, null);
}

/// <summary>
/// Pure licence transition decision over server-owned inputs. Rules are frozen in the
/// FeatureDescription/EPIC-002 deep-dive:
///  - baseline_free requires verified no-active and targets the current Direct Free plan;
///  - confirmed_upgrade requires an active indexed entitlement whose plan/licence match the
///    payload's expected-current precondition and a strictly higher available Veritas target;
///  - same/lower/renewal/unknown/retired/Enterprise/stale transitions fail with stable codes.
/// The caller resolves identity and catalogue; this core derives semantics from the immutable
/// catalogue only (rank, cap, term, governance, availability) and never trusts payload values
/// beyond the closed shape guard.
/// </summary>
public static class HushVotingLicenceTransitionDecisionCore
{
    public static HushVotingLicenceTransitionDecision Decide(
        HushVotingLicenceCatalogue catalogue,
        HushVotingLicenceAssignmentPayload payload,
        HushVotingLicenceCurrentState currentState)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(currentState);

        var intent = payload.TransitionIntent;
        var target = HushVotingLicencePlanId.TryGetKnown(payload.RequestedPlanId);

        if (target is null)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.PlanUnknown,
                "Requested plan is unknown.");
        }

        var targetPlan = catalogue.FindPlan(target);
        if (targetPlan is null || targetPlan.Retirement.IsRetired)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.PlanUnknown,
                "Requested plan is unknown or retired in the current catalogue.");
        }

        if (targetPlan.Family == HushVotingLicenceFamily.Enterprise)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.EnterpriseAdminOnly,
                "Enterprise licences are administrator-assigned only in v1.");
        }

        return intent switch
        {
            HushVotingLicenceTransitionIntent.BaselineFree => DecideBaseline(catalogue, payload, target, targetPlan, currentState),
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade => DecideUpgrade(catalogue, payload, target, targetPlan, currentState),
            _ => HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.IntentUnknown,
                "Licence assignment transition intent is unknown."),
        };
    }

    private static HushVotingLicenceTransitionDecision DecideBaseline(
        HushVotingLicenceCatalogue catalogue,
        HushVotingLicenceAssignmentPayload payload,
        HushVotingLicencePlanId target,
        HushVotingLicencePlan plan,
        HushVotingLicenceCurrentState currentState)
    {
        // Baseline must target the current Direct Free stable plan.
        var directFree = HushVotingLicencePlanId.DirectFree;
        if (target != directFree || plan.Family != HushVotingLicenceFamily.Direct)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.TransitionNotHigher,
                "A baseline transition must target Direct Free.");
        }

        // Baseline requires verified no-active entitlement.
        if (currentState is HushVotingLicenceCurrentState.Active)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.BaselineRequiresNoActiveEntitlement,
                "A baseline is only valid when no entitlement is currently active.");
        }

        return HushVotingLicenceTransitionDecision.Allow(BuildFacts(
            catalogue, target, plan, payload, HushVotingLicenceTransitionIntent.BaselineFree));
    }

    private static HushVotingLicenceTransitionDecision DecideUpgrade(
        HushVotingLicenceCatalogue catalogue,
        HushVotingLicenceAssignmentPayload payload,
        HushVotingLicencePlanId target,
        HushVotingLicencePlan plan,
        HushVotingLicenceCurrentState currentState)
    {
        if (currentState is not HushVotingLicenceCurrentState.Active active)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.UpgradeRequiresActiveEntitlement,
                "A confirmed upgrade requires an active indexed entitlement.");
        }

        // Expected-current precondition must match indexed truth exactly.
        var expectedCurrentPlan = HushVotingLicencePlanId.TryGetKnown(payload.ExpectedCurrentPlanId);
        var expectedCurrentTx = payload.ExpectedCurrentLicenceTransactionId;
        if (expectedCurrentPlan is null
            || expectedCurrentPlan != active.CurrentPlanId
            || expectedCurrentTx is null
            || active.CurrentLicenceTransactionId is null
            || expectedCurrentTx.Value != active.CurrentLicenceTransactionId.Value)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.ExpectedCurrentInvalid,
                "Expected current plan or licence transaction does not match indexed truth.");
        }

        // Upgrade target must be a strictly higher available Veritas plan in the current catalogue.
        if (plan.Family != HushVotingLicenceFamily.Veritas)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.TransitionNotHigher,
                "Only Veritas plans are actionable upgrade targets in v1.");
        }

        if (plan.Availability != HushVotingLicenceAvailability.AutomaticUpgrade)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.PlanUnavailable,
                "The target Veritas plan is not currently available for automatic upgrade.");
        }

        var currentPlan = catalogue.FindPlan(active.CurrentPlanId);
        if (currentPlan is null)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.PreconditionStale,
                "The indexed current plan is unknown in the current catalogue.");
        }

        // Renewal of the current plan is unchanged; lower rank / downgrade is not higher.
        if (target == active.CurrentPlanId)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.TransitionUnchanged,
                "Selecting the current plan is not an actionable transition.");
        }

        if (plan.UpgradeRank <= currentPlan.UpgradeRank)
        {
            return HushVotingLicenceTransitionDecision.Reject(
                HushVotingLicenceValidationCodes.TransitionNotHigher,
                "An upgrade requires a strictly higher plan rank.");
        }

        return HushVotingLicenceTransitionDecision.Allow(BuildFacts(
            catalogue, target, plan, payload, HushVotingLicenceTransitionIntent.ConfirmedUpgrade));
    }

    private static HushVotingLicenceOperativeFacts BuildFacts(
        HushVotingLicenceCatalogue catalogue,
        HushVotingLicencePlanId target,
        HushVotingLicencePlan plan,
        HushVotingLicenceAssignmentPayload payload,
        string transitionIntent)
    {
        // Governance option ids come from the immutable plan's governance options (server-owned).
        var governanceIds = plan.GovernanceOptions
            .Select(option => option.Id.Value)
            .ToArray();

        return new HushVotingLicenceOperativeFacts(
            target,
            HushVotingLicenceEnumNames.FamilyToWire(plan.Family).ToLowerInvariant(),
            plan.UpgradeRank,
            plan.EligibleVoterCap,
            plan.UnlimitedElections,
            plan.Term,
            plan.Term.IsPerpetual ? "perpetual" : "annual",
            plan.Term.Years,
            governanceIds,
            payload.ObservedCatalogueVersion,
            transitionIntent);
    }
}
