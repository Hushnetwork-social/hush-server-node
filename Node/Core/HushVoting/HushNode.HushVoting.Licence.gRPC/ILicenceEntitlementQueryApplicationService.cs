// FEAT-015 Task 6.1 — dependency-safe entitlement query application port.
//
// The gRPC service layer depends only on this small port; the HushServerNode host adapter
// (Phase 6.3/6.5) implements it over identity storage (exact indexed identity + creation
// block), the FEAT-014 Redis-first cache reader, the FEAT-013 indexed projection, and the
// FEAT-012 current catalogue snapshot. The service never touches storage or cache directly
// and never performs a licence-state write.

using HushNode.HushVoting.Licence.Transactions;

namespace HushNode.HushVoting.Licence.gRPC;

/// <summary>Query application result for GetMyEntitlement (typed, transport-neutral).</summary>
public sealed record LicenceEntitlementQueryApplicationResult(
    HushVotingLicenceEntitlementQueryState State,
    HushVotingLicenceActiveView? Active,
    HushVotingLicenceDirectFreeTemplate? DirectFreeTemplate,
    string? UnavailableCode);

/// <summary>Strictly read-only query application service contract.</summary>
public interface ILicenceEntitlementQueryApplicationService
{
    Task<LicenceEntitlementQueryApplicationResult> GetMyEntitlementAsync(
        string canonicalActorAddress,
        CancellationToken cancellationToken);
}
