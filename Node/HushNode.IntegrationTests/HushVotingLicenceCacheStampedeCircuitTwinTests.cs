using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using CircuitState = HushNode.HushVoting.Licensing.Cache.LicenceCacheCircuitBreaker.CircuitState;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-014 Phase 7 Task 7.4 real-Redis TwinTests: same-subject stampede coalescing (FEAT-013 loads
/// exactly once across two cache-service instances), distinct-subject independence (no cross-blocking
/// or global lock), and the connection circuit (three failures open, reads bypass Redis, a single
/// half-open probe recovers without restart). Data corruption is never counted as a connection
/// failure. No assertion depends on a wall-clock SLO; counts are exact.
/// </summary>
[Collection("FEAT-014 Redis+PostgreSQL")]
[Trait("Category", "FEAT-014")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceCacheStampedeCircuitTwinTests
{
    private const string CatalogueVersion = "hushvoting-licence-catalogue/v1.0.0";
    private static readonly string CatalogueDigest = new('A', 64);
    private static readonly DateTime BaseUtc = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly LicenceCacheOptions Options = new();
    private static readonly LicenceCacheEnvelopeCodec Codec = new();
    private static readonly LicenceCacheKeyRing KeyRing = LicenceCacheKeyRing.TryCreate(
        LicenceCacheMasterKey.Create("v2", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), BaseUtc, Options, out _),
        LicenceCacheMasterKey.Create("v1", Enumerable.Range(90, 32).Select(i => (byte)i).ToArray(), new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc), Options, out _),
        Options,
        out _)!;

    private readonly LicenceCacheRedisPostgresFixture _fixture;

    public HushVotingLicenceCacheStampedeCircuitTwinTests(LicenceCacheRedisPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class MutableClock
    {
        public DateTime UtcNow = BaseUtc;
    }

    private sealed class StubCatalogue : ICurrentLicenceCatalogueProvider
    {
        public (string Version, string DigestSha256) Current => (CatalogueVersion, CatalogueDigest);
    }

    private static AuthenticatedIdentitySubject Subject(int n) =>
        AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity,
            // Distinct valid-format addresses per subject index.
            "NXc1cYVmGJZcbzF5wY2tT9DP5cFpFT9k".Substring(0, 24) + n.ToString("D4"),
            7,
            out var s,
            out _) ? s! : throw new InvalidOperationException();

    private static readonly AuthenticatedIdentitySubject SharedSubject = Subject(0);

    private sealed class CountingAuthority : IEntitlementAuthorityResolver
    {
        private readonly int _delayMs;
        private readonly Guid _subjectId;
        private int _calls;

        public CountingAuthority(Guid subjectId, int delayMs = 0)
        {
            _subjectId = subjectId;
            _delayMs = delayMs;
        }

        public int Calls => _calls;

        public async Task<LicenceResolutionResult> ResolveEffectiveEntitlementAsync(
            AuthenticatedIdentitySubject subject,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (_delayMs > 0)
            {
                await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
            }

            return LicenceResolutionResult.Ok(LicenceResolutionOutcome.ResolvedExisting, Entitlement(_subjectId));
        }
    }

    private static EffectiveLicenceEntitlement Entitlement(Guid subjectId) =>
        new(
            subjectId,
            Guid.NewGuid(),
            "hushvoting.standard",
            "Standard",
            1,
            null,
            false,
            "annual",
            1,
            new[] { "proposal", "vote" },
            "licence-assignment",
            BaseUtc,
            BaseUtc.AddYears(1),
            CatalogueVersion,
            CatalogueDigest,
            9);

    /// <summary>Fault-injecting facade over the real Redis store (used to arm connection failures).</summary>
    private sealed class FaultingStore : IEntitlementProjectionStore
    {
        private readonly IEntitlementProjectionStore _inner;
        public bool FailAll;

        public FaultingStore(IEntitlementProjectionStore inner)
        {
            _inner = inner;
        }

        private void ThrowIfArmed()
        {
            if (FailAll)
            {
                throw new TimeoutException("simulated_redis_connection_failure");
            }
        }

        public Task<StoreReadResult> ReadCurrentAsync(string fullKey, CancellationToken cancellationToken)
        {
            ThrowIfArmed();
            return _inner.ReadCurrentAsync(fullKey, cancellationToken);
        }

        public Task<StoreWriteResult> WriteAsync(
            string fullKey,
            byte[] canonicalEnvelopeBytes,
            long entitlementRevision,
            long redisTtlSeconds,
            byte[] valueAuthenticationKey,
            CancellationToken cancellationToken)
        {
            ThrowIfArmed();
            return _inner.WriteAsync(fullKey, canonicalEnvelopeBytes, entitlementRevision, redisTtlSeconds, valueAuthenticationKey, cancellationToken);
        }

        public Task<StoreWriteResult> RemoveAsync(string fullKey, CancellationToken cancellationToken)
        {
            ThrowIfArmed();
            return _inner.RemoveAsync(fullKey, cancellationToken);
        }

        public Task<bool> TryAcquireLeaseAsync(string leaseKey, string ownershipToken, int leaseSeconds, CancellationToken cancellationToken)
        {
            ThrowIfArmed();
            return _inner.TryAcquireLeaseAsync(leaseKey, ownershipToken, leaseSeconds, cancellationToken);
        }

        public Task<bool> TryReleaseLeaseAsync(string leaseKey, string ownershipToken, CancellationToken cancellationToken)
        {
            ThrowIfArmed();
            return _inner.TryReleaseLeaseAsync(leaseKey, ownershipToken, cancellationToken);
        }
    }

    private (LicenceEntitlementCachedReader Reader, LicenceCacheTelemetry Telemetry, LicenceCacheCircuitBreaker Circuit) NewReader(
        IEntitlementProjectionStore store,
        IEntitlementAuthorityResolver authority,
        MutableClock clock)
    {
        var telemetry = new LicenceCacheTelemetry();
        var circuit = new LicenceCacheCircuitBreaker(() => clock.UtcNow, Options);
        var reader = new LicenceEntitlementCachedReader(
            store,
            new LicenceCacheValueValidator(Codec, Options),
            Codec,
            Options,
            KeyRing,
            authority,
            new StubCatalogue(),
            circuit,
            new LicenceCacheSingleFlight(),
            telemetry,
            () => clock.UtcNow,
            _fixture.InstancePrefix);
        return (reader, telemetry, circuit);
    }

    private RedisEntitlementProjectionStore RealStore() =>
        new(_fixture.RedisDatabase, Options, Codec);

    [Fact]
    public async Task One_hundred_same_subject_misses_load_authority_exactly_once()
    {
        await _fixture.FlushRedisAsync();
        var subjectId = Guid.NewGuid();
        var clock = new MutableClock();
        // Authority is deliberately slower than one poll interval so waiters poll instead of falling
        // back, but far inside the 750 ms waiter budget so no waiter ever calls authority.
        var authority = new CountingAuthority(subjectId, delayMs: 200);
        var (instanceA, _, _) = NewReader(RealStore(), authority, clock);
        var (instanceB, _, _) = NewReader(RealStore(), authority, clock);

        var calls = Enumerable.Range(0, 100)
            .Select(i => (i % 2 == 0 ? instanceA : instanceB).GetEffectiveEntitlementAsync(SharedSubject, CancellationToken.None))
            .ToArray();
        var results = await Task.WhenAll(calls);

        results.Should().OnlyContain(r => r.IsSuccess);
        results.Select(r => r.Projection!.EntitlementRevision).Distinct().Should().Equal(9L);
        results.Select(r => r.Outcome).Distinct().Should().BeEquivalentTo(
            new[] { EntitlementCacheReadOutcome.CacheHit, EntitlementCacheReadOutcome.AuthorityFallback });
        authority.Calls.Should().Be(1, "one hundred same-subject misses must load FEAT-013 exactly once");
    }

    [Fact]
    public async Task One_hundred_distinct_subjects_progress_independently_without_cross_blocking()
    {
        await _fixture.FlushRedisAsync();
        var clock = new MutableClock();
        var tasks = Enumerable.Range(1, 100)
            .Select(n =>
            {
                var subjectId = Guid.NewGuid();
                var authority = new CountingAuthority(subjectId);
                var (reader, _, _) = NewReader(RealStore(), authority, clock);
                return reader.GetEffectiveEntitlementAsync(Subject(n), CancellationToken.None)
                    .ContinueWith(t => (SubjectN: n, Result: t.Result), CancellationToken.None);
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r.Result.IsSuccess);
        results.Should().OnlyContain(r => r.Result.Projection!.PlanId == "hushvoting.standard");
        results.Length.Should().Be(100);
    }

    [Fact]
    public async Task Circuit_opens_bypasses_reopens_on_success_and_never_counts_corruption()
    {
        await _fixture.FlushRedisAsync();
        var subjectId = Guid.NewGuid();
        var clock = new MutableClock();
        var authority = new CountingAuthority(subjectId);
        var faulting = new FaultingStore(RealStore()) { FailAll = true };
        var (reader, telemetry, circuit) = NewReader(faulting, authority, clock);

        // Three consecutive connection failures open the circuit.
        for (var i = 0; i < 3; i++)
        {
            var r = await reader.GetEffectiveEntitlementAsync(SharedSubject, CancellationToken.None);
            r.IsSuccess.Should().BeTrue("the reader degrades to FEAT-013 authority during Redis failure");
        }

        circuit.State.Should().Be(CircuitState.Open, "telemetry: {0}", string.Join(",", telemetry.Snapshot().Select(kv => $"{kv.Key}={kv.Value}")));
        telemetry.Get("redis_read_error").Should().Be(3);

        // While open, reads bypass Redis and still resolve authoritatively.
        var bypassed = await reader.GetEffectiveEntitlementAsync(SharedSubject, CancellationToken.None);
        bypassed.IsSuccess.Should().BeTrue();
        telemetry.Get("circuit_open_bypass").Should().Be(1);

        // Deterministic time advances past the interval: exactly one half-open probe is permitted.
        clock.UtcNow = BaseUtc.AddSeconds(31);
        faulting.FailAll = false;

        var probe = await reader.GetEffectiveEntitlementAsync(SharedSubject, CancellationToken.None);
        probe.IsSuccess.Should().BeTrue();
        circuit.State.Should().Be(CircuitState.Closed, "a successful half-open probe closes the circuit");

        // The projection written during the probe serves ordinary reads as hits.
        var hit = await reader.GetEffectiveEntitlementAsync(SharedSubject, CancellationToken.None);
        hit.IsSuccess.Should().BeTrue();
        hit.Outcome.Should().Be(EntitlementCacheReadOutcome.CacheHit);
        telemetry.Get("circuit_open_bypass").Should().Be(1, "the circuit is closed after recovery");

        // Corruption is not a connection failure: store a malformed value and confirm the circuit
        // stays closed and the read degrades to authority (no redis_read_error counter movement).
        await _fixture.FlushRedisAsync();
        var digest = LicenceCacheKeyDerivation.ComputeSubjectDigest(
            LicenceCacheKeyDerivation.DeriveSubjectKey(KeyRing.Current.SecretBytes), SharedSubject.CanonicalPublicSigningAddress);
        var key = LicenceCacheKeyBuilder.BuildProjectionKey(
            _fixture.InstancePrefix,
            LicenceCacheKeyBuilder.BuildCatalogueToken(CatalogueVersion, CatalogueDigest),
            KeyRing.Current.KeyId,
            digest);
        await _fixture.RedisDatabase.StringSetAsync(key, "not-an-envelope");

        var afterCorruption = await reader.GetEffectiveEntitlementAsync(SharedSubject, CancellationToken.None);
        afterCorruption.IsSuccess.Should().BeTrue();
        afterCorruption.Outcome.Should().Be(EntitlementCacheReadOutcome.AuthorityFallback,
            "a corrupt value is a complete miss filled authoritatively");
        telemetry.Get("redis_read_error").Should().Be(3, "a corrupt value is a miss, never a connection failure");
        telemetry.Snapshot().Any(kv => kv.Key.StartsWith("corrupt_miss_") && kv.Value > 0).Should().BeTrue(
            "a bounded corrupt-miss label is recorded, never the raw value");
        circuit.State.Should().Be(CircuitState.Closed);
    }
}
