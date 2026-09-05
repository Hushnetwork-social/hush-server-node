using HushNode.HushVoting.Licensing.Storage;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Maps an authoritative FEAT-013 <see cref="EffectiveLicenceEntitlement"/> into the
/// non-authoritative <see cref="CachedEntitlementProjection"/>. This is the only sanctioned
/// direction: the cache module never maps a cached projection back into an authoritative type.
/// </summary>
internal static class CachedEntitlementProjectionMapper
{
    public static CachedEntitlementProjection FromAuthoritative(EffectiveLicenceEntitlement entitlement)
    {
        ArgumentNullException.ThrowIfNull(entitlement);

        return new CachedEntitlementProjection(
            planId: entitlement.PlanId,
            planFamily: entitlement.PlanFamily,
            upgradeRank: entitlement.UpgradeRank,
            eligibleVoterCap: entitlement.EligibleVoterCap,
            unlimitedElectionPolicy: entitlement.UnlimitedElectionPolicy,
            termKind: entitlement.TermKind,
            termYears: entitlement.TermYears,
            allowedGovernanceOptionIds: entitlement.AllowedGovernanceOptionIds,
            expiresAtUtc: entitlement.ExpiresAtUtc,
            entitlementRevision: entitlement.EntitlementRevision);
    }
}
