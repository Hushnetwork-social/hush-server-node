using HushNode.HushVoting.Licensing.Storage;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Resolves the authoritative effective entitlement. Production wires FEAT-013
/// <c>LicenceEntitlementService.GetOrProvisionAsync</c>; tests inject deterministic fakes.
/// </summary>
public interface IEntitlementAuthorityResolver
{
    Task<LicenceResolutionResult> ResolveEffectiveEntitlementAsync(
        AuthenticatedIdentitySubject subject,
        CancellationToken cancellationToken);
}

/// <summary>Provides the node's current FEAT-012 catalogue release (version + SHA-256 digest).</summary>
public interface ICurrentLicenceCatalogueProvider
{
    (string Version, string DigestSha256) Current { get; }
}
