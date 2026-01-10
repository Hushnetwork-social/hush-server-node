using FluentAssertions;
using HushNode.Notifications.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Notifications.Tests;

/// <summary>
/// Tests for ConnectionTracker - tracks active gRPC connections per user.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class ConnectionTrackerTests
{
    private const string InstanceName = "HushFeeds:";
    private const string TestUserId = "test-user-id";
    private const string TestConnectionId = "test-connection-id";

    #region MarkOnlineAsync Tests

    [Fact]
    public async Task MarkOnlineAsync_AddsConnectionToRedisSet()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateConnectionTracker();
        var expectedKey = $"{InstanceName}connections:{TestUserId}";

        databaseMock
            .Setup(x => x.SetAddAsync(expectedKey, TestConnectionId, CommandFlags.None))
            .ReturnsAsync(true);
        databaseMock
            .Setup(x => x.KeyExpireAsync(expectedKey, It.IsAny<TimeSpan>(), ExpireWhen.Always, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.MarkOnlineAsync(TestUserId, TestConnectionId);

        // Assert
        databaseMock.Verify(
            x => x.SetAddAsync(expectedKey, TestConnectionId, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task MarkOnlineAsync_SetsTtlTo5Minutes()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateConnectionTracker();
        var expectedKey = $"{InstanceName}connections:{TestUserId}";
        var expectedTtl = TimeSpan.FromMinutes(5);

        databaseMock
            .Setup(x => x.SetAddAsync(expectedKey, TestConnectionId, CommandFlags.None))
            .ReturnsAsync(true);
        databaseMock
            .Setup(x => x.KeyExpireAsync(expectedKey, expectedTtl, ExpireWhen.Always, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.MarkOnlineAsync(TestUserId, TestConnectionId);

        // Assert
        databaseMock.Verify(
            x => x.KeyExpireAsync(expectedKey, expectedTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task MarkOnlineAsync_HandlesRedisConnectionError_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateConnectionTracker();
        var expectedKey = $"{InstanceName}connections:{TestUserId}";

        databaseMock
            .Setup(x => x.SetAddAsync(expectedKey, TestConnectionId, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.MarkOnlineAsync(TestUserId, TestConnectionId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region MarkOfflineAsync Tests

    [Fact]
    public async Task MarkOfflineAsync_RemovesConnectionFromRedisSet()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateConnectionTracker();
        var expectedKey = $"{InstanceName}connections:{TestUserId}";

        databaseMock
            .Setup(x => x.SetRemoveAsync(expectedKey, TestConnectionId, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.MarkOfflineAsync(TestUserId, TestConnectionId);

        // Assert
        databaseMock.Verify(
            x => x.SetRemoveAsync(expectedKey, TestConnectionId, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task MarkOfflineAsync_HandlesRedisConnectionError_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateConnectionTracker();
        var expectedKey = $"{InstanceName}connections:{TestUserId}";

        databaseMock
            .Setup(x => x.SetRemoveAsync(expectedKey, TestConnectionId, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.MarkOfflineAsync(TestUserId, TestConnectionId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region IsUserOnlineAsync Tests

    [Fact]
    public async Task IsUserOnlineAsync_ReturnsTrue_WhenUserHasConnections()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateConnectionTracker();
        var expectedKey = $"{InstanceName}connections:{TestUserId}";

        databaseMock
            .Setup(x => x.SetLengthAsync(expectedKey, CommandFlags.None))
            .ReturnsAsync(2);

        // Act
        var result = await sut.IsUserOnlineAsync(TestUserId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsUserOnlineAsync_ReturnsFalse_WhenUserHasNoConnections()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateConnectionTracker();
        var expectedKey = $"{InstanceName}connections:{TestUserId}";

        databaseMock
            .Setup(x => x.SetLengthAsync(expectedKey, CommandFlags.None))
            .ReturnsAsync(0);

        // Act
        var result = await sut.IsUserOnlineAsync(TestUserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsUserOnlineAsync_ReturnsFalse_WhenRedisConnectionFails()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateConnectionTracker();
        var expectedKey = $"{InstanceName}connections:{TestUserId}";

        databaseMock
            .Setup(x => x.SetLengthAsync(expectedKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.IsUserOnlineAsync(TestUserId);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetOnlineUserCountAsync Tests

    [Fact]
    public async Task GetOnlineUserCountAsync_ReturnsZero_WhenNoServerAvailable()
    {
        // Arrange
        var (sut, _, redisConnectionManagerMock) = CreateConnectionTracker();

        // Mock Database.Multiplexer to return empty endpoints
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock.Setup(x => x.GetEndPoints(It.IsAny<bool>())).Returns(Array.Empty<System.Net.EndPoint>());

        var databaseMock = new Mock<IDatabase>();
        databaseMock.Setup(x => x.Multiplexer).Returns(multiplexerMock.Object);

        redisConnectionManagerMock.Setup(x => x.Database).Returns(databaseMock.Object);

        // Act
        var result = await sut.GetOnlineUserCountAsync();

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region Helper Methods

    private (ConnectionTracker sut, Mock<IDatabase> databaseMock, Mock<RedisConnectionManager> redisConnectionManagerMock) CreateConnectionTracker()
    {
        var loggerMock = new Mock<ILogger<ConnectionTracker>>();
        var databaseMock = new Mock<IDatabase>();

        // Create mock for RedisConnectionManager
        var redisSettings = new RedisSettings { InstanceName = InstanceName };
        var optionsMock = new Mock<IOptions<RedisSettings>>();
        optionsMock.Setup(x => x.Value).Returns(redisSettings);

        var redisLoggerMock = new Mock<ILogger<RedisConnectionManager>>();

        // We need to mock the RedisConnectionManager, but it's not an interface
        // So we'll create a partial mock approach
        var redisConnectionManagerMock = new Mock<RedisConnectionManager>(
            optionsMock.Object, redisLoggerMock.Object) { CallBase = false };

        redisConnectionManagerMock.Setup(x => x.Database).Returns(databaseMock.Object);
        redisConnectionManagerMock.Setup(x => x.GetConnectionsKey(It.IsAny<string>()))
            .Returns((string userId) => $"{InstanceName}connections:{userId}");
        redisConnectionManagerMock.Setup(x => x.GetConnectionsPattern())
            .Returns($"{InstanceName}connections:*");

        var sut = new ConnectionTracker(redisConnectionManagerMock.Object, loggerMock.Object);

        return (sut, databaseMock, redisConnectionManagerMock);
    }

    #endregion
}
