// FEAT-015 Phase 6.5 — GetMyEntitlement query application service.
//
// Resolves indexed authority truth (ILicenceIndexedProjectionReader) and projects the
// client-safe application result. Strictly read-only: never provisions, activates, expires, or
// writes licence state; infrastructure failure is UNAVAILABLE, never no-active. Mempool pending
// state and cache provenance are never surfaced. (FEAT-014's Redis reader accelerates ordinary UI
// display reads; this query validates indexed truth and is the authority path the clients call.)

using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushShared.HushVoting.Licensing.Model;

namespace HushNode.HushVoting.Licence.gRPC;

public sealed class LicenceEntitlementQueryApplicationService(
    ILicenceIndexedProjectionReader indexedProjectionReader,
    LicenceServiceConfiguration configuration,
    Func<DateTime>? utcNow = null,
    LicenceCatalogueArchive? archive = null) : ILicenceEntitlementQueryApplicationService
{
    private readonly ILicenceIndexedProjectionReader _indexedProjectionReader = indexedProjectionReader;
    private readonly LicenceServiceConfiguration _configuration = configuration;
    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);
    private readonly LicenceCatalogueArchive _archive = archive ?? new LicenceCatalogueArchive([configuration]);

    public async Task<LicenceEntitlementQueryApplicationResult> GetMyEntitlementAsync(
        string canonicalActorAddress,
        CancellationToken cancellationToken)
    {
        // The trusted subject is derived from the authenticated canonical signatory (the request
        // carries no selectable identity). A subject anchor that does not exist means the identity
        // has never had any licence indexed -> verified absence is safe ONLY when the caller is an
        // exact indexed identity; the transport gate authenticates identity before this call.
        if (!AuthenticatedIdentitySubject.TryCreate(
                LicencePersistenceVocabulary.SubjectTypeIdentity,
                canonicalActorAddress,
                identityCreationBlockIndex: 0,
                out var subject,
                out _)
            || subject is null)
        {
            return Unavailable("licence_index_unavailable");
        }

        var read = await _indexedProjectionReader.ResolveEffectiveAsync(
            subject,
            _utcNow(),
            cancellationToken);

        if (!read.IsSuccess) return Unavailable("licence_index_unavailable");

        return read.Outcome switch
        {
            IndexedEntitlementReadOutcome.Active when read.Entitlement is not null =>
                ProjectActive(read.Entitlement),
            IndexedEntitlementReadOutcome.NoActive => ProjectNoActive(),
            _ => Unavailable(read.StableErrorCode ?? "licence_index_unavailable"),
        };
    }

    private LicenceEntitlementQueryApplicationResult ProjectActive(
        EffectiveLicenceEntitlement entitlement)
    {
        var assignedRelease = _archive.Find(entitlement.AssignedCatalogueVersion, entitlement.AssignedCatalogueDigestSha256);
        var planId = HushVotingLicencePlanId.TryGetKnown(entitlement.PlanId);
        var label = planId is null ? null : assignedRelease?.Catalogue.FindPlan(planId);
        if (label is null || entitlement.LicenceReference is null || entitlement.LicenceReference == Guid.Empty
            || entitlement.UpgradeRank < 0 || entitlement.EligibleVoterCap is < 0
            || entitlement.PlanFamily != HushVotingLicenceEnumNames.FamilyToWire(label.Family).ToLowerInvariant()
            || entitlement.AllowedGovernanceOptionIds.Count == 0
            || entitlement.AllowedGovernanceOptionIds.Any(id => HushVotingGovernanceOptionId.TryGetKnown(id) is null)
            || (entitlement.TermKind == "perpetual" ? entitlement.TermYears != 0 || entitlement.ExpiresAtUtc is not null
                : entitlement.TermKind != "annual" || entitlement.TermYears <= 0 || entitlement.ExpiresAtUtc <= entitlement.EffectiveFromUtc || entitlement.ExpiresAtUtc is null))
        {
            return Unavailable("licence_index_inconsistent");
        }

        // The retained release supplies safe display copy; persisted terms remain authoritative.
        // Only new upgrade options come from the current release, ranked against the assigned rank.
        var higher = _configuration.Catalogue.Plans
            .Where(p => p.Family == HushVotingLicenceFamily.Veritas && p.Id != planId
                && p.UpgradeRank > entitlement.UpgradeRank
                && p.Availability == HushVotingLicenceAvailability.AutomaticUpgrade && !p.Retirement.IsRetired)
            .OrderBy(p => p.UpgradeRank)
            .Select(p => new HushVotingLicenceOptionTemplate(p.Id.Value, p.DisplayName, p.SafeDescription,
                p.EligibleVoterCap, p.UnlimitedElections, p.Term.IsPerpetual ? "perpetual" : "annual", p.Term.Years))
            .ToArray();
        var enterprise = _configuration.Catalogue.FindPlan(HushVotingLicencePlanId.Enterprise);
        return new LicenceEntitlementQueryApplicationResult(HushVotingLicenceEntitlementQueryState.Active,
            new HushVotingLicenceActiveView(entitlement.LicenceReference.Value.ToString(), entitlement.PlanId,
                entitlement.PlanFamily, label.DisplayName, label.SafeDescription, entitlement.EligibleVoterCap,
                entitlement.UnlimitedElectionPolicy, entitlement.TermKind, entitlement.TermYears,
                entitlement.AllowedGovernanceOptionIds.ToArray(), entitlement.EffectiveFromUtc, entitlement.ExpiresAtUtc,
                entitlement.AssignedCatalogueVersion, higher, enterprise is null ? null :
                    new HushVotingLicenceEnterpriseInfo(enterprise.Id.Value, enterprise.DisplayName, enterprise.SafeDescription)),
            null, null);
    }

    private LicenceEntitlementQueryApplicationResult ProjectNoActive()
    {
        var absent = HushVotingLicenceEntitlementApplicationProjector.Project(
            _configuration.Catalogue,
            new HushVotingLicenceCurrentState.NoActive());
        if (absent.State == HushVotingLicenceEntitlementQueryState.NoActive
            && absent.DirectFreeTemplate is not null)
        {
            return new LicenceEntitlementQueryApplicationResult(
                HushVotingLicenceEntitlementQueryState.NoActive,
                null,
                absent.DirectFreeTemplate,
                null);
        }

        return Unavailable("licence_catalogue_invalid");
    }

    private static LicenceEntitlementQueryApplicationResult Unavailable(string stableCode) =>
        new(HushVotingLicenceEntitlementQueryState.Unavailable, null, null, stableCode);
}
