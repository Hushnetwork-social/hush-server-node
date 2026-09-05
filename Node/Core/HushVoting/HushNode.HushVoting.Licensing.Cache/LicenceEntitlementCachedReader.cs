using HushNode.HushVoting.Licensing.Storage;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Redis-first read-through orchestration over FEAT-013: a valid authenticated hit is served without
/// an authority call; misses/invalid entries are removed best-effort and filled once through local
/// coalescing plus a token-owned distributed lease; Redis/lease failures degrade to FEAT-013; no
/// absence or failure is ever cached; a valid hit may support display during a PostgreSQL outage but
/// never authorizes activation or enforcement.
/// </summary>
public sealed class LicenceEntitlementCachedReader : ICachedEntitlementReader
{
    private readonly IEntitlementProjectionStore _store;
    private readonly LicenceCacheValueValidator _validator;
    private readonly LicenceCacheEnvelopeCodec _codec;
    private readonly LicenceCacheOptions _options;
    private readonly LicenceCacheKeyRing _keyRing;
    private readonly IEntitlementAuthorityResolver _authority;
    private readonly ICurrentLicenceCatalogueProvider _catalogue;
    private readonly LicenceCacheCircuitBreaker _circuit;
    private readonly LicenceCacheSingleFlight _singleFlight;
    private readonly LicenceCacheTelemetry _telemetry;
    private readonly Func<DateTime> _utcNow;
    private readonly string _instancePrefix;

    public LicenceEntitlementCachedReader(
        IEntitlementProjectionStore store,
        LicenceCacheValueValidator validator,
        LicenceCacheEnvelopeCodec codec,
        LicenceCacheOptions options,
        LicenceCacheKeyRing keyRing,
        IEntitlementAuthorityResolver authority,
        ICurrentLicenceCatalogueProvider catalogue,
        LicenceCacheCircuitBreaker circuit,
        LicenceCacheSingleFlight singleFlight,
        LicenceCacheTelemetry telemetry,
        Func<DateTime> utcNow,
        string instancePrefix)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
        _singleFlight = singleFlight ?? throw new ArgumentNullException(nameof(singleFlight));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _instancePrefix = instancePrefix;
    }

    public async Task<CachedEntitlementReadResult> GetEffectiveEntitlementAsync(
        AuthenticatedIdentitySubject subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _utcNow();
        var catalogue = _catalogue.Current;
        var catalogueToken = LicenceCacheKeyBuilder.BuildCatalogueToken(catalogue.Version, catalogue.DigestSha256);

        if (!_options.Enabled)
        {
            _telemetry.Count("cache_disabled_resolve");
            return (await ResolveAuthoritativeAsync(subject, cancellationToken, EntitlementCacheReadOutcome.CacheDisabled).ConfigureAwait(false)).Result;
        }

        // 1) Read Redis first while the circuit is closed or during one half-open probe.
        var redisAttempted = false;
        if (_circuit.IsAttemptPermitted())
        {
            redisAttempted = true;
            try
            {
                var hit = await TryReadHitAsync(subject, catalogueToken, now, cancellationToken).ConfigureAwait(false);
                if (hit is not null)
                {
                    return hit;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                _telemetry.Count("redis_read_error");
                _circuit.RecordConnectionFailure();
            }
        }
        else
        {
            _telemetry.Count("circuit_open_bypass");
        }

        // 2) Single flight: coalesce same-subject fills; the lease owner fills via FEAT-013 and caches.
        return await ResolveWithSingleFlightAsync(subject, catalogueToken, redisAttempted, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<CachedEntitlementReadResult?> TryReadHitAsync(
        AuthenticatedIdentitySubject subject,
        string catalogueToken,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var (currentKey, currentDigest) = CurrentKeyAndDigest(subject, catalogueToken);

        var read = await _store.ReadCurrentAsync(currentKey, cancellationToken).ConfigureAwait(false);
        if (!read.Found)
        {
            // 3) Current-key miss during rotation: try the single configured previous key.
            if (_keyRing.Previous is not null)
            {
                var previousDigest = ComputeDigest(_keyRing.Previous, subject.CanonicalPublicSigningAddress);
                var previousKey = LicenceCacheKeyBuilder.BuildProjectionKey(
                    _instancePrefix, catalogueToken, _keyRing.Previous.KeyId, previousDigest);
                var previousRead = await _store.ReadCurrentAsync(previousKey, cancellationToken).ConfigureAwait(false);
                if (previousRead.Found && previousRead.Valid &&
                    _validator.TryValidate(
                        previousKey,
                        previousRead.CanonicalEnvelopeBytes,
                        previousRead.TagBytes,
                        catalogueToken,
                        _keyRing,
                        now,
                        out var previousEnvelope,
                        out _))
                {
                    _telemetry.Count("hit_previous_key");
                    await MigratePreviousKeyAsync(currentKey, previousKey, previousEnvelope!, cancellationToken)
                        .ConfigureAwait(false);
                    return ToHitResult(previousEnvelope!);
                }
            }

            _telemetry.Count("miss_no_entry");
            return null;
        }

        if (!read.Valid)
        {
            _telemetry.Count("corrupt_miss_" + (read.StableRejectReason ?? "malformed"));
            await SafeRemoveAsync(currentKey, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (!_validator.TryValidate(
                currentKey,
                read.CanonicalEnvelopeBytes,
                read.TagBytes,
                catalogueToken,
                _keyRing,
                now,
                out var envelope,
                out var reason))
        {
            _telemetry.Count("invalid_miss_" + reason);
            await SafeRemoveAsync(currentKey, cancellationToken).ConfigureAwait(false);
            return null;
        }

        _telemetry.Count(envelope!.KeyId == _keyRing.Current.KeyId ? "hit_current_key" : "hit_previous_key");
        return ToHitResult(envelope);
    }

    private async Task<CachedEntitlementReadResult> ResolveWithSingleFlightAsync(
        AuthenticatedIdentitySubject subject,
        string catalogueToken,
        bool redisAttempted,
        CancellationToken cancellationToken)
    {
        var isOwner = _singleFlight.TryBecomeOwner(subject.CanonicalPublicSigningAddress, out _);
        if (isOwner)
        {
            try
            {
                var (currentKey, _) = CurrentKeyAndDigest(subject, catalogueToken);
                return await FillAsOwnerAsync(subject, catalogueToken, currentKey, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _singleFlight.FinishOwner(subject.CanonicalPublicSigningAddress);
            }
        }

        // Non-owner: poll Redis within the bounded waiter budget, then fall back to FEAT-013.
        _telemetry.Count("wait_joining");
        var deadline = _utcNow() + TimeSpan.FromMilliseconds(_options.WaiterPollBudgetMs);
        while (_utcNow() < deadline)
        {
            try
            {
                var hit = await TryReadHitAsync(subject, catalogueToken, _utcNow(), cancellationToken).ConfigureAwait(false);
                if (hit is not null)
                {
                    return hit;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                _telemetry.Count("redis_poll_error");
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(15), cancellationToken).ConfigureAwait(false);
        }

        _telemetry.Count("waiter_timeout_fallback");
        return (await ResolveAuthoritativeAsync(subject, cancellationToken).ConfigureAwait(false)).Result;
    }

    private async Task<CachedEntitlementReadResult> FillAsOwnerAsync(
        AuthenticatedIdentitySubject subject,
        string catalogueToken,
        string currentKey,
        CancellationToken cancellationToken)
    {
        var ownershipToken = RedisEntitlementProjectionStore.NewOwnershipToken();
        var (_, currentDigest) = CurrentKeyAndDigest(subject, catalogueToken);
        var leaseKey = LicenceCacheKeyBuilder.BuildFillLeaseKey(
            _instancePrefix, catalogueToken, _keyRing.Current.KeyId, currentDigest);

        bool leaseAcquired;
        try
        {
            leaseAcquired = await _store.TryAcquireLeaseAsync(
                leaseKey, ownershipToken, _options.FillLeaseSeconds, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _telemetry.Count("lease_error");
            // Lease failure never blocks or fails authority resolution.
            return (await ResolveAuthoritativeAsync(subject, cancellationToken).ConfigureAwait(false)).Result;
        }

        if (!leaseAcquired)
        {
            _telemetry.Count("lease_contended");
            // Another instance owns the fill; join as a bounded waiter.
            return await WaitForFillThenFallbackAsync(subject, catalogueToken, cancellationToken).ConfigureAwait(false);
        }

        _telemetry.Count("distributed_lease_acquired");
        try
        {
            var (resolution, entitlement) = await ResolveAuthoritativeAsync(subject, cancellationToken)
                .ConfigureAwait(false);
            if (resolution.IsSuccess && entitlement is not null)
            {
                _telemetry.Count("fill_success");
                await TryCacheAuthoritativeProjectionAsync(
                    subject, entitlement, currentKey, catalogueToken, cancellationToken).ConfigureAwait(false);
            }

            return resolution;
        }
        finally
        {
            try
            {
                await _store.TryReleaseLeaseAsync(leaseKey, ownershipToken, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Abandoned leases become claimable after their bounded expiry.
            }
        }
    }

    private async Task<CachedEntitlementReadResult> WaitForFillThenFallbackAsync(
        AuthenticatedIdentitySubject subject,
        string catalogueToken,
        CancellationToken cancellationToken)
    {
        var deadline = _utcNow() + TimeSpan.FromMilliseconds(_options.WaiterPollBudgetMs);
        while (_utcNow() < deadline)
        {
            var hit = await TryReadHitAsync(subject, catalogueToken, _utcNow(), cancellationToken).ConfigureAwait(false);
            if (hit is not null)
            {
                return hit;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(15), cancellationToken).ConfigureAwait(false);
        }

        return (await ResolveAuthoritativeAsync(subject, cancellationToken).ConfigureAwait(false)).Result;
    }

    private async Task TryCacheAuthoritativeProjectionAsync(
        AuthenticatedIdentitySubject subject,
        EffectiveLicenceEntitlement entitlement,
        string currentKey,
        string catalogueToken,
        CancellationToken cancellationToken)
    {
        try
        {
            // Never cache across a catalogue release: only the current-namespace assignment is cached.
            var current = _catalogue.Current;
            if (!string.Equals(entitlement.AssignedCatalogueVersion, current.Version, StringComparison.Ordinal) ||
                !string.Equals(entitlement.AssignedCatalogueDigestSha256, current.DigestSha256, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var now = _utcNow();
            var digest = ComputeDigest(_keyRing.Current, subject.CanonicalPublicSigningAddress);
            var ttl = LicenceCacheTtlCalculator.Compute(digest, now, entitlement.ExpiresAtUtc, _options);
            if (!ttl.HasPositiveLifetime)
            {
                return;
            }

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
                currentKey,
                bytes,
                entitlement.EntitlementRevision,
                ttl.RedisTtlSeconds,
                authKey,
                cancellationToken).ConfigureAwait(false);

            _telemetry.Count("write_" + result.Outcome.ToString().ToLowerInvariant());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Best-effort caching never changes the committed authoritative result.
            _telemetry.Count("immediate_write_error");
        }
    }

    private (string Key, byte[] Digest) CurrentKeyAndDigest(AuthenticatedIdentitySubject subject, string catalogueToken)
    {
        var digest = ComputeDigest(_keyRing.Current, subject.CanonicalPublicSigningAddress);
        var key = LicenceCacheKeyBuilder.BuildProjectionKey(
            _instancePrefix, catalogueToken, _keyRing.Current.KeyId, digest);
        return (key, digest);
    }

    private async Task MigratePreviousKeyAsync(
        string currentKey,
        string previousKey,
        CachedEntitlementEnvelope envelope,
        CancellationToken cancellationToken)
    {
        try
        {
            var migrated = new CachedEntitlementEnvelope
            {
                KeyId = _keyRing.Current.KeyId,
                CatalogueVersion = envelope.CatalogueVersion,
                CatalogueToken = envelope.CatalogueToken,
                CacheWrittenUtc = envelope.CacheWrittenUtc,
                CacheValidUntilUtc = envelope.CacheValidUntilUtc,
                PlanId = envelope.PlanId,
                PlanFamily = envelope.PlanFamily,
                UpgradeRank = envelope.UpgradeRank,
                EligibleVoterCap = envelope.EligibleVoterCap,
                UnlimitedElections = envelope.UnlimitedElections,
                TermKind = envelope.TermKind,
                TermYears = envelope.TermYears,
                AllowedGovernanceOptionIds = envelope.AllowedGovernanceOptionIds,
                ExpiresAtUtc = envelope.ExpiresAtUtc,
                EntitlementRevision = envelope.EntitlementRevision,
            };
            var bytes = _codec.SerializeCanonical(migrated);
            var authKey = LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(_keyRing.Current.SecretBytes);
            var result = await _store.WriteAsync(
                currentKey,
                bytes,
                migrated.EntitlementRevision,
                ComputeTtlSeconds(envelope),
                authKey,
                cancellationToken).ConfigureAwait(false);
            if (result.Success &&
                result.Outcome is ProjectionWriteOutcome.AcceptedNew or ProjectionWriteOutcome.AcceptedHigherRevision)
            {
                _telemetry.Count("rotation_migration");
                await _store.RemoveAsync(previousKey, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Best-effort migration failure keeps the previous-key entry readable until it expires.
        }
    }

    private long ComputeTtlSeconds(CachedEntitlementEnvelope envelope)
    {
        var remaining = envelope.CacheValidUntilUtc - _utcNow();
        return remaining > TimeSpan.Zero ? (long)Math.Floor(remaining.TotalSeconds) : 0;
    }

    private async Task SafeRemoveAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _store.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort removal; expiry bounds the damage.
        }
    }

    private async Task<(CachedEntitlementReadResult Result, EffectiveLicenceEntitlement? Entitlement)>
        ResolveAuthoritativeAsync(
            AuthenticatedIdentitySubject subject,
            CancellationToken cancellationToken,
            EntitlementCacheReadOutcome successOutcome = EntitlementCacheReadOutcome.AuthorityFallback)
    {
        LicenceResolutionResult resolution;
        try
        {
            resolution = await _authority.ResolveEffectiveEntitlementAsync(subject, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return (
                CachedEntitlementReadResult.Failure(
                    EntitlementCacheReadOutcome.AuthorityUnavailable,
                    "authority_unavailable",
                    "effective entitlement authority is unavailable"),
                null);
        }

        if (!resolution.IsSuccess || resolution.Entitlement is null)
        {
            return (
                CachedEntitlementReadResult.Failure(
                    EntitlementCacheReadOutcome.AuthorityUnavailable,
                    resolution.StableErrorCode ?? "authority_unavailable",
                    resolution.SafeErrorReason ?? "effective entitlement could not be resolved"),
                null);
        }

        var projection = CachedEntitlementProjectionMapper.FromAuthoritative(resolution.Entitlement);
        return (
            CachedEntitlementReadResult.Success(
                successOutcome,
                projection),
            resolution.Entitlement);
    }

    private static byte[] ComputeDigest(LicenceCacheMasterKey master, string canonicalAddress) =>
        LicenceCacheKeyDerivation.ComputeSubjectDigest(
            LicenceCacheKeyDerivation.DeriveSubjectKey(master.SecretBytes),
            canonicalAddress);

    private static CachedEntitlementReadResult ToHitResult(CachedEntitlementEnvelope envelope) =>
        CachedEntitlementReadResult.Success(
            EntitlementCacheReadOutcome.CacheHit,
            new CachedEntitlementProjection(
                envelope.PlanId,
                envelope.PlanFamily,
                envelope.UpgradeRank,
                envelope.EligibleVoterCap,
                envelope.UnlimitedElections,
                envelope.TermKind,
                envelope.TermYears,
                envelope.AllowedGovernanceOptionIds,
                envelope.ExpiresAtUtc,
                envelope.EntitlementRevision));
}
