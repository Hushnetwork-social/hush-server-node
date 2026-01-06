using FluentAssertions;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushNode.Identity;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Olimpo;
using Xunit;

namespace HushNode.Notifications.Tests;

/// <summary>
/// Tests for NotificationEventHandler - handles new feed message events and triggers notifications.
/// Tests verify that both regular feeds and group feeds are handled correctly.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class NotificationEventHandlerTests
{
    #region Regular Feed Tests

    [Fact]
    public async Task HandleAsync_RegularFeed_NotifiesAllParticipantsExceptSender()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = new FeedId(Guid.NewGuid());
        var senderAddress = "sender-address";
        var participant1Address = "participant1-address";
        var participant2Address = "participant2-address";

        var feed = CreateFeed(feedId, senderAddress, participant1Address, participant2Address);
        var feedMessage = CreateFeedMessage(feedId, senderAddress);

        SetupFeedsStorageService(mocker, feed, groupFeed: null);
        SetupIdentityService(mocker, senderAddress, "Sender Name");
        SetupNotificationService(mocker);
        SetupUnreadTrackingService(mocker);

        var sut = mocker.CreateInstance<NotificationEventHandler>();
        var evt = new NewFeedMessageCreatedEvent(feedMessage);

        // Act
        await sut.HandleAsync(evt);

        // Assert - Should notify participant1 and participant2, but NOT sender
        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                participant1Address,
                feedId.ToString(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);

        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                participant2Address,
                feedId.ToString(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);

        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                senderAddress,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_RegularFeed_IncrementsUnreadCountForRecipients()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = new FeedId(Guid.NewGuid());
        var senderAddress = "sender-address";
        var participantAddress = "participant-address";

        var feed = CreateFeed(feedId, senderAddress, participantAddress);
        var feedMessage = CreateFeedMessage(feedId, senderAddress);

        SetupFeedsStorageService(mocker, feed, groupFeed: null);
        SetupIdentityService(mocker, senderAddress, "Sender Name");
        SetupNotificationService(mocker);
        SetupUnreadTrackingService(mocker);

        var sut = mocker.CreateInstance<NotificationEventHandler>();
        var evt = new NewFeedMessageCreatedEvent(feedMessage);

        // Act
        await sut.HandleAsync(evt);

        // Assert
        mocker.GetMock<IUnreadTrackingService>()
            .Verify(x => x.IncrementUnreadAsync(participantAddress, feedId.ToString()), Times.Once);

        mocker.GetMock<IUnreadTrackingService>()
            .Verify(x => x.IncrementUnreadAsync(senderAddress, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_RegularFeed_UsesSenderDisplayName()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = new FeedId(Guid.NewGuid());
        var senderAddress = "sender-address";
        var participantAddress = "participant-address";
        var expectedSenderName = "Alice";

        var feed = CreateFeed(feedId, senderAddress, participantAddress);
        var feedMessage = CreateFeedMessage(feedId, senderAddress);

        SetupFeedsStorageService(mocker, feed, groupFeed: null);
        SetupIdentityService(mocker, senderAddress, expectedSenderName);
        SetupNotificationService(mocker);
        SetupUnreadTrackingService(mocker);

        var sut = mocker.CreateInstance<NotificationEventHandler>();
        var evt = new NewFeedMessageCreatedEvent(feedMessage);

        // Act
        await sut.HandleAsync(evt);

        // Assert
        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                participantAddress,
                feedId.ToString(),
                expectedSenderName,
                It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Group Feed Tests

    [Fact]
    public async Task HandleAsync_GroupFeed_NotifiesActiveParticipantsExceptSender()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = new FeedId(Guid.NewGuid());
        var senderAddress = "sender-address";
        var participant1Address = "participant1-address";
        var participant2Address = "participant2-address";

        var groupFeed = CreateGroupFeed(feedId, senderAddress, participant1Address, participant2Address);
        var feedMessage = CreateFeedMessage(feedId, senderAddress);

        SetupFeedsStorageService(mocker, regularFeed: null, groupFeed);
        SetupIdentityService(mocker, senderAddress, "Sender Name");
        SetupNotificationService(mocker);
        SetupUnreadTrackingService(mocker);

        var sut = mocker.CreateInstance<NotificationEventHandler>();
        var evt = new NewFeedMessageCreatedEvent(feedMessage);

        // Act
        await sut.HandleAsync(evt);

        // Assert - Should notify participant1 and participant2, but NOT sender
        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                participant1Address,
                feedId.ToString(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);

        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                participant2Address,
                feedId.ToString(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);

        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                senderAddress,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_GroupFeed_ExcludesLeftParticipants()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = new FeedId(Guid.NewGuid());
        var senderAddress = "sender-address";
        var activeParticipantAddress = "active-participant-address";
        var leftParticipantAddress = "left-participant-address";

        var groupFeed = CreateGroupFeedWithLeftMember(
            feedId, senderAddress, activeParticipantAddress, leftParticipantAddress);
        var feedMessage = CreateFeedMessage(feedId, senderAddress);

        SetupFeedsStorageService(mocker, regularFeed: null, groupFeed);
        SetupIdentityService(mocker, senderAddress, "Sender Name");
        SetupNotificationService(mocker);
        SetupUnreadTrackingService(mocker);

        var sut = mocker.CreateInstance<NotificationEventHandler>();
        var evt = new NewFeedMessageCreatedEvent(feedMessage);

        // Act
        await sut.HandleAsync(evt);

        // Assert - Should only notify active participant, NOT the one who left
        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                activeParticipantAddress,
                feedId.ToString(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);

        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                leftParticipantAddress,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_GroupFeed_ExcludesBannedParticipants()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = new FeedId(Guid.NewGuid());
        var senderAddress = "sender-address";
        var activeParticipantAddress = "active-participant-address";
        var bannedParticipantAddress = "banned-participant-address";

        var groupFeed = CreateGroupFeedWithBannedMember(
            feedId, senderAddress, activeParticipantAddress, bannedParticipantAddress);
        var feedMessage = CreateFeedMessage(feedId, senderAddress);

        SetupFeedsStorageService(mocker, regularFeed: null, groupFeed);
        SetupIdentityService(mocker, senderAddress, "Sender Name");
        SetupNotificationService(mocker);
        SetupUnreadTrackingService(mocker);

        var sut = mocker.CreateInstance<NotificationEventHandler>();
        var evt = new NewFeedMessageCreatedEvent(feedMessage);

        // Act
        await sut.HandleAsync(evt);

        // Assert - Should only notify active participant, NOT the banned one
        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                activeParticipantAddress,
                feedId.ToString(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);

        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                bannedParticipantAddress,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_GroupFeed_IncrementsUnreadCountForActiveRecipients()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = new FeedId(Guid.NewGuid());
        var senderAddress = "sender-address";
        var participantAddress = "participant-address";

        var groupFeed = CreateGroupFeed(feedId, senderAddress, participantAddress);
        var feedMessage = CreateFeedMessage(feedId, senderAddress);

        SetupFeedsStorageService(mocker, regularFeed: null, groupFeed);
        SetupIdentityService(mocker, senderAddress, "Sender Name");
        SetupNotificationService(mocker);
        SetupUnreadTrackingService(mocker);

        var sut = mocker.CreateInstance<NotificationEventHandler>();
        var evt = new NewFeedMessageCreatedEvent(feedMessage);

        // Act
        await sut.HandleAsync(evt);

        // Assert
        mocker.GetMock<IUnreadTrackingService>()
            .Verify(x => x.IncrementUnreadAsync(participantAddress, feedId.ToString()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_GroupFeed_IncludesBlockedParticipants()
    {
        // Arrange - Blocked members can still receive messages, they just can't send
        var mocker = new AutoMocker();
        var feedId = new FeedId(Guid.NewGuid());
        var senderAddress = "sender-address";
        var blockedParticipantAddress = "blocked-participant-address";

        var groupFeed = CreateGroupFeedWithBlockedMember(feedId, senderAddress, blockedParticipantAddress);
        var feedMessage = CreateFeedMessage(feedId, senderAddress);

        SetupFeedsStorageService(mocker, regularFeed: null, groupFeed);
        SetupIdentityService(mocker, senderAddress, "Sender Name");
        SetupNotificationService(mocker);
        SetupUnreadTrackingService(mocker);

        var sut = mocker.CreateInstance<NotificationEventHandler>();
        var evt = new NewFeedMessageCreatedEvent(feedMessage);

        // Act
        await sut.HandleAsync(evt);

        // Assert - Blocked members should still receive notifications
        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                blockedParticipantAddress,
                feedId.ToString(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task HandleAsync_FeedNotFound_DoesNotThrow()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = new FeedId(Guid.NewGuid());
        var feedMessage = CreateFeedMessage(feedId, "sender-address");

        SetupFeedsStorageService(mocker, regularFeed: null, groupFeed: null);
        SetupIdentityService(mocker, "sender-address", "Sender Name");

        var sut = mocker.CreateInstance<NotificationEventHandler>();
        var evt = new NewFeedMessageCreatedEvent(feedMessage);

        // Act
        var act = async () => await sut.HandleAsync(evt);

        // Assert
        await act.Should().NotThrowAsync();
        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_IdentityLookupFails_UsesTruncatedAddress()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = new FeedId(Guid.NewGuid());
        var senderAddress = "sender-address-that-is-long";
        var participantAddress = "participant-address";

        var feed = CreateFeed(feedId, senderAddress, participantAddress);
        var feedMessage = CreateFeedMessage(feedId, senderAddress);

        SetupFeedsStorageService(mocker, feed, groupFeed: null);
        mocker.GetMock<IIdentityService>()
            .Setup(x => x.RetrieveIdentityAsync(senderAddress))
            .ThrowsAsync(new Exception("Identity not found"));
        SetupNotificationService(mocker);
        SetupUnreadTrackingService(mocker);

        var sut = mocker.CreateInstance<NotificationEventHandler>();
        var evt = new NewFeedMessageCreatedEvent(feedMessage);

        // Act
        await sut.HandleAsync(evt);

        // Assert - Should use truncated address as fallback
        mocker.GetMock<INotificationService>()
            .Verify(x => x.PublishNewMessageAsync(
                participantAddress,
                feedId.ToString(),
                "sender-add...",
                It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_MessageContentTruncated_To255Characters()
    {
        // Arrange
        var mocker = new AutoMocker();
        var feedId = new FeedId(Guid.NewGuid());
        var senderAddress = "sender-address";
        var participantAddress = "participant-address";
        var longContent = new string('A', 500);

        var feed = CreateFeed(feedId, senderAddress, participantAddress);
        var feedMessage = CreateFeedMessage(feedId, senderAddress, longContent);

        SetupFeedsStorageService(mocker, feed, groupFeed: null);
        SetupIdentityService(mocker, senderAddress, "Sender Name");
        SetupNotificationService(mocker);
        SetupUnreadTrackingService(mocker);

        string? capturedPreview = null;
        mocker.GetMock<INotificationService>()
            .Setup(x => x.PublishNewMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string, string>((_, _, _, preview) => capturedPreview = preview)
            .Returns(Task.CompletedTask);

        var sut = mocker.CreateInstance<NotificationEventHandler>();
        var evt = new NewFeedMessageCreatedEvent(feedMessage);

        // Act
        await sut.HandleAsync(evt);

        // Assert
        capturedPreview.Should().NotBeNull();
        capturedPreview!.Length.Should().BeLessOrEqualTo(255);
        capturedPreview.Should().EndWith("...");
    }

    #endregion

    #region Helper Methods

    private static Feed CreateFeed(FeedId feedId, params string[] participantAddresses)
    {
        var feed = new Feed(feedId, "Test Feed", FeedType.Chat, new BlockIndex(100));
        feed.Participants = participantAddresses
            .Select(addr => new FeedParticipant(feedId, addr, ParticipantType.Member, "encrypted-key") { Feed = feed })
            .ToArray();
        return feed;
    }

    private static GroupFeed CreateGroupFeed(FeedId feedId, params string[] participantAddresses)
    {
        var groupFeed = new GroupFeed(feedId, "Test Group", "Description", false, new BlockIndex(100), 0);
        groupFeed.Participants = participantAddresses
            .Select(addr => new GroupFeedParticipantEntity(feedId, addr, ParticipantType.Member, new BlockIndex(100)))
            .ToList();
        return groupFeed;
    }

    private static GroupFeed CreateGroupFeedWithLeftMember(
        FeedId feedId, string senderAddress, string activeAddress, string leftAddress)
    {
        var groupFeed = new GroupFeed(feedId, "Test Group", "Description", false, new BlockIndex(100), 0);
        groupFeed.Participants = new List<GroupFeedParticipantEntity>
        {
            new(feedId, senderAddress, ParticipantType.Admin, new BlockIndex(100)),
            new(feedId, activeAddress, ParticipantType.Member, new BlockIndex(100)),
            new(feedId, leftAddress, ParticipantType.Member, new BlockIndex(100)) { LeftAtBlock = new BlockIndex(150) }
        };
        return groupFeed;
    }

    private static GroupFeed CreateGroupFeedWithBannedMember(
        FeedId feedId, string senderAddress, string activeAddress, string bannedAddress)
    {
        var groupFeed = new GroupFeed(feedId, "Test Group", "Description", false, new BlockIndex(100), 0);
        groupFeed.Participants = new List<GroupFeedParticipantEntity>
        {
            new(feedId, senderAddress, ParticipantType.Admin, new BlockIndex(100)),
            new(feedId, activeAddress, ParticipantType.Member, new BlockIndex(100)),
            new(feedId, bannedAddress, ParticipantType.Banned, new BlockIndex(100))
        };
        return groupFeed;
    }

    private static GroupFeed CreateGroupFeedWithBlockedMember(
        FeedId feedId, string senderAddress, string blockedAddress)
    {
        var groupFeed = new GroupFeed(feedId, "Test Group", "Description", false, new BlockIndex(100), 0);
        groupFeed.Participants = new List<GroupFeedParticipantEntity>
        {
            new(feedId, senderAddress, ParticipantType.Admin, new BlockIndex(100)),
            new(feedId, blockedAddress, ParticipantType.Blocked, new BlockIndex(100))
        };
        return groupFeed;
    }

    private static FeedMessage CreateFeedMessage(FeedId feedId, string senderAddress, string content = "Test message")
    {
        return new FeedMessage(
            new FeedMessageId(Guid.NewGuid()),
            feedId,
            content,
            senderAddress,
            Timestamp.Current,
            new BlockIndex(100));
    }

    private static void SetupFeedsStorageService(AutoMocker mocker, Feed? regularFeed, GroupFeed? groupFeed)
    {
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetFeedByIdAsync(It.IsAny<FeedId>()))
            .ReturnsAsync(regularFeed);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetGroupFeedAsync(It.IsAny<FeedId>()))
            .ReturnsAsync(groupFeed);
    }

    private static void SetupIdentityService(AutoMocker mocker, string publicAddress, string displayName)
    {
        var profile = new Profile(
            displayName,
            displayName.Length > 10 ? displayName.Substring(0, 10) : displayName,
            publicAddress,
            "encrypt-key",
            true,
            new BlockIndex(100));

        mocker.GetMock<IIdentityService>()
            .Setup(x => x.RetrieveIdentityAsync(publicAddress))
            .ReturnsAsync(profile);
    }

    private static void SetupNotificationService(AutoMocker mocker)
    {
        mocker.GetMock<INotificationService>()
            .Setup(x => x.PublishNewMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(Task.CompletedTask);
    }

    private static void SetupUnreadTrackingService(AutoMocker mocker)
    {
        mocker.GetMock<IUnreadTrackingService>()
            .Setup(x => x.IncrementUnreadAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(1);
    }

    #endregion
}
