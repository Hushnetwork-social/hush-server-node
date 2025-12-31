using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Feeds.Model;
using Moq;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

/// <summary>
/// Tests for GroupFeedInfoProvider - retrieves feed info for Protocol Omega integration.
/// </summary>
public class GroupFeedInfoProviderTests
{
    [Fact]
    public async Task GetFeedPublicKeyAsync_ReturnsValidPoint_ForExistingFeed()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();
        var groupFeed = new GroupFeed(
            feedId,
            "Test Group",
            "Description",
            IsPublic: true,
            new BlockIndex(1),
            CurrentKeyGeneration: 1);

        var feedsStorageMock = new Mock<IFeedsStorageService>();
        feedsStorageMock.Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(groupFeed);

        var feedMessageStorageMock = new Mock<IFeedMessageStorageService>();
        var curve = new BabyJubJubCurve();
        var poseidon = new PoseidonHash();

        var provider = new GroupFeedInfoProvider(
            feedMessageStorageMock.Object,
            feedsStorageMock.Object,
            curve,
            poseidon);

        // Act
        var result = await provider.GetFeedPublicKeyAsync(feedId);

        // Assert
        result.Should().NotBeNull("existing feed should return a public key");
        curve.IsOnCurve(result!).Should().BeTrue("returned point should be on the curve");
    }

    [Fact]
    public async Task GetFeedPublicKeyAsync_ReturnsNull_ForNonExistentFeed()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();

        var feedsStorageMock = new Mock<IFeedsStorageService>();
        feedsStorageMock.Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync((GroupFeed?)null);

        var feedMessageStorageMock = new Mock<IFeedMessageStorageService>();
        var curve = new BabyJubJubCurve();
        var poseidon = new PoseidonHash();

        var provider = new GroupFeedInfoProvider(
            feedMessageStorageMock.Object,
            feedsStorageMock.Object,
            curve,
            poseidon);

        // Act
        var result = await provider.GetFeedPublicKeyAsync(feedId);

        // Assert
        result.Should().BeNull("non-existent feed should return null");
    }

    [Fact]
    public async Task GetFeedPublicKeyAsync_ReturnsSameKey_ForSameFeed()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();
        var groupFeed = new GroupFeed(
            feedId,
            "Test Group",
            "Description",
            IsPublic: true,
            new BlockIndex(1),
            CurrentKeyGeneration: 1);

        var feedsStorageMock = new Mock<IFeedsStorageService>();
        feedsStorageMock.Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(groupFeed);

        var feedMessageStorageMock = new Mock<IFeedMessageStorageService>();
        var curve = new BabyJubJubCurve();
        var poseidon = new PoseidonHash();

        var provider = new GroupFeedInfoProvider(
            feedMessageStorageMock.Object,
            feedsStorageMock.Object,
            curve,
            poseidon);

        // Act
        var result1 = await provider.GetFeedPublicKeyAsync(feedId);
        var result2 = await provider.GetFeedPublicKeyAsync(feedId);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1!.X.Should().Be(result2!.X, "same feed should return same key");
        result1.Y.Should().Be(result2.Y, "same feed should return same key");
    }

    [Fact]
    public async Task GetAuthorCommitmentAsync_ReturnsCommitment_WhenStored()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = TestDataFactory.CreateMessageId();
        var authorCommitment = TestDataFactory.CreateCommitment();

        var message = new FeedMessage(
            messageId,
            feedId,
            "encrypted-content",
            "author-address",
            TestDataFactory.CreateTimestamp(),
            new BlockIndex(1),
            authorCommitment);

        var feedsStorageMock = new Mock<IFeedsStorageService>();
        var feedMessageStorageMock = new Mock<IFeedMessageStorageService>();
        feedMessageStorageMock.Setup(x => x.GetFeedMessageByIdAsync(messageId))
            .ReturnsAsync(message);

        var curve = new BabyJubJubCurve();
        var poseidon = new PoseidonHash();

        var provider = new GroupFeedInfoProvider(
            feedMessageStorageMock.Object,
            feedsStorageMock.Object,
            curve,
            poseidon);

        // Act
        var result = await provider.GetAuthorCommitmentAsync(messageId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(authorCommitment, "should return stored commitment");
    }

    [Fact]
    public async Task GetAuthorCommitmentAsync_ReturnsNull_WhenNotStored()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();
        var messageId = TestDataFactory.CreateMessageId();

        var message = new FeedMessage(
            messageId,
            feedId,
            "encrypted-content",
            "author-address",
            TestDataFactory.CreateTimestamp(),
            new BlockIndex(1),
            null); // No commitment stored

        var feedsStorageMock = new Mock<IFeedsStorageService>();
        var feedMessageStorageMock = new Mock<IFeedMessageStorageService>();
        feedMessageStorageMock.Setup(x => x.GetFeedMessageByIdAsync(messageId))
            .ReturnsAsync(message);

        var curve = new BabyJubJubCurve();
        var poseidon = new PoseidonHash();

        var provider = new GroupFeedInfoProvider(
            feedMessageStorageMock.Object,
            feedsStorageMock.Object,
            curve,
            poseidon);

        // Act
        var result = await provider.GetAuthorCommitmentAsync(messageId);

        // Assert
        result.Should().BeNull("message without commitment should return null");
    }

    [Fact]
    public async Task GetAuthorCommitmentAsync_ReturnsNull_WhenMessageNotFound()
    {
        // Arrange
        var messageId = TestDataFactory.CreateMessageId();

        var feedsStorageMock = new Mock<IFeedsStorageService>();
        var feedMessageStorageMock = new Mock<IFeedMessageStorageService>();
        feedMessageStorageMock.Setup(x => x.GetFeedMessageByIdAsync(messageId))
            .ReturnsAsync((FeedMessage?)null);

        var curve = new BabyJubJubCurve();
        var poseidon = new PoseidonHash();

        var provider = new GroupFeedInfoProvider(
            feedMessageStorageMock.Object,
            feedsStorageMock.Object,
            curve,
            poseidon);

        // Act
        var result = await provider.GetAuthorCommitmentAsync(messageId);

        // Assert
        result.Should().BeNull("non-existent message should return null");
    }
}
