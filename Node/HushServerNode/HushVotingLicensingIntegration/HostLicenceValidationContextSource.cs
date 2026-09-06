// FEAT-015 Phase 6.3/6.5 — host adapter implementing the licence validation context source.
//
// Resolves the dependency-safe inputs the licence validator needs:
//   - exact indexed licence subject for a canonical signatory (the subject row is created only by
//     block indexing, so its presence proves an indexed identity with creation-block provenance);
//   - the current immutable FEAT-012 catalogue snapshot (already composed as
//     LicenceServiceConfiguration);
//   - the current indexed effective state (ILicenceIndexedProjectionReader — never a direct write).
// Authentication happens before this adapter is ever called (signed metadata gate).

using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
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
