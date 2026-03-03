using FluentAssertions;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Moq;
using Moq.AutoMock;
using Olimpo;
using Xunit;

namespace HushNode.Feeds.Tests;

public class CreateInnerCircleTransactionHandlerTests
{
    [Fact]
    public async Task HandleCreateInnerCircleTransactionAsync_WithValidOwner_ShouldPersistInnerCircle()
    {
        var mocker = new AutoMocker();

        var owner = TestDataFactory.CreateAddress();

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        mocker.GetMock<HushNode.Identity.Storage.IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner", "owner", owner, new EncryptKeys().PublicKey, true, new BlockIndex(1)));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Returns(Task.CompletedTask);

        mocker.GetMock<IUserFeedsCacheService>()
            .Setup(x => x.AddFeedToUserCacheAsync(owner, It.IsAny<FeedId>()))
            .Returns(Task.CompletedTask);

        var handler = mocker.CreateInstance<CreateInnerCircleTransactionHandler>();
        var tx = BuildValidated(new CreateInnerCirclePayload(owner), owner);

        await handler.HandleCreateInnerCircleTransactionAsync(tx);

        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.CreateGroupFeed(It.Is<GroupFeed>(g => g.IsInnerCircle && g.OwnerPublicAddress == owner)), Times.Once);
    }

    private static ValidatedTransaction<CreateInnerCirclePayload> BuildValidated(
        CreateInnerCirclePayload payload,
        string signatory)
    {
        var unsignedTx = new UnsignedTransaction<CreateInnerCirclePayload>(
            new TransactionId(Guid.NewGuid()),
            CreateInnerCirclePayloadHandler.CreateInnerCirclePayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            50);

        var signed = new SignedTransaction<CreateInnerCirclePayload>(
            unsignedTx,
            new SignatureInfo(signatory, "sig"));

        return new ValidatedTransaction<CreateInnerCirclePayload>(
            signed,
            new SignatureInfo("validator", "sig"));
    }
}
