using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using StackExchange.Redis;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-014 Phase 7 Task 7.1 real-Redis TwinTests: exact privacy-safe key shape/hash tag, strict
/// key-bound HMAC + strict schema validation (tamper matrix), absolute TTL/expiry boundaries, and
/// monotonic revision-aware atomic writes under real Redis concurrency. Redis keys are inspected only
/// inside test assertions and never logged.
/// </summary>
[Collection("FEAT-014 Redis+PostgreSQL")]
[Trait("Category", "FEAT-014")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceCacheSecurityTwinTests
{
    private const string Address = "NXc1cYVmGJZcbzF5wY2tT9DP5cFpFT9k";
    private const string CatalogueVersion = "hushvoting-licence-catalogue/v1.0.0";
    private static readonly string DigestA = new('A', 64);
    private static readonly DateTime NowUtc = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] CurrentMaster = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] PreviousMaster = Enumerable.Range(90, 32).Select(i => (byte)i).ToArray();
    private static readonly LicenceCacheOptions Options = new();
    private static readonly LicenceCacheEnvelopeCodec Codec = new();

    private readonly LicenceCacheRedisPostgresFixture _fixture;
    private readonly string _prefix;

    public HushVotingLicenceCacheSecurityTwinTests(LicenceCacheRedisPostgresFixture fixture)
    {
        _fixture = fixture;
        _prefix = fixture.InstancePrefix;
    }

    private static LicenceCacheKeyRing Ring() =>
        LicenceCacheKeyRing.TryCreate(
            LicenceCacheMasterKey.Create("v2", CurrentMaster, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), Options, out _),
            LicenceCacheMasterKey.Create("v1", PreviousMaster, new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), Options, out _),
            Options,
            out _)!;

    private static AuthenticatedIdentitySubject Subject() =>
        AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity, Address, 7, out var s, out _) ? s! : throw new InvalidOperationException();

    private static string Token() =>
        LicenceCacheKeyBuilder.BuildCatalogueToken(CatalogueVersion, DigestA);

    private static RedisEntitlementProjectionStore NewStore(LicenceCacheRedisPostgresFixture fixture) =>
        new(fixture.RedisDatabase, Options, Codec);

    private string CurrentKey(LicenceCacheKeyRing ring)
    {
        var digest = LicenceCacheKeyDerivation.ComputeSubjectDigest(
            LicenceCacheKeyDerivation.DeriveSubjectKey(ring.Current.SecretBytes), Subject().CanonicalPublicSigningAddress);
        return LicenceCacheKeyBuilder.BuildProjectionKey(_prefix, Token(), ring.Current.KeyId, digest);
    }

        private static CachedEntitlementEnvelope Envelope(
        LicenceCacheKeyRing ring,
        long revision,
        DateTime? assignmentExpiry = null,
        string? planFamily = null) =>
        new()
        {
            KeyId = ring.Current.KeyId,
            CatalogueVersion = CatalogueVersion,
            CatalogueToken = Token(),
            CacheWrittenUtc = NowUtc,
            CacheValidUntilUtc = NowUtc.AddDays(7),
            PlanId = "hushvoting.directfree",
            PlanFamily = planFamily ?? "Direct Free",
            UpgradeRank = 0,
            EligibleVoterCap = null,
            UnlimitedElections = true,
            TermKind = "indefinite",
            TermYears = 1,
            AllowedGovernanceOptionIds = Array.Empty<string>(),
            ExpiresAtUtc = assignmentExpiry,
            EntitlementRevision = revision,
        };

    private static byte[] AuthKey(LicenceCacheKeyRing ring) =>
        LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(ring.Current.SecretBytes);

    [Fact]
    public async Task Key_shape_is_privacy_safe_and_preserves_one_hash_tag()
    {
        await _fixture.FlushRedisAsync();
        var ring = Ring();
        var key = CurrentKey(ring);
        var digestHex = LicenceCacheKeyBuilder.ToDigestHex(
            LicenceCacheKeyDerivation.ComputeSubjectDigest(
                LicenceCacheKeyDerivation.DeriveSubjectKey(ring.Current.SecretBytes), Subject().CanonicalPublicSigningAddress));

        key.Should().NotContain(Address);
        key.Should().NotContain("plan");
        key.Should().NotContain("veritas");
        key.Should().NotContain(ring.Current.SecretBytes.ToString());
        key.Should().StartWith(_prefix + LicenceCacheKeyBuilder.KeyRoot);
        key.Should().Contain(Token());
        key.Should().Contain(ring.Current.KeyId);
        key.Should().Contain("{" + digestHex + "}");
        // Projection and lease share one subject hash tag for cluster-slot affinity.
        var digest = LicenceCacheKeyDerivation.ComputeSubjectDigest(
            LicenceCacheKeyDerivation.DeriveSubjectKey(ring.Current.SecretBytes), Subject().CanonicalPublicSigningAddress);
        var lease = LicenceCacheKeyBuilder.BuildFillLeaseKey(_prefix, Token(), ring.Current.KeyId, digest);
        lease.Should().Contain("{" + digestHex + "}");
    }

    [Fact]
    public async Task Tampered_and_wrong_key_values_are_complete_corrupt_misses()
    {
        await _fixture.FlushRedisAsync();
        var ring = Ring();
        var store = NewStore(_fixture);
        var validator = new LicenceCacheValueValidator(Codec, Options);
        var key = CurrentKey(ring);
        var token = Token();
        var envelopeBytes = Codec.SerializeCanonical(Envelope(ring, 1));
        var tag = Codec.ComputeAuthenticationTag(key, envelopeBytes, AuthKey(ring));

        // Tamper with the value body.
        var tamperedBody = (byte[])envelopeBytes.Clone();
        tamperedBody[0] ^= 0x01;
        var tamperedTag = Codec.ComputeAuthenticationTag(key, tamperedBody, AuthKey(ring));

        await _fixture.RedisDatabase.StringSetAsync(key, Codec.FormatRedisValue(envelopeBytes, tag));
        var validRead = await store.ReadCurrentAsync(key, CancellationToken.None);
        validRead.Valid.Should().BeTrue();
        validator.TryValidate(key, validRead.CanonicalEnvelopeBytes, validRead.TagBytes, token, ring, NowUtc, out _, out var okReason)
            .Should().BeTrue(okReason);

        // A value signed under a different (previous) key version must be rejected by key-bound HMAC.
        var previousKey = LicenceCacheKeyBuilder.BuildProjectionKey(
            _prefix, token, ring.Previous!.KeyId,
            LicenceCacheKeyDerivation.ComputeSubjectDigest(
                LicenceCacheKeyDerivation.DeriveSubjectKey(ring.Previous.SecretBytes), Subject().CanonicalPublicSigningAddress));
        var prevEnvelope = Codec.SerializeCanonical(Envelope(ring, 1));
        var prevTag = Codec.ComputeAuthenticationTag(previousKey, prevEnvelope,
            LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(ring.Previous.SecretBytes));
        await _fixture.RedisDatabase.StringSetAsync(previousKey, Codec.FormatRedisValue(prevEnvelope, prevTag));

        // Current-key lookup must not find the previous-key value (key-bound namespace separation).
        var currentRead = await store.ReadCurrentAsync(key, CancellationToken.None);
        currentRead.Valid.Should().BeTrue();

        // Direct validator rejects a moved/tampered combination.
        validator.TryValidate(key, tamperedBody, tamperedTag, token, ring, NowUtc, out _, out var reason)
            .Should().BeFalse();
        reason.Should().BeOneOf(LicenceCacheReasonCodes.EnvelopeMalformed, LicenceCacheReasonCodes.EnvelopeUnauthenticated);
    }

    [Fact]
    public async Task Expired_values_are_rejected_on_read_even_if_redis_still_holds_them()
    {
        await _fixture.FlushRedisAsync();
        var ring = Ring();
        var store = NewStore(_fixture);
        var validator = new LicenceCacheValueValidator(Codec, Options);
        var key = CurrentKey(ring);
        var expiredEnvelope = Envelope(ring, 2);
        var envelopeBytes = Codec.SerializeCanonical(expiredEnvelope);
        var tag = Codec.ComputeAuthenticationTag(key, envelopeBytes, AuthKey(ring));
        await _fixture.RedisDatabase.StringSetAsync(key, Codec.FormatRedisValue(envelopeBytes, tag));

        var read = await store.ReadCurrentAsync(key, CancellationToken.None);
        read.Valid.Should().BeTrue();

        validator.TryValidate(key, read.CanonicalEnvelopeBytes, read.TagBytes, Token(), ring,
                expiredEnvelope.CacheValidUntilUtc.AddMinutes(1), out _, out var reason)
            .Should().BeFalse();
        reason.Should().Be(LicenceCacheReasonCodes.EnvelopeExpired);
    }

    [Fact]
    public async Task Atomic_revision_write_is_monotonic_under_real_redis()
    {
        await _fixture.FlushRedisAsync();
        var ring = Ring();
        var store = NewStore(_fixture);
        var key = CurrentKey(ring);
        var auth = AuthKey(ring);

        // Revision 5 accepted first.
        var rev5 = Codec.SerializeCanonical(Envelope(ring, 5));
        var w5 = await store.WriteAsync(key, rev5, 5, 3600, auth, CancellationToken.None);
        w5.Outcome.Should().Be(ProjectionWriteOutcome.AcceptedNew);

        // Lower revision rejected (stale).
        var rev4 = Codec.SerializeCanonical(Envelope(ring, 4));
        var w4 = await store.WriteAsync(key, rev4, 4, 3600, auth, CancellationToken.None);
        w4.Outcome.Should().Be(ProjectionWriteOutcome.StaleRevision);

        // Equal but different content rejected as divergence.
        var diff5 = Codec.SerializeCanonical(Envelope(ring, 5, planFamily: "Changed"));
        var wDiff = await store.WriteAsync(key, diff5, 5, 3600, auth, CancellationToken.None);
        wDiff.Outcome.Should().Be(ProjectionWriteOutcome.SameRevisionDivergence);

        // Higher revision replaces.
        var rev6 = Codec.SerializeCanonical(Envelope(ring, 6));
        var w6 = await store.WriteAsync(key, rev6, 6, 3600, auth, CancellationToken.None);
        w6.Outcome.Should().Be(ProjectionWriteOutcome.AcceptedHigherRevision);

        // Stored value is now revision 6.
        var read = await store.ReadCurrentAsync(key, CancellationToken.None);
        read.Valid.Should().BeTrue();
        Codec.TryDeserialize(read.CanonicalEnvelopeBytes, out var envelope, out var error).Should().BeTrue(error);
        envelope!.EntitlementRevision.Should().Be(6);
    }
}
