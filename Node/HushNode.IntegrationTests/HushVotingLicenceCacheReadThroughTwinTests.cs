using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-014 Phase 7 Task 7.2 real-Redis TwinTests: valid hits never call FEAT-013 authority, misses
/// fall back to authoritative provisioning and cache only the successful projection, and authority
/// (PostgreSQL) outage differentiates a valid display hit from an unavailable miss. The injected
/// authority resolver is the exact count boundary for FEAT-013 entitlement reads (authoritative
/// reads in the production host all pass through this resolver seam).
/// </summary>
[Collection("FEAT-014 Redis+PostgreSQL")]
[Trait("Category", "FEAT-014")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceCacheReadThroughTwinTests
{
    private const string CatalogueVersion = "hushvoting-licence-catalogue/v1.0.0";
    private static readonly string CatalogueDigest = new('A', 64);
    private static readonly DateTime NowUtc = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly LicenceCacheOptions Options = new();
    private static readonly LicenceCacheEnvelopeCodec Codec = new();
    private static readonly LicenceCacheKeyRing KeyRing = LicenceCacheKeyRing.TryCreate(
        LicenceCacheMasterKey.Create("v2", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), NowUtc, Options, out _),
        LicenceCacheMasterKey.Create("v1", Enumerable.Range(90, 32).Select(i => (byte)i).ToArray(), new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc), Options, out _),
        Options,
        out _)!;

    private readonly LicenceCacheRedisPostgresFixture _fixture;

    public HushVotingLicenceCacheReadThroughTwinTests(LicenceCacheRedisPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Token() => LicenceCacheKeyBuilder.BuildCatalogueToken(CatalogueVersion, CatalogueDigest);

    private sealed class StubCatalogue : ICurrentLicenceCatalogueProvider
    {
        public (string Version, string DigestSha256) Current => (CatalogueVersion, CatalogueDigest);
    }

    private sealed class CountingAuthority : IEntitlementAuthorityResolver
    {
        private int _calls;
        private readonly LicenceResolutionResult _result;
        private readonly bool _throw;

        public CountingAuthority(LicenceResolutionResult result, bool @throw = false)
        {
            _result = result;
            _throw = @throw;
        }

        public int Calls => _calls;

        public Task<LicenceResolutionResult> ResolveEffectiveEntitlementAsync(
            AuthenticatedIdentitySubject subject,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (_throw)
            {
                throw new InvalidOperationException("postgres_unavailable");
            }

            return Task.FromResult(_result);
        }
    }

    private static AuthenticatedIdentitySubject Subject(string address) =>
        AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity, address, 7, out var s, out _) ? s! : throw new InvalidOperationException();

    private static EffectiveLicenceEntitlement DirectFree(Guid subjectId) =>
        new(
            subjectId,
            Guid.NewGuid(),
            "hushvoting.directfree",
            "Direct Free",
            0,
            null,
            true,
            "indefinite",
            1,
            Array.Empty<string>(),
            "direct-free-provisioning",
            NowUtc,
            null,
            CatalogueVersion,
            CatalogueDigest,
            1);

    private static EffectiveLicenceEntitlement StandardPlan(Guid subjectId) =>
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
            NowUtc,
            NowUtc.AddYears(1),
            CatalogueVersion,
            CatalogueDigest,
            9);

    private LicenceEntitlementCachedReader NewReader(ICurrentLicenceCatalogueProvider catalogue, IEntitlementAuthorityResolver authority)
    {
        var store = new RedisEntitlementProjectionStore(_fixture.RedisDatabase, Options, Codec);
        return new LicenceEntitlementCachedReader(
            store,
            new LicenceCacheValueValidator(Codec, Options),
            Codec,
            Options,
            KeyRing,
            authority,
            catalogue,
            new LicenceCacheCircuitBreaker(() => NowUtc, Options),
            new LicenceCacheSingleFlight(),
            new LicenceCacheTelemetry(),
            () => NowUtc,
            _fixture.InstancePrefix);
    }

    [Fact]
    public async Task One_thousand_valid_hits_perform_zero_authority_reads()
    {
        await _fixture.FlushRedisAsync();
        var subjectId = Guid.NewGuid();
        var subject = Subject("NXc1cYVmGJZcbzF5wY2tT9DP5cFpFT9k");
        var authority = new CountingAuthority(LicenceResolutionResult.Ok(LicenceResolutionOutcome.ResolvedExisting, StandardPlan(subjectId)));
        var catalogue = new StubCatalogue();
        var reader = NewReader(catalogue, authority);

        // Seed one valid authenticated projection directly through the real Redis store (revision 9).
        var token = Token();
        var digest = LicenceCacheKeyDerivation.ComputeSubjectDigest(
            LicenceCacheKeyDerivation.DeriveSubjectKey(KeyRing.Current.SecretBytes), subject.CanonicalPublicSigningAddress);
        var key = LicenceCacheKeyBuilder.BuildProjectionKey(_fixture.InstancePrefix, token, KeyRing.Current.KeyId, digest);
        var envelope = new CachedEntitlementEnvelope
        {
            KeyId = KeyRing.Current.KeyId,
            CatalogueVersion = CatalogueVersion,
            CatalogueToken = token,
            CacheWrittenUtc = NowUtc,
            CacheValidUntilUtc = NowUtc.AddDays(7),
            PlanId = "hushvoting.standard",
            PlanFamily = "Standard",
            UpgradeRank = 1,
            EligibleVoterCap = null,
            UnlimitedElections = false,
            TermKind = "annual",
            TermYears = 1,
            AllowedGovernanceOptionIds = new[] { "proposal", "vote" },
            ExpiresAtUtc = NowUtc.AddYears(1),
            EntitlementRevision = 9,
        };
        var bytes = Codec.SerializeCanonical(envelope);
        var authKey = LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(KeyRing.Current.SecretBytes);
        var tag = Codec.ComputeAuthenticationTag(key, bytes, authKey);
        var write = await new RedisEntitlementProjectionStore(_fixture.RedisDatabase, Options, Codec)
            .WriteAsync(key, bytes, 9, 3600, authKey, CancellationToken.None);
        write.Outcome.Should().Be(ProjectionWriteOutcome.AcceptedNew);

        var tasks = Enumerable.Range(0, 1000)
            .Select(_ => reader.GetEffectiveEntitlementAsync(subject, CancellationToken.None))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.IsSuccess && r.Outcome == EntitlementCacheReadOutcome.CacheHit);
        results.Select(r => r.Projection!.PlanId).Distinct().Should().Equal("hushvoting.standard");
        results.Select(r => r.Projection!.EntitlementRevision).Distinct().Should().Equal(9L);
        authority.Calls.Should().Be(0, "a valid hit never performs a FEAT-013 entitlement read");
    }

    [Fact]
    public async Task Absence_is_filled_by_authority_and_never_negatively_cached()
    {
        await _fixture.FlushRedisAsync();
        var subjectId = Guid.NewGuid();
        var subject = Subject("NXc1cYVmGJZcbzF5wY2tT9DP5cFpFT9k");
        var authority = new CountingAuthority(LicenceResolutionResult.Ok(LicenceResolutionOutcome.ProvisionedDefault, DirectFree(subjectId)));
        var catalogue = new StubCatalogue();
        var reader = NewReader(catalogue, authority);

        var first = await reader.GetEffectiveEntitlementAsync(subject, CancellationToken.None);
        first.IsSuccess.Should().BeTrue();
        first.Outcome.Should().Be(EntitlementCacheReadOutcome.AuthorityFallback);
        first.Projection!.PlanId.Should().Be("hushvoting.directfree");
        authority.Calls.Should().Be(1);

        // Second read is a hit; only the successful projection was cached (no negative cache).
        var second = await reader.GetEffectiveEntitlementAsync(subject, CancellationToken.None);
        second.IsSuccess.Should().BeTrue();
        second.Outcome.Should().Be(EntitlementCacheReadOutcome.CacheHit);
        authority.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Postgres_outage_serves_only_existing_hits_and_reports_unavailable_for_misses()
    {
        await _fixture.FlushRedisAsync();
        var subjectId = Guid.NewGuid();
        var cachedSubject = Subject("NXc1cYVmGJZcbzF5wY2tT9DP5cFpFT9k");
        var coldSubject = Subject("PYq2dZQoVhAKiU5fLkDxRwvjS9yDkM1Q");
        var authority = new CountingAuthority(LicenceResolutionResult.Ok(LicenceResolutionOutcome.ResolvedExisting, StandardPlan(subjectId)));
        var catalogue = new StubCatalogue();

        // Warm the cache while authority is healthy.
        var warmReader = NewReader(catalogue, authority);
        var warm = await warmReader.GetEffectiveEntitlementAsync(cachedSubject, CancellationToken.None);
        warm.Outcome.Should().Be(EntitlementCacheReadOutcome.AuthorityFallback);

        // Authority goes away (PostgreSQL outage).
        var downAuthority = new CountingAuthority(LicenceResolutionResult.Fail("authority_unavailable", "postgres_unavailable"));
        var reader = NewReader(catalogue, downAuthority);

        var hit = await reader.GetEffectiveEntitlementAsync(cachedSubject, CancellationToken.None);
        hit.IsSuccess.Should().BeTrue();
        hit.Outcome.Should().Be(EntitlementCacheReadOutcome.CacheHit);
        hit.Projection!.PlanId.Should().Be("hushvoting.standard");

        var miss = await reader.GetEffectiveEntitlementAsync(coldSubject, CancellationToken.None);
        miss.IsSuccess.Should().BeFalse();
        miss.Outcome.Should().Be(EntitlementCacheReadOutcome.AuthorityUnavailable);
        miss.Projection.Should().BeNull();
        miss.StableErrorCode.Should().Be("authority_unavailable");

        // Nothing was invented or cached for the cold subject.
        var digest = LicenceCacheKeyDerivation.ComputeSubjectDigest(
            LicenceCacheKeyDerivation.DeriveSubjectKey(KeyRing.Current.SecretBytes), coldSubject.CanonicalPublicSigningAddress);
        var coldKey = LicenceCacheKeyBuilder.BuildProjectionKey(
            _fixture.InstancePrefix, Token(), KeyRing.Current.KeyId, digest);
        var read = await new RedisEntitlementProjectionStore(_fixture.RedisDatabase, Options, Codec)
            .ReadCurrentAsync(coldKey, CancellationToken.None);
        read.Found.Should().BeFalse();
    }
}
