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
        feedsStorageMock.Setup(x => x.GetFeedByIdAsync(feedId))
            .ReturnsAsync(new Feed(feedId, groupFeed.Title, FeedType.Group, new BlockIndex(1)));

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
        feedsStorageMock.Setup(x => x.GetFeedByIdAsync(feedId))
            .ReturnsAsync((Feed?)null);
        feedsStorageMock.Setup(x => x.GetSocialPostAsync(feedId.Value))
            .ReturnsAsync((SocialPostEntity?)null);

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
    public async Task GetFeedPublicKeyAsync_ReturnsValidPoint_ForPrivateSocialPostReactionScope()
    {
        var reactionScopeId = TestDataFactory.CreateFeedId();
        var audienceCircleId = TestDataFactory.CreateFeedId();

        var feedsStorageMock = new Mock<IFeedsStorageService>();
        feedsStorageMock.Setup(x => x.GetFeedByIdAsync(reactionScopeId))
            .ReturnsAsync((Feed?)null);
        feedsStorageMock.Setup(x => x.GetSocialPostAsync(reactionScopeId.Value))
            .ReturnsAsync(new SocialPostEntity
            {
                PostId = reactionScopeId.Value,
                ReactionScopeId = reactionScopeId.Value,
                AuthorPublicAddress = "author-address",
                AuthorCommitment = TestDataFactory.CreateCommitment(),
                Content = "Private post",
                AudienceVisibility = SocialPostVisibility.Private,
                AudienceCircles =
                [
                    new SocialPostAudienceCircleEntity
                    {
                        PostId = reactionScopeId.Value,
                        CircleFeedId = audienceCircleId
                    }
                ]
            });

        var feedMessageStorageMock = new Mock<IFeedMessageStorageService>();
        var curve = new BabyJubJubCurve();
        var poseidon = new PoseidonHash();

        var provider = new GroupFeedInfoProvider(
            feedMessageStorageMock.Object,
            feedsStorageMock.Object,
            curve,
            poseidon);

        var result = await provider.GetFeedPublicKeyAsync(reactionScopeId);

        result.Should().NotBeNull("private social post reaction scope should resolve to an effective feed key");
        curve.IsOnCurve(result!).Should().BeTrue();
    }

    [Fact]
    public async Task GetFeedPublicKeyAsync_ReturnsValidPoint_ForOpenSocialPostReactionScope()
    {
        var reactionScopeId = TestDataFactory.CreateFeedId();

        var feedsStorageMock = new Mock<IFeedsStorageService>();
        feedsStorageMock.Setup(x => x.GetFeedByIdAsync(reactionScopeId))
            .ReturnsAsync((Feed?)null);
        feedsStorageMock.Setup(x => x.GetSocialPostAsync(reactionScopeId.Value))
            .ReturnsAsync(new SocialPostEntity
            {
                PostId = reactionScopeId.Value,
                ReactionScopeId = reactionScopeId.Value,
                AuthorPublicAddress = "author-address",
                AuthorCommitment = TestDataFactory.CreateCommitment(),
                Content = "Open post",
                AudienceVisibility = SocialPostVisibility.Open
            });

        var feedMessageStorageMock = new Mock<IFeedMessageStorageService>();
        var curve = new BabyJubJubCurve();
        var poseidon = new PoseidonHash();

        var provider = new GroupFeedInfoProvider(
            feedMessageStorageMock.Object,
            feedsStorageMock.Object,
            curve,
            poseidon);

        var result = await provider.GetFeedPublicKeyAsync(reactionScopeId);

        result.Should().NotBeNull("open social post reaction scope should resolve to a deterministic public key");
        curve.IsOnCurve(result!).Should().BeTrue();
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
        feedsStorageMock.Setup(x => x.GetFeedByIdAsync(feedId))
            .ReturnsAsync(new Feed(feedId, groupFeed.Title, FeedType.Group, new BlockIndex(1)));

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
    public async Task GetFeedPublicKeyAsync_ReturnsValidPoint_ForExistingChatFeed()
    {
        // Arrange
        var feedId = TestDataFactory.CreateFeedId();
        var chatFeed = new Feed(feedId, "Direct Chat", FeedType.Chat, new BlockIndex(7));

        var feedsStorageMock = new Mock<IFeedsStorageService>();
        feedsStorageMock.Setup(x => x.GetFeedByIdAsync(feedId))
            .ReturnsAsync(chatFeed);

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
        result.Should().NotBeNull("existing chat feed should return a public key");
        curve.IsOnCurve(result!).Should().BeTrue("returned point should be on the curve");
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

    [Fact]
    public async Task GetAuthorCommitmentAsync_ReturnsSocialPostCommitment_WhenMessageLookupMisses()
    {
        var messageId = TestDataFactory.CreateMessageId();
        var authorCommitment = TestDataFactory.CreateCommitment();

        var feedsStorageMock = new Mock<IFeedsStorageService>();
        feedsStorageMock.Setup(x => x.GetSocialPostAsync(messageId.Value))
            .ReturnsAsync(new SocialPostEntity
            {
                PostId = messageId.Value,
                ReactionScopeId = messageId.Value,
                AuthorPublicAddress = "author-address",
                AuthorCommitment = authorCommitment,
                Content = "Private post",
                AudienceVisibility = SocialPostVisibility.Private
            });

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

        var result = await provider.GetAuthorCommitmentAsync(messageId);

        result.Should().BeEquivalentTo(authorCommitment);
    }
}
