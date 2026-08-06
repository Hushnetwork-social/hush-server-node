// FEAT-011 Task 3.7/3.8 — bounded GetIdentity behavior over the real node:
// invalid requests are rejected at the transport level (InvalidArgument) so a
// defect can never be mistaken for authoritative absence; valid requests keep
// the explicit not-found contract.

using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("Integration Tests")]
[Trait("Category", "FEAT-011")]
[Trait("Category", "NON_E2E")]
public sealed class IdentityGrpcServiceBoundsTwinTests : IAsyncLifetime
{
    private const string OversizedAddress = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"; // 131 chars
    private const string ValidAddress = "0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5";

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
        if (_node is not null)
        {
            await _node.DisposeAsync();
            _node = null;
        }

        _grpcFactory?.Dispose();
        _grpcFactory = null;
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }
    }

    private async Task StartNodeAsync()
    {
        await _fixture!.ResetAllAsync();
        (_node, _, _grpcFactory) = await _fixture.StartNodeAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-hex-at-all")]
    [InlineData("12345")] // valid hex but not an Approved 66/130 length
    [InlineData("03")] // too short
    public async Task InvalidRequest_IsTransportRejection_NeverAbsence(string address)
    {
        await StartNodeAsync();

        var client = _grpcFactory!.CreateClient<HushIdentity.HushIdentityClient>();
        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            client.GetIdentityAsync(new GetIdentityRequest { PublicSigningAddress = address }).ResponseAsync);

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task NullAddress_IsRejectedClientSide_NeverAbsence()
    {
        await StartNodeAsync();

        var client = _grpcFactory!.CreateClient<HushIdentity.HushIdentityClient>();
        // Protobuf setters reject null before the request leaves the client —
        // equally fail-closed, never an absence signal.
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.GetIdentityAsync(new GetIdentityRequest { PublicSigningAddress = null! }).ResponseAsync);

        exception.Should().NotBeNull();
    }

    [Fact]
    public async Task OversizedRequest_IsTransportRejection_NeverAbsence()
    {
        await StartNodeAsync();

        var client = _grpcFactory!.CreateClient<HushIdentity.HushIdentityClient>();
        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            client.GetIdentityAsync(new GetIdentityRequest { PublicSigningAddress = OversizedAddress }).ResponseAsync);

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task BoundedValidRequest_KeepsExplicitNotfoundContract()
    {
        await StartNodeAsync();

        var client = _grpcFactory!.CreateClient<HushIdentity.HushIdentityClient>();
        var reply = await client.GetIdentityAsync(new GetIdentityRequest { PublicSigningAddress = ValidAddress });

        reply.Successfull.Should().BeFalse();
        reply.PublicSigningAddress.Should().BeNullOrEmpty();
    }
}
