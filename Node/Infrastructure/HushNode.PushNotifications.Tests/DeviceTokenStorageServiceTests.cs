using FluentAssertions;
using HushNode.PushNotifications.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.PushNotifications.Tests;

/// <summary>
/// Tests for DeviceTokenStorageService.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class DeviceTokenStorageServiceTests
{
    private const string TestUserId = "test-user-id";
    private const string TestToken = "test-fcm-token-abc123";
    private const string TestDeviceName = "Test Phone";

    #region RegisterTokenAsync Tests

    [Fact]
    public async Task RegisterTokenAsync_NewToken_CreatesTokenAndReturnsTrue()
    {
        // Arrange
        var (sut, repositoryMock, unitOfWorkMock) = CreateStorageService();

        repositoryMock
            .Setup(x => x.GetByTokenAsync(TestToken))
            .ReturnsAsync((DeviceToken?)null);

        // Act
        var result = await sut.RegisterTokenAsync(TestUserId, PushPlatform.Android, TestToken, TestDeviceName);

        // Assert
        result.Should().BeTrue();
        repositoryMock.Verify(
            x => x.AddAsync(It.Is<DeviceToken>(t =>
                t.UserId == TestUserId &&
                t.Platform == PushPlatform.Android &&
                t.Token == TestToken &&
                t.DeviceName == TestDeviceName &&
                t.IsActive)),
            Times.Once);
        unitOfWorkMock.Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task RegisterTokenAsync_ExistingToken_UpdatesTokenAndReturnsTrue()
    {
        // Arrange
        var (sut, repositoryMock, unitOfWorkMock) = CreateStorageService();

        var existingToken = new DeviceToken
        {
            Id = "existing-id",
            UserId = "old-user-id",
            Platform = PushPlatform.iOS,
            Token = TestToken,
            DeviceName = "Old Device",
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            LastUsedAt = DateTime.UtcNow.AddDays(-7),
            IsActive = false
        };

        repositoryMock
            .Setup(x => x.GetByTokenAsync(TestToken))
            .ReturnsAsync(existingToken);

        // Act
        var result = await sut.RegisterTokenAsync(TestUserId, PushPlatform.Android, TestToken, TestDeviceName);

        // Assert
        result.Should().BeTrue();
        existingToken.UserId.Should().Be(TestUserId);
        existingToken.Platform.Should().Be(PushPlatform.Android);
        existingToken.DeviceName.Should().Be(TestDeviceName);
        existingToken.IsActive.Should().BeTrue();
        repositoryMock.Verify(x => x.UpdateAsync(existingToken), Times.Once);
        unitOfWorkMock.Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task RegisterTokenAsync_WhenRepositoryThrows_ReturnsFalse()
    {
        // Arrange
        var (sut, repositoryMock, _) = CreateStorageService();

        repositoryMock
            .Setup(x => x.GetByTokenAsync(TestToken))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await sut.RegisterTokenAsync(TestUserId, PushPlatform.Android, TestToken, TestDeviceName);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region UnregisterTokenAsync Tests

    [Fact]
    public async Task UnregisterTokenAsync_ExistingToken_DeactivatesAndReturnsTrue()
    {
        // Arrange
        var (sut, repositoryMock, unitOfWorkMock) = CreateStorageService();

        var existingToken = new DeviceToken
        {
            Id = "token-id",
            UserId = TestUserId,
            Token = TestToken,
            IsActive = true
        };

        repositoryMock
            .Setup(x => x.GetByTokenAsync(TestToken))
            .ReturnsAsync(existingToken);

        // Act
        var result = await sut.UnregisterTokenAsync(TestUserId, TestToken);

        // Assert
        result.Should().BeTrue();
        repositoryMock.Verify(x => x.DeactivateTokenAsync(TestToken), Times.Once);
        unitOfWorkMock.Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task UnregisterTokenAsync_TokenNotFound_ReturnsTrue()
    {
        // Arrange
        var (sut, repositoryMock, _) = CreateStorageService();

        repositoryMock
            .Setup(x => x.GetByTokenAsync(TestToken))
            .ReturnsAsync((DeviceToken?)null);

        // Act
        var result = await sut.UnregisterTokenAsync(TestUserId, TestToken);

        // Assert
        result.Should().BeTrue();
        repositoryMock.Verify(x => x.DeactivateTokenAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UnregisterTokenAsync_TokenBelongsToDifferentUser_ReturnsFalse()
    {
        // Arrange
        var (sut, repositoryMock, _) = CreateStorageService();

        var existingToken = new DeviceToken
        {
            Id = "token-id",
            UserId = "different-user-id",
            Token = TestToken,
            IsActive = true
        };

        repositoryMock
            .Setup(x => x.GetByTokenAsync(TestToken))
            .ReturnsAsync(existingToken);

        // Act
        var result = await sut.UnregisterTokenAsync(TestUserId, TestToken);

        // Assert
        result.Should().BeFalse();
        repositoryMock.Verify(x => x.DeactivateTokenAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UnregisterTokenAsync_WhenRepositoryThrows_ReturnsFalse()
    {
        // Arrange
        var (sut, repositoryMock, _) = CreateStorageService();

        repositoryMock
            .Setup(x => x.GetByTokenAsync(TestToken))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await sut.UnregisterTokenAsync(TestUserId, TestToken);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetActiveTokensForUserAsync Tests

    [Fact]
    public async Task GetActiveTokensForUserAsync_ReturnsTokens()
    {
        // Arrange
        var (sut, repositoryMock, _) = CreateStorageService();

        var tokens = new List<DeviceToken>
        {
            new() { Id = "1", UserId = TestUserId, Token = "token1", IsActive = true },
            new() { Id = "2", UserId = TestUserId, Token = "token2", IsActive = true }
        };

        repositoryMock
            .Setup(x => x.GetActiveTokensForUserAsync(TestUserId))
            .ReturnsAsync(tokens);

        // Act
        var result = await sut.GetActiveTokensForUserAsync(TestUserId);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetActiveTokensForUserAsync_WhenRepositoryThrows_ReturnsEmptyCollection()
    {
        // Arrange
        var (sut, repositoryMock, _) = CreateStorageService();

        repositoryMock
            .Setup(x => x.GetActiveTokensForUserAsync(TestUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await sut.GetActiveTokensForUserAsync(TestUserId);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region DeactivateStaleTokensAsync Tests

    [Fact]
    public async Task DeactivateStaleTokensAsync_DeactivatesAllStaleTokens()
    {
        // Arrange
        var (sut, repositoryMock, unitOfWorkMock) = CreateStorageService();
        var threshold = DateTime.UtcNow.AddDays(-30);

        var staleTokens = new List<DeviceToken>
        {
            new() { Id = "1", Token = "token1", LastUsedAt = DateTime.UtcNow.AddDays(-60) },
            new() { Id = "2", Token = "token2", LastUsedAt = DateTime.UtcNow.AddDays(-45) },
            new() { Id = "3", Token = "token3", LastUsedAt = DateTime.UtcNow.AddDays(-31) }
        };

        repositoryMock
            .Setup(x => x.GetStaleTokensAsync(threshold))
            .ReturnsAsync(staleTokens);

        // Act
        var result = await sut.DeactivateStaleTokensAsync(threshold);

        // Assert
        result.Should().Be(3);
        repositoryMock.Verify(x => x.DeactivateTokenAsync("token1"), Times.Once);
        repositoryMock.Verify(x => x.DeactivateTokenAsync("token2"), Times.Once);
        repositoryMock.Verify(x => x.DeactivateTokenAsync("token3"), Times.Once);
        unitOfWorkMock.Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task DeactivateStaleTokensAsync_NoStaleTokens_ReturnsZero()
    {
        // Arrange
        var (sut, repositoryMock, _) = CreateStorageService();
        var threshold = DateTime.UtcNow.AddDays(-30);

        repositoryMock
            .Setup(x => x.GetStaleTokensAsync(threshold))
            .ReturnsAsync(new List<DeviceToken>());

        // Act
        var result = await sut.DeactivateStaleTokensAsync(threshold);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task DeactivateStaleTokensAsync_WhenRepositoryThrows_ReturnsZero()
    {
        // Arrange
        var (sut, repositoryMock, _) = CreateStorageService();
        var threshold = DateTime.UtcNow.AddDays(-30);

        repositoryMock
            .Setup(x => x.GetStaleTokensAsync(threshold))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await sut.DeactivateStaleTokensAsync(threshold);

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region Helper Methods

    private (DeviceTokenStorageService sut, Mock<IDeviceTokenRepository> repositoryMock, Mock<IWritableUnitOfWork<PushNotificationsDbContext>> unitOfWorkMock) CreateStorageService()
    {
        var loggerMock = new Mock<ILogger<DeviceTokenStorageService>>();
        var repositoryMock = new Mock<IDeviceTokenRepository>();

        // Create mock for writable unit of work
        var unitOfWorkMock = new Mock<IWritableUnitOfWork<PushNotificationsDbContext>>();
        unitOfWorkMock
            .Setup(x => x.GetRepository<IDeviceTokenRepository>())
            .Returns(repositoryMock.Object);
        unitOfWorkMock
            .Setup(x => x.CommitAsync())
            .Returns(Task.CompletedTask);
        unitOfWorkMock
            .Setup(x => x.Dispose());

        // Create mock for readonly unit of work
        var readOnlyUnitOfWorkMock = new Mock<IReadOnlyUnitOfWork<PushNotificationsDbContext>>();
        readOnlyUnitOfWorkMock
            .Setup(x => x.GetRepository<IDeviceTokenRepository>())
            .Returns(repositoryMock.Object);
        readOnlyUnitOfWorkMock
            .Setup(x => x.Dispose());

        // Create mock for unit of work provider
        var unitOfWorkProviderMock = new Mock<IUnitOfWorkProvider<PushNotificationsDbContext>>();
        unitOfWorkProviderMock
            .Setup(x => x.CreateWritable())
            .Returns(unitOfWorkMock.Object);
        unitOfWorkProviderMock
            .Setup(x => x.CreateReadOnly())
            .Returns(readOnlyUnitOfWorkMock.Object);

        var sut = new DeviceTokenStorageService(unitOfWorkProviderMock.Object, loggerMock.Object);

        return (sut, repositoryMock, unitOfWorkMock);
    }

    #endregion
}
