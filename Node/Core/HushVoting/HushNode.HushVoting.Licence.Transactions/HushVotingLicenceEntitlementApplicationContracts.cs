// FEAT-015 Task 3.7 — indexed entitlement application contracts + FEAT-018 scheduled-window seam.
//
// Application-layer query contracts (active / no-active / unavailable) plus the FEAT-018
// scheduled-close coverage seam. All derivation is server-owned and pure: given an indexed
// EffectiveLicenceEntitlement and the immutable catalogue, we expose the safe client view, the
// strictly-higher actionable Veritas options, and the informational Enterprise entry — never
// history, database keys, cache provenance, signatures, or a client-selected subject. Mempool
// pending state is never surfaced here; only indexed truth appears.
//
// The FEAT-018 seam is intentionally a pure decision seam: it computes whether a scheduled
// election window is fully covered at Open and yields a captured-entitlement snapshot for later
// completion. It does NOT implement election enforcement (FEAT-018 owns that).

using HushShared.HushVoting.Licensing.Model;

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>Closed query state for the entitlement application contract.</summary>
public enum HushVotingLicenceEntitlementQueryState
{
    Active,
    NoActive,
    Unavailable,
}

/// <summary>One actionable higher-option/template presented to the client (safe fields only).</summary>
public sealed record HushVotingLicenceOptionTemplate(
    string PlanId,
    string DisplayName,
    string SafeDescription,
    int? EligibleVoterCap,
    bool UnlimitedElections,
    string TermKind,
    int TermYears);

/// <summary>Informational Enterprise entry — no action, link, or transaction is offered.</summary>
public sealed record HushVotingLicenceEnterpriseInfo(
    string PlanId,
    string DisplayName,
    string SafeDescription);

/// <summary>Safe active-entitlement application view (client-safe indexed truth).</summary>
public sealed record HushVotingLicenceActiveView(
    string LicenceReference,
    string PlanId,
    string PlanFamily,
    string DisplayName,
    string SafeDescription,
    int? EligibleVoterCap,
    bool UnlimitedElections,
    string TermKind,
    int TermYears,
    IReadOnlyList<string> AllowedGovernanceOptionIds,
    DateTime EffectiveFromUtc,
    DateTime? ExpiresAtUtc,
    string AssignedCatalogueVersion,
    IReadOnlyList<HushVotingLicenceOptionTemplate> HigherOptions,
    HushVotingLicenceEnterpriseInfo? Enterprise);

/// <summary>Direct Free baseline template for no-active (server-owned, non-authorizing).</summary>
public sealed record HushVotingLicenceDirectFreeTemplate(
    string TransitionIntent,
    string RequestedPlanId,
    string ObservedCatalogueVersion);

/// <summary>Typed entitlement application result. Expected states are data, never exceptions.</summary>
public sealed record HushVotingLicenceEntitlementApplicationResult(
    HushVotingLicenceEntitlementQueryState State,
    HushVotingLicenceActiveView? Active,
    HushVotingLicenceDirectFreeTemplate? DirectFreeTemplate,
    string? StableErrorCode)
{
    public static HushVotingLicenceEntitlementApplicationResult ActiveView(HushVotingLicenceActiveView view) =>
        new(HushVotingLicenceEntitlementQueryState.Active, view, null, null);

    public static HushVotingLicenceEntitlementApplicationResult NoActive(
        HushVotingLicenceDirectFreeTemplate template) =>
        new(HushVotingLicenceEntitlementQueryState.NoActive, null, template, null);

    public static HushVotingLicenceEntitlementApplicationResult Unavailable(string stableErrorCode) =>
        new(HushVotingLicenceEntitlementQueryState.Unavailable, null, null, stableErrorCode);
}

/// <summary>
/// Pure application-view projector. Consumes the indexed entitlement (or verified absence) plus the
/// immutable catalogue and produces the client-safe application result. Never writes, never reads
/// pending mempool state, and never exposes internal/provenance/cache fields.
/// </summary>
public static class HushVotingLicenceEntitlementApplicationProjector
{
    public static HushVotingLicenceEntitlementApplicationResult Project(
        HushVotingLicenceCatalogue catalogue,
        HushVotingLicenceCurrentState state)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        if (state is HushVotingLicenceCurrentState.NoActive)
        {
            var directFree = catalogue.FindPlan(HushVotingLicencePlanId.DirectFree);
            if (directFree is null)
            {
                return HushVotingLicenceEntitlementApplicationResult.Unavailable(
                    "licence_catalogue_invalid");
            }

            return HushVotingLicenceEntitlementApplicationResult.NoActive(
                new HushVotingLicenceDirectFreeTemplate(
                    HushVotingLicenceTransitionIntent.BaselineFree,
                    HushVotingLicencePlanId.DirectFree.Value,
                    catalogue.Version.Value));
        }

        if (state is not HushVotingLicenceCurrentState.Active active)
        {
            return HushVotingLicenceEntitlementApplicationResult.Unavailable(
                "licence_index_unavailable");
        }

        var currentPlan = catalogue.FindPlan(active.CurrentPlanId);
        if (currentPlan is null)
        {
            return HushVotingLicenceEntitlementApplicationResult.Unavailable(
                "licence_index_inconsistent");
        }

        var higher = catalogue.Plans
            .Where(plan => plan.Family == HushVotingLicenceFamily.Veritas
                && plan.UpgradeRank > currentPlan.UpgradeRank
                && plan.Availability == HushVotingLicenceAvailability.AutomaticUpgrade
                && !plan.Retirement.IsRetired)
            .OrderBy(plan => plan.UpgradeRank)
            .Select(plan => new HushVotingLicenceOptionTemplate(
                plan.Id.Value,
                plan.DisplayName,
                plan.SafeDescription,
                plan.EligibleVoterCap,
                plan.UnlimitedElections,
                plan.Term.IsPerpetual ? "perpetual" : "annual",
                plan.Term.Years))
            .ToArray();

        var enterprise = catalogue.FindPlan(HushVotingLicencePlanId.Enterprise);
        var enterpriseInfo = enterprise is null
            ? null
            : new HushVotingLicenceEnterpriseInfo(
                enterprise.Id.Value,
                enterprise.DisplayName,
                enterprise.SafeDescription);

        var licenceReference = active.CurrentLicenceTransactionId?.ToString() ?? string.Empty;

        return HushVotingLicenceEntitlementApplicationResult.ActiveView(
            new HushVotingLicenceActiveView(
                licenceReference,
                currentPlan.Id.Value,
                HushVotingLicenceEnumNames.FamilyToWire(currentPlan.Family).ToLowerInvariant(),
                currentPlan.DisplayName,
                currentPlan.SafeDescription,
                currentPlan.EligibleVoterCap,
                currentPlan.UnlimitedElections,
                currentPlan.Term.IsPerpetual ? "perpetual" : "annual",
                currentPlan.Term.Years,
                currentPlan.GovernanceOptions.Select(option => option.Id.Value).ToArray(),
                active.EffectiveFromUtc,
                active.ExpiresAtUtc,
                catalogue.Version.Value,
                higher,
                enterpriseInfo));
    }
}

// FEAT-018 DD018-06 supersedes the provisional scheduled-window seam formerly here.
// Actual enforcement uses HushVotingLicenceOpenInstantPolicy and the complete durable
// ElectionEntitlementCapture in the election lifecycle transaction. No wall-clock snapshot
// or future-close coverage helper remains as an alternative authorization path.
