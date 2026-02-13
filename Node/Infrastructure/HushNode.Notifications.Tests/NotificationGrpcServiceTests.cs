using FluentAssertions;
using Grpc.Core;
using HushNode.Feeds.Storage;
using HushNode.Notifications.gRPC;
using HushNode.PushNotifications;
using HushNode.Interfaces.Models;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Xunit;
using ProtoTypes = HushNetwork.proto;
using InternalModels = HushNode.Notifications.Models;

namespace HushNode.Notifications.Tests;

/// <summary>
/// Tests for NotificationGrpcService - connection tracking integration.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class NotificationGrpcServiceTests
{
    private const string TestUserId = "test-user-id";

    #region SubscribeToEvents Connection Tracking Tests

    [Fact]
    public async Task SubscribeToEvents_CallsMarkOnlineAsync_AtSubscriptionStart()
    {
        // Arrange
        var mocker = new AutoMocker();
        var cancellationTokenSource = new CancellationTokenSource();
        var connectionTrackerMock = mocker.GetMock<IConnectionTracker>();
        var notificationServiceMock = mocker.GetMock<INotificationService>();

        // Setup notification service to yield nothing and complete immediately
        notificationServiceMock
            .Setup(x => x.SubscribeToEventsAsync(TestUserId, It.IsAny<CancellationToken>()))
            .Returns(CreateEmptyAsyncEnumerable());

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.SubscribeToEventsRequest
        {
            UserId = TestUserId,
            Platform = "web",
            DeviceId = "test-device"
        };

        var responseStreamMock = new Mock<IServerStreamWriter<ProtoTypes.FeedEvent>>();
        var serverCallContext = CreateMockServerCallContext(cancellationTokenSource.Token);

        // Act
        await service.SubscribeToEvents(request, responseStreamMock.Object, serverCallContext);

        // Assert
        connectionTrackerMock.Verify(
            x => x.MarkOnlineAsync(TestUserId, It.IsAny<string>()),
            Times.Once,
            "MarkOnlineAsync should be called once at subscription start");
    }

    [Fact]
    public async Task SubscribeToEvents_CallsMarkOfflineAsync_OnNormalCompletion()
    {
        // Arrange
        var mocker = new AutoMocker();
        var cancellationTokenSource = new CancellationTokenSource();
        var connectionTrackerMock = mocker.GetMock<IConnectionTracker>();
        var notificationServiceMock = mocker.GetMock<INotificationService>();

        notificationServiceMock
            .Setup(x => x.SubscribeToEventsAsync(TestUserId, It.IsAny<CancellationToken>()))
            .Returns(CreateEmptyAsyncEnumerable());

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.SubscribeToEventsRequest
        {
            UserId = TestUserId,
            Platform = "web",
            DeviceId = "test-device"
        };

        var responseStreamMock = new Mock<IServerStreamWriter<ProtoTypes.FeedEvent>>();
        var serverCallContext = CreateMockServerCallContext(cancellationTokenSource.Token);

        // Act
        await service.SubscribeToEvents(request, responseStreamMock.Object, serverCallContext);

        // Assert
        connectionTrackerMock.Verify(
            x => x.MarkOfflineAsync(TestUserId, It.IsAny<string>()),
            Times.Once,
            "MarkOfflineAsync should be called once when subscription ends");
    }

    [Fact]
    public async Task SubscribeToEvents_CallsMarkOfflineAsync_OnCancellation()
    {
        // Arrange
        var mocker = new AutoMocker();
        var cancellationTokenSource = new CancellationTokenSource();
        var connectionTrackerMock = mocker.GetMock<IConnectionTracker>();
        var notificationServiceMock = mocker.GetMock<INotificationService>();

        // Setup notification service to throw OperationCanceledException when cancelled
        notificationServiceMock
            .Setup(x => x.SubscribeToEventsAsync(TestUserId, It.IsAny<CancellationToken>()))
            .Returns(CreateCancellableAsyncEnumerable(cancellationTokenSource.Token));

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.SubscribeToEventsRequest
        {
            UserId = TestUserId,
            Platform = "web",
            DeviceId = "test-device"
        };

        var responseStreamMock = new Mock<IServerStreamWriter<ProtoTypes.FeedEvent>>();
        var serverCallContext = CreateMockServerCallContext(cancellationTokenSource.Token);

        // Act - Cancel immediately after starting
        cancellationTokenSource.Cancel();
        await service.SubscribeToEvents(request, responseStreamMock.Object, serverCallContext);

        // Assert
        connectionTrackerMock.Verify(
            x => x.MarkOfflineAsync(TestUserId, It.IsAny<string>()),
            Times.Once,
            "MarkOfflineAsync should be called even when cancelled");
    }

    [Fact]
    public async Task SubscribeToEvents_UsesSameConnectionId_ForOnlineAndOffline()
    {
        // Arrange
        var mocker = new AutoMocker();
        var cancellationTokenSource = new CancellationTokenSource();
        var connectionTrackerMock = mocker.GetMock<IConnectionTracker>();
        var notificationServiceMock = mocker.GetMock<INotificationService>();

        string? capturedOnlineConnectionId = null;
        string? capturedOfflineConnectionId = null;

        connectionTrackerMock
            .Setup(x => x.MarkOnlineAsync(TestUserId, It.IsAny<string>()))
            .Callback<string, string>((_, connId) => capturedOnlineConnectionId = connId)
            .Returns(Task.CompletedTask);

        connectionTrackerMock
            .Setup(x => x.MarkOfflineAsync(TestUserId, It.IsAny<string>()))
            .Callback<string, string>((_, connId) => capturedOfflineConnectionId = connId)
            .Returns(Task.CompletedTask);

        notificationServiceMock
            .Setup(x => x.SubscribeToEventsAsync(TestUserId, It.IsAny<CancellationToken>()))
            .Returns(CreateEmptyAsyncEnumerable());

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.SubscribeToEventsRequest
        {
            UserId = TestUserId,
            Platform = "web",
            DeviceId = "test-device"
        };

        var responseStreamMock = new Mock<IServerStreamWriter<ProtoTypes.FeedEvent>>();
        var serverCallContext = CreateMockServerCallContext(cancellationTokenSource.Token);

        // Act
        await service.SubscribeToEvents(request, responseStreamMock.Object, serverCallContext);

        // Assert
        capturedOnlineConnectionId.Should().NotBeNullOrEmpty("Connection ID should be generated");
        capturedOfflineConnectionId.Should().NotBeNullOrEmpty("Connection ID should be passed to offline");
        capturedOnlineConnectionId.Should().Be(capturedOfflineConnectionId,
            "Same connection ID should be used for online and offline calls");
    }

    [Fact]
    public async Task SubscribeToEvents_GeneratesUniqueConnectionId_PerSubscription()
    {
        // Arrange
        var mocker = new AutoMocker();
        var connectionTrackerMock = mocker.GetMock<IConnectionTracker>();
        var notificationServiceMock = mocker.GetMock<INotificationService>();

        var capturedConnectionIds = new List<string>();

        connectionTrackerMock
            .Setup(x => x.MarkOnlineAsync(TestUserId, It.IsAny<string>()))
            .Callback<string, string>((_, connId) => capturedConnectionIds.Add(connId))
            .Returns(Task.CompletedTask);

        notificationServiceMock
            .Setup(x => x.SubscribeToEventsAsync(TestUserId, It.IsAny<CancellationToken>()))
            .Returns(CreateEmptyAsyncEnumerable());

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.SubscribeToEventsRequest
        {
            UserId = TestUserId,
            Platform = "web",
            DeviceId = "test-device"
        };

        var responseStreamMock = new Mock<IServerStreamWriter<ProtoTypes.FeedEvent>>();

        // Act - Call twice to get two different connection IDs
        await service.SubscribeToEvents(request, responseStreamMock.Object,
            CreateMockServerCallContext(CancellationToken.None));
        await service.SubscribeToEvents(request, responseStreamMock.Object,
            CreateMockServerCallContext(CancellationToken.None));

        // Assert
        capturedConnectionIds.Should().HaveCount(2, "Two subscriptions should have been made");
        capturedConnectionIds[0].Should().NotBe(capturedConnectionIds[1],
            "Each subscription should have a unique connection ID");
    }

    [Fact]
    public async Task SubscribeToEvents_ConnectionIdIsValidGuid()
    {
        // Arrange
        var mocker = new AutoMocker();
        var connectionTrackerMock = mocker.GetMock<IConnectionTracker>();
        var notificationServiceMock = mocker.GetMock<INotificationService>();

        string? capturedConnectionId = null;

        connectionTrackerMock
            .Setup(x => x.MarkOnlineAsync(TestUserId, It.IsAny<string>()))
            .Callback<string, string>((_, connId) => capturedConnectionId = connId)
            .Returns(Task.CompletedTask);

        notificationServiceMock
            .Setup(x => x.SubscribeToEventsAsync(TestUserId, It.IsAny<CancellationToken>()))
            .Returns(CreateEmptyAsyncEnumerable());

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.SubscribeToEventsRequest
        {
            UserId = TestUserId,
            Platform = "web",
            DeviceId = "test-device"
        };

        var responseStreamMock = new Mock<IServerStreamWriter<ProtoTypes.FeedEvent>>();
        var serverCallContext = CreateMockServerCallContext(CancellationToken.None);

        // Act
        await service.SubscribeToEvents(request, responseStreamMock.Object, serverCallContext);

        // Assert
        capturedConnectionId.Should().NotBeNullOrEmpty();
        Guid.TryParse(capturedConnectionId, out _).Should().BeTrue(
            "Connection ID should be a valid GUID string");
    }

    #endregion

    #region RegisterDeviceToken Tests

    [Fact]
    public async Task RegisterDeviceToken_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var mocker = new AutoMocker();
        mocker.GetMock<IDeviceTokenStorageService>()
            .Setup(x => x.RegisterTokenAsync(
                TestUserId,
                PushPlatform.Android,
                "test-fcm-token",
                "Test Device"))
            .ReturnsAsync(true);

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = ProtoTypes.PushPlatform.Android,
            Token = "test-fcm-token",
            DeviceName = "Test Device"
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.RegisterDeviceToken(request, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().BeEmpty();
        mocker.GetMock<IDeviceTokenStorageService>()
            .Verify(x => x.RegisterTokenAsync(
                TestUserId,
                PushPlatform.Android,
                "test-fcm-token",
                "Test Device"), Times.Once);
    }

    [Fact]
    public async Task RegisterDeviceToken_WithEmptyUserId_ReturnsFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.RegisterDeviceTokenRequest
        {
            UserId = "",
            Platform = ProtoTypes.PushPlatform.Android,
            Token = "test-fcm-token"
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.RegisterDeviceToken(request, context);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("User ID is required");
        mocker.GetMock<IDeviceTokenStorageService>()
            .Verify(x => x.RegisterTokenAsync(
                It.IsAny<string>(),
                It.IsAny<PushPlatform>(),
                It.IsAny<string>(),
                It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task RegisterDeviceToken_WithEmptyToken_ReturnsFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = ProtoTypes.PushPlatform.Android,
            Token = ""
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.RegisterDeviceToken(request, context);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Token is required");
    }

    [Fact]
    public async Task RegisterDeviceToken_WithUnspecifiedPlatform_ReturnsFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = ProtoTypes.PushPlatform.Unspecified,
            Token = "test-fcm-token"
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.RegisterDeviceToken(request, context);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Platform is required");
    }

    [Fact]
    public async Task RegisterDeviceToken_WhenStorageServiceThrows_ReturnsFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        mocker.GetMock<IDeviceTokenStorageService>()
            .Setup(x => x.RegisterTokenAsync(
                It.IsAny<string>(),
                It.IsAny<PushPlatform>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ThrowsAsync(new Exception("Database error"));

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = ProtoTypes.PushPlatform.Android,
            Token = "test-fcm-token"
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.RegisterDeviceToken(request, context);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Internal error registering token");
    }

    [Fact]
    public async Task RegisterDeviceToken_MapsIosPlatformCorrectly()
    {
        // Arrange
        var mocker = new AutoMocker();
        PushPlatform? capturedPlatform = null;
        mocker.GetMock<IDeviceTokenStorageService>()
            .Setup(x => x.RegisterTokenAsync(
                It.IsAny<string>(),
                It.IsAny<PushPlatform>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .Callback<string, PushPlatform, string, string?>((_, p, _, _) => capturedPlatform = p)
            .ReturnsAsync(true);

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = ProtoTypes.PushPlatform.Ios,
            Token = "test-apns-token"
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        await service.RegisterDeviceToken(request, context);

        // Assert
        capturedPlatform.Should().Be(PushPlatform.iOS);
    }

    [Fact]
    public async Task RegisterDeviceToken_MapsWebPlatformCorrectly()
    {
        // Arrange
        var mocker = new AutoMocker();
        PushPlatform? capturedPlatform = null;
        mocker.GetMock<IDeviceTokenStorageService>()
            .Setup(x => x.RegisterTokenAsync(
                It.IsAny<string>(),
                It.IsAny<PushPlatform>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .Callback<string, PushPlatform, string, string?>((_, p, _, _) => capturedPlatform = p)
            .ReturnsAsync(true);

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = ProtoTypes.PushPlatform.Web,
            Token = "test-web-token"
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        await service.RegisterDeviceToken(request, context);

        // Assert
        capturedPlatform.Should().Be(PushPlatform.Web);
    }

    #endregion

    #region UnregisterDeviceToken Tests

    [Fact]
    public async Task UnregisterDeviceToken_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var mocker = new AutoMocker();
        mocker.GetMock<IDeviceTokenStorageService>()
            .Setup(x => x.UnregisterTokenAsync(TestUserId, "test-fcm-token"))
            .ReturnsAsync(true);

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.UnregisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Token = "test-fcm-token"
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.UnregisterDeviceToken(request, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().BeEmpty();
        mocker.GetMock<IDeviceTokenStorageService>()
            .Verify(x => x.UnregisterTokenAsync(TestUserId, "test-fcm-token"), Times.Once);
    }

    [Fact]
    public async Task UnregisterDeviceToken_WithEmptyUserId_ReturnsFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.UnregisterDeviceTokenRequest
        {
            UserId = "",
            Token = "test-fcm-token"
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.UnregisterDeviceToken(request, context);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("User ID is required");
        mocker.GetMock<IDeviceTokenStorageService>()
            .Verify(x => x.UnregisterTokenAsync(
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UnregisterDeviceToken_WithEmptyToken_ReturnsFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.UnregisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Token = ""
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.UnregisterDeviceToken(request, context);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Token is required");
    }

    [Fact]
    public async Task UnregisterDeviceToken_WhenStorageServiceThrows_ReturnsFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        mocker.GetMock<IDeviceTokenStorageService>()
            .Setup(x => x.UnregisterTokenAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Database error"));

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.UnregisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Token = "test-fcm-token"
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.UnregisterDeviceToken(request, context);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Internal error unregistering token");
    }

    #endregion

    #region GetActiveDeviceTokens Tests

    [Fact]
    public async Task GetActiveDeviceTokens_WithValidUserId_ReturnsMappedTokens()
    {
        // Arrange
        var mocker = new AutoMocker();
        var tokens = new List<DeviceToken>
        {
            new() { Token = "token1", Platform = PushPlatform.Android, DeviceName = "Pixel 7" },
            new() { Token = "token2", Platform = PushPlatform.iOS, DeviceName = "iPhone 14" }
        };
        mocker.GetMock<IDeviceTokenStorageService>()
            .Setup(x => x.GetActiveTokensForUserAsync(TestUserId))
            .ReturnsAsync(tokens);

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.GetActiveDeviceTokensRequest { UserId = TestUserId };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.GetActiveDeviceTokens(request, context);

        // Assert
        result.Tokens.Should().HaveCount(2);
        result.Tokens[0].Token.Should().Be("token1");
        result.Tokens[0].Platform.Should().Be(ProtoTypes.PushPlatform.Android);
        result.Tokens[0].DeviceName.Should().Be("Pixel 7");
        result.Tokens[1].Token.Should().Be("token2");
        result.Tokens[1].Platform.Should().Be(ProtoTypes.PushPlatform.Ios);
        result.Tokens[1].DeviceName.Should().Be("iPhone 14");
    }

    [Fact]
    public async Task GetActiveDeviceTokens_WithEmptyUserId_ReturnsEmptyList()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.GetActiveDeviceTokensRequest { UserId = "" };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.GetActiveDeviceTokens(request, context);

        // Assert
        result.Tokens.Should().BeEmpty();
        mocker.GetMock<IDeviceTokenStorageService>()
            .Verify(x => x.GetActiveTokensForUserAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetActiveDeviceTokens_WithNoTokens_ReturnsEmptyList()
    {
        // Arrange
        var mocker = new AutoMocker();
        mocker.GetMock<IDeviceTokenStorageService>()
            .Setup(x => x.GetActiveTokensForUserAsync(TestUserId))
            .ReturnsAsync(new List<DeviceToken>());

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.GetActiveDeviceTokensRequest { UserId = TestUserId };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.GetActiveDeviceTokens(request, context);

        // Assert
        result.Tokens.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveDeviceTokens_WhenStorageServiceThrows_ReturnsEmptyList()
    {
        // Arrange
        var mocker = new AutoMocker();
        mocker.GetMock<IDeviceTokenStorageService>()
            .Setup(x => x.GetActiveTokensForUserAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("Database error"));

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.GetActiveDeviceTokensRequest { UserId = TestUserId };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.GetActiveDeviceTokens(request, context);

        // Assert
        result.Tokens.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveDeviceTokens_MapsWebPlatformCorrectly()
    {
        // Arrange
        var mocker = new AutoMocker();
        var tokens = new List<DeviceToken>
        {
            new() { Token = "web-token", Platform = PushPlatform.Web, DeviceName = "Chrome Browser" }
        };
        mocker.GetMock<IDeviceTokenStorageService>()
            .Setup(x => x.GetActiveTokensForUserAsync(TestUserId))
            .ReturnsAsync(tokens);

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.GetActiveDeviceTokensRequest { UserId = TestUserId };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.GetActiveDeviceTokens(request, context);

        // Assert
        result.Tokens.Should().HaveCount(1);
        result.Tokens[0].Platform.Should().Be(ProtoTypes.PushPlatform.Web);
    }

    [Fact]
    public async Task GetActiveDeviceTokens_HandlesNullDeviceName()
    {
        // Arrange
        var mocker = new AutoMocker();
        var tokens = new List<DeviceToken>
        {
            new() { Token = "token1", Platform = PushPlatform.Android, DeviceName = null }
        };
        mocker.GetMock<IDeviceTokenStorageService>()
            .Setup(x => x.GetActiveTokensForUserAsync(TestUserId))
            .ReturnsAsync(tokens);

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.GetActiveDeviceTokensRequest { UserId = TestUserId };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.GetActiveDeviceTokens(request, context);

        // Assert
        result.Tokens.Should().HaveCount(1);
        result.Tokens[0].DeviceName.Should().BeEmpty();
    }

    #endregion

    #region MarkFeedAsRead Tests (FEAT-051)

    private const string TestFeedId = "11111111-1111-1111-1111-111111111111";  // Valid GUID format for FeedId

    [Fact]
    public async Task MarkFeedAsRead_WithValidInput_ReturnsSuccessAndCallsStorageService()
    {
        // Arrange
        var mocker = new AutoMocker();
        var readPositionStorageServiceMock = mocker.GetMock<IFeedReadPositionStorageService>();
        readPositionStorageServiceMock
            .Setup(x => x.MarkFeedAsReadAsync(
                TestUserId,
                It.IsAny<FeedId>(),
                It.IsAny<BlockIndex>()))
            .ReturnsAsync(true);

        mocker.GetMock<IUnreadTrackingService>()
            .Setup(x => x.MarkFeedAsReadAsync(TestUserId, TestFeedId))
            .Returns(Task.CompletedTask);

        mocker.GetMock<INotificationService>()
            .Setup(x => x.PublishMessagesReadAsync(TestUserId, TestFeedId, 500))
            .Returns(Task.CompletedTask);

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.MarkFeedAsReadRequest
        {
            UserId = TestUserId,
            FeedId = TestFeedId,
            UpToBlockIndex = 500
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.MarkFeedAsRead(request, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().BeEmpty();
        readPositionStorageServiceMock.Verify(
            x => x.MarkFeedAsReadAsync(
                TestUserId,
                It.Is<FeedId>(f => f.ToString() == TestFeedId),
                It.Is<BlockIndex>(b => b.Value == 500)),
            Times.Once);
    }

    [Fact]
    public async Task MarkFeedAsRead_WithZeroBlockIndex_DoesNotCallReadPositionStorageService()
    {
        // Arrange
        var mocker = new AutoMocker();
        var readPositionStorageServiceMock = mocker.GetMock<IFeedReadPositionStorageService>();

        mocker.GetMock<IUnreadTrackingService>()
            .Setup(x => x.MarkFeedAsReadAsync(TestUserId, TestFeedId))
            .Returns(Task.CompletedTask);

        mocker.GetMock<INotificationService>()
            .Setup(x => x.PublishMessagesReadAsync(TestUserId, TestFeedId, 0))
            .Returns(Task.CompletedTask);

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.MarkFeedAsReadRequest
        {
            UserId = TestUserId,
            FeedId = TestFeedId,
            UpToBlockIndex = 0  // Zero means "mark all as read" - legacy behavior
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.MarkFeedAsRead(request, context);

        // Assert
        result.Success.Should().BeTrue();
        readPositionStorageServiceMock.Verify(
            x => x.MarkFeedAsReadAsync(
                It.IsAny<string>(),
                It.IsAny<FeedId>(),
                It.IsAny<BlockIndex>()),
            Times.Never,
            "Storage service should not be called when UpToBlockIndex is 0");
    }

    [Fact]
    public async Task MarkFeedAsRead_WithEmptyFeedId_ReturnsFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.MarkFeedAsReadRequest
        {
            UserId = TestUserId,
            FeedId = "",
            UpToBlockIndex = 500
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.MarkFeedAsRead(request, context);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("FeedId");
        mocker.GetMock<IFeedReadPositionStorageService>()
            .Verify(
                x => x.MarkFeedAsReadAsync(It.IsAny<string>(), It.IsAny<FeedId>(), It.IsAny<BlockIndex>()),
                Times.Never);
    }

    [Fact]
    public async Task MarkFeedAsRead_WithEmptyUserId_ReturnsFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.MarkFeedAsReadRequest
        {
            UserId = "",
            FeedId = TestFeedId,
            UpToBlockIndex = 500
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.MarkFeedAsRead(request, context);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("UserId");
        mocker.GetMock<IFeedReadPositionStorageService>()
            .Verify(
                x => x.MarkFeedAsReadAsync(It.IsAny<string>(), It.IsAny<FeedId>(), It.IsAny<BlockIndex>()),
                Times.Never);
    }

    [Fact]
    public async Task MarkFeedAsRead_WhenStorageServiceThrows_ReturnsFailure()
    {
        // Arrange
        var mocker = new AutoMocker();
        mocker.GetMock<IFeedReadPositionStorageService>()
            .Setup(x => x.MarkFeedAsReadAsync(
                It.IsAny<string>(),
                It.IsAny<FeedId>(),
                It.IsAny<BlockIndex>()))
            .ThrowsAsync(new Exception("Database error"));

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.MarkFeedAsReadRequest
        {
            UserId = TestUserId,
            FeedId = TestFeedId,
            UpToBlockIndex = 500
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        var result = await service.MarkFeedAsRead(request, context);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MarkFeedAsRead_AlwaysCallsUnreadTrackingAndNotificationService()
    {
        // Arrange
        var mocker = new AutoMocker();
        var unreadTrackingMock = mocker.GetMock<IUnreadTrackingService>();
        var notificationServiceMock = mocker.GetMock<INotificationService>();

        mocker.GetMock<IFeedReadPositionStorageService>()
            .Setup(x => x.MarkFeedAsReadAsync(
                It.IsAny<string>(),
                It.IsAny<FeedId>(),
                It.IsAny<BlockIndex>()))
            .ReturnsAsync(true);

        unreadTrackingMock
            .Setup(x => x.MarkFeedAsReadAsync(TestUserId, TestFeedId))
            .Returns(Task.CompletedTask);

        notificationServiceMock
            .Setup(x => x.PublishMessagesReadAsync(TestUserId, TestFeedId, 500))
            .Returns(Task.CompletedTask);

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.MarkFeedAsReadRequest
        {
            UserId = TestUserId,
            FeedId = TestFeedId,
            UpToBlockIndex = 500
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        await service.MarkFeedAsRead(request, context);

        // Assert - verify both services are always called
        unreadTrackingMock.Verify(
            x => x.MarkFeedAsReadAsync(TestUserId, TestFeedId),
            Times.Once,
            "UnreadTrackingService.MarkFeedAsReadAsync should always be called");

        notificationServiceMock.Verify(
            x => x.PublishMessagesReadAsync(TestUserId, TestFeedId, 500),
            Times.Once,
            "NotificationService.PublishMessagesReadAsync should always be called with upToBlockIndex");
    }

    [Fact]
    public async Task MarkFeedAsRead_PassesUpToBlockIndexToPublishMessagesReadAsync()
    {
        // Arrange - FEAT-063: Verify upToBlockIndex is passed through to notification service
        var mocker = new AutoMocker();
        var notificationServiceMock = mocker.GetMock<INotificationService>();

        mocker.GetMock<IFeedReadPositionStorageService>()
            .Setup(x => x.MarkFeedAsReadAsync(
                It.IsAny<string>(),
                It.IsAny<FeedId>(),
                It.IsAny<BlockIndex>()))
            .ReturnsAsync(true);

        mocker.GetMock<IUnreadTrackingService>()
            .Setup(x => x.MarkFeedAsReadAsync(TestUserId, TestFeedId))
            .Returns(Task.CompletedTask);

        notificationServiceMock
            .Setup(x => x.PublishMessagesReadAsync(TestUserId, TestFeedId, 800))
            .Returns(Task.CompletedTask);

        var service = mocker.CreateInstance<NotificationGrpcService>();
        var request = new ProtoTypes.MarkFeedAsReadRequest
        {
            UserId = TestUserId,
            FeedId = TestFeedId,
            UpToBlockIndex = 800
        };
        var context = CreateMockServerCallContext(CancellationToken.None);

        // Act
        await service.MarkFeedAsRead(request, context);

        // Assert - FEAT-063: upToBlockIndex must be passed through
        notificationServiceMock.Verify(
            x => x.PublishMessagesReadAsync(TestUserId, TestFeedId, 800),
            Times.Once,
            "PublishMessagesReadAsync must receive the exact upToBlockIndex from the request");
    }

    #endregion

    #region Helper Methods

    private static ServerCallContext CreateMockServerCallContext(CancellationToken cancellationToken)
    {
        return new MockServerCallContext(cancellationToken);
    }

    private static async IAsyncEnumerable<InternalModels.FeedEvent> CreateEmptyAsyncEnumerable()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<InternalModels.FeedEvent> CreateCancellableAsyncEnumerable(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // This will throw OperationCanceledException when cancelled
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        yield break;
    }

    /// <summary>
    /// Minimal mock implementation of ServerCallContext for testing.
    /// </summary>
    private class MockServerCallContext : ServerCallContext
    {
        private readonly CancellationToken _cancellationToken;

        public MockServerCallContext(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        protected override string MethodCore => "TestMethod";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => _cancellationToken;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new("test", new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        {
            throw new NotImplementedException();
        }

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        {
            return Task.CompletedTask;
        }
    }

    #endregion
}
