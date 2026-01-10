using FluentAssertions;
using HushNode.PushNotifications.Models;
using HushNode.PushNotifications.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HushNode.PushNotifications.Tests;

/// <summary>
/// Tests for FcmProvider.
/// Each test follows AAA pattern with isolated setup.
/// Note: Firebase SDK methods are difficult to mock directly. These tests focus on
/// configuration handling and disabled Firebase behavior. Full integration tests
/// would require actual Firebase credentials.
/// </summary>
public class FcmProviderTests
{
    private const string TestServiceAccountPath = "./test-firebase-sa.json";

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidConfiguration_CreatesProviderSuccessfully()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: true, serviceAccountPath: TestServiceAccountPath);
        var logger = CreateLogger();

        // Act
        var provider = new FcmProvider(configuration, logger.Object);

        // Assert
        provider.Should().NotBeNull();
        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Enabled: True")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WithDisabledFirebase_CreatesProviderWithDisabledState()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false, serviceAccountPath: TestServiceAccountPath);
        var logger = CreateLogger();

        // Act
        var provider = new FcmProvider(configuration, logger.Object);

        // Assert
        provider.Should().NotBeNull();
        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Enabled: False")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WithEnabledButMissingPath_DisablesFirebase()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: true, serviceAccountPath: null);
        var logger = CreateLogger();

        // Act
        var provider = new FcmProvider(configuration, logger.Object);

        // Assert
        provider.Should().NotBeNull();
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("ServiceAccountPath is not configured")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WithEnabledButEmptyPath_DisablesFirebase()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: true, serviceAccountPath: "");
        var logger = CreateLogger();

        // Act
        var provider = new FcmProvider(configuration, logger.Object);

        // Assert
        provider.Should().NotBeNull();
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("ServiceAccountPath is not configured")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region SendAsync Tests - Disabled Firebase

    [Fact]
    public async Task SendAsync_WhenFirebaseDisabled_SkipsNotificationSilently()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false, serviceAccountPath: TestServiceAccountPath);
        var logger = CreateLogger();
        var provider = new FcmProvider(configuration, logger.Object);
        var payload = new PushPayload("Test Title", "Test Body");

        // Act
        await provider.SendAsync("test-token", payload);

        // Assert - should log debug message and not throw
        logger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Firebase is disabled")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_WhenFirebaseDisabledDueToMissingPath_SkipsNotificationSilently()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: true, serviceAccountPath: null);
        var logger = CreateLogger();
        var provider = new FcmProvider(configuration, logger.Object);
        var payload = new PushPayload("Test Title", "Test Body");

        // Act
        await provider.SendAsync("test-token", payload);

        // Assert - should log debug message and not throw
        logger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Firebase is disabled")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_WithEmptyToken_WhenDisabled_DoesNotThrow()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false, serviceAccountPath: TestServiceAccountPath);
        var logger = CreateLogger();
        var provider = new FcmProvider(configuration, logger.Object);
        var payload = new PushPayload("Test Title", "Test Body");

        // Act
        var act = async () => await provider.SendAsync("", payload);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Payload Tests

    [Fact]
    public void PushPayload_WithAllFields_ShouldBeCreatedCorrectly()
    {
        // Arrange & Act
        var data = new Dictionary<string, string> { { "key1", "value1" } };
        var payload = new PushPayload("Title", "Body", "feed-123", data);

        // Assert
        payload.Title.Should().Be("Title");
        payload.Body.Should().Be("Body");
        payload.FeedId.Should().Be("feed-123");
        payload.Data.Should().ContainKey("key1");
    }

    [Fact]
    public void PushPayload_WithMinimalFields_ShouldHaveNullOptionalFields()
    {
        // Arrange & Act
        var payload = new PushPayload("Title", "Body");

        // Assert
        payload.Title.Should().Be("Title");
        payload.Body.Should().Be("Body");
        payload.FeedId.Should().BeNull();
        payload.Data.Should().BeNull();
    }

    #endregion

    #region Helper Methods

    private static IConfiguration CreateConfiguration(bool enabled, string? serviceAccountPath)
    {
        var configData = new Dictionary<string, string?>
        {
            ["Firebase:Enabled"] = enabled.ToString(),
            ["Firebase:ServiceAccountPath"] = serviceAccountPath,
            ["Firebase:ProjectId"] = "test-project"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private static Mock<ILogger<FcmProvider>> CreateLogger()
    {
        return new Mock<ILogger<FcmProvider>>();
    }

    #endregion
}
