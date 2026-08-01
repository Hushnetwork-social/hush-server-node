using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-001 focused identity-lookup TwinTest (Phase 6).
/// =====================================================
/// Proves the real HushServerNode identity lookup contract (HushIdentity
/// gRPC GetIdentity by exact PublicSigningAddress) returns the controlled
/// outcomes that the FEAT-001 pure candidate resolution consumes, without
/// folding profile-registration orchestration, feed initialization, or
/// transaction-ingress security work into FEAT-001.
///
/// Controlled identities are the PUBLIC TEST fixture addresses from the
/// canonical corpus (conformance/identity/v1/vectors/mnemonic-vectors.json):
///   P-01 compressed pair (M-001): 0237fdd4... / 032ebaf0...
///   P-02 uncompressed pair (M-004): 042b347a... / 0400e26b...
/// They are synthetic, non-secret, and prohibited from production use. No
/// remote state; no profile creation via registration orchestration.
///
/// Scenarios (Gherkin):
///   1. No controlled candidate exists  -> zero exact matches
///   2. One exact signing/encryption profile exists -> one matching outcome
///      with exact public addresses
///   3. Distinct exact encoded address profiles exist (compressed vs
///      uncompressed) -> each remains a distinct lookup outcome; the server
///      never selects an identity on the caller's behalf
/// </summary>
[Collection("Integration Tests")]
[Trait("Category", "FEAT-001")]
[Trait("Category", "NON_E2E")]
public sealed class IdentityCompatibilityTwinTests : IAsyncLifetime
{
    // Controlled PUBLIC TEST addresses (corpus M-001 P-01 compressed).
    private const string P01Signing = "0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5";
    private const string P01Encrypt = "032ebaf076203f15ac8119cfdbc9394d1c7b9929b0647e4f607e27da95701f8556";

    // Controlled PUBLIC TEST addresses (corpus M-004 P-02 uncompressed).
    private const string P02Signing = "042b347a16473bab675d469b6094deb96cd21239b855b98f98a4f8194953988306f9a5c50de7d68eb543364cbca033c05201a7cc93e96b61a8fb95402bc6e3b66e";
    private const string P02Encrypt = "0400e26b2c6e77ce44f40f56eadd6add5a134d4f6f09d43ba38faadbf2f9a6b781d07e19ea77fdd21ba5c31c3975d2f7107c9e3be247401a7b0e5853fea09b3a10";

    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private GrpcClientFactory? _grpcFactory;

    public async Task InitializeAsync()
    {
        _fixture = new HushTestFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await DisposeNodeAsync();
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    private async Task DisposeNodeAsync()
    {
        if (_node is not null)
        {
            await _node.DisposeAsync();
            _node = null;
        }
        _grpcFactory?.Dispose();
        _grpcFactory = null;
    }

    private async Task StartNodeAsync()
    {
        await DisposeNodeAsync();
        await _fixture!.ResetAllAsync();
        (_node, _, _grpcFactory) = await _fixture.StartNodeAsync();
    }

    /// <summary>Seed a controlled profile row directly (no registration orchestration).</summary>
    private async Task SeedProfileAsync(string alias, string signing, string encrypt)
    {
        var shortAlias = alias.Length > 2 ? alias[..2].ToUpperInvariant() : alias.ToUpperInvariant();
        var insertSql = $"""
            INSERT INTO "Identity"."Profile"
            ("PublicSigningAddress", "Alias", "ShortAlias", "PublicEncryptAddress", "IsPublic", "BlockIndex")
            VALUES ('{signing}', '{alias}', '{shortAlias}', '{encrypt}', true, 1)
            """;
        await _fixture!.ExecuteNonQueryAsync(insertSql);
    }

    private async Task<GetIdentityReply> LookupAsync(string signingAddress)
    {
        var client = _grpcFactory!.CreateClient<HushIdentity.HushIdentityClient>();
        return await client.GetIdentityAsync(new GetIdentityRequest { PublicSigningAddress = signingAddress });
    }

    [Fact]
    public async Task NoControlledCandidate_ReturnsZeroMatches()
    {
        await StartNodeAsync();

        // Scenario: no profile exists for a controlled public address.
        var reply = await LookupAsync(P02Signing);

        reply.Successfull.Should().BeFalse("no profile exists for the controlled P-02 signing address");
        reply.PublicSigningAddress.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task OneExactProfile_ReturnsOneMatchWithExactAddresses()
    {
        await StartNodeAsync();
        await SeedProfileAsync("compat-p01-001", P01Signing, P01Encrypt);

        var reply = await LookupAsync(P01Signing);

        reply.Successfull.Should().BeTrue();
        reply.ProfileName.Should().Be("compat-p01-001");
        reply.PublicSigningAddress.Should().Be(P01Signing, "lookup is keyed by the exact signing address string");
        reply.PublicEncryptAddress.Should().Be(P01Encrypt, "the exact encryption address is returned with the profile");
    }

    [Fact]
    public async Task DistinctEncodedCandidates_RemainDistinctOutcomes_NoServerSelection()
    {
        await StartNodeAsync();

        // Distinct exact encoded address pairs: compressed (P-01) and
        // uncompressed (P-02) encodings of different curve points.
        await SeedProfileAsync("compat-p01-002", P01Signing, P01Encrypt);
        await SeedProfileAsync("compat-p02-002", P02Signing, P02Encrypt);

        var p01Reply = await LookupAsync(P01Signing);
        var p02Reply = await LookupAsync(P02Signing);

        p01Reply.Successfull.Should().BeTrue();
        p02Reply.Successfull.Should().BeTrue();

        // Each exact address yields its own profile — no resolution/selection.
        p01Reply.PublicSigningAddress.Should().Be(P01Signing);
        p01Reply.PublicEncryptAddress.Should().Be(P01Encrypt);
        p02Reply.PublicSigningAddress.Should().Be(P02Signing);
        p02Reply.PublicEncryptAddress.Should().Be(P02Encrypt);

        p01Reply.PublicSigningAddress.Should().NotBe(p02Reply.PublicSigningAddress, "compressed and uncompressed encodings are distinct lookup candidates");
        p01Reply.ProfileName.Should().NotBe(p02Reply.ProfileName);
    }

    [Fact]
    public async Task AbsentProfile_AfterSeedingOthers_RemainsNotFound()
    {
        await StartNodeAsync();
        await SeedProfileAsync("compat-p01-003", P01Signing, P01Encrypt);

        // A different controlled address with no profile must stay zero-match
        // even while another identity exists (exact binding, no wildcard).
        var reply = await LookupAsync(P02Signing);

        reply.Successfull.Should().BeFalse();
        reply.Message.Should().Contain("not found", "the identity contract reports absence explicitly");
    }
}
