using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>Privacy-preserving key shape tests (Task 2.5/2.6).</summary>
public sealed class LicenceCacheKeyBuilderTests
{
    private const string Address = "NXc1cYVmGJZcbzF5wY2tT9DP5cFpFT9k";
    private static readonly byte[] SubjectKey =
        LicenceCacheKeyDerivation.DeriveSubjectKey(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private static byte[] Digest() =>
        LicenceCacheKeyDerivation.ComputeSubjectDigest(SubjectKey, Address);

    [Fact]
    public void Projection_key_shape_is_exact_and_privacy_safe()
    {
        var digest = Digest();
        var token = LicenceCacheKeyBuilder.BuildCatalogueToken(
            "hushvoting-licence-catalogue/v1.0.0",
            "AB".PadLeft(64, '0'));

        var key = LicenceCacheKeyBuilder.BuildProjectionKey(
            "dev:",
            token,
            "v2",
            digest);

        key.Should().StartWith("dev:hushvoting:licence-entitlement:v1:");
        key.Should().Contain(":hushvoting-licence-catalogue/v1.0.0:0000");
        key.Should().Contain(":v2:{");
        key.Should().Contain($"}}:projection");
        key.Should().NotContain(Address);            // no raw identity
        key.Should().NotContain("veritas");          // no plan
        key.Should().NotContain("revision");         // no state keyword
        key.Should().NotContain("500");

        // The digest is a 64-char lowercase hex inside a Redis hash tag.
        var digestHex = LicenceCacheKeyBuilder.ToDigestHex(digest);
        key.Should().Contain("{" + digestHex + "}");
        digestHex.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Projection_and_lease_keys_share_one_hash_tag()
    {
        var digest = Digest();
        var token = LicenceCacheKeyBuilder.BuildCatalogueToken(
            "hushvoting-licence-catalogue/v1.0.0",
            "CD".PadLeft(64, '0'));

        var projection = LicenceCacheKeyBuilder.BuildProjectionKey("", token, "v1", digest);
        var lease = LicenceCacheKeyBuilder.BuildFillLeaseKey("", token, "v1", digest);

        var tagA = projection[projection.IndexOf('{')..(projection.IndexOf('}') + 1)];
        var tagB = lease[lease.IndexOf('{')..(lease.IndexOf('}') + 1)];
        tagA.Should().Be(tagB);
        projection.Should().EndWith("}:projection");
        lease.Should().EndWith("}:fill-lease");
    }

    [Fact]
    public void Catalogue_digest_is_normalized_to_lowercase()
    {
        var digest = "EF".PadLeft(64, 'F');
        var token = LicenceCacheKeyBuilder.BuildCatalogueToken(
            "hushvoting-licence-catalogue/v1.0.0",
            digest);

        token.Should().EndWith(":" + digest.ToLowerInvariant());
    }

    [Fact]
    public void Catalogue_token_binds_version_and_digest()
    {
        var token1 = LicenceCacheKeyBuilder.BuildCatalogueToken("hushvoting-licence-catalogue/v1.0.0", "AA".PadLeft(64, '0'));
        var token2 = LicenceCacheKeyBuilder.BuildCatalogueToken("hushvoting-licence-catalogue/v1.0.0", "AA".PadLeft(64, '1'));
        var token3 = LicenceCacheKeyBuilder.BuildCatalogueToken("hushvoting-licence-catalogue/v1.0.1", "AA".PadLeft(64, '0'));

        token1.Should().NotBe(token2); // digest change switches namespace
        token1.Should().NotBe(token3); // version change switches namespace
    }

    [Theory]
    [InlineData("hushvoting-licence-catalogue/v1.0.0", "not-hex")]
    [InlineData("", "AA")]
    public void Invalid_catalogue_inputs_are_rejected(string version, string digest)
    {
        var act = () => LicenceCacheKeyBuilder.BuildCatalogueToken(version, digest);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Invalid_digest_length_is_rejected()
    {
        var digest = LicenceCacheKeyDerivation.ComputeSubjectDigest(
            SubjectKey,
            Address);
        var act = () => LicenceCacheKeyBuilder.BuildProjectionKey("", "tok", "k", digest[..^1]);
        act.Should().Throw<ArgumentException>();
    }
}
