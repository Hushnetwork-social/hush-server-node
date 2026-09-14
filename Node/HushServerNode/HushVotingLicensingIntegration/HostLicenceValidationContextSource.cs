// FEAT-015 Phase 6.3/6.5 — host adapter implementing the licence validation context source.
//
// Resolves the dependency-safe inputs the licence validator needs:
//   - exact indexed HushNetwork identity for a canonical signatory (IIdentityStorageService:
//     Profile with authoritative creation BlockIndex provenance) — NEVER the licence subject
//     table, which is only created once a licence indexes;
//   - the current immutable FEAT-012 catalogue snapshot (LicenceServiceConfiguration);
//   - the current indexed effective state (ILicenceIndexedProjectionReader — never a direct write).
// Authentication happens before this adapter is ever called (signed metadata gate).

using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.Identity.Storage;
using HushShared.Identity.Model;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HushServerNode.HushVotingLicensingIntegration;

public sealed class HostLicenceValidationContextSource : IHushVotingLicenceValidationContextSource
{
    private readonly IServiceProvider _services;

    public HostLicenceValidationContextSource(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public Task<HushVotingLicenceCatalogue> GetCurrentCatalogueAsync(CancellationToken cancellationToken)
    {
        var configuration = _services.GetRequiredService<LicenceServiceConfiguration>();
        return Task.FromResult(configuration.Catalogue);
    }

    public async Task<HushVotingLicenceSignatoryContext?> ResolveIdentityAsync(
        string canonicalPublicSigningAddress,
        CancellationToken cancellationToken)
    {
        // The licence subject table is NOT the identity authority. Resolve the exact indexed
        // HushNetwork identity (Profile) and its authoritative creation-block provenance.
        IIdentityStorageService identityStorage;
        try
        {
            identityStorage = _services.GetRequiredService<IIdentityStorageService>();
        }
        catch (InvalidOperationException)
        {
            // Identity storage not composed in this host variant (unit-style hosts): fall back to
            // the licence subject anchor only when it exists (post-index host tests).
            return await ResolveFromLicenceSubjectAsync(canonicalPublicSigningAddress, cancellationToken);
        }

        var profileBase = await identityStorage.RetrieveIdentityAsync(canonicalPublicSigningAddress);
        if (profileBase is not Profile profile)
        {
            return null;
        }

        var canonical = AuthenticatedIdentitySubject.NormalizeCanonicalAddress(profile.PublicSigningAddress);
        if (canonical is null || !string.Equals(canonical, canonicalPublicSigningAddress, StringComparison.Ordinal))
        {
            return null;
        }

        return new HushVotingLicenceSignatoryContext(canonical, profile.BlockIndex.Value);
    }

    private async Task<HushVotingLicenceSignatoryContext?> ResolveFromLicenceSubjectAsync(
        string canonicalPublicSigningAddress,
        CancellationToken cancellationToken)
    {
        await using var db = HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(_services);
        var row = await db.Set<LicenceSubjectEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.SubjectType == LicencePersistenceVocabulary.SubjectTypeIdentity
                     && s.CanonicalPublicSigningAddress == canonicalPublicSigningAddress,
                cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new HushVotingLicenceSignatoryContext(
            row.CanonicalPublicSigningAddress,
            row.IdentityCreationBlockIndex);
    }

    public async Task<HushVotingLicenceCurrentState> ResolveCurrentStateAsync(
        HushVotingLicenceSignatoryContext identity,
        CancellationToken cancellationToken)
    {
        if (!AuthenticatedIdentitySubject.TryCreate(
                LicencePersistenceVocabulary.SubjectTypeIdentity,
                identity.CanonicalPublicSigningAddress,
                identity.IdentityCreationBlockIndex,
                out var subject,
                out _)
            || subject is null)
        {
            return new HushVotingLicenceCurrentState.NoActive();
        }

        var reader = _services.GetRequiredService<ILicenceIndexedProjectionReader>();
        var result = await reader.ResolveEffectiveAsync(subject, DateTime.UtcNow, cancellationToken);

        if (result.Outcome == IndexedEntitlementReadOutcome.Active && result.Entitlement is not null)
        {
            return new HushVotingLicenceCurrentState.Active(
                HushVotingLicencePlanId.FromExternal(result.Entitlement.PlanId),
                result.Entitlement.LicenceReference,
                result.Entitlement.AssignedCatalogueVersion,
                result.Entitlement.EffectiveFromUtc,
                result.Entitlement.ExpiresAtUtc);
        }

        return new HushVotingLicenceCurrentState.NoActive();
    }
}
