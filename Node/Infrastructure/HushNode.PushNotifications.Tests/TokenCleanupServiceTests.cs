using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HushNode.PushNotifications.Tests;

/// <summary>
/// Tests for TokenCleanupService.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class TokenCleanupServiceTests
{
    #region RunCleanupAsync Tests

    [Fact]
    public async Task RunCleanupAsync_CallsStorageServiceWithCorrectThreshold()
    {
        // Arrange
        var storageServiceMock = new Mock<IDeviceTokenStorageService>();
        var loggerMock = new Mock<ILogger<TokenCleanupService>>();

        storageServiceMock
            .Setup(x => x.DeactivateStaleTokensAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(0);

        var sut = new TokenCleanupService(storageServiceMock.Object, loggerMock.Object);

        // Act
        await sut.RunCleanupAsync();

        // Assert
        storageServiceMock.Verify(
            x => x.DeactivateStaleTokensAsync(It.Is<DateTime>(d =>
                d < DateTime.UtcNow.AddDays(-59) && d > DateTime.UtcNow.AddDays(-61))),
            Times.Once);
    }

    [Fact]
    public async Task RunCleanupAsync_WhenTokensDeactivated_LogsCount()
    {
        // Arrange
        var storageServiceMock = new Mock<IDeviceTokenStorageService>();
        var loggerMock = new Mock<ILogger<TokenCleanupService>>();

        storageServiceMock
            .Setup(x => x.DeactivateStaleTokensAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(5);

        var sut = new TokenCleanupService(storageServiceMock.Object, loggerMock.Object);

        // Act
        await sut.RunCleanupAsync();

        // Assert
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("5")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RunCleanupAsync_WhenNoTokensDeactivated_LogsDebug()
    {
        // Arrange
        var storageServiceMock = new Mock<IDeviceTokenStorageService>();
        var loggerMock = new Mock<ILogger<TokenCleanupService>>();

        storageServiceMock
            .Setup(x => x.DeactivateStaleTokensAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(0);

        var sut = new TokenCleanupService(storageServiceMock.Object, loggerMock.Object);

        // Act
        await sut.RunCleanupAsync();

        // Assert
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No stale tokens found")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RunCleanupAsync_WhenStorageServiceThrows_DoesNotCrash()
    {
        // Arrange
        var storageServiceMock = new Mock<IDeviceTokenStorageService>();
        var loggerMock = new Mock<ILogger<TokenCleanupService>>();

        storageServiceMock
            .Setup(x => x.DeactivateStaleTokensAsync(It.IsAny<DateTime>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        var sut = new TokenCleanupService(storageServiceMock.Object, loggerMock.Object);

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => sut.RunCleanupAsync());

        // The exception should propagate (it's handled in ExecuteAsync loop)
        exception.Should().BeOfType<Exception>();
        exception!.Message.Should().Be("Database connection failed");
    }

    [Fact]
    public async Task RunCleanupAsync_UsesCorrectStaleThresholdOf60Days()
    {
        // Arrange
        var storageServiceMock = new Mock<IDeviceTokenStorageService>();
        var loggerMock = new Mock<ILogger<TokenCleanupService>>();
        DateTime capturedThreshold = default;

        storageServiceMock
            .Setup(x => x.DeactivateStaleTokensAsync(It.IsAny<DateTime>()))
            .Callback<DateTime>(d => capturedThreshold = d)
            .ReturnsAsync(0);

        var sut = new TokenCleanupService(storageServiceMock.Object, loggerMock.Object);

        // Act
        await sut.RunCleanupAsync();

        // Assert - should be approximately 60 days ago
        var expectedThreshold = DateTime.UtcNow.AddDays(-60);
        var difference = Math.Abs((capturedThreshold - expectedThreshold).TotalSeconds);
        difference.Should().BeLessThan(5); // Allow 5 seconds tolerance for test execution time
    }

    #endregion
}
