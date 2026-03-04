using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushNode.MemPool;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

public class CreateCustomCircleContentHandlerTests
{
    [Fact]
    public void ValidateAndSign_WithValidRequest_ShouldSucceed()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var owner = TestDataFactory.CreateAddress();
        var payload = new CreateCustomCirclePayload(TestDataFactory.CreateFeedId(), owner, "Close Friends");
        var tx = CreateSigned(payload, owner);

        mocker.GetMock<HushNode.Identity.Storage.IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner", "owner", owner, "owner-encrypt", true, new HushShared.Blockchain.BlockModel.BlockIndex(1)));

        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.GetCustomCircleCountByOwnerAsync(owner))
            .ReturnsAsync(2);
        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.OwnerHasCustomCircleNamedAsync(owner, "close friends"))
            .ReturnsAsync(false);
        mocker.GetMock<IMemPoolService>()
            .Setup(x => x.PeekPendingValidatedTransactions())
            .Returns([]);

        var handler = mocker.CreateInstance<CreateCustomCircleContentHandler>();

        var result = handler.ValidateAndSign(tx);

        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WhenNameIsInvalid_ShouldReturnNull()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var owner = TestDataFactory.CreateAddress();
        var payload = new CreateCustomCirclePayload(TestDataFactory.CreateFeedId(), owner, "x");
        var tx = CreateSigned(payload, owner);

        var handler = mocker.CreateInstance<CreateCustomCircleContentHandler>();

        var result = handler.ValidateAndSign(tx);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WhenSameOwnerAndNamePendingInMempool_ShouldReturnNull()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var owner = TestDataFactory.CreateAddress();
        var payload = new CreateCustomCirclePayload(TestDataFactory.CreateFeedId(), owner, "Close Friends");
        var tx = CreateSigned(payload, owner);

        mocker.GetMock<HushNode.Identity.Storage.IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner", "owner", owner, "owner-encrypt", true, new HushShared.Blockchain.BlockModel.BlockIndex(1)));

        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.GetCustomCircleCountByOwnerAsync(owner))
            .ReturnsAsync(0);
        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.OwnerHasCustomCircleNamedAsync(owner, "close friends"))
            .ReturnsAsync(false);

        var pendingTransactions = new[]
        {
            (AbstractTransaction)CreateValidated(new CreateCustomCirclePayload(TestDataFactory.CreateFeedId(), owner, " close friends "), owner)
        };

        mocker.GetMock<IMemPoolService>()
            .Setup(x => x.PeekPendingValidatedTransactions())
            .Returns(pendingTransactions);

        var handler = mocker.CreateInstance<CreateCustomCircleContentHandler>();

        var result = handler.ValidateAndSign(tx);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void ValidateAndSign_WhenPendingRequestsUseDifferentOwners_ShouldSucceed(int ownerCount)
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var owner = TestDataFactory.CreateAddress();
        var payload = new CreateCustomCirclePayload(TestDataFactory.CreateFeedId(), owner, "Close Friends");
        var tx = CreateSigned(payload, owner);

        mocker.GetMock<HushNode.Identity.Storage.IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner", "owner", owner, "owner-encrypt", true, new HushShared.Blockchain.BlockModel.BlockIndex(1)));

        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.GetCustomCircleCountByOwnerAsync(owner))
            .ReturnsAsync(0);
        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.OwnerHasCustomCircleNamedAsync(owner, "close friends"))
            .ReturnsAsync(false);

        var pendingTransactions = Enumerable.Range(0, ownerCount)
            .Select(_ =>
            {
                var otherOwner = TestDataFactory.CreateAddress();
                var pendingPayload = new CreateCustomCirclePayload(
                    TestDataFactory.CreateFeedId(),
                    otherOwner,
                    "close friends");
                return (AbstractTransaction)CreateValidated(pendingPayload, otherOwner);
            })
            .ToArray();

        mocker.GetMock<IMemPoolService>()
            .Setup(x => x.PeekPendingValidatedTransactions())
            .Returns(pendingTransactions);

        var handler = mocker.CreateInstance<CreateCustomCircleContentHandler>();

        var result = handler.ValidateAndSign(tx);

        result.Should().NotBeNull("same-name pending requests from different owners must not block this owner");
    }

    private static SignedTransaction<CreateCustomCirclePayload> CreateSigned(CreateCustomCirclePayload payload, string signatory)
    {
        var unsigned = new UnsignedTransaction<CreateCustomCirclePayload>(
            new TransactionId(Guid.NewGuid()),
            CreateCustomCirclePayloadHandler.CreateCustomCirclePayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            100);

        return new SignedTransaction<CreateCustomCirclePayload>(
            unsigned,
            new SignatureInfo(signatory, "sig"));
    }

    private static ValidatedTransaction<CreateCustomCirclePayload> CreateValidated(CreateCustomCirclePayload payload, string signatory)
    {
        var signed = CreateSigned(payload, signatory);
        return new ValidatedTransaction<CreateCustomCirclePayload>(
            signed,
            new SignatureInfo("validator", "validator-sig"));
    }
}
