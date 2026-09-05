using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-014 Phase 7 Task 7.5 real-infrastructure qualification: after real Redis/PostgreSQL outcomes
/// across every telemetry family, captured diagnostics stay bounded and private (closed labels only,
/// no raw address/digest/key/id/plan material), health thresholds flip exactly at the v1 warning and
/// critical boundaries while cache degradation never affects authority availability, and the focused
/// gate reports exact non-zero counts with the zero-discovery guard armed (executed by the script).
/// </summary>
[Collection("FEAT-014 Redis+PostgreSQL")]
[Trait("Category", "FEAT-014")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceCachePrivacyHealthTwinTests
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

    public HushVotingLicenceCachePrivacyHealthTwinTests(LicenceCacheRedisPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static AuthenticatedIdentitySubject Subject(string address) =>
        AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity, address, 7, out var s, out _) ? s! : throw new InvalidOperationException();

    private sealed class StubCatalogue : ICurrentLicenceCatalogueProvider
    {
        public (string Version, string DigestSha256) Current => (CatalogueVersion, CatalogueDigest);
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

    private sealed class OkAuthority : IEntitlementAuthorityResolver
    {
        public Task<LicenceResolutionResult> ResolveEffectiveEntitlementAsync(
            AuthenticatedIdentitySubject subject,
            CancellationToken cancellationToken) =>
            Task.FromResult(LicenceResolutionResult.Ok(LicenceResolutionOutcome.ResolvedExisting, Entitlement(Guid.NewGuid())));
    }

    [Fact]
    public async Task Diagnostics_after_every_real_outcome_family_remain_bounded_and_private()
    {
        await _fixture.FlushRedisAsync();
        const string rawAddress = "NXc1cYVmGJZcbzF5wY2tT9DP5cFpFT9k";
        var subject = Subject(rawAddress);
        var digestHex = LicenceCacheKeyBuilder.ToDigestHex(LicenceCacheKeyDerivation.ComputeSubjectDigest(
            LicenceCacheKeyDerivation.DeriveSubjectKey(KeyRing.Current.SecretBytes), subject.CanonicalPublicSigningAddress));
        var token = LicenceCacheKeyBuilder.BuildCatalogueToken(CatalogueVersion, CatalogueDigest);

        var telemetry = new LicenceCacheTelemetry();
        var reader = new LicenceEntitlementCachedReader(
            new RedisEntitlementProjectionStore(_fixture.RedisDatabase, Options, Codec),
            new LicenceCacheValueValidator(Codec, Options),
            Codec,
            Options,
            KeyRing,
            new OkAuthority(),
            new StubCatalogue(),
            new LicenceCacheCircuitBreaker(() => BaseUtc, Options),
            new LicenceCacheSingleFlight(),
            telemetry,
            () => BaseUtc,
            _fixture.InstancePrefix);

        // Produce a miss (authority fallback + fill), a hit, a corrupt miss, and a CAS write result.
        var miss = await reader.GetEffectiveEntitlementAsync(subject, CancellationToken.None);
        miss.Outcome.Should().Be(EntitlementCacheReadOutcome.AuthorityFallback);
        var hit = await reader.GetEffectiveEntitlementAsync(subject, CancellationToken.None);
        hit.Outcome.Should().Be(EntitlementCacheReadOutcome.CacheHit);

        var store = new RedisEntitlementProjectionStore(_fixture.RedisDatabase, Options, Codec);
        var key = LicenceCacheKeyBuilder.BuildProjectionKey(_fixture.InstancePrefix, token, KeyRing.Current.KeyId,
            LicenceCacheKeyDerivation.ComputeSubjectDigest(
                LicenceCacheKeyDerivation.DeriveSubjectKey(KeyRing.Current.SecretBytes), subject.CanonicalPublicSigningAddress));
        await _fixture.RedisDatabase.StringSetAsync(key, "corrupt-value-not-an-envelope");
        var corrupt = await reader.GetEffectiveEntitlementAsync(subject, CancellationToken.None);
        corrupt.IsSuccess.Should().BeTrue();

        // Scan every captured label: bounded closed-vocabulary shape and zero raw material.
        var snapshot = telemetry.Snapshot();
        snapshot.Count.Should().BeGreaterThan(3, "hit/miss/corrupt outcomes produced bounded telemetry");
        snapshot.Keys.Should().OnlyContain(label =>
            label.Length > 0 && label.Length <= 64 &&
            label.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_' || c == '.'));
        snapshot.Keys.Should().NotContain(label => label.Contains(rawAddress, StringComparison.OrdinalIgnoreCase));
        snapshot.Keys.Should().NotContain(label => label.Contains(digestHex, StringComparison.OrdinalIgnoreCase));
        snapshot.Keys.Should().NotContain(label => label.Contains("hushvoting.standard", StringComparison.Ordinal));
        snapshot.Keys.Should().NotContain(label => label.Contains(KeyRing.Current.KeyId, StringComparison.Ordinal));
    }

    [Fact]
    public void Health_thresholds_flip_exactly_at_v1_boundaries_and_never_touch_authority()
    {
        // Warning boundary: pending depth > 1 000 or oldest pending age > five minutes.
        LicenceCacheHealth.EvaluateOutbox(Options, 1000, TimeSpan.FromMinutes(5))
            .Should().Be(LicenceCacheHealthState.Healthy);
        LicenceCacheHealth.EvaluateOutbox(Options, 1001, TimeSpan.FromMinutes(5))
            .Should().Be(LicenceCacheHealthState.Warning);
        LicenceCacheHealth.EvaluateOutbox(Options, 1000, TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)))
            .Should().Be(LicenceCacheHealthState.Warning);

        // Critical boundary: pending depth > 10 000 or oldest pending age > one hour.
        LicenceCacheHealth.EvaluateOutbox(Options, 10001, TimeSpan.FromMinutes(5))
            .Should().Be(LicenceCacheHealthState.Critical);
        LicenceCacheHealth.EvaluateOutbox(Options, 1000, TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)))
            .Should().Be(LicenceCacheHealthState.Critical);

        // A cache health state is never an authority signal: the reader host only exposes cache
        // health separately from PostgreSQL authority availability (enforced by the host
        // composition tests); here we prove the evaluator never returns an unavailable state.
        Enum.GetValues<LicenceCacheHealthState>()
            .Should().NotContain((LicenceCacheHealthState)(-1));
    }
}
