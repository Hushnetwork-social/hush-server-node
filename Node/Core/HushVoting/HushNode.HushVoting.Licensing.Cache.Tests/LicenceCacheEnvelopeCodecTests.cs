using System.Text;
using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>
/// Strict canonical envelope and key-bound authentication tests (Task 2.5/2.6): round-trip,
/// duplicate/unknown-field rejection, wrong-key/rotation rejection, and a tamper matrix proving no
/// modified byte can authenticate.
/// </summary>
public sealed class LicenceCacheEnvelopeCodecTests
{
    private static readonly LicenceCacheEnvelopeCodec Codec = new();
    private static readonly LicenceCacheOptions Options = new();

    private static readonly byte[] MasterBytes = Enumerable.Range(0, 32).Select(i => (byte)(i + 3)).ToArray();
    private static readonly byte[] AuthKey = LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(MasterBytes);
    private static readonly byte[] OtherAuthKey =
        LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private const string RedisKey =
        "dev:hushvoting:licence-entitlement:v1:hushvoting-licence-catalogue/v1.0.0:0000ab:v2:{abcdef}:projection";

    private static CachedEntitlementEnvelope NewEnvelope() =>
        new()
        {
            KeyId = "v2",
            CatalogueVersion = "hushvoting-licence-catalogue/v1.0.0",
            CatalogueToken = "hushvoting-licence-catalogue/v1.0.0:0000ab",
            CacheWrittenUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            CacheValidUntilUtc = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc),
            PlanId = "hushvoting.veritas.500",
            PlanFamily = "Veritas",
            UpgradeRank = 2,
            EligibleVoterCap = 500,
            UnlimitedElections = false,
            TermKind = "annual",
            TermYears = 1,
            AllowedGovernanceOptionIds = new[] { "hushvoting.governance.standard" },
            ExpiresAtUtc = new DateTime(2027, 9, 5, 0, 0, 0, DateTimeKind.Utc),
            EntitlementRevision = 8,
        };

    [Fact]
    public void Round_trip_serialize_format_parse_authenticate_succeeds()
    {
        var envelope = NewEnvelope();
        var bytes = Codec.SerializeCanonical(envelope);
        var tag = Codec.ComputeAuthenticationTag(RedisKey, bytes, AuthKey);
        var value = Codec.FormatRedisValue(bytes, tag);

        Codec.TrySplitRedisValue(value, Options.MaxEnvelopeBytes, out var parsedBytes, out var parsedTag, out var reason)
            .Should().BeTrue(reason);
        Codec.VerifyAuthentication(RedisKey, parsedBytes, parsedTag, AuthKey).Should().BeTrue();

        Codec.TryDeserialize(parsedBytes, out var parsed, out var parseReason)
            .Should().BeTrue(parseReason);
        parsed!.KeyId.Should().Be("v2");
        parsed.PlanId.Should().Be("hushvoting.veritas.500");
        parsed.EntitlementRevision.Should().Be(8);
        parsed.ExpiresAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Serialization_is_deterministic_canonical()
    {
        var a = Codec.SerializeCanonical(NewEnvelope());
        var b = Codec.SerializeCanonical(NewEnvelope());
        a.Should().Equal(b);

        var text = Encoding.UTF8.GetString(a);
        text.Should().NotContain("\n"); // no whitespace
        text.Should().StartWith("{\"schema\":\"hushvoting/licence-cache/envelope/v1\"");
    }

    [Theory]
    [InlineData(0)]   // schema start
    [InlineData(1)]   // key id
    [InlineData(50)]  // catalogue/plan region
    public void Any_byte_flip_fails_authentication(int index)
    {
        var bytes = Codec.SerializeCanonical(NewEnvelope());
        if (index >= bytes.Length)
        {
            index = bytes.Length - 1;
        }

        // Tag is computed over the ORIGINAL canonical bytes; the tampered copy must not verify.
        var tag = Codec.ComputeAuthenticationTag(RedisKey, bytes, AuthKey);
        var tampered = (byte[])bytes.Clone();
        tampered[index] ^= 0x01;

        Codec.VerifyAuthentication(RedisKey, tampered, tag, AuthKey).Should().BeFalse();
    }

    [Fact]
    public void Tag_is_bound_to_the_full_redis_key()
    {
        var bytes = Codec.SerializeCanonical(NewEnvelope());
        var tag = Codec.ComputeAuthenticationTag(RedisKey, bytes, AuthKey);

        // Same payload under a different subject/catalogue/prefix key must not authenticate.
        Codec.VerifyAuthentication(RedisKey + "-moved", bytes, tag, AuthKey).Should().BeFalse();
    }

    [Fact]
    public void Tag_is_bound_to_the_configured_key_version()
    {
        var bytes = Codec.SerializeCanonical(NewEnvelope());
        var tag = Codec.ComputeAuthenticationTag(RedisKey, bytes, AuthKey);

        // A different master (or the subject-key purpose) must never verify the value tag.
        Codec.VerifyAuthentication(RedisKey, bytes, tag, OtherAuthKey).Should().BeFalse();
    }

    [Fact]
    public void Unknown_field_is_rejected_as_complete_miss()
    {
        var json =
            "{\"schema\":\"hushvoting/licence-cache/envelope/v1\",\"keyId\":\"v2\",\"evil\":true," +
            "\"catalogueVersion\":\"hushvoting-licence-catalogue/v1.0.0\",\"catalogueToken\":\"t\"," +
            "\"cacheWrittenUtc\":\"2026-09-01T00:00:00Z\",\"cacheValidUntilUtc\":\"2026-09-08T00:00:00Z\"," +
            "\"planId\":\"p\",\"planFamily\":\"f\",\"upgradeRank\":0,\"eligibleVoterCap\":null," +
            "\"unlimitedElections\":false,\"termKind\":\"perpetual\",\"termYears\":1," +
            "\"allowedGovernanceOptionIds\":[],\"expiresAtUtc\":null,\"entitlementRevision\":1}";

        var result = Codec.TryDeserialize(Encoding.UTF8.GetBytes(json), out _, out var reason);
        result.Should().BeFalse();
        reason.Should().Be(LicenceCacheReasonCodes.EnvelopeUnknownField);
    }

    [Fact]
    public void Duplicate_field_is_rejected()
    {
        var json =
            "{\"schema\":\"hushvoting/licence-cache/envelope/v1\",\"keyId\":\"v2\",\"keyId\":\"v1\"," +
            "\"catalogueVersion\":\"c\",\"catalogueToken\":\"t\"," +
            "\"cacheWrittenUtc\":\"2026-09-01T00:00:00Z\",\"cacheValidUntilUtc\":\"2026-09-08T00:00:00Z\"," +
            "\"planId\":\"p\",\"planFamily\":\"f\",\"upgradeRank\":0,\"eligibleVoterCap\":null," +
            "\"unlimitedElections\":false,\"termKind\":\"perpetual\",\"termYears\":1," +
            "\"allowedGovernanceOptionIds\":[],\"expiresAtUtc\":null,\"entitlementRevision\":1}";

        var result = Codec.TryDeserialize(Encoding.UTF8.GetBytes(json), out _, out var reason);
        result.Should().BeFalse();
        reason.Should().Be(LicenceCacheReasonCodes.EnvelopeDuplicateField);
    }

    [Fact]
    public void Wrong_schema_version_is_rejected()
    {
        var json =
            "{\"schema\":\"hushvoting/licence-cache/envelope/v9\",\"keyId\":\"v2\"," +
            "\"catalogueVersion\":\"c\",\"catalogueToken\":\"t\"," +
            "\"cacheWrittenUtc\":\"2026-09-01T00:00:00Z\",\"cacheValidUntilUtc\":\"2026-09-08T00:00:00Z\"," +
            "\"planId\":\"p\",\"planFamily\":\"f\",\"upgradeRank\":0,\"eligibleVoterCap\":null," +
            "\"unlimitedElections\":false,\"termKind\":\"perpetual\",\"termYears\":1," +
            "\"allowedGovernanceOptionIds\":[],\"expiresAtUtc\":null,\"entitlementRevision\":1}";

        var result = Codec.TryDeserialize(Encoding.UTF8.GetBytes(json), out _, out var reason);
        result.Should().BeFalse();
        reason.Should().Be(LicenceCacheReasonCodes.EnvelopeWrongSchema);
    }

    [Fact]
    public void Invalid_date_order_is_rejected()
    {
        var envelope = NewEnvelope();
        var invalid = new CachedEntitlementEnvelope
        {
            KeyId = envelope.KeyId,
            CatalogueVersion = envelope.CatalogueVersion,
            CatalogueToken = envelope.CatalogueToken,
            CacheWrittenUtc = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc),
            CacheValidUntilUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
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

        var bytes = Codec.SerializeCanonical(invalid);
        var result = Codec.TryDeserialize(bytes, out _, out var reason);
        result.Should().BeFalse();
        reason.Should().Be(LicenceCacheReasonCodes.EnvelopeInvalidDates);
    }

    [Fact]
    public void Oversized_redis_value_is_rejected_without_parsing()
    {
        var big = new string('A', Options.MaxEnvelopeBytes + 1);
        var value = big + "\n" + new string('0', 64);

        Codec.TrySplitRedisValue(value, Options.MaxEnvelopeBytes, out _, out _, out var reason)
            .Should().BeFalse();
        reason.Should().Be(LicenceCacheReasonCodes.EnvelopeOversized);
    }

    [Fact]
    public void Malformed_redis_value_is_rejected()
    {
        Codec.TrySplitRedisValue("not-base64!", Options.MaxEnvelopeBytes, out _, out _, out var reason1)
            .Should().BeFalse();
        reason1.Should().Be(LicenceCacheReasonCodes.EnvelopeMalformed);

        Codec.TrySplitRedisValue(null, Options.MaxEnvelopeBytes, out _, out _, out var reason2)
            .Should().BeFalse();
        reason2.Should().Be(LicenceCacheReasonCodes.EnvelopeMalformed);
    }

    [Fact]
    public void Malformed_canonical_json_is_rejected()
    {
        var result = Codec.TryDeserialize(Encoding.UTF8.GetBytes("{\"schema\":"), out _, out var reason);
        result.Should().BeFalse();
        reason.Should().Be(LicenceCacheReasonCodes.EnvelopeMalformed);
    }
}
