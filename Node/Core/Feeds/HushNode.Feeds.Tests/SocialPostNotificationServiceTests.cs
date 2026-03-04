using FluentAssertions;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushNode.Notifications;
using HushNode.PushNotifications;
using HushNode.PushNotifications.Models;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HushNode.Feeds.Tests;

public class SocialPostNotificationServiceTests
{
    [Fact]
    public async Task NotifyPostCreatedAsync_PrivateAudience_ShouldSendGenericPreviewToOnlineMembers()
    {
        const string owner = "owner-address";
        const string memberA = "member-a";
        const string memberB = "member-b";

        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        var identityService = new Mock<IIdentityService>(MockBehavior.Strict);
        var connectionTracker = new Mock<IConnectionTracker>(MockBehavior.Strict);
        var notificationService = new Mock<INotificationService>(MockBehavior.Strict);
        var pushDeliveryService = new Mock<IPushDeliveryService>(MockBehavior.Strict);
        var logger = new Mock<ILogger<SocialPostNotificationService>>();

        identityService
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner Alias", "OA", owner, "enc", true, new BlockIndex(1)));

        connectionTracker.Setup(x => x.IsUserOnlineAsync(memberA)).ReturnsAsync(true);
        connectionTracker.Setup(x => x.IsUserOnlineAsync(memberB)).ReturnsAsync(true);

        notificationService
            .Setup(x => x.PublishNewMessageAsync(memberA, "social", "Owner Alias", "New private post", "HushSocial"))
            .Returns(Task.CompletedTask);
        notificationService
            .Setup(x => x.PublishNewMessageAsync(memberB, "social", "Owner Alias", "New private post", "HushSocial"))
            .Returns(Task.CompletedTask);

        var sut = new SocialPostNotificationService(
            feedsStorage.Object,
            identityService.Object,
            connectionTracker.Object,
            notificationService.Object,
            pushDeliveryService.Object,
            logger.Object);

        await sut.NotifyPostCreatedAsync(
            owner,
            "Highly sensitive private content",
            isPrivate: true,
            postId: Guid.NewGuid().ToString("D"),
            authorizedPrivateViewers: new[] { owner, memberA, memberB, memberA });

        notificationService.VerifyAll();
        pushDeliveryService.Verify(
            x => x.SendPushAsync(It.IsAny<string>(), It.IsAny<PushPayload>()),
            Times.Never);
    }

    [Fact]
    public async Task NotifyPostCreatedAsync_OpenAudience_ShouldResolveInnerCircleAndSendPushToOfflineMember()
    {
        const string owner = "owner-address";
        const string member = "member-a";
        var innerCircleId = new FeedId(Guid.NewGuid());

        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        var identityService = new Mock<IIdentityService>(MockBehavior.Strict);
        var connectionTracker = new Mock<IConnectionTracker>(MockBehavior.Strict);
        var notificationService = new Mock<INotificationService>(MockBehavior.Strict);
        var pushDeliveryService = new Mock<IPushDeliveryService>(MockBehavior.Strict);
        var logger = new Mock<ILogger<SocialPostNotificationService>>();

        feedsStorage
            .Setup(x => x.GetInnerCircleByOwnerAsync(owner))
            .ReturnsAsync(new GroupFeed(
                innerCircleId,
                "Inner Circle",
                "",
                false,
                new BlockIndex(1),
                0,
                null,
                true,
                owner));
        feedsStorage
            .Setup(x => x.GetActiveParticipantsAsync(innerCircleId))
            .ReturnsAsync(new[]
            {
                new GroupFeedParticipantEntity(innerCircleId, owner, ParticipantType.Owner, new BlockIndex(1), null, null),
                new GroupFeedParticipantEntity(innerCircleId, member, ParticipantType.Member, new BlockIndex(1), null, null)
            });

        identityService
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner Alias", "OA", owner, "enc", true, new BlockIndex(1)));

        connectionTracker.Setup(x => x.IsUserOnlineAsync(member)).ReturnsAsync(false);

        pushDeliveryService
            .Setup(x => x.SendPushAsync(
                member,
                It.Is<PushPayload>(p =>
                    p.Title == "Owner Alias" &&
                    p.Body != "New private post" &&
                    p.Data != null &&
                    p.Data["type"] == "social_post" &&
                    p.Data["visibility"] == "open")))
            .Returns(Task.CompletedTask);

        var sut = new SocialPostNotificationService(
            feedsStorage.Object,
            identityService.Object,
            connectionTracker.Object,
            notificationService.Object,
            pushDeliveryService.Object,
            logger.Object);

        await sut.NotifyPostCreatedAsync(
            owner,
            new string('x', 200) + "Open update",
            isPrivate: false,
            postId: Guid.NewGuid().ToString("D"),
            authorizedPrivateViewers: Array.Empty<string>());

        pushDeliveryService.VerifyAll();
        notificationService.Verify(
            x => x.PublishNewMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task NotifyPostCreatedAsync_PrivateAudience_OfflineRecipient_ShouldUsePrivacySafePushBody()
    {
        const string owner = "owner-address";
        const string member = "member-a";

        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        var identityService = new Mock<IIdentityService>(MockBehavior.Strict);
        var connectionTracker = new Mock<IConnectionTracker>(MockBehavior.Strict);
        var notificationService = new Mock<INotificationService>(MockBehavior.Strict);
        var pushDeliveryService = new Mock<IPushDeliveryService>(MockBehavior.Strict);
        var logger = new Mock<ILogger<SocialPostNotificationService>>();

        identityService
            .Setup(x => x.RetrieveIdentityAsync(owner))
            .ReturnsAsync(new Profile("Owner Alias", "OA", owner, "enc", true, new BlockIndex(1)));
        connectionTracker.Setup(x => x.IsUserOnlineAsync(member)).ReturnsAsync(false);

        PushPayload? capturedPayload = null;
        pushDeliveryService
            .Setup(x => x.SendPushAsync(member, It.IsAny<PushPayload>()))
            .Callback<string, PushPayload>((_, payload) => capturedPayload = payload)
            .Returns(Task.CompletedTask);

        var sut = new SocialPostNotificationService(
            feedsStorage.Object,
            identityService.Object,
            connectionTracker.Object,
            notificationService.Object,
            pushDeliveryService.Object,
            logger.Object);

        await sut.NotifyPostCreatedAsync(
            owner,
            "Sensitive secret should never appear in push preview",
            isPrivate: true,
            postId: "post-123",
            authorizedPrivateViewers: new[] { owner, member });

        capturedPayload.Should().NotBeNull();
        capturedPayload!.Body.Should().Be("New private post");
        capturedPayload.Data.Should().NotBeNull();
        capturedPayload.Data!["visibility"].Should().Be("private");
    }
}
