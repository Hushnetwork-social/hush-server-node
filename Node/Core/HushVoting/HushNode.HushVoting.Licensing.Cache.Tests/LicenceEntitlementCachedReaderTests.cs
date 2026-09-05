using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using HushNode.HushVoting.Licensing.Storage;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>
/// Deterministic reader orchestration tests (Tasks 3.2/3.4): valid hits never call FEAT-013, misses
/// fill once under contention, failures are never cached, outage distinguishes hit from unavailable,
/// rotation migrates a previous-key hit, and the circuit bypasses Redis after repeated failure.
/// The in-memory store double mirrors the CAS/lease semantics; real Redis qualifies the Lua scripts
/// in Phase 7 TwinTests.
/// </summary>
public sealed class LicenceEntitlementCachedReaderTests
{
    private const string Address = "NXc1cYVmGJZcbzF5wY2tT9DP5cFpFT9k";
    private const string InstancePrefix = "test:";
    private static readonly DateTime FixedNow = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly LicenceCacheOptions Options = new();
    private static readonly LicenceCacheEnvelopeCodec Codec = new();

    private static readonly byte[] CurrentMaster = Enumerable.Range(0, 32).Select(i => (byte)(i + 1)).ToArray();
    private static readonly byte[] PreviousMaster = Enumerable.Range(0, 32).Select(i => (byte)(i + 90)).ToArray();

    private static readonly LicenceCacheMasterKey CurrentKey = LicenceCacheMasterKey.Create(
        "v2", CurrentMaster, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), Options, out _);
    private static readonly LicenceCacheMasterKey PreviousKey = LicenceCacheMasterKey.Create(
        "v1", PreviousMaster, new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), Options, out _);

    private static readonly ICurrentLicenceCatalogueProvider Catalogue = new FixedCatalogue(
        "hushvoting-licence-catalogue/v1.0.0", "AB".PadLeft(64, '0'));

    private sealed class FixedCatalogue(string version, string digest) : ICurrentLicenceCatalogueProvider
    {
        public (string Version, string DigestSha256) Current => (version, digest);
    }

    private sealed class FakeAuthority : IEntitlementAuthorityResolver
    {
        private readonly Queue<LicenceResolutionResult> _results = new();
        public int CallCount { get; private set; }

        public void Enqueue(LicenceResolutionResult result) => _results.Enqueue(result);

        public Task<LicenceResolutionResult> ResolveEffectiveEntitlementAsync(
            AuthenticatedIdentitySubject subject,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : ResolutionOk());
        }
    }

    private static AuthenticatedIdentitySubject Subject() =>
        AuthenticatedIdentitySubject.TryCreate("Identity", Address, 7, out var subject, out _)
            ? subject!
            : throw new InvalidOperationException("subject failed");

    private static EffectiveLicenceEntitlement Entitlement(long revision, DateTime? expiresAtUtc = null) =>
        new(
            LicenceSubjectId: Guid.NewGuid(),
            LicenceAssignmentId: Guid.NewGuid(),
            PlanId: "hushvoting.veritas.500",
            PlanFamily: "Veritas",
            UpgradeRank: 2,
            EligibleVoterCap: 500,
            UnlimitedElectionPolicy: false,
            TermKind: "annual",
            TermYears: 1,
            AllowedGovernanceOptionIds: new[] { "hushvoting.governance.standard" },
            Source: "automatic_upgrade",
            EffectiveFromUtc: FixedNow.AddMonths(-1),
            ExpiresAtUtc: expiresAtUtc,
            AssignedCatalogueVersion: "hushvoting-licence-catalogue/v1.0.0",
            AssignedCatalogueDigestSha256: "AB".PadLeft(64, '0'),
            EntitlementRevision: revision);

    private static LicenceResolutionResult ResolutionOk(long revision = 1, DateTime? expiresAtUtc = null) =>
        LicenceResolutionResult.Ok(LicenceResolutionOutcome.ResolvedExisting, Entitlement(revision, expiresAtUtc));

    private sealed class Harness : IDisposable
    {
        public InMemoryEntitlementStore Store { get; } = new();
        public FakeAuthority Authority { get; } = new();
        public LicenceCacheCircuitBreaker Circuit { get; }
        public LicenceCacheSingleFlight SingleFlight { get; } = new();
        public LicenceCacheTelemetry Telemetry { get; } = new();
        public DateTime CurrentTime { get; set; } = FixedNow;

        private readonly LicenceEntitlementCachedReader _reader;
        private readonly LicenceCacheValueValidator _validator = new(new LicenceCacheEnvelopeCodec(), Options);

        public Harness(LicenceCacheKeyRing ring, bool enabled = true)
        {
            var options = enabled ? Options : new LicenceCacheOptions { Enabled = false };
            Circuit = new LicenceCacheCircuitBreaker(() => CurrentTime, options);
            _reader = new LicenceEntitlementCachedReader(
                Store,
                _validator,
                new LicenceCacheEnvelopeCodec(),
                options,
                ring,
                Authority,
                Catalogue,
                Circuit,
                SingleFlight,
                Telemetry,
                () => CurrentTime,
                InstancePrefix);
        }

        private static string CanonicalAddress() => Subject().CanonicalPublicSigningAddress;

        public string CurrentKey()
        {
            var digest = LicenceCacheKeyDerivation.ComputeSubjectDigest(
                LicenceCacheKeyDerivation.DeriveSubjectKey(CurrentMaster), CanonicalAddress());
            var token = LicenceCacheKeyBuilder.BuildCatalogueToken(
                "hushvoting-licence-catalogue/v1.0.0", "AB".PadLeft(64, '0'));
            return LicenceCacheKeyBuilder.BuildProjectionKey(InstancePrefix, token, "v2", digest);
        }

        public string PreviousKey()
        {
            var digest = LicenceCacheKeyDerivation.ComputeSubjectDigest(
                LicenceCacheKeyDerivation.DeriveSubjectKey(PreviousMaster), CanonicalAddress());
            var token = LicenceCacheKeyBuilder.BuildCatalogueToken(
                "hushvoting-licence-catalogue/v1.0.0", "AB".PadLeft(64, '0'));
            return LicenceCacheKeyBuilder.BuildProjectionKey(InstancePrefix, token, "v1", digest);
        }

        public void SeedCurrent(EffectiveLicenceEntitlement entitlement)
        {
            var envelope = BuildEnvelope(entitlement, CurrentKey(), LicenceCacheKeyDerivation.DeriveSubjectKey(CurrentMaster));
            Store.Seed(CurrentKey(), envelope, LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(CurrentMaster));
        }

        public void SeedPrevious(EffectiveLicenceEntitlement entitlement)
        {
            var key = PreviousKey();
            var envelope = BuildEnvelope(entitlement, key, LicenceCacheKeyDerivation.DeriveSubjectKey(PreviousMaster));
            Store.Seed(key, envelope, LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(PreviousMaster));
        }

        public Task<CachedEntitlementReadResult> ReadAsync() =>
            _reader.GetEffectiveEntitlementAsync(Subject(), CancellationToken.None);

        public void Dispose()
        {
        }

        private static byte[] BuildEnvelope(EffectiveLicenceEntitlement e, string key, byte[] subjectKey)
        {
            var envelope = new CachedEntitlementEnvelope
            {
                KeyId = key.Contains(":v2:", StringComparison.Ordinal) ? "v2" : "v1",
                CatalogueVersion = e.AssignedCatalogueVersion,
                CatalogueToken = LicenceCacheKeyBuilder.BuildCatalogueToken(
                    e.AssignedCatalogueVersion, e.AssignedCatalogueDigestSha256),
                CacheWrittenUtc = FixedNow.AddDays(-1),
                CacheValidUntilUtc = FixedNow.AddDays(6),
                PlanId = e.PlanId,
                PlanFamily = e.PlanFamily,
                UpgradeRank = e.UpgradeRank,
                EligibleVoterCap = e.EligibleVoterCap,
                UnlimitedElections = e.UnlimitedElectionPolicy,
                TermKind = e.TermKind,
                TermYears = e.TermYears,
                AllowedGovernanceOptionIds = e.AllowedGovernanceOptionIds,
                ExpiresAtUtc = e.ExpiresAtUtc,
                EntitlementRevision = e.EntitlementRevision,
            };
            return new LicenceCacheEnvelopeCodec().SerializeCanonical(envelope);
        }
    }

    [Fact]
    public async Task Valid_hit_returns_projection_without_authority_call()
    {
        using var harness = new Harness(LicenceCacheKeyRing.TryCreate(CurrentKey, null, Options, out _)!);
        harness.SeedCurrent(Entitlement(5));
        harness.Authority.Enqueue(ResolutionOk(99)); // must never be consumed

        var result = await harness.ReadAsync();

        result.IsSuccess.Should().BeTrue();
        result.Outcome.Should().Be(EntitlementCacheReadOutcome.CacheHit);
        result.Projection!.EntitlementRevision.Should().Be(5);
        result.Projection.PlanId.Should().Be("hushvoting.veritas.500");
        harness.Authority.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_misses_fill_once_from_authority()
    {
        using var harness = new Harness(LicenceCacheKeyRing.TryCreate(CurrentKey, null, Options, out _)!);

        var reads = Enumerable.Range(0, 20).Select(_ => harness.ReadAsync()).ToArray();
        var results = await Task.WhenAll(reads);

        results.Should().OnlyContain(r => r.IsSuccess);
        results.Should().OnlyContain(r => r.Projection!.EntitlementRevision == 1);
        // Local single flight + lease coalescing: exactly one authority resolution.
        harness.Authority.CallCount.Should().Be(1);
        harness.Store.Values.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Authority_failure_is_never_cached()
    {
        using var harness = new Harness(LicenceCacheKeyRing.TryCreate(CurrentKey, null, Options, out _)!);
        harness.Authority.Enqueue(LicenceResolutionResult.Fail("storage_unavailable", "postgres unavailable"));

        var result = await harness.ReadAsync();

        result.IsSuccess.Should().BeFalse();
        result.Outcome.Should().Be(EntitlementCacheReadOutcome.AuthorityUnavailable);
        result.StableErrorCode.Should().Be("storage_unavailable");
        harness.Store.Values.Should().BeEmpty(); // no negative caching
    }

    [Fact]
    public async Task Postgresql_outage_distinguishes_hit_from_unavailable()
    {
        using var harness = new Harness(LicenceCacheKeyRing.TryCreate(CurrentKey, null, Options, out _)!);
        harness.SeedCurrent(Entitlement(8, FixedNow.AddYears(1)));
        // Authority is unavailable; the valid cached projection still serves display.
        harness.Authority.Enqueue(LicenceResolutionResult.Fail("storage_unavailable", "down"));

        var hit = await harness.ReadAsync();
        hit.IsSuccess.Should().BeTrue();
        hit.Outcome.Should().Be(EntitlementCacheReadOutcome.CacheHit);
        hit.Projection!.EntitlementRevision.Should().Be(8);
        harness.Authority.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Cache_miss_during_outage_returns_unavailable_and_caches_nothing()
    {
        using var harness = new Harness(LicenceCacheKeyRing.TryCreate(CurrentKey, null, Options, out _)!);
        harness.Authority.Enqueue(LicenceResolutionResult.Fail("storage_unavailable", "down"));

        var result = await harness.ReadAsync();

        result.IsSuccess.Should().BeFalse();
        result.Outcome.Should().Be(EntitlementCacheReadOutcome.AuthorityUnavailable);
        harness.Store.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task Previous_key_hit_migrates_to_current_key()
    {
        using var harness = new Harness(LicenceCacheKeyRing.TryCreate(CurrentKey, PreviousKey, Options, out _)!);
        harness.SeedPrevious(Entitlement(3));

        var result = await harness.ReadAsync();

        result.IsSuccess.Should().BeTrue();
        result.Outcome.Should().Be(EntitlementCacheReadOutcome.CacheHit);
        result.Projection!.EntitlementRevision.Should().Be(3);
        harness.Authority.CallCount.Should().Be(0);

        var currentAuth = LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(CurrentMaster);
        harness.Store.TryGetEnvelope(harness.CurrentKey(), currentAuth, out var envelope).Should().BeTrue();
        envelope!.KeyId.Should().Be("v2");
    }

    [Fact]
    public async Task Repeated_connection_failure_opens_circuit_and_bypasses_redis()
    {
        using var harness = new Harness(LicenceCacheKeyRing.TryCreate(CurrentKey, null, Options, out _)!);
        harness.Authority.Enqueue(ResolutionOk(1));
        harness.Authority.Enqueue(ResolutionOk(2));
        harness.Authority.Enqueue(ResolutionOk(3));
        harness.Authority.Enqueue(ResolutionOk(4));

        // Three consecutive Redis failures open the circuit.
        harness.Circuit.RecordConnectionFailure();
        harness.Circuit.RecordConnectionFailure();
        harness.Circuit.RecordConnectionFailure();

        harness.Circuit.State.Should().Be(LicenceCacheCircuitBreaker.CircuitState.Open);

        var readsBefore = harness.Store.ReadCalls;
        var result = await harness.ReadAsync();
        // While open, the read bypasses Redis and resolves from authority.
        harness.Store.ReadCalls.Should().Be(readsBefore);
        result.IsSuccess.Should().BeTrue();
        result.Outcome.Should().Be(EntitlementCacheReadOutcome.AuthorityFallback);
    }
}
