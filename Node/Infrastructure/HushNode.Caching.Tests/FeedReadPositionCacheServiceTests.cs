using FluentAssertions;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Caching.Tests;

/// <summary>
/// Tests for FeedReadPositionCacheService - caches user read positions in Redis.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedReadPositionCacheServiceTests
{
    private const string KeyPrefix = "HushFeeds:";
    private const string TestUserId = "0x1234567890abcdef";
    private static readonly FeedId TestFeedId = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly string ExpectedKey = $"{KeyPrefix}user:{TestUserId}:read:{TestFeedId}";

    #region GetReadPositionAsync Tests

    [Fact]
    public async Task GetReadPositionAsync_WhenValueExists_ReturnsCachedValue()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var expectedBlockIndex = 500L;

        databaseMock
            .Setup(x => x.StringGetAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(expectedBlockIndex.ToString());

        // Act
        var result = await sut.GetReadPositionAsync(TestUserId, TestFeedId);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Should().Be(expectedBlockIndex);
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetReadPositionAsync_WhenKeyNotExists_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.StringGetAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await sut.GetReadPositionAsync(TestUserId, TestFeedId);

        // Assert
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetReadPositionAsync_WhenUserIdIsNull_ReturnsNull()
    {
        // Arrange
        var (sut, _) = CreateCacheService();

        // Act
        var result = await sut.GetReadPositionAsync(null!, TestFeedId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReadPositionAsync_WhenUserIdIsEmpty_ReturnsNull()
    {
        // Arrange
        var (sut, _) = CreateCacheService();

        // Act
        var result = await sut.GetReadPositionAsync(string.Empty, TestFeedId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReadPositionAsync_WhenRedisThrows_ReturnsNullAndLogsError()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.StringGetAsync(ExpectedKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.GetReadPositionAsync(TestUserId, TestFeedId);

        // Assert
        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetReadPositionAsync_WhenValueIsInvalid_ReturnsNullAndLogsMiss()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.StringGetAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync("not-a-number");

        // Act
        var result = await sut.GetReadPositionAsync(TestUserId, TestFeedId);

        // Assert
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    #endregion

    #region SetReadPositionAsync Tests

    [Fact]
    public async Task SetReadPositionAsync_SetsValueWithTtl()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        databaseMock
            .Setup(x => x.StringSetAsync(
                ExpectedKey,
                "500",
                FeedReadPositionCacheConstants.CacheTtl,
                false,
                When.Always,
                CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.SetReadPositionAsync(TestUserId, TestFeedId, blockIndex);

        // Assert
        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
        databaseMock.Verify(
            x => x.StringSetAsync(
                ExpectedKey,
                "500",
                FeedReadPositionCacheConstants.CacheTtl,
                false,
                When.Always,
                CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task SetReadPositionAsync_WhenUserIdIsNull_ReturnsFalse()
    {
        // Arrange
        var (sut, _) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        // Act
        var result = await sut.SetReadPositionAsync(null!, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetReadPositionAsync_WhenUserIdIsEmpty_ReturnsFalse()
    {
        // Arrange
        var (sut, _) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        // Act
        var result = await sut.SetReadPositionAsync(string.Empty, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetReadPositionAsync_WhenRedisThrows_ReturnsFalseAndLogsError()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        databaseMock
            .Setup(x => x.StringSetAsync(
                ExpectedKey,
                "500",
                FeedReadPositionCacheConstants.CacheTtl,
                false,
                When.Always,
                CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.SetReadPositionAsync(TestUserId, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region InvalidateCacheAsync Tests

    [Fact]
    public async Task InvalidateCacheAsync_DeletesKey()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.InvalidateCacheAsync(TestUserId, TestFeedId);

        // Assert
        databaseMock.Verify(
            x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task InvalidateCacheAsync_WhenUserIdIsNull_DoesNothing()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        // Act
        await sut.InvalidateCacheAsync(null!, TestFeedId);

        // Assert
        databaseMock.Verify(
            x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), CommandFlags.None),
            Times.Never);
    }

    [Fact]
    public async Task InvalidateCacheAsync_WhenRedisThrows_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.InvalidateCacheAsync(TestUserId, TestFeedId);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Helper Methods

    private static (FeedReadPositionCacheService sut, Mock<IDatabase> databaseMock)
        CreateCacheService()
    {
        var connectionMultiplexerMock = new Mock<IConnectionMultiplexer>();
        var databaseMock = new Mock<IDatabase>();
        var loggerMock = new Mock<ILogger<FeedReadPositionCacheService>>();

        connectionMultiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        var sut = new FeedReadPositionCacheService(
            connectionMultiplexerMock.Object,
            KeyPrefix,
            loggerMock.Object);

        return (sut, databaseMock);
    }

    #endregion
}
