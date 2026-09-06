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
    Func<DateTime>? utcNow = null) : ILicenceEntitlementQueryApplicationService
{
    private readonly ILicenceIndexedProjectionReader _indexedProjectionReader = indexedProjectionReader;
    private readonly LicenceServiceConfiguration _configuration = configuration;
    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);

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
        var state = new HushVotingLicenceCurrentState.Active(
            HushVotingLicencePlanId.FromExternal(entitlement.PlanId),
            entitlement.LicenceReference,
            entitlement.AssignedCatalogueVersion,
            entitlement.EffectiveFromUtc,
            entitlement.ExpiresAtUtc);

        var view = HushVotingLicenceEntitlementApplicationProjector.Project(_configuration.Catalogue, state);
        if (view.State == HushVotingLicenceEntitlementQueryState.Active && view.Active is not null)
        {
            return new LicenceEntitlementQueryApplicationResult(
                HushVotingLicenceEntitlementQueryState.Active,
                view.Active,
                null,
                null);
        }

        // Indexed row exists but the catalogue cannot interpret it: never fabricate Direct Free.
        return Unavailable("licence_index_inconsistent");
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
