// FEAT-011 Task 3.8 — focused HushServerNode identity TwinTests with the
// stable FEAT-011 scenario IDs (paired with the client real-root scenarios in
// acceptance-traceability-ledger.md §4).
//
// Focused block only (workspace E2E rule): run with
//   dotnet test --no-build --filter "FullyQualifiedName~IdentityHappypathTwinTests"
// These tests reuse the shared HushTestFixture (isolated Testcontainers) and
// the exact canonical FullIdentity signed-transaction builder.

using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Blockchain.TransactionModel.States;
using HushServerNode;
using HushServerNode.Testing;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("Integration Tests")]
[Trait("Category", "FEAT-011")]
[Trait("Category", "NON_E2E")]
public sealed class IdentityHappypathTwinTests : IAsyncLifetime
{
    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private BlockProductionControl? _blockControl;
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
        (_node, _blockControl, _grpcFactory) = await _fixture.StartNodeAsync();
    }

    private HushBlockchain.HushBlockchainClient BlockchainClient() =>
        _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();

    private HushIdentity.HushIdentityClient IdentityClient() =>
        _grpcFactory!.CreateClient<HushIdentity.HushIdentityClient>();

    private async Task<SubmitSignedTransactionReply> SubmitAsync(HushShared.Blockchain.TransactionModel.States.SignedTransaction<HushShared.Identity.Model.FullIdentityPayload> transaction)
    {
        var client = BlockchainClient();
        return await client.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = System.Text.Json.JsonSerializer.Serialize(transaction),
        });
    }

    /// <summary>HV-ID-SIGN-002 — forged/mutated FullIdentity is rejected before admission with a stable code.</summary>
    [Fact]
    public async Task HVID_SIGN_002_ForgedTransaction_IsRejectedWithStableCode()
    {
        await StartNodeAsync();

        // Structurally valid compact base64 (64 bytes) that does not verify.
        var forged = FullIdentityTwinTestData.BuildSigned(signatureOverride: Convert.ToBase64String(new byte[64]));

        var reply = await SubmitAsync(forged);

        reply.Successfull.Should().BeFalse();
        reply.Status.Should().Be(TransactionStatus.Rejected);
        reply.ValidationCode.Should().Be("FULL_IDENTITY_INVALID_SIGNATURE");
    }

    /// <summary>HV-ID-SIGN-002 — malformed JSON never escapes as an exception.</summary>
    [Fact]
    public async Task HVID_SIGN_002_MalformedJson_IsStableRejectedOutcome()
    {
        await StartNodeAsync();

        var reply = await BlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = "{ this is not json",
        });

        reply.Successfull.Should().BeFalse();
        reply.Status.Should().Be(TransactionStatus.Rejected);
        reply.ValidationCode.Should().Be("MALFORMED_TRANSACTION_JSON");
    }

    /// <summary>HV-ID-SUBMIT-001 — first valid admission is ACCEPTED.</summary>
    [Fact]
    public async Task HVID_SUBMIT_001_ValidAdmission_IsAccepted()
    {
        await StartNodeAsync();

        var reply = await SubmitAsync(FullIdentityTwinTestData.BuildSigned());

        reply.Successfull.Should().BeTrue();
        reply.Status.Should().Be(TransactionStatus.Accepted);
    }

    /// <summary>HV-ID-SUBMIT-002 — exact retry is PENDING; a second profile is never created.</summary>
    [Fact]
    public async Task HVID_SUBMIT_002_ExactRetry_IsPending_AndIndexedIsAlreadyExists()
    {
        await StartNodeAsync();
        var transaction = FullIdentityTwinTestData.BuildSigned();

        var first = await SubmitAsync(transaction);
        var retry = await SubmitAsync(transaction);

        first.Status.Should().Be(TransactionStatus.Accepted);
        retry.Status.Should().Be(TransactionStatus.Pending);

        // Produce blocks so the transaction indexes, then submit again.
        await ProduceBlockAsync();

        var afterIndex = await SubmitAsync(transaction);
        afterIndex.Status.Should().Be(TransactionStatus.AlreadyExists);
    }

    /// <summary>HV-ID-CACHE-001/LOOKUP-001 — exact both-key projection after indexing.</summary>
    [Fact]
    public async Task HVID_LOOKUP_001_ExactProfile_AfterIndexing()
    {
        await StartNodeAsync();

        var submit = await SubmitAsync(FullIdentityTwinTestData.BuildSigned());
        submit.Status.Should().Be(TransactionStatus.Accepted);

        await ProduceBlockAsync();

        var lookup = await IdentityClient().GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = FullIdentityTwinTestData.K001SigningAddress,
        });

        lookup.Successfull.Should().BeTrue();
        lookup.PublicSigningAddress.Should().Be(FullIdentityTwinTestData.K001SigningAddress);
        lookup.PublicEncryptAddress.Should().Be(FullIdentityTwinTestData.K001EncryptAddress);
        lookup.ProfileName.Should().Be(FullIdentityTwinTestData.Alias);
    }

    /// <summary>HV-ID-LOOKUP-005 — explicit not-found contract before indexing.</summary>
    [Fact]
    public async Task HVID_LOOKUP_005_NotYetIndexed_IsExplicitNotfound()
    {
        await StartNodeAsync();

        var lookup = await IdentityClient().GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = FullIdentityTwinTestData.K001SigningAddress,
        });

        lookup.Successfull.Should().BeFalse();
        lookup.PublicSigningAddress.Should().BeNullOrEmpty();
    }

    /// <summary>HV-ID-LOOKUP-002 — bounded request: invalid input is InvalidArgument, never absence.</summary>
    [Fact]
    public async Task HVID_LOOKUP_002_InvalidRequest_IsTransportRejection_NeverAbsence()
    {
        await StartNodeAsync();

        var exception = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() =>
            IdentityClient().GetIdentityAsync(new GetIdentityRequest { PublicSigningAddress = "" }).ResponseAsync);

        exception.StatusCode.Should().Be(Grpc.Core.StatusCode.InvalidArgument);
    }

    private async Task ProduceBlockAsync()
    {
        // Deterministic fixture-controlled block production; wait for the
        // index to settle before asserting.
        await _blockControl!.ProduceBlockAsync();
        await Task.Delay(2_000);
    }
}
