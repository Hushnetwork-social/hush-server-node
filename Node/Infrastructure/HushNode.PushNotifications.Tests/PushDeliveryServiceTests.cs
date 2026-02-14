using FluentAssertions;
using HushNode.Caching;
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

        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
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

        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
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

        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        // First token succeeds
        mockFcmProvider
            .Setup(x => x.SendAsync("valid-token", It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);
        // Second throws InvalidTokenException
        mockFcmProvider
            .Setup(x => x.SendAsync("invalid-token", It.IsAny<PushPayload>()))
            .ThrowsAsync(new InvalidTokenException("Token is unregistered"));

        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
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

        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()))
            .ThrowsAsync(new InvalidTokenException("Token is unregistered"));

        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
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

        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync("error-token", It.IsAny<PushPayload>()))
            .ThrowsAsync(new Exception("Network error"));
        mockFcmProvider
            .Setup(x => x.SendAsync("good-token", It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
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
    public async Task SendPushToDeviceAsync_WithAndroidDevice_SendsViaFcm()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceToken = CreateDeviceToken(userId, PushPlatform.Android, "android-token");

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync("android-token", It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushToDeviceAsync(deviceToken, payload);

        // Assert
        mockFcmProvider.Verify(x => x.SendAsync("android-token", payload), Times.Once);
        mockApnsProvider.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);
    }

    [Fact]
    public async Task SendPushToDeviceAsync_WithiOSDevice_SendsViaApns()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceToken = CreateDeviceToken(userId, PushPlatform.iOS, "ios-token");

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        var mockApnsProvider = new Mock<IApnsProvider>();
        mockApnsProvider
            .Setup(x => x.SendAsync("ios-token", It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushToDeviceAsync(deviceToken, payload);

        // Assert
        mockApnsProvider.Verify(x => x.SendAsync("ios-token", payload), Times.Once);
        mockFcmProvider.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);
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

        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync("invalid-token", It.IsAny<PushPayload>()))
            .ThrowsAsync(new InvalidTokenException("Token is unregistered"));

        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        var act = async () => await sut.SendPushToDeviceAsync(deviceToken, payload);

        // Assert - should rethrow InvalidTokenException
        await act.Should().ThrowAsync<InvalidTokenException>();

        // Token should be deactivated
        mockTokenStorage.Verify(x => x.UnregisterTokenAsync(userId, "invalid-token"), Times.Once);
    }

    [Fact]
    public async Task SendPushToDeviceAsync_WithInvalidiOSToken_DeactivatesAndRethrows()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceToken = CreateDeviceToken(userId, PushPlatform.iOS, "invalid-ios-token");

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.UnregisterTokenAsync(userId, "invalid-ios-token"))
            .ReturnsAsync(true);

        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        var mockApnsProvider = new Mock<IApnsProvider>();
        mockApnsProvider
            .Setup(x => x.SendAsync("invalid-ios-token", It.IsAny<PushPayload>()))
            .ThrowsAsync(new InvalidTokenException("APNs device token is invalid (BadDeviceToken)"));

        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        var act = async () => await sut.SendPushToDeviceAsync(deviceToken, payload);

        // Assert - should rethrow InvalidTokenException
        await act.Should().ThrowAsync<InvalidTokenException>();

        // Token should be deactivated
        mockTokenStorage.Verify(x => x.UnregisterTokenAsync(userId, "invalid-ios-token"), Times.Once);
    }

    [Fact]
    public async Task SendPushToDeviceAsync_WithWebDevice_SkipsNotification()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceToken = CreateDeviceToken(userId, PushPlatform.Web, "web-token");

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushToDeviceAsync(deviceToken, payload);

        // Assert - neither provider should be called for Web
        mockFcmProvider.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);
        mockApnsProvider.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);
    }

    [Fact]
    public async Task SendPushToDeviceAsync_WithUnknownPlatform_SkipsNotification()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceToken = CreateDeviceToken(userId, PushPlatform.Unknown, "unknown-token");

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushToDeviceAsync(deviceToken, payload);

        // Assert - neither provider should be called for Unknown
        mockFcmProvider.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);
        mockApnsProvider.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()), Times.Never);
    }

    #endregion

    #region iOS Routing Tests (FEAT-064 Phase 4)

    [Fact]
    public async Task SendPushAsync_WithMixedPlatforms_RoutesToCorrectProviders()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceTokens = new List<DeviceToken>
        {
            CreateDeviceToken(userId, PushPlatform.Android, "android-token"),
            CreateDeviceToken(userId, PushPlatform.iOS, "ios-token")
        };

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.GetActiveTokensForUserAsync(userId))
            .ReturnsAsync(deviceTokens);

        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockApnsProvider = new Mock<IApnsProvider>();
        mockApnsProvider
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushAsync(userId, payload);

        // Assert - Android goes to FCM, iOS goes to APNs
        mockFcmProvider.Verify(x => x.SendAsync("android-token", payload), Times.Once);
        mockApnsProvider.Verify(x => x.SendAsync("ios-token", payload), Times.Once);

        // Cross-verify: FCM was NOT called with iOS token, APNs was NOT called with Android token
        mockFcmProvider.Verify(x => x.SendAsync("ios-token", It.IsAny<PushPayload>()), Times.Never);
        mockApnsProvider.Verify(x => x.SendAsync("android-token", It.IsAny<PushPayload>()), Times.Never);
    }

    [Fact]
    public async Task SendPushAsync_ApnsErrorDoesNotAffectFcmDelivery()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceTokens = new List<DeviceToken>
        {
            CreateDeviceToken(userId, PushPlatform.iOS, "invalid-ios-token"),
            CreateDeviceToken(userId, PushPlatform.Android, "good-android-token")
        };

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.GetActiveTokensForUserAsync(userId))
            .ReturnsAsync(deviceTokens);
        mockTokenStorage
            .Setup(x => x.UnregisterTokenAsync(userId, "invalid-ios-token"))
            .ReturnsAsync(true);

        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync("good-android-token", It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockApnsProvider = new Mock<IApnsProvider>();
        mockApnsProvider
            .Setup(x => x.SendAsync("invalid-ios-token", It.IsAny<PushPayload>()))
            .ThrowsAsync(new InvalidTokenException("APNs device token is invalid"));

        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act - should not throw
        var act = async () => await sut.SendPushAsync(userId, payload);

        // Assert
        await act.Should().NotThrowAsync();

        // FCM should still be called for the Android token
        mockFcmProvider.Verify(x => x.SendAsync("good-android-token", payload), Times.Once);

        // iOS token should be deactivated
        mockTokenStorage.Verify(x => x.UnregisterTokenAsync(userId, "invalid-ios-token"), Times.Once);

        // Android token should NOT be unregistered
        mockTokenStorage.Verify(x => x.UnregisterTokenAsync(userId, "good-android-token"), Times.Never);
    }

    #endregion

    #region Cache Integration Tests (FEAT-047 Phase 4)

    [Fact]
    public async Task SendPushAsync_CacheHit_ReadsFromCache()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var cachedTokens = new List<DeviceToken>
        {
            CreateDeviceToken(userId, PushPlatform.Android, "cached-token")
        };

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        var mockCacheService = new Mock<IPushTokenCacheService>();
        mockCacheService
            .Setup(x => x.GetTokensAsync(userId))
            .ReturnsAsync(cachedTokens);

        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushAsync(userId, payload);

        // Assert - cache was checked
        mockCacheService.Verify(x => x.GetTokensAsync(userId), Times.Once);
        // Storage service should NOT be called (cache hit)
        mockTokenStorage.Verify(x => x.GetActiveTokensForUserAsync(It.IsAny<string>()), Times.Never);
        // Push should still be sent
        mockFcmProvider.Verify(x => x.SendAsync("cached-token", payload), Times.Once);
    }

    [Fact]
    public async Task SendPushAsync_CacheMiss_FallsBackToDatabase()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var dbTokens = new List<DeviceToken>
        {
            CreateDeviceToken(userId, PushPlatform.Android, "db-token")
        };

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.GetActiveTokensForUserAsync(userId))
            .ReturnsAsync(dbTokens);

        var mockCacheService = new Mock<IPushTokenCacheService>();
        mockCacheService
            .Setup(x => x.GetTokensAsync(userId))
            .ReturnsAsync((IEnumerable<DeviceToken>?)null); // Cache miss

        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushAsync(userId, payload);

        // Assert - cache was checked first
        mockCacheService.Verify(x => x.GetTokensAsync(userId), Times.Once);
        // Then database was queried
        mockTokenStorage.Verify(x => x.GetActiveTokensForUserAsync(userId), Times.Once);
        // Cache should be populated
        mockCacheService.Verify(x => x.SetTokensAsync(userId, It.IsAny<IEnumerable<DeviceToken>>()), Times.Once);
        // Push should still be sent
        mockFcmProvider.Verify(x => x.SendAsync("db-token", payload), Times.Once);
    }

    [Fact]
    public async Task SendPushAsync_CachePopulationFails_StillDeliversPush()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var dbTokens = new List<DeviceToken>
        {
            CreateDeviceToken(userId, PushPlatform.Android, "db-token")
        };

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.GetActiveTokensForUserAsync(userId))
            .ReturnsAsync(dbTokens);

        var mockCacheService = new Mock<IPushTokenCacheService>();
        mockCacheService
            .Setup(x => x.GetTokensAsync(userId))
            .ReturnsAsync((IEnumerable<DeviceToken>?)null); // Cache miss
        mockCacheService
            .Setup(x => x.SetTokensAsync(userId, It.IsAny<IEnumerable<DeviceToken>>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act - should not throw
        var act = async () => await sut.SendPushAsync(userId, payload);

        // Assert
        await act.Should().NotThrowAsync();

        // Push should still be delivered
        mockFcmProvider.Verify(x => x.SendAsync("db-token", payload), Times.Once);

        // Warning should be logged for cache population failure
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Failed to populate cache")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendPushAsync_CacheUnavailable_FallsBackToDatabase()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var dbTokens = new List<DeviceToken>
        {
            CreateDeviceToken(userId, PushPlatform.Android, "db-token")
        };

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.GetActiveTokensForUserAsync(userId))
            .ReturnsAsync(dbTokens);

        var mockCacheService = new Mock<IPushTokenCacheService>();
        mockCacheService
            .Setup(x => x.GetTokensAsync(userId))
            .ThrowsAsync(new Exception("Redis unavailable"));

        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act - should not throw
        var act = async () => await sut.SendPushAsync(userId, payload);

        // Assert
        await act.Should().NotThrowAsync();

        // Database should be queried as fallback
        mockTokenStorage.Verify(x => x.GetActiveTokensForUserAsync(userId), Times.Once);

        // Push should still be delivered
        mockFcmProvider.Verify(x => x.SendAsync("db-token", payload), Times.Once);

        // Warning should be logged
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("falling back to database")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendPushAsync_SuccessfulPush_UpdatesLastUsedAtInCache()
    {
        // Arrange
        var userId = "user-abc123def456ghi789jkl";
        var deviceTokens = new List<DeviceToken>
        {
            CreateDeviceToken(userId, PushPlatform.Android, "token-1")
        };

        var mockTokenStorage = new Mock<IDeviceTokenStorageService>();
        mockTokenStorage
            .Setup(x => x.GetActiveTokensForUserAsync(userId))
            .ReturnsAsync(deviceTokens);

        var mockCacheService = CreateCacheService();
        var mockFcmProvider = new Mock<IFcmProvider>();
        mockFcmProvider
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<PushPayload>()))
            .Returns(Task.CompletedTask);

        var mockApnsProvider = new Mock<IApnsProvider>();
        var mockLogger = CreateLogger();
        var sut = new PushDeliveryService(mockTokenStorage.Object, mockCacheService.Object, mockFcmProvider.Object, mockApnsProvider.Object, mockLogger.Object);
        var payload = new PushPayload("New Message", "Hello");

        // Act
        await sut.SendPushAsync(userId, payload);

        // Assert - cache should be updated with new LastUsedAt
        mockCacheService.Verify(
            x => x.AddOrUpdateTokenAsync(userId, It.IsAny<DeviceToken>()),
            Times.Once);
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

    private static Mock<IPushTokenCacheService> CreateCacheService()
    {
        var mock = new Mock<IPushTokenCacheService>();
        // Default: cache miss (returns null)
        mock.Setup(x => x.GetTokensAsync(It.IsAny<string>()))
            .ReturnsAsync((IEnumerable<DeviceToken>?)null);
        return mock;
    }

    /// <summary>
    /// Creates PushDeliveryService with all mocks. Returns the service and mocks for verification.
    /// </summary>
    private static (PushDeliveryService sut, Mock<IDeviceTokenStorageService> storageServiceMock, Mock<IPushTokenCacheService> cacheServiceMock, Mock<IFcmProvider> fcmProviderMock, Mock<IApnsProvider> apnsProviderMock, Mock<ILogger<PushDeliveryService>> loggerMock) CreateServiceWithMocks()
    {
        var storageServiceMock = new Mock<IDeviceTokenStorageService>();
        var cacheServiceMock = CreateCacheService();
        var fcmProviderMock = new Mock<IFcmProvider>();
        var apnsProviderMock = new Mock<IApnsProvider>();
        var loggerMock = CreateLogger();

        var sut = new PushDeliveryService(
            storageServiceMock.Object,
            cacheServiceMock.Object,
            fcmProviderMock.Object,
            apnsProviderMock.Object,
            loggerMock.Object);

        return (sut, storageServiceMock, cacheServiceMock, fcmProviderMock, apnsProviderMock, loggerMock);
    }

    #endregion
}
