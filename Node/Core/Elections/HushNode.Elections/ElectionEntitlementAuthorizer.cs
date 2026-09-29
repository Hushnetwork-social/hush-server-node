using HushNode.Elections.Storage;
using HushNode.HushVoting.Licensing.Storage;
using HushShared.Elections.Model;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Elections;

/// <summary>Current-owner authorization; all reads/locks share the election write transaction.</summary>
public sealed class ElectionEntitlementAuthorizer(LicenceCatalogueArchive? archive, IElectionExecutionContextSource? executionSource = null)
{
    private readonly IElectionExecutionContextSource _execution = executionSource ?? new ElectionExecutionContextSource();

    public async Task<IndexedEntitlementReadResult> LockOwnerAsync(IElectionsRepository repository,
        string owner, Guid? sourceTransactionId)
    {
        var execution = _execution.Current;
        if (execution is null || !execution.IsValid || (sourceTransactionId.HasValue && execution.TransactionId != sourceTransactionId))
            return IndexedEntitlementReadResult.Unavailable("licence_index_unavailable", "Canonical execution context is unavailable.");
        return await repository.LockOwnerEntitlementAsync(owner, execution.BlockTimeUtc);
    }

    public async Task<IndexedEntitlementReadResult> LockElectionOwnerAsync(IElectionsRepository repository,
        ElectionId electionId, Guid? sourceTransactionId)
    {
        // Owner is immutable; read its scalar before locks, then lock subject before election.
        var owner = await repository.GetElectionOwnerAsync(electionId);
        return await LockOwnerAsync(repository, owner ?? "", sourceTransactionId);
    }

    public ElectionEntitlementReason Check(IndexedEntitlementReadResult read, string profileId,
        ElectionBindingStatus binding, ElectionGovernanceMode governance, int resultingRosterCount,
        out string? governanceOptionId)
    {
        governanceOptionId = null;
        if (!read.IsSuccess || read.Outcome == IndexedEntitlementReadOutcome.IndexUnavailable)
            return ElectionEntitlementReason.AuthorityUnavailable;
        if (read.Outcome == IndexedEntitlementReadOutcome.NoActive) return ElectionEntitlementReason.NotActive;
        var entitlement = read.Entitlement;
        if (entitlement is null || entitlement.LicenceReference is null || entitlement.LicenceReference == Guid.Empty
            || entitlement.EntitlementRevision <= 0 || entitlement.EligibleVoterCap is <= 0
            || !entitlement.UnlimitedElectionPolicy || entitlement.AllowedGovernanceOptionIds.Count == 0
            || entitlement.AllowedGovernanceOptionIds.Any(id => HushVotingGovernanceOptionId.TryGetKnown(id)?.Value != id))
            return ElectionEntitlementReason.SemanticsUnsupported;
        var release = archive?.Find(entitlement.AssignedCatalogueVersion, entitlement.AssignedCatalogueDigestSha256);
        var planId = HushVotingLicencePlanId.TryGetKnown(entitlement.PlanId);
        var retainedPlan = planId is null ? null : release?.Catalogue.FindPlan(planId);
        if (release is null || retainedPlan is null || entitlement.UpgradeRank < 0
            || entitlement.PlanFamily != HushVotingLicenceEnumNames.FamilyToWire(retainedPlan.Family).ToLowerInvariant()
            || entitlement.EffectiveFromUtc.Kind != DateTimeKind.Utc
            || (entitlement.TermKind == "perpetual" ? entitlement.TermYears != 0 || entitlement.ExpiresAtUtc is not null
                : entitlement.TermKind != "annual" || entitlement.TermYears <= 0 || entitlement.ExpiresAtUtc is null
                    || entitlement.ExpiresAtUtc.Value.Kind != DateTimeKind.Utc || entitlement.ExpiresAtUtc <= entitlement.EffectiveFromUtc))
            return ElectionEntitlementReason.SemanticsUnsupported;
        if (resultingRosterCount < 0) return ElectionEntitlementReason.SemanticsUnsupported;
        if (entitlement.EligibleVoterCap is int cap && resultingRosterCount > cap)
            return ElectionEntitlementReason.LimitExceeded;

        var bindingMode = binding switch
        {
            ElectionBindingStatus.Binding => HushVotingBindingStatus.Binding,
            ElectionBindingStatus.NonBinding => HushVotingBindingStatus.NonBinding,
            _ => (HushVotingBindingStatus)(-1),
        };
        var mapping = release.Catalogue.ProfileCompatibility.SingleOrDefault(m => m.RuntimeProfileId == profileId
            && m.BindingStatus == bindingMode && entitlement.AllowedGovernanceOptionIds.Contains(m.GovernanceOptionId.Value));
        if (mapping is null || planId == HushVotingLicencePlanId.Enterprise)
            return ElectionEntitlementReason.ProfileNotAllowed;
        var requiredGovernance = mapping.GovernanceOptionId == HushVotingGovernanceOptionId.NoCustomerTrustees
            ? ElectionGovernanceMode.AdminOnly : ElectionGovernanceMode.TrusteeThreshold;
        if (governance != requiredGovernance) return ElectionEntitlementReason.ProfileNotAllowed;
        governanceOptionId = mapping.GovernanceOptionId.Value;
        return ElectionEntitlementReason.None;
    }
}
