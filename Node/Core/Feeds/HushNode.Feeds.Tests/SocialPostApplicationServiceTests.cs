using FluentAssertions;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Moq;
using Xunit;

namespace HushNode.Feeds.Tests;

public class SocialPostApplicationServiceTests
{
    [Fact]
    public async Task CreateSocialPostAsync_PrivateWithoutCircles_ShouldFail()
    {
        var service = CreateService();

        var response = await service.CreateSocialPostAsync(new CreateSocialPostRequest
        {
            PostId = Guid.NewGuid().ToString(),
            AuthorPublicAddress = "owner",
            Content = "private post",
            Audience = new SocialPostAudienceProto
            {
                Visibility = SocialPostVisibilityProto.SocialPostVisibilityPrivate
            }
        });

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(SocialPostContractErrorCode.PrivateAudienceRequiresAtLeastOneCircle.ToString());
    }

    [Fact]
    public async Task CreateSocialPostAsync_PrivateWithMissingCircle_ShouldFailAtomically()
    {
        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        var blockchainCache = CreateBlockchainCache();
        var notificationService = CreateNotificationService();
        var service = new SocialPostApplicationService(feedsStorage.Object, blockchainCache.Object, notificationService.Object);

        var circleId = Guid.NewGuid();
        feedsStorage
            .Setup(x => x.GetGroupFeedAsync(It.Is<FeedId>(f => f.Value == circleId)))
            .ReturnsAsync((GroupFeed?)null);

        var response = await service.CreateSocialPostAsync(new CreateSocialPostRequest
        {
            PostId = Guid.NewGuid().ToString(),
            AuthorPublicAddress = "owner-address",
            Content = "private post",
            Audience = new SocialPostAudienceProto
            {
                Visibility = SocialPostVisibilityProto.SocialPostVisibilityPrivate,
                CircleFeedIds = { circleId.ToString() }
            }
        });

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("SOCIAL_POST_CIRCLE_INVALID");
    }

    [Fact]
    public async Task CreateSocialPostAsync_PrivateWithOverlappingCircles_ShouldAllowAuthorizedViewerOnce()
    {
        const string owner = "owner-address";
        const string sharedMember = "member-shared";

        var feedA = Guid.NewGuid();
        var feedB = Guid.NewGuid();
        var postId = Guid.NewGuid();

        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        var blockchainCache = CreateBlockchainCache();
        var notificationService = CreateNotificationService();
        var service = new SocialPostApplicationService(feedsStorage.Object, blockchainCache.Object, notificationService.Object);

        feedsStorage
            .Setup(x => x.GetGroupFeedAsync(It.Is<FeedId>(f => f.Value == feedA)))
            .ReturnsAsync(new GroupFeed(
                new FeedId(feedA),
                "Circle A",
                "",
                false,
                new BlockIndex(1),
                0,
                null,
                false,
                owner));
        feedsStorage
            .Setup(x => x.GetGroupFeedAsync(It.Is<FeedId>(f => f.Value == feedB)))
            .ReturnsAsync(new GroupFeed(
                new FeedId(feedB),
                "Circle B",
                "",
                false,
                new BlockIndex(1),
                0,
                null,
                false,
                owner));

        feedsStorage
            .Setup(x => x.GetActiveParticipantsAsync(It.Is<FeedId>(f => f.Value == feedA)))
            .ReturnsAsync(new[]
            {
                new GroupFeedParticipantEntity(new FeedId(feedA), owner, ParticipantType.Owner, new BlockIndex(1), null, null),
                new GroupFeedParticipantEntity(new FeedId(feedA), sharedMember, ParticipantType.Member, new BlockIndex(1), null, null)
            });
        feedsStorage
            .Setup(x => x.GetActiveParticipantsAsync(It.Is<FeedId>(f => f.Value == feedB)))
            .ReturnsAsync(new[]
            {
                new GroupFeedParticipantEntity(new FeedId(feedB), owner, ParticipantType.Owner, new BlockIndex(1), null, null),
                new GroupFeedParticipantEntity(new FeedId(feedB), sharedMember, ParticipantType.Member, new BlockIndex(1), null, null)
            });

        var createResponse = await service.CreateSocialPostAsync(new CreateSocialPostRequest
        {
            PostId = postId.ToString(),
            AuthorPublicAddress = owner,
            Content = "hello circles",
            Audience = new SocialPostAudienceProto
            {
                Visibility = SocialPostVisibilityProto.SocialPostVisibilityPrivate,
                CircleFeedIds = { feedA.ToString(), feedB.ToString() }
            }
        });

        createResponse.Success.Should().BeTrue();
        notificationService.Verify(x => x.NotifyPostCreatedAsync(
            owner,
            "hello circles",
            true,
            postId.ToString(),
            It.Is<IReadOnlyCollection<string>>(addresses =>
                addresses.Contains(owner) &&
                addresses.Contains(sharedMember))),
            Times.Once);

        var permalinkResponse = await service.GetSocialPostPermalinkAsync(new GetSocialPostPermalinkRequest
        {
            PostId = postId.ToString(),
            IsAuthenticated = true,
            RequesterPublicAddress = sharedMember
        });

        permalinkResponse.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateAllowed);
        permalinkResponse.Content.Should().Be("hello circles");
    }

    [Fact]
    public async Task GetSocialPostPermalinkAsync_PrivateGuest_ShouldReturnGuestDeniedGenericOg()
    {
        var owner = "owner-address";
        var circleId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        var blockchainCache = CreateBlockchainCache();
        var notificationService = CreateNotificationService();
        var service = new SocialPostApplicationService(feedsStorage.Object, blockchainCache.Object, notificationService.Object);

        feedsStorage
            .Setup(x => x.GetGroupFeedAsync(It.Is<FeedId>(f => f.Value == circleId)))
            .ReturnsAsync(new GroupFeed(
                new FeedId(circleId),
                "Inner Circle",
                "",
                false,
                new BlockIndex(1),
                0,
                null,
                true,
                owner));
        feedsStorage
            .Setup(x => x.GetActiveParticipantsAsync(It.Is<FeedId>(f => f.Value == circleId)))
            .ReturnsAsync(new[]
            {
                new GroupFeedParticipantEntity(new FeedId(circleId), owner, ParticipantType.Owner, new BlockIndex(1), null, null)
            });

        var createResponse = await service.CreateSocialPostAsync(new CreateSocialPostRequest
        {
            PostId = postId.ToString(),
            AuthorPublicAddress = owner,
            Content = "private content",
            Audience = new SocialPostAudienceProto
            {
                Visibility = SocialPostVisibilityProto.SocialPostVisibilityPrivate,
                CircleFeedIds = { circleId.ToString() }
            }
        });
        createResponse.Success.Should().BeTrue();

        var response = await service.GetSocialPostPermalinkAsync(new GetSocialPostPermalinkRequest
        {
            PostId = postId.ToString(),
            IsAuthenticated = false
        });

        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateGuestDenied);
        response.DenialKind.Should().Be(SocialPermalinkDenialKindProto.SocialPermalinkDenialKindGuestCreateAccount);
        response.PrimaryCtaLabel.Should().Be("Create account");
        response.PrimaryCtaRoute.Should().Be("/register");
        response.OpenGraph.IsGenericPrivate.Should().BeTrue();
        response.OpenGraph.CacheControl.Should().Be("no-store");
        response.AuthorPublicAddress.Should().BeNullOrEmpty();
        response.Content.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task GetSocialPostPermalinkAsync_PrivateUnauthorizedUser_ShouldReturnUnauthorizedDenied()
    {
        var owner = "owner-address";
        var circleId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        var blockchainCache = CreateBlockchainCache();
        var notificationService = CreateNotificationService();
        var service = new SocialPostApplicationService(feedsStorage.Object, blockchainCache.Object, notificationService.Object);

        feedsStorage
            .Setup(x => x.GetGroupFeedAsync(It.Is<FeedId>(f => f.Value == circleId)))
            .ReturnsAsync(new GroupFeed(
                new FeedId(circleId),
                "Inner Circle",
                "",
                false,
                new BlockIndex(1),
                0,
                null,
                true,
                owner));
        feedsStorage
            .Setup(x => x.GetActiveParticipantsAsync(It.Is<FeedId>(f => f.Value == circleId)))
            .ReturnsAsync(new[]
            {
                new GroupFeedParticipantEntity(new FeedId(circleId), owner, ParticipantType.Owner, new BlockIndex(1), null, null)
            });

        var createResponse = await service.CreateSocialPostAsync(new CreateSocialPostRequest
        {
            PostId = postId.ToString(),
            AuthorPublicAddress = owner,
            Content = "private content",
            Audience = new SocialPostAudienceProto
            {
                Visibility = SocialPostVisibilityProto.SocialPostVisibilityPrivate,
                CircleFeedIds = { circleId.ToString() }
            }
        });
        createResponse.Success.Should().BeTrue();

        var response = await service.GetSocialPostPermalinkAsync(new GetSocialPostPermalinkRequest
        {
            PostId = postId.ToString(),
            IsAuthenticated = true,
            RequesterPublicAddress = "unauthorized-user"
        });

        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateUnauthorizedDenied);
        response.ErrorCode.Should().Be("SOCIAL_POST_ACCESS_DENIED");
        response.DenialKind.Should().Be(SocialPermalinkDenialKindProto.SocialPermalinkDenialKindUnauthorizedRequestAccess);
        response.PrimaryCtaLabel.Should().Be("Request access");
        response.PrimaryCtaRoute.Should().Be("/social/following");
        response.OpenGraph.IsGenericPrivate.Should().BeTrue();
    }

    private static SocialPostApplicationService CreateService()
    {
        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Loose);
        var blockchainCache = CreateBlockchainCache();
        var notificationService = CreateNotificationService();
        return new SocialPostApplicationService(feedsStorage.Object, blockchainCache.Object, notificationService.Object);
    }

    private static Mock<IBlockchainCache> CreateBlockchainCache()
    {
        var blockchainCache = new Mock<IBlockchainCache>();
        blockchainCache.SetupGet(x => x.LastBlockIndex).Returns(new BlockIndex(100));
        return blockchainCache;
    }

    private static Mock<ISocialPostNotificationService> CreateNotificationService()
    {
        var notificationService = new Mock<ISocialPostNotificationService>();
        notificationService
            .Setup(x => x.NotifyPostCreatedAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(Task.CompletedTask);
        return notificationService;
    }
}
