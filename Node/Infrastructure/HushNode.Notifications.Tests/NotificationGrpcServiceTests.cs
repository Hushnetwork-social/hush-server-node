using FluentAssertions;
using Grpc.Core;
using HushNode.Notifications.gRPC;
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
