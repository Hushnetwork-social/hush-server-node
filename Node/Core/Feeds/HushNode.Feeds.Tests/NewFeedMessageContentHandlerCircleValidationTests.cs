using FluentAssertions;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

public class NewFeedMessageContentHandlerCircleValidationTests
{
    [Fact]
    public void ValidateAndSign_WhenTargetIsInnerCircle_ShouldReject()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        MockServices.ConfigureBlockchainCache(mocker, currentBlockIndex: 200);

        var sender = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = new NewFeedMessagePayload(
            new FeedMessageId(Guid.NewGuid()),
            feedId,
            "message",
            KeyGeneration: 0);
        var tx = CreateSigned(payload, sender);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(new GroupFeed(feedId, "Inner Circle", "", false, new BlockIndex(10), 0, IsInnerCircle: true, OwnerPublicAddress: sender));

        var sut = mocker.CreateInstance<NewFeedMessageContentHandler>();

        var result = sut.ValidateAndSign(tx);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WhenTargetIsCustomCircle_ShouldReject()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        MockServices.ConfigureBlockchainCache(mocker, currentBlockIndex: 200);

        var sender = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = new NewFeedMessagePayload(
            new FeedMessageId(Guid.NewGuid()),
            feedId,
            "message",
            KeyGeneration: 0);
        var tx = CreateSigned(payload, sender);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(new GroupFeed(feedId, "Friends", "owner-managed custom circle", false, new BlockIndex(10), 0, IsInnerCircle: false, OwnerPublicAddress: sender));

        var sut = mocker.CreateInstance<NewFeedMessageContentHandler>();

        var result = sut.ValidateAndSign(tx);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WhenTargetIsRegularGroup_ShouldAccept()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        MockServices.ConfigureBlockchainCache(mocker, currentBlockIndex: 200);

        var sender = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = new NewFeedMessagePayload(
            new FeedMessageId(Guid.NewGuid()),
            feedId,
            "message",
            KeyGeneration: 0);
        var tx = CreateSigned(payload, sender);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(new GroupFeed(feedId, "General Group", "", true, new BlockIndex(10), 0, IsInnerCircle: false, OwnerPublicAddress: null));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.CanMemberSendMessagesAsync(feedId, sender))
            .ReturnsAsync(true);

        var sut = mocker.CreateInstance<NewFeedMessageContentHandler>();

        var result = sut.ValidateAndSign(tx);

        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WhenTargetIsGroupAndKeyGenerationIsMissing_ShouldReject()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);
        MockServices.ConfigureBlockchainCache(mocker, currentBlockIndex: 200);

        var sender = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var payload = new NewFeedMessagePayload(
            new FeedMessageId(Guid.NewGuid()),
            feedId,
            "message",
            KeyGeneration: null);
        var tx = CreateSigned(payload, sender);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(new GroupFeed(feedId, "General Group", "", true, new BlockIndex(10), 0, IsInnerCircle: false, OwnerPublicAddress: null));

        var sut = mocker.CreateInstance<NewFeedMessageContentHandler>();

        var result = sut.ValidateAndSign(tx);

        result.Should().BeNull();
    }

    private static SignedTransaction<NewFeedMessagePayload> CreateSigned(NewFeedMessagePayload payload, string signatory)
    {
        var unsigned = new UnsignedTransaction<NewFeedMessagePayload>(
            new TransactionId(Guid.NewGuid()),
            NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind,
            Timestamp.Current,
            payload,
            200);

        return new SignedTransaction<NewFeedMessagePayload>(unsigned, new SignatureInfo(signatory, "sig"));
    }
}
