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
/// Tests cover HASH-based methods (FEAT-060) and legacy STRING methods (FEAT-051).
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedReadPositionCacheServiceTests
{
    private const string KeyPrefix = "HushFeeds:";
    private const string TestUserId = "0x1234567890abcdef";
    private static readonly FeedId TestFeedId = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly FeedId TestFeedId2 = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly string ExpectedHashKey = $"{KeyPrefix}user:{TestUserId}:read_positions";
    private static readonly string ExpectedLegacyKey = $"{KeyPrefix}user:{TestUserId}:read:{TestFeedId}";

    #region GetAllReadPositionsAsync Tests (HASH — FEAT-060)

    [Fact]
    public async Task GetAllReadPositionsAsync_WhenHashExists_ReturnsCachedPositions()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var entries = new HashEntry[]
        {
            new(TestFeedId.ToString(), "500"),
            new(TestFeedId2.ToString(), "300")
        };

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ReturnsAsync(entries);

        // Act
        var result = await sut.GetAllReadPositionsAsync(TestUserId);

        // Assert
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result[TestFeedId].Value.Should().Be(500);
        result[TestFeedId2].Value.Should().Be(300);
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetAllReadPositionsAsync_WhenHashEmpty_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ReturnsAsync(Array.Empty<HashEntry>());

        // Act
        var result = await sut.GetAllReadPositionsAsync(TestUserId);

        // Assert
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetAllReadPositionsAsync_WhenUserIdIsNull_ReturnsNull()
    {
        // Arrange
        var (sut, _) = CreateCacheService();

        // Act
        var result = await sut.GetAllReadPositionsAsync(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllReadPositionsAsync_WhenUserIdIsEmpty_ReturnsNull()
    {
        // Arrange
        var (sut, _) = CreateCacheService();

        // Act
        var result = await sut.GetAllReadPositionsAsync(string.Empty);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllReadPositionsAsync_WhenRedisThrows_ReturnsNullAndLogsError()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.GetAllReadPositionsAsync(TestUserId);

        // Assert
        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetAllReadPositionsAsync_WhenValueIsInvalid_SkipsEntry()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var entries = new HashEntry[]
        {
            new(TestFeedId.ToString(), "500"),
            new(TestFeedId2.ToString(), "not-a-number")
        };

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ReturnsAsync(entries);

        // Act
        var result = await sut.GetAllReadPositionsAsync(TestUserId);

        // Assert
        result.Should().NotBeNull();
        result!.Count.Should().Be(1);
        result[TestFeedId].Value.Should().Be(500);
        sut.CacheHits.Should().Be(1);
    }

    #endregion

    #region SetReadPositionWithMaxWinsAsync Tests (Lua — FEAT-060)

    [Fact]
    public async Task SetReadPositionWithMaxWinsAsync_WhenUpdateSucceeds_ReturnsTrueAndRefreshesTtl()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        databaseMock
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]?>(),
                It.IsAny<RedisValue[]?>(),
                CommandFlags.None))
            .ReturnsAsync(RedisResult.Create((RedisValue)1));

        databaseMock
            .Setup(x => x.KeyExpireAsync(
                ExpectedHashKey,
                FeedReadPositionCacheConstants.CacheTtl,
                ExpireWhen.Always,
                CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.SetReadPositionWithMaxWinsAsync(TestUserId, TestFeedId, blockIndex);

        // Assert
        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
        databaseMock.Verify(
            x => x.KeyExpireAsync(
                ExpectedHashKey,
                FeedReadPositionCacheConstants.CacheTtl,
                ExpireWhen.Always,
                CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task SetReadPositionWithMaxWinsAsync_WhenCurrentIsHigher_ReturnsFalseAndDoesNotRefreshTtl()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var blockIndex = new BlockIndex(100);

        databaseMock
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]?>(),
                It.IsAny<RedisValue[]?>(),
                CommandFlags.None))
            .ReturnsAsync(RedisResult.Create((RedisValue)0));

        // Act
        var result = await sut.SetReadPositionWithMaxWinsAsync(TestUserId, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
        sut.WriteOperations.Should().Be(1);
        databaseMock.Verify(
            x => x.KeyExpireAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(),
                CommandFlags.None),
            Times.Never);
    }

    [Fact]
    public async Task SetReadPositionWithMaxWinsAsync_WhenUserIdIsNull_ReturnsFalse()
    {
        // Arrange
        var (sut, _) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        // Act
        var result = await sut.SetReadPositionWithMaxWinsAsync(null!, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetReadPositionWithMaxWinsAsync_WhenUserIdIsEmpty_ReturnsFalse()
    {
        // Arrange
        var (sut, _) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        // Act
        var result = await sut.SetReadPositionWithMaxWinsAsync(string.Empty, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetReadPositionWithMaxWinsAsync_WhenRedisThrows_ReturnsFalseAndLogsError()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        databaseMock
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]?>(),
                It.IsAny<RedisValue[]?>(),
                CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.SetReadPositionWithMaxWinsAsync(TestUserId, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region SetAllReadPositionsAsync Tests (HMSET — FEAT-060)

    [Fact]
    public async Task SetAllReadPositionsAsync_WhenPositionsExist_SetsHashAndTtl()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var positions = new Dictionary<FeedId, BlockIndex>
        {
            { TestFeedId, new BlockIndex(500) },
            { TestFeedId2, new BlockIndex(300) }
        };

        databaseMock
            .Setup(x => x.HashSetAsync(ExpectedHashKey, It.IsAny<HashEntry[]>(), CommandFlags.None))
            .Returns(Task.CompletedTask);

        databaseMock
            .Setup(x => x.KeyExpireAsync(
                ExpectedHashKey,
                FeedReadPositionCacheConstants.CacheTtl,
                ExpireWhen.Always,
                CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.SetAllReadPositionsAsync(TestUserId, positions);

        // Assert
        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
        databaseMock.Verify(
            x => x.HashSetAsync(
                ExpectedHashKey,
                It.Is<HashEntry[]>(e => e.Length == 2),
                CommandFlags.None),
            Times.Once);
        databaseMock.Verify(
            x => x.KeyExpireAsync(
                ExpectedHashKey,
                FeedReadPositionCacheConstants.CacheTtl,
                ExpireWhen.Always,
                CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task SetAllReadPositionsAsync_WhenUserIdIsNull_ReturnsFalse()
    {
        // Arrange
        var (sut, _) = CreateCacheService();
        var positions = new Dictionary<FeedId, BlockIndex>
        {
            { TestFeedId, new BlockIndex(500) }
        };

        // Act
        var result = await sut.SetAllReadPositionsAsync(null!, positions);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetAllReadPositionsAsync_WhenPositionsEmpty_ReturnsFalse()
    {
        // Arrange
        var (sut, _) = CreateCacheService();
        var positions = new Dictionary<FeedId, BlockIndex>();

        // Act
        var result = await sut.SetAllReadPositionsAsync(TestUserId, positions);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetAllReadPositionsAsync_WhenRedisThrows_ReturnsFalseAndLogsError()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var positions = new Dictionary<FeedId, BlockIndex>
        {
            { TestFeedId, new BlockIndex(500) }
        };

        databaseMock
            .Setup(x => x.HashSetAsync(ExpectedHashKey, It.IsAny<HashEntry[]>(), CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.SetAllReadPositionsAsync(TestUserId, positions);

        // Assert
        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region InvalidateCacheAsync Tests (Legacy STRING — FEAT-051)

    [Fact]
    public async Task InvalidateCacheAsync_DeletesKey()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyDeleteAsync(ExpectedLegacyKey, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.InvalidateCacheAsync(TestUserId, TestFeedId);

        // Assert
        databaseMock.Verify(
            x => x.KeyDeleteAsync(ExpectedLegacyKey, CommandFlags.None),
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
            .Setup(x => x.KeyDeleteAsync(ExpectedLegacyKey, CommandFlags.None))
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
