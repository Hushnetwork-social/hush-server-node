using FluentAssertions;
using HushNode.PushNotifications.Exceptions;
using HushNode.Interfaces.Models;
using HushNode.PushNotifications.Models;
using HushNode.PushNotifications.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HushNode.PushNotifications.Tests;

/// <summary>
/// Tests for PushDeliveryService.
/// Each test follows AAA pattern with isolated setup.
/// </summary>
public class PushDeliveryServiceTests
{
    #region SendPushAsync Tests

    [Fact]
    public async Task SendPushAsync_WithMultipleDevices_SendsToAllDevices()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceTokens = new List<DeviceToken>
        {
            CreateDeviceToken(userId, PushPlatform.Android, "token-1"),
            CreateDeviceToken(userId, PushPlatform.Android, "token-2"),
            CreateDeviceToken(userId, PushPlatform.Android, "token-3")
        };

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.GetActiveTokensForUserAsync(userId))
            .ReturnsAsync(deviceTokens);

        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockFcmProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello from Bob");

        // Act
        await sut.SendPushAsync(userId, payload);

        // Assert
        mockFcmProvider.Verify(x => x.SendAsync("token-1", payload), Times.Once);
        mockFcmProvider.Verify(x => x.SendAsync("token-2", payload), Times.Once);
        mockFcmProvider.Verify(x => x.SendAsync("token-3", payload), Times.Once);
    }

    [Fact]
    public async Task SendPushAsync_WithNoDevices_DoesNothing()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.GetActiveTokensForUserAsync(userId))
            .ReturnsAsync(Enumerable.Empty<DeviceToken>());

        var mockFcmProvider = new Mock<IFcmProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockFcmProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushAsync(userId, payload);

        // Assert
        mockFcmProvider.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("No active device tokens found")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendPushAsync_WithInvalidToken_DeactivatesTokenAndContinues()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceTokens = new List<DeviceToken>
        {
            CreateDeviceToken(userId, PushPlatform.Android, "valid-token"),
            CreateDeviceToken(userId, PushPlatform.Android, "invalid-token")
        };

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.GetActiveTokensForUserAsync(userId))
            .ReturnsAsync(deviceTokens);
        mockTokenStorage
            .Setup(x => x.UnregisterTokenAsync(userId, "invalid-token"))
            .ReturnsAsync(true);

        var mockFcmProvider = new Mock<IFcmProvider>();
        // First token succeeds
        mockFcmProvider
            .Setup(x => x.SendAsync("valid-token", It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);
        // Second throws InvalidTokenException
        mockFcmProvider
            .Setup(x => x.SendAsync("invalid-token", It.IsAny<PushPayload>()))
            .ThrowsAsync(new InvalidTokenException("Token is unregistered"));

        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockFcmProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act - should not throw, even though one token is invalid
        var act = async () => await sut.SendPushAsync(userId, payload);

        // Assert
        await act.Should().NotThrowAsync();

        // Verify both tokens were attempted
        mockFcmProvider.Verify(x => x.SendAsync("valid-token", payload), Times.Once);
        mockFcmProvider.Verify(x => x.SendAsync("invalid-token", payload), Times.Once);

        // Verify invalid token was deactivated
        mockTokenStorage.Verify(x => x.UnregisterTokenAsync(userId, "invalid-token"), Times.Once);

        // Verify valid token was NOT unregistered
        mockTokenStorage.Verify(x => x.UnregisterTokenAsync(userId, "valid-token"), Times.Never);
    }

    [Fact]
    public async Task SendPushAsync_WithAllInvalidTokens_DeactivatesAllAndDoesNotThrow()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceTokens = new List<DeviceToken>
        {
            CreateDeviceToken(userId, PushPlatform.Android, "invalid-token-1"),
            CreateDeviceToken(userId, PushPlatform.Android, "invalid-token-2")
        };

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.GetActiveTokensForUserAsync(userId))
            .ReturnsAsync(deviceTokens);
        mockTokenStorage
            .Setup(x => x.UnregisterTokenAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()))
            .ThrowsAsync(new InvalidTokenException("Token is unregistered"));

        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockFcmProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        var act = async () => await sut.SendPushAsync(userId, payload);

        // Assert
        await act.Should().NotThrowAsync();

        mockTokenStorage.Verify(x => x.UnregisterTokenAsync(userId, "invalid-token-1"), Times.Once);
        mockTokenStorage.Verify(x => x.UnregisterTokenAsync(userId, "invalid-token-2"), Times.Once);
    }

    [Fact]
    public async Task SendPushAsync_WithUnexpectedError_LogsErrorAndContinues()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceTokens = new List<DeviceToken>
        {
            CreateDeviceToken(userId, PushPlatform.Android, "error-token"),
            CreateDeviceToken(userId, PushPlatform.Android, "good-token")
        };

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.GetActiveTokensForUserAsync(userId))
            .ReturnsAsync(deviceTokens);

        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync("error-token", It.IsAny<PushPayload>()))
            .ThrowsAsync(new Exception("Network error"));
        mockFcmProvider
            .Setup(x => x.SendAsync("good-token", It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockFcmProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        var act = async () => await sut.SendPushAsync(userId, payload);

        // Assert - should not throw
        await act.Should().NotThrowAsync();

        // Both sends should have been attempted
        mockFcmProvider.Verify(x => x.SendAsync("error-token", payload), Times.Once);
        mockFcmProvider.Verify(x => x.SendAsync("good-token", payload), Times.Once);

        // Error should be logged
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Unexpected error")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // UnregisterTokenAsync should NOT be called for non-InvalidTokenException
        mockTokenStorage.Verify(x => x.UnregisterTokenAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region SendPushToDeviceAsync Tests

    [Fact]
    public async Task SendPushToDeviceAsync_WithAndroidDevice_SendsPush()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceToken = CreateDeviceToken(userId, PushPlatform.Android, "android-token");

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync("android-token", It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockFcmProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushToDeviceAsync(deviceToken, payload);

        // Assert
        mockFcmProvider.Verify(x => x.SendAsync("android-token", payload), Times.Once);
    }

    [Fact]
    public async Task SendPushToDeviceAsync_WithInvalidToken_DeactivatesAndRethrows()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceToken = CreateDeviceToken(userId, PushPlatform.Android, "invalid-token");

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.UnregisterTokenAsync(userId, "invalid-token"))
            .ReturnsAsync(true);

        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync("invalid-token", It.IsAny<PushPayload>()))
            .ThrowsAsync(new InvalidTokenException("Token is unregistered"));

        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockFcmProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        var act = async () => await sut.SendPushToDeviceAsync(deviceToken, payload);

        // Assert - should rethrow InvalidTokenException
        await act.Should().ThrowAsync<InvalidTokenException>();

        // Token should be deactivated
        mockTokenStorage.Verify(x => x.UnregisterTokenAsync(userId, "invalid-token"), Times.Once);
    }

    [Fact]
    public async Task SendPushToDeviceAsync_WithiOSDevice_SkipsNotification()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceToken = CreateDeviceToken(userId, PushPlatform.iOS, "ios-token");

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        var mockFcmProvider = new Mock<IFcmProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockFcmProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushToDeviceAsync(deviceToken, payload);

        // Assert - FCM provider should NOT be called for iOS
        mockFcmProvider.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);

        // Debug log should indicate skip
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("unsupported platform")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendPushToDeviceAsync_WithWebDevice_SkipsNotification()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceToken = CreateDeviceToken(userId, PushPlatform.Web, "web-token");

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        var mockFcmProvider = new Mock<IFcmProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockFcmProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushToDeviceAsync(deviceToken, payload);

        // Assert - FCM provider should NOT be called for Web
        mockFcmProvider.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);
    }

    [Fact]
    public async Task SendPushToDeviceAsync_WithUnknownPlatform_SkipsNotification()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceToken = CreateDeviceToken(userId, PushPlatform.Unknown, "unknown-token");

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        var mockFcmProvider = new Mock<IFcmProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockFcmProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushToDeviceAsync(deviceToken, payload);

        // Assert - FCM provider should NOT be called for Unknown
        mockFcmProvider.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);
    }

    #endregion

    #region Helper Methods

    private static DeviceToken CreateDeviceToken(string userId, PushPlatform platform, string token)
    {
        return new DeviceToken
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Platform = platform,
            Token = token,
            DeviceName = $"Test Device - {token}",
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    private static Mock<ILogger<PushDeliveryService>> CreateLogger()
    {
        return new Mock<ILogger<PushDeliveryService>>();
    }

    #endregion
}
