using FluentAssertions;
using HushNetwork.proto;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Feeds.Model;
using Moq;
using Olimpo;
using Xunit;

namespace HushNode.Feeds.Tests;

public class SocialPostApplicationServiceTests
{
    [Fact]
    public async Task CreateSocialPostAsync_ShouldRequireBlockchainPath()
    {
        var service = CreateService(out _);

        var response = await service.CreateSocialPostAsync(new CreateSocialPostRequest
        {
            PostId = Guid.NewGuid().ToString(),
            AuthorPublicAddress = "owner",
            Content = "content"
        });

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("SOCIAL_POST_BLOCKCHAIN_REQUIRED");
    }

    [Fact]
    public async Task GetSocialPostPermalinkAsync_PrivateGuest_ShouldReturnGuestDeniedGenericOg()
    {
        var postId = Guid.NewGuid();
        var service = CreateService(out var feedsStorage);
        var post = BuildPrivatePost(postId, "owner-address", Guid.NewGuid());

        feedsStorage
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(post);

        var response = await service.GetSocialPostPermalinkAsync(new GetSocialPostPermalinkRequest
        {
            PostId = postId.ToString("D"),
            IsAuthenticated = false
        });

        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateGuestDenied);
        response.PostId.Should().Be(postId.ToString("D"));
        response.DenialKind.Should().Be(SocialPermalinkDenialKindProto.SocialPermalinkDenialKindGuestCreateAccount);
        response.PrimaryCtaLabel.Should().Be("Create account");
        response.PrimaryCtaRoute.Should().Be($"/auth?returnTo=%2Fsocial%2Fpost%2F{postId:D}");
        response.OpenGraph.IsGenericPrivate.Should().BeTrue();
        response.AuthorPublicAddress.Should().BeNullOrEmpty();
        response.Content.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task GetSocialPostPermalinkAsync_PrivateUnauthorizedUser_ShouldReturnUnauthorizedDenied()
    {
        var postId = Guid.NewGuid();
        var circleId = Guid.NewGuid();
        var service = CreateService(out var feedsStorage);
        var post = BuildPrivatePost(postId, "owner-address", circleId);

        feedsStorage
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(post);
        feedsStorage
            .Setup(x => x.IsUserInAnyActiveCircleAsync(
                "unauthorized-user",
                It.Is<IReadOnlyList<FeedId>>(list => list.Count == 1 && list[0].Value == circleId)))
            .ReturnsAsync(false);

        var response = await service.GetSocialPostPermalinkAsync(new GetSocialPostPermalinkRequest
        {
            PostId = postId.ToString("D"),
            IsAuthenticated = true,
            RequesterPublicAddress = "unauthorized-user"
        });

        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateUnauthorizedDenied);
        response.ErrorCode.Should().Be("SOCIAL_POST_ACCESS_DENIED");
        response.DenialKind.Should().Be(SocialPermalinkDenialKindProto.SocialPermalinkDenialKindUnauthorizedRequestAccess);
    }

    [Fact]
    public async Task GetSocialPostPermalinkAsync_OpenPost_ShouldReturnAllowed()
    {
        var postId = Guid.NewGuid();
        var service = CreateService(out var feedsStorage);

        feedsStorage
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(new SocialPostEntity
            {
                PostId = postId,
                ReactionScopeId = postId,
                AuthorPublicAddress = "owner-address",
                AuthorCommitment = new byte[32],
                Content = "public content",
                AudienceVisibility = SocialPostVisibility.Open,
                CreatedAtBlock = new BlockIndex(99)
            });
        feedsStorage
            .Setup(x => x.GetSocialFollowStateAsync("someone", "owner-address"))
            .ReturnsAsync(new SocialFollowStateResolution(false, true));

        var response = await service.GetSocialPostPermalinkAsync(new GetSocialPostPermalinkRequest
        {
            PostId = postId.ToString("D"),
            IsAuthenticated = true,
            RequesterPublicAddress = "someone"
        });

        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateAllowed);
        response.Content.Should().Be("public content");
        response.ReactionScopeId.Should().Be(postId.ToString("D"));
        response.AuthorCommitment.Length.Should().Be(32);
        response.CanInteract.Should().BeTrue();
        response.FollowState.Should().NotBeNull();
        response.FollowState.IsFollowing.Should().BeFalse();
        response.FollowState.CanFollow.Should().BeTrue();
    }

    [Fact]
    public async Task GetSocialPostPermalinkAsync_ShouldResolveAttachmentsByPostIdAsFeedMessageId()
    {
        var postId = Guid.NewGuid();
        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        var attachmentStorage = new Mock<IAttachmentStorageService>(MockBehavior.Strict);
        var feedMessageStorage = new Mock<IFeedMessageStorageService>(MockBehavior.Strict);

        feedsStorage
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(new SocialPostEntity
            {
                PostId = postId,
                ReactionScopeId = postId,
                AuthorPublicAddress = "owner-address",
                Content = "public content",
                AudienceVisibility = SocialPostVisibility.Open,
                CreatedAtBlock = new BlockIndex(99)
            });
        feedsStorage
            .Setup(x => x.GetSocialFollowStateAsync("someone", "owner-address"))
            .ReturnsAsync(new SocialFollowStateResolution(false, true));

        attachmentStorage
            .Setup(x => x.GetByMessageIdAsync(It.Is<FeedMessageId>(id => id == new FeedMessageId(postId))))
            .ReturnsAsync(Array.Empty<AttachmentEntity>())
            .Verifiable();

        var service = new SocialPostApplicationService(feedsStorage.Object, attachmentStorage.Object, feedMessageStorage.Object);

        var response = await service.GetSocialPostPermalinkAsync(new GetSocialPostPermalinkRequest
        {
            PostId = postId.ToString("D"),
            IsAuthenticated = true,
            RequesterPublicAddress = "someone"
        });

        response.Success.Should().BeTrue();
        attachmentStorage.Verify();
    }

    [Fact]
    public async Task GetSocialFeedWallAsync_ShouldIncludeReplyCountForVisiblePosts()
    {
        var postId = Guid.NewGuid();
        var feedId = new FeedId(postId);
        var service = CreateService(out var feedsStorage, out _, out var feedMessageStorage);

        feedsStorage
            .Setup(x => x.GetLatestSocialPostsAsync(50))
            .ReturnsAsync(new[]
            {
                new SocialPostEntity
                {
                    PostId = postId,
                    ReactionScopeId = postId,
                    AuthorPublicAddress = "owner-address",
                    Content = "public content",
                    AudienceVisibility = SocialPostVisibility.Open,
                    CreatedAtBlock = new BlockIndex(99)
                }
            });
        feedsStorage
            .Setup(x => x.GetSocialFollowStateAsync("viewer-address", "owner-address"))
            .ReturnsAsync(new SocialFollowStateResolution(false, true));

        feedMessageStorage
            .Setup(x => x.RetrieveLastFeedMessagesForFeedAsync(feedId, It.Is<BlockIndex>(blockIndex => blockIndex.Value == 0)))
            .ReturnsAsync(new[]
            {
                BuildFeedMessage(postId, postId, null, 100),
                BuildFeedMessage(postId, Guid.NewGuid(), new FeedMessageId(postId), 101)
            });

        var response = await service.GetSocialFeedWallAsync(new GetSocialFeedWallRequest
        {
            IsAuthenticated = true,
            RequesterPublicAddress = "viewer-address"
        });

        response.Success.Should().BeTrue();
        response.Posts.Should().ContainSingle();
        response.Posts[0].ReplyCount.Should().Be(2);
        response.Posts[0].FollowState.Should().NotBeNull();
        response.Posts[0].FollowState.IsFollowing.Should().BeFalse();
        response.Posts[0].FollowState.CanFollow.Should().BeTrue();
    }

    private static SocialPostEntity BuildPrivatePost(Guid postId, string ownerAddress, Guid circleId)
    {
        var post = new SocialPostEntity
        {
            PostId = postId,
            ReactionScopeId = postId,
            AuthorPublicAddress = ownerAddress,
            AuthorCommitment = new byte[32],
            Content = "private content",
            AudienceVisibility = SocialPostVisibility.Private,
            CreatedAtBlock = new BlockIndex(77)
        };

        post.AudienceCircles.Add(new SocialPostAudienceCircleEntity
        {
            PostId = postId,
            CircleFeedId = new FeedId(circleId),
            Post = post
        });

        return post;
    }

    private static FeedMessage BuildFeedMessage(Guid postId, Guid messageId, FeedMessageId? replyToMessageId, long timestamp) =>
        new(
            new FeedMessageId(messageId),
            new FeedId(postId),
            $"message-{messageId}",
            "author-address",
            new Timestamp(DateTime.UnixEpoch.AddSeconds(timestamp)),
            new BlockIndex(timestamp),
            ReplyToMessageId: replyToMessageId);

    private static SocialPostApplicationService CreateService(out Mock<IFeedsStorageService> feedsStorage)
    {
        return CreateService(out feedsStorage, out _, out _);
    }

    private static SocialPostApplicationService CreateService(
        out Mock<IFeedsStorageService> feedsStorage,
        out Mock<IAttachmentStorageService> attachmentStorage,
        out Mock<IFeedMessageStorageService> feedMessageStorage)
    {
        feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        attachmentStorage = new Mock<IAttachmentStorageService>(MockBehavior.Strict);
        feedMessageStorage = new Mock<IFeedMessageStorageService>(MockBehavior.Strict);
        attachmentStorage
            .Setup(x => x.GetByMessageIdAsync(It.IsAny<FeedMessageId>()))
            .ReturnsAsync(Array.Empty<AttachmentEntity>());
        feedMessageStorage
            .Setup(x => x.RetrieveLastFeedMessagesForFeedAsync(It.IsAny<FeedId>(), It.IsAny<BlockIndex>()))
            .ReturnsAsync(Array.Empty<FeedMessage>());
        feedsStorage
            .Setup(x => x.GetSocialFollowStateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new SocialFollowStateResolution(false, true));

        return new SocialPostApplicationService(feedsStorage.Object, attachmentStorage.Object, feedMessageStorage.Object);
    }
}
