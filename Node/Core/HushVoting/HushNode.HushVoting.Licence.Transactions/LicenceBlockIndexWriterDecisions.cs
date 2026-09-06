// FEAT-015 Task 6.3 — deterministic licence block-time derivation helpers.
//
// The pre-mempool composite decision is advisory: between admission and block inclusion other
// valid transitions may index first. Deterministic block validation therefore derives the
// authoritative decision against the LOCKED indexed state at the containing-block instant and
// refuses to activate stale/lower/same transitions (at-most-one effective assignment). These
// helpers are pure: no I/O, no wall clock, no client authority.

using HushNode.HushVoting.Licensing.Storage;
using HushShared.HushVoting.Licensing.Model;

namespace HushNode.HushVoting.Licence.Transactions;

public static class LicenceBlockIndexWriterDecisions
{
    /// <summary>
    /// Builds the current indexed state view used for deterministic block-time re-derivation. A
    /// null/absent active row is verified no-active; an active row maps its plan/reference/interval.
    /// </summary>
    public static HushVotingLicenceCurrentState CurrentlyActiveState(
        HushVotingLicenceCatalogue catalogue,
        LicenceAssignmentEntity? currentlyActive)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        if (currentlyActive is null)
        {
            return new HushVotingLicenceCurrentState.NoActive();
        }

        var planId = HushVotingLicencePlanId.TryGetKnown(currentlyActive.PlanId)
            ?? HushVotingLicencePlanId.FromExternal(currentlyActive.PlanId);

        return new HushVotingLicenceCurrentState.Active(
            planId,
            currentlyActive.OriginatingTransactionId,
            currentlyActive.AssignedCatalogueVersion,
            currentlyActive.EffectiveFromUtc,
            currentlyActive.ExpiresAtUtc);
    }
}
