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

public class CreateCustomCircleTransactionHandlerTests
{
    [Fact]
    public async Task HandleCreateCustomCircleTransactionAsync_WithValidRequest_ShouldCreateCircleAndSeedOwner()
    {
        var mocker = new AutoMocker();

        var owner = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = new CreateCustomCirclePayload(feedId, owner, "Friends");

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(12));

        mocker.GetMock<HushNode.Identity.Storage.IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner", "owner", owner, new EncryptKeys().PublicKey, true, new BlockIndex(1)));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetCustomCircleCountByOwnerAsync(owner))
            .ReturnsAsync(0);
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.OwnerHasCustomCircleNamedAsync(owner, "friends"))
            .ReturnsAsync(false);

        var createdFeeds = new List<GroupFeed>();
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()))
            .Callback<GroupFeed>(createdFeeds.Add)
            .Returns(Task.CompletedTask);

        var handler = mocker.CreateInstance<CreateCustomCircleTransactionHandler>();
        var validTx = BuildValidated(payload, owner);

        await handler.HandleCreateCustomCircleTransactionAsync(validTx);

        createdFeeds.Should().ContainSingle();
        var created = createdFeeds[0];
        created.FeedId.Should().Be(feedId);
        created.Title.Should().Be("Friends");
        created.IsInnerCircle.Should().BeFalse();
        created.OwnerPublicAddress.Should().Be(owner);
        created.Participants.Should().ContainSingle(p => p.ParticipantPublicAddress == owner && p.ParticipantType == ParticipantType.Owner);
        created.KeyGenerations.Should().ContainSingle(kg => kg.KeyGeneration == 0);

        mocker.GetMock<IUserFeedsCacheService>()
            .Verify(x => x.AddFeedToUserCacheAsync(owner, feedId), Times.Once);
    }

    [Fact]
    public async Task HandleCreateCustomCircleTransactionAsync_WhenNameAlreadyExists_ShouldThrow()
    {
        var mocker = new AutoMocker();

        var owner = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = new CreateCustomCirclePayload(feedId, owner, "Friends");

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(12));

        mocker.GetMock<HushNode.Identity.Storage.IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner", "owner", owner, "owner-encrypt", true, new BlockIndex(1)));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetCustomCircleCountByOwnerAsync(owner))
            .ReturnsAsync(1);
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.OwnerHasCustomCircleNamedAsync(owner, "friends"))
            .ReturnsAsync(true);

        var handler = mocker.CreateInstance<CreateCustomCircleTransactionHandler>();
        var validTx = BuildValidated(payload, owner);

        var act = () => handler.HandleCreateCustomCircleTransactionAsync(validTx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("CUSTOM_CIRCLE_ALREADY_EXISTS");

        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.CreateGroupFeed(It.IsAny<GroupFeed>()), Times.Never);
    }

    private static ValidatedTransaction<CreateCustomCirclePayload> BuildValidated(
        CreateCustomCirclePayload payload,
        string signatory)
    {
        var unsignedTx = new UnsignedTransaction<CreateCustomCirclePayload>(
            new TransactionId(Guid.NewGuid()),
            CreateCustomCirclePayloadHandler.CreateCustomCirclePayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            100);

        var signed = new SignedTransaction<CreateCustomCirclePayload>(
            unsignedTx,
            new SignatureInfo(signatory, "sig"));

        return new ValidatedTransaction<CreateCustomCirclePayload>(
            signed,
            new SignatureInfo("validator", "sig"));
    }
}
