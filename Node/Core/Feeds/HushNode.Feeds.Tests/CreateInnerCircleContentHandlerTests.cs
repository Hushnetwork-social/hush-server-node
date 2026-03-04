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

public class CreateInnerCircleContentHandlerTests
{
    [Fact]
    public void ValidateAndSign_WithValidOwner_ShouldSucceed()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var owner = TestDataFactory.CreateAddress();
        var payload = new CreateInnerCirclePayload(owner);
        var tx = CreateSigned(payload, owner);

        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.OwnerHasInnerCircleAsync(owner))
            .ReturnsAsync(false);

        mocker.GetMock<HushNode.Identity.Storage.IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner", "owner", owner, "owner-encrypt", true, new HushShared.Blockchain.BlockModel.BlockIndex(1)));

        mocker.GetMock<IMemPoolService>()
            .Setup(x => x.PeekPendingValidatedTransactions())
            .Returns([]);

        var handler = mocker.CreateInstance<CreateInnerCircleContentHandler>();

        var result = handler.ValidateAndSign(tx);

        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WhenInnerCircleAlreadyExists_ShouldReturnNull()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var owner = TestDataFactory.CreateAddress();
        var payload = new CreateInnerCirclePayload(owner);
        var tx = CreateSigned(payload, owner);

        mocker.GetMock<HushNode.Identity.Storage.IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner", "owner", owner, "owner-encrypt", true, new HushShared.Blockchain.BlockModel.BlockIndex(1)));

        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.OwnerHasInnerCircleAsync(owner))
            .ReturnsAsync(true);

        var handler = mocker.CreateInstance<CreateInnerCircleContentHandler>();

        var result = handler.ValidateAndSign(tx);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WhenSameOwnerCreateIsPendingInMemPool_ShouldReturnNull()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var owner = TestDataFactory.CreateAddress();
        var payload = new CreateInnerCirclePayload(owner);
        var tx = CreateSigned(payload, owner);

        mocker.GetMock<HushNode.Identity.Storage.IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner", "owner", owner, "owner-encrypt", true, new HushShared.Blockchain.BlockModel.BlockIndex(1)));

        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.OwnerHasInnerCircleAsync(owner))
            .ReturnsAsync(false);

        var pendingTransactions = new[]
        {
            (AbstractTransaction)CreateValidated(payload, owner)
        };

        mocker.GetMock<IMemPoolService>()
            .Setup(x => x.PeekPendingValidatedTransactions())
            .Returns(pendingTransactions);

        var handler = mocker.CreateInstance<CreateInnerCircleContentHandler>();

        var result = handler.ValidateAndSign(tx);

        result.Should().BeNull();
    }

    private static SignedTransaction<CreateInnerCirclePayload> CreateSigned(CreateInnerCirclePayload payload, string signatory)
    {
        var unsigned = new UnsignedTransaction<CreateInnerCirclePayload>(
            new TransactionId(Guid.NewGuid()),
            CreateInnerCirclePayloadHandler.CreateInnerCirclePayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            100);

        return new SignedTransaction<CreateInnerCirclePayload>(
            unsigned,
            new SignatureInfo(signatory, "sig"));
    }

    private static ValidatedTransaction<CreateInnerCirclePayload> CreateValidated(CreateInnerCirclePayload payload, string signatory)
    {
        var signed = CreateSigned(payload, signatory);
        return new ValidatedTransaction<CreateInnerCirclePayload>(
            signed,
            new SignatureInfo("validator", "validator-sig"));
    }
}
