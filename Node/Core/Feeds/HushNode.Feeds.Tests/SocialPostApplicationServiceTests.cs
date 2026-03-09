using FluentAssertions;
using HushNetwork.proto;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
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
        response.DenialKind.Should().Be(SocialPermalinkDenialKindProto.SocialPermalinkDenialKindGuestCreateAccount);
        response.PrimaryCtaLabel.Should().Be("Create account");
        response.PrimaryCtaRoute.Should().Be("/register");
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
    }

    [Fact]
    public async Task GetSocialPostPermalinkAsync_ShouldResolveAttachmentsByPostIdAsFeedMessageId()
    {
        var postId = Guid.NewGuid();
        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        var attachmentStorage = new Mock<IAttachmentStorageService>(MockBehavior.Strict);

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

        attachmentStorage
            .Setup(x => x.GetByMessageIdAsync(It.Is<FeedMessageId>(id => id == new FeedMessageId(postId))))
            .ReturnsAsync(Array.Empty<AttachmentEntity>())
            .Verifiable();

        var service = new SocialPostApplicationService(feedsStorage.Object, attachmentStorage.Object);

        var response = await service.GetSocialPostPermalinkAsync(new GetSocialPostPermalinkRequest
        {
            PostId = postId.ToString("D"),
            IsAuthenticated = true,
            RequesterPublicAddress = "someone"
        });

        response.Success.Should().BeTrue();
        attachmentStorage.Verify();
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

    private static SocialPostApplicationService CreateService(out Mock<IFeedsStorageService> feedsStorage)
    {
        feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        var attachmentStorage = new Mock<IAttachmentStorageService>(MockBehavior.Strict);
        attachmentStorage
            .Setup(x => x.GetByMessageIdAsync(It.IsAny<FeedMessageId>()))
            .ReturnsAsync(Array.Empty<AttachmentEntity>());

        return new SocialPostApplicationService(feedsStorage.Object, attachmentStorage.Object);
    }
}
