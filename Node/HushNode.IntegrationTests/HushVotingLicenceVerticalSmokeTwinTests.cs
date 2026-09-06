// FEAT-015 Phase 6 Task 6.6 — real vertical smoke TwinTest.
//
// Drives the LIVE node over real gRPC with real PostgreSQL + Redis + block production:
//   1. index the exact FullIdentity (K-001 public corpus) via SubmitSignedTransaction + block;
//   2. call the signed GetMyEntitlement RPC before any licence -> no_active + Direct Free template;
//   3. submit the canonical signed Direct Free licence transaction -> ACCEPTED (mempool, not active);
//   4. produce a block so it indexes;
//   5. re-query -> active Direct Free with the originating transaction licence reference.
// No mocked direct service call is used anywhere on this path.

using FluentAssertions;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("Integration Tests")]
[Trait("Category", "FEAT-015")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceVerticalSmokeTwinTests : IAsyncLifetime
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

    private HushVotingLicence.HushVotingLicenceClient LicenceClient() =>
        _grpcFactory!.CreateClient<HushVotingLicence.HushVotingLicenceClient>();

    private async Task ProduceBlockAsync()
    {
        await _blockControl!.ProduceBlockAsync();
        await Task.Delay(2_000);
    }

    private async Task IndexIdentityAsync()
    {
        var identity = FullIdentityTwinTestData.BuildSigned();
        var reply = await BlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = System.Text.Json.JsonSerializer.Serialize(identity),
        });
        reply.Status.Should().Be(TransactionStatus.Accepted);
        await ProduceBlockAsync();
    }

    /// <summary>Signed GetMyEntitlement headers using the K-001 key (compact base64).</summary>
    private async Task<Metadata> SignedLicenceMetadataAsync()
    {
        var signedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var actor = FullIdentityTwinTestData.K001SigningAddress;
        var payload = HushNode.HushVoting.Licence.gRPC.LicenceQueryRequestAuthValidator.BuildSignedPayload(
            "GetMyEntitlement", actor, signedAt);
        var signature = Olimpo.DigitalSignature.SignMessageCompactBase64(
            payload, FullIdentityTwinTestData.K001PrivateScalarHex);

        var headers = new Metadata();
        headers.Add(
            HushNode.HushVoting.Licence.gRPC.LicenceQueryRequestAuthValidator.SignatoryHeader,
            actor);
        headers.Add(
            HushNode.HushVoting.Licence.gRPC.LicenceQueryRequestAuthValidator.SignedAtHeader,
            signedAt);
        headers.Add(
            HushNode.HushVoting.Licence.gRPC.LicenceQueryRequestAuthValidator.SignatureHeader,
            signature);
        return headers;
    }

    [Fact]
    public async Task ClientAuthoredCatalogue_IsRejected_ByRealValidator()
    {
        await StartNodeAsync();
        await IndexIdentityAsync();

        // A client cannot author authority: it may only name the observed catalogue release. An
        // invented/unknown release must be rejected before mempool (AT-LIC-015-005).
        var payload = new HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAssignmentPayload(
            HushNode.HushVoting.Licence.Transactions.HushVotingLicenceTransitionIntent.BaselineFree,
            "hushvoting.direct.free",
            "hushvoting-licence-catalogue/v99.0.0"); // client-invented release
        var size = HushNode.HushVoting.Licence.Transactions.HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload);
        var unsigned = new HushShared.Blockchain.TransactionModel.States.UnsignedTransaction<
            HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAssignmentPayload>(
            new HushShared.Blockchain.TransactionModel.TransactionId(Guid.NewGuid()),
            HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind,
            new HushShared.Blockchain.Model.Timestamp(DateTime.UtcNow),
            payload,
            size);
        var canonical = new HushNode.HushVoting.Licence.Transactions.HushVotingLicenceCanonicalSerializer()
            .SerializeCanonicalUnsignedJson(new HushShared.Blockchain.TransactionModel.States.SignedTransaction<
                HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAssignmentPayload>(
                unsigned,
                new HushShared.Blockchain.Model.SignatureInfo(FullIdentityTwinTestData.K001SigningAddress, string.Empty)));
        var signature = Olimpo.DigitalSignature.SignMessageCompactBase64(
            canonical, FullIdentityTwinTestData.K001PrivateScalarHex);
        var tx = new HushShared.Blockchain.TransactionModel.States.SignedTransaction<
            HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAssignmentPayload>(
            unsigned,
            new HushShared.Blockchain.Model.SignatureInfo(FullIdentityTwinTestData.K001SigningAddress, signature));

        var reply = await BlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = System.Text.Json.JsonSerializer.Serialize(tx),
        });

        reply.Successfull.Should().BeFalse();
        reply.Status.Should().Be(TransactionStatus.Rejected);
        reply.ValidationCode.Should().Be("LICENCE_CATALOGUE_STALE");
    }

    private static HushShared.Blockchain.TransactionModel.States.SignedTransaction<
        HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAssignmentPayload> BuildBaselineLicence()
    {
        var payload = new HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAssignmentPayload(
            HushNode.HushVoting.Licence.Transactions.HushVotingLicenceTransitionIntent.BaselineFree,
            "hushvoting.direct.free",
            "hushvoting-licence-catalogue/v1.0.0");
        var size = HushNode.HushVoting.Licence.Transactions.HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload);
        var unsigned = new HushShared.Blockchain.TransactionModel.States.UnsignedTransaction<
            HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAssignmentPayload>(
            new HushShared.Blockchain.TransactionModel.TransactionId(Guid.NewGuid()),
            HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind,
            new HushShared.Blockchain.Model.Timestamp(DateTime.UtcNow),
            payload,
            size);
        var canonical = new HushNode.HushVoting.Licence.Transactions.HushVotingLicenceCanonicalSerializer()
            .SerializeCanonicalUnsignedJson(new HushShared.Blockchain.TransactionModel.States.SignedTransaction<
                HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAssignmentPayload>(
                unsigned,
                new HushShared.Blockchain.Model.SignatureInfo(FullIdentityTwinTestData.K001SigningAddress, string.Empty)));
        var signature = Olimpo.DigitalSignature.SignMessageCompactBase64(
            canonical, FullIdentityTwinTestData.K001PrivateScalarHex);
        return new HushShared.Blockchain.TransactionModel.States.SignedTransaction<
            HushNode.HushVoting.Licence.Transactions.HushVotingLicenceAssignmentPayload>(
            unsigned,
            new HushShared.Blockchain.Model.SignatureInfo(FullIdentityTwinTestData.K001SigningAddress, signature));
    }

    [Fact]
    public async Task DirectFree_TraversesTheRealPipeline()
    {
        await StartNodeAsync();
        await IndexIdentityAsync();

        // Before any licence: no_active + Direct Free template.
        var noActive = await LicenceClient().GetMyEntitlementAsync(
            new GetMyEntitlementRequest(), await SignedLicenceMetadataAsync());
        noActive.State.Should().Be(LicenceEntitlementState.NoActive);
        noActive.DirectFreeTemplate.TransitionIntent.Should().Be("baseline_free");
        noActive.DirectFreeTemplate.RequestedPlanId.Should().Be("hushvoting.direct.free");

        // Submit the signed Direct Free transaction -> ACCEPTED (mempool only).
        var licence = BuildBaselineLicence();
        var serialized = System.Text.Json.JsonSerializer.Serialize(licence);
        var submit = await BlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = serialized,
        });
        submit.Successfull.Should().BeTrue();
        submit.Status.Should().Be(TransactionStatus.Accepted);

        // AT-LIC-015-009: exact retry while pending -> PENDING, no second mempool item.
        var retry = await BlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = serialized,
        });
        retry.Successfull.Should().BeTrue();
        retry.Status.Should().Be(TransactionStatus.Pending);

        // Mempool acceptance does NOT activate the licence.
        var beforeBlock = await LicenceClient().GetMyEntitlementAsync(
            new GetMyEntitlementRequest(), await SignedLicenceMetadataAsync());
        beforeBlock.State.Should().Be(LicenceEntitlementState.NoActive);

        // Index it.
        await ProduceBlockAsync();
        await Task.Delay(3_000);

        // Query now returns active Direct Free with the originating licence reference.
        var active = await LicenceClient().GetMyEntitlementAsync(
            new GetMyEntitlementRequest(), await SignedLicenceMetadataAsync());
        active.State.Should().Be(LicenceEntitlementState.Active);
        active.Active.PlanId.Should().Be("hushvoting.direct.free");
        active.Active.LicenceReference.Should().Be(licence.TransactionId.Value.ToString());

        // AT-LIC-015-009: after indexing, the exact transaction is ALREADY_EXISTS.
        var afterIndex = await BlockchainClient().SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = serialized,
        });
        afterIndex.Successfull.Should().BeTrue();
        afterIndex.Status.Should().Be(TransactionStatus.AlreadyExists);
    }
}
