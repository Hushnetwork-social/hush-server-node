using HushNode.HushVoting.Licensing.Storage;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Shared internal writer that converges one authoritative entitlement into Redis through strict
/// monotonic CAS. Used by the outbox dispatcher and immediate post-commit publication. Only
/// successful authoritative projections in the current catalogue namespace are ever written.
/// </summary>
public sealed class LicenceCacheRedisWriter
{
    private readonly IEntitlementProjectionStore _store;
    private readonly LicenceCacheEnvelopeCodec _codec;
    private readonly LicenceCacheOptions _options;
    private readonly LicenceCacheKeyRing _keyRing;
    private readonly ICurrentLicenceCatalogueProvider _catalogue;
    private readonly LicenceCacheTelemetry _telemetry;
    private readonly Func<DateTime> _utcNow;
    private readonly string _instancePrefix;

    public LicenceCacheRedisWriter(
        IEntitlementProjectionStore store,
        LicenceCacheEnvelopeCodec codec,
        LicenceCacheOptions options,
        LicenceCacheKeyRing keyRing,
        ICurrentLicenceCatalogueProvider catalogue,
        LicenceCacheTelemetry telemetry,
        Func<DateTime> utcNow,
        string instancePrefix)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _instancePrefix = instancePrefix;
    }

    public async Task<bool> TryWriteAsync(
        AuthenticatedIdentitySubject subject,
        EffectiveLicenceEntitlement entitlement,
        CancellationToken cancellationToken)
    {
        var current = _catalogue.Current;
        if (!string.Equals(entitlement.AssignedCatalogueVersion, current.Version, StringComparison.Ordinal) ||
            !string.Equals(entitlement.AssignedCatalogueDigestSha256, current.DigestSha256, StringComparison.OrdinalIgnoreCase))
        {
            // Cross-catalogue delivery is out of namespace; a release change switched the namespace
            // instead of creating per-subject outbox rows.
            return true;
        }

        var now = _utcNow();
        var catalogueToken = LicenceCacheKeyBuilder.BuildCatalogueToken(current.Version, current.DigestSha256);
        var digest = LicenceCacheKeyDerivation.ComputeSubjectDigest(
            LicenceCacheKeyDerivation.DeriveSubjectKey(_keyRing.Current.SecretBytes),
            subject.CanonicalPublicSigningAddress);
        var ttl = LicenceCacheTtlCalculator.Compute(digest, now, entitlement.ExpiresAtUtc, _options);
        if (!ttl.HasPositiveLifetime)
        {
            return true;
        }

        var key = LicenceCacheKeyBuilder.BuildProjectionKey(
            _instancePrefix, catalogueToken, _keyRing.Current.KeyId, digest);

        var envelope = new CachedEntitlementEnvelope
        {
            KeyId = _keyRing.Current.KeyId,
            CatalogueVersion = entitlement.AssignedCatalogueVersion,
            CatalogueToken = catalogueToken,
            CacheWrittenUtc = now,
            CacheValidUntilUtc = ttl.CacheValidUntilUtc,
            PlanId = entitlement.PlanId,
            PlanFamily = entitlement.PlanFamily,
            UpgradeRank = entitlement.UpgradeRank,
            EligibleVoterCap = entitlement.EligibleVoterCap,
            UnlimitedElections = entitlement.UnlimitedElectionPolicy,
            TermKind = entitlement.TermKind,
            TermYears = entitlement.TermYears,
            AllowedGovernanceOptionIds = entitlement.AllowedGovernanceOptionIds,
            ExpiresAtUtc = entitlement.ExpiresAtUtc,
            EntitlementRevision = entitlement.EntitlementRevision,
        };

        var bytes = _codec.SerializeCanonical(envelope);
        var authKey = LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(_keyRing.Current.SecretBytes);
        var result = await _store.WriteAsync(
            key,
            bytes,
            entitlement.EntitlementRevision,
            ttl.RedisTtlSeconds,
            authKey,
            cancellationToken).ConfigureAwait(false);

        _telemetry.Count("delivery_" + result.Outcome.ToString().ToLowerInvariant());
        return result.Success;
    }
}
