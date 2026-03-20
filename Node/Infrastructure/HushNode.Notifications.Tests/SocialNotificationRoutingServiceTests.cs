using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Notifications.Models;
using HushNode.PushNotifications;
using HushNode.PushNotifications.Models;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Notifications.Tests;

public sealed class SocialNotificationRoutingServiceTests
{
    [Fact]
    public async Task RoutePostCreatedAsync_OpenPost_DeliversToFollower()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        var owner = "owner-address";
        var follower = "follower-address";
        var innerCircleId = new FeedId(Guid.NewGuid());

        SetupIdentity(mocker, owner, "Owner");
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(CreatePost(postId, owner, SocialPostVisibility.Open));
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetInnerCircleByOwnerAsync(owner))
            .ReturnsAsync(new GroupFeed(innerCircleId, "Inner Circle", "", false, new BlockIndex(1), 0, null, true, owner));
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveParticipantsAsync(innerCircleId))
            .ReturnsAsync(
            [
                new GroupFeedParticipantEntity(innerCircleId, owner, ParticipantType.Owner, new BlockIndex(1)),
                new GroupFeedParticipantEntity(innerCircleId, follower, ParticipantType.Member, new BlockIndex(1))
            ]);
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialFollowStateAsync(follower, owner))
            .ReturnsAsync(new SocialFollowStateResolution(true, true));
        mocker.GetMock<ISocialNotificationStateService>()
            .Setup(x => x.GetPreferencesAsync(follower, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialNotificationPreferences { OpenActivityEnabled = true, CloseActivityEnabled = true });
        mocker.GetMock<ISocialNotificationStateService>()
            .Setup(x => x.StoreNotificationAsync(
                It.Is<SocialNotificationItem>(item =>
                    item.RecipientUserId == follower &&
                    item.Kind == SocialNotificationKind.NewPost &&
                    item.VisibilityClass == SocialNotificationVisibilityClass.Open &&
                    item.Body == "hello open world"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mocker.GetMock<IConnectionTracker>()
            .Setup(x => x.IsUserOnlineAsync(follower))
            .ReturnsAsync(false);
        mocker.GetMock<IPushDeliveryService>()
            .Setup(x => x.SendPushAsync(
                follower,
                It.Is<PushPayload>(payload => payload.Body == "hello open world")))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(mocker);

        await sut.RoutePostCreatedAsync(postId);

        mocker.GetMock<ISocialNotificationStateService>().VerifyAll();
        mocker.GetMock<IPushDeliveryService>().VerifyAll();
    }

    [Fact]
    public async Task RoutePostCreatedAsync_PrivatePost_SuppressesWhenAllMatchedCirclesMuted()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        var owner = "owner-address";
        var recipient = "recipient-address";
        var circleA = new FeedId(Guid.NewGuid());
        var circleB = new FeedId(Guid.NewGuid());
        var post = CreatePost(postId, owner, SocialPostVisibility.Private, circleA, circleB);

        SetupIdentity(mocker, owner, "Owner");
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(post);
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveParticipantsAsync(circleA))
            .ReturnsAsync(
            [
                new GroupFeedParticipantEntity(circleA, recipient, ParticipantType.Member, new BlockIndex(1))
            ]);
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetActiveParticipantsAsync(circleB))
            .ReturnsAsync(
            [
                new GroupFeedParticipantEntity(circleB, recipient, ParticipantType.Member, new BlockIndex(1))
            ]);
        mocker.GetMock<ISocialNotificationStateService>()
            .Setup(x => x.GetPreferencesAsync(recipient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialNotificationPreferences
            {
                OpenActivityEnabled = true,
                CloseActivityEnabled = true,
                CircleMutes =
                [
                    new SocialCircleMuteState { CircleId = circleA.ToString(), IsMuted = true },
                    new SocialCircleMuteState { CircleId = circleB.ToString(), IsMuted = true }
                ]
            });

        var sut = CreateSut(mocker);

        await sut.RoutePostCreatedAsync(postId);

        mocker.GetMock<ISocialNotificationStateService>()
            .Verify(x => x.StoreNotificationAsync(It.IsAny<SocialNotificationItem>(), It.IsAny<CancellationToken>()), Times.Never);
        mocker.GetMock<IPushDeliveryService>()
            .Verify(x => x.SendPushAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);
    }

    [Fact]
    public async Task RouteThreadMessageCreatedAsync_Reply_DeduplicatesPostAndParentOwnerOverlap()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        var owner = "owner-address";
        var actor = "actor-address";
        var parentCommentId = new FeedMessageId(Guid.NewGuid());
        var replyId = new FeedMessageId(Guid.NewGuid());
        var post = CreatePost(postId, owner, SocialPostVisibility.Open);
        var parentComment = new FeedMessage(parentCommentId, new FeedId(postId), "parent", owner, Timestamp.Current, new BlockIndex(10));
        var reply = new FeedMessage(replyId, new FeedId(postId), "reply", actor, Timestamp.Current, new BlockIndex(11), ReplyToMessageId: parentCommentId);

        SetupIdentity(mocker, actor, "Actor");
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(post);
        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.GetFeedMessageByIdAsync(parentCommentId))
            .ReturnsAsync(parentComment);
        mocker.GetMock<ISocialNotificationStateService>()
            .Setup(x => x.GetPreferencesAsync(owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialNotificationPreferences());
        mocker.GetMock<ISocialNotificationStateService>()
            .Setup(x => x.StoreNotificationAsync(
                It.Is<SocialNotificationItem>(item =>
                    item.RecipientUserId == owner &&
                    item.Kind == SocialNotificationKind.Reply &&
                    item.ParentCommentId == parentCommentId.ToString()),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mocker.GetMock<IConnectionTracker>()
            .Setup(x => x.IsUserOnlineAsync(owner))
            .ReturnsAsync(false);
        mocker.GetMock<IPushDeliveryService>()
            .Setup(x => x.SendPushAsync(owner, It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(mocker);

        await sut.RouteThreadMessageCreatedAsync(reply);

        mocker.GetMock<ISocialNotificationStateService>()
            .Verify(x => x.StoreNotificationAsync(It.IsAny<SocialNotificationItem>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RouteReactionCreatedAsync_PrivatePost_UsesPrivacySafePushBody()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        var owner = "owner-address";
        var actor = "actor-address";
        var circleId = new FeedId(Guid.NewGuid());
        var post = CreatePost(postId, owner, SocialPostVisibility.Private, circleId);

        SetupIdentity(mocker, actor, "Actor");
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostByReactionScopeIdAsync(postId))
            .ReturnsAsync(post);
        mocker.GetMock<ISocialNotificationStateService>()
            .Setup(x => x.GetPreferencesAsync(owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialNotificationPreferences { CloseActivityEnabled = true });
        SocialNotificationItem? stored = null;
        mocker.GetMock<ISocialNotificationStateService>()
            .Setup(x => x.StoreNotificationAsync(It.IsAny<SocialNotificationItem>(), It.IsAny<CancellationToken>()))
            .Callback<SocialNotificationItem, CancellationToken>((item, _) => stored = item)
            .Returns(Task.CompletedTask);
        mocker.GetMock<IConnectionTracker>()
            .Setup(x => x.IsUserOnlineAsync(owner))
            .ReturnsAsync(false);

        PushPayload? pushPayload = null;
        mocker.GetMock<IPushDeliveryService>()
            .Setup(x => x.SendPushAsync(owner, It.IsAny<PushPayload>()))
            .Callback<string, PushPayload>((_, payload) => pushPayload = payload)
            .Returns(Task.CompletedTask);

        var sut = CreateSut(mocker);

        await sut.RouteReactionCreatedAsync(actor, new FeedId(postId), new FeedMessageId(postId));

        stored.Should().NotBeNull();
        stored!.IsPrivatePreviewSuppressed.Should().BeTrue();
        pushPayload.Should().NotBeNull();
        pushPayload!.Body.Should().Be("reacted to your private post");
        pushPayload.Data!["visibility"].Should().Be("private");
    }

    [Fact]
    public async Task RouteReactionCreatedAsync_PrivateComment_SuppressesWhenAccessIsLostBeforeDelivery()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        var commentId = new FeedMessageId(Guid.NewGuid());
        var owner = "post-owner";
        var recipient = "comment-owner";
        var actor = "actor-address";
        var circleId = new FeedId(Guid.NewGuid());
        var post = CreatePost(postId, owner, SocialPostVisibility.Private, circleId);
        var comment = new FeedMessage(commentId, new FeedId(postId), "comment", recipient, Timestamp.Current, new BlockIndex(9));

        SetupIdentity(mocker, actor, "Actor");
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostByReactionScopeIdAsync(commentId.Value))
            .ReturnsAsync((SocialPostEntity?)null);
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(post);
        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.GetFeedMessageByIdAsync(commentId))
            .ReturnsAsync(comment);
        mocker.GetMock<IFeedsStorageService>()
            .SetupSequence(x => x.GetActiveParticipantsAsync(circleId))
            .ReturnsAsync(
            [
                new GroupFeedParticipantEntity(circleId, recipient, ParticipantType.Member, new BlockIndex(1))
            ])
            .ReturnsAsync(Array.Empty<GroupFeedParticipantEntity>());
        mocker.GetMock<ISocialNotificationStateService>()
            .Setup(x => x.GetPreferencesAsync(recipient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialNotificationPreferences { CloseActivityEnabled = true });

        var sut = CreateSut(mocker);

        await sut.RouteReactionCreatedAsync(actor, new FeedId(postId), commentId);

        mocker.GetMock<ISocialNotificationStateService>()
            .Verify(x => x.StoreNotificationAsync(It.IsAny<SocialNotificationItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RouteReactionCreatedAsync_SelfAction_DoesNotStoreNotification()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        var actor = "owner-address";
        var post = CreatePost(postId, actor, SocialPostVisibility.Open);

        SetupIdentity(mocker, actor, "Owner");
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostByReactionScopeIdAsync(postId))
            .ReturnsAsync(post);

        var sut = CreateSut(mocker);

        await sut.RouteReactionCreatedAsync(actor, new FeedId(postId), new FeedMessageId(postId));

        mocker.GetMock<ISocialNotificationStateService>()
            .Verify(x => x.StoreNotificationAsync(It.IsAny<SocialNotificationItem>(), It.IsAny<CancellationToken>()), Times.Never);
        mocker.GetMock<IPushDeliveryService>()
            .Verify(x => x.SendPushAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);
    }

    private static SocialNotificationRoutingService CreateSut(AutoMocker mocker)
    {
        return new SocialNotificationRoutingService(
            mocker.GetMock<IFeedsStorageService>().Object,
            mocker.GetMock<IFeedMessageStorageService>().Object,
            mocker.GetMock<HushNode.Identity.Storage.IIdentityService>().Object,
            mocker.GetMock<IConnectionTracker>().Object,
            mocker.GetMock<INotificationService>().Object,
            mocker.GetMock<IPushDeliveryService>().Object,
            mocker.GetMock<ISocialNotificationStateService>().Object,
            NullLogger<SocialNotificationRoutingService>.Instance);
    }

    private static SocialPostEntity CreatePost(
        Guid postId,
        string author,
        SocialPostVisibility visibility,
        params FeedId[] circleIds)
    {
        var post = new SocialPostEntity
        {
            PostId = postId,
            ReactionScopeId = postId,
            AuthorPublicAddress = author,
            Content = "hello open world",
            AudienceVisibility = visibility,
            CreatedAtBlock = new BlockIndex(1),
            CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        foreach (var circleId in circleIds)
        {
            post.AudienceCircles.Add(new SocialPostAudienceCircleEntity
            {
                PostId = postId,
                CircleFeedId = circleId,
                Post = post
            });
        }

        return post;
    }

    private static void SetupIdentity(AutoMocker mocker, string publicAddress, string alias)
    {
        mocker.GetMock<HushNode.Identity.Storage.IIdentityService>()
            .Setup(x => x.RetrieveIdentityAsync(publicAddress))
            .ReturnsAsync(new Profile(alias, "AL", publicAddress, "enc", true, new BlockIndex(1)));
    }
}
