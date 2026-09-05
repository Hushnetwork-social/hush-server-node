using HushNode.HushVoting.Licensing.Storage;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Optimizes ordinary effective-entitlement display reads by checking Redis first and falling back to
/// the authoritative FEAT-013 service on miss, invalidity, Redis failure, or intentional disablement.
///
/// <para>This reader is a read/display optimization only. It is NOT an activation or authorization
/// provider: activation and enforcement must depend on FEAT-013's authoritative service and must never
/// reference this contract (enforced by architecture tests).</para>
/// </summary>
public interface ICachedEntitlementReader
{
    /// <summary>
    /// Resolves the effective entitlement for display through the cache. Returns a cached hit without
    /// a FEAT-013 entitlement call when a valid unexpired entry exists; otherwise resolves through
    /// FEAT-013 and caches only a successful authoritative projection. No absence, failure, conflict,
    /// timeout, or unavailable outcome is ever cached.
    /// </summary>
    /// <param name="subject">FEAT-013 trusted authenticated subject; the raw signing address never leaves this call boundary.</param>
    /// <param name="cancellationToken">Bounded, cancellable operation token.</param>
    Task<CachedEntitlementReadResult> GetEffectiveEntitlementAsync(
        AuthenticatedIdentitySubject subject,
        CancellationToken cancellationToken = default);
}
