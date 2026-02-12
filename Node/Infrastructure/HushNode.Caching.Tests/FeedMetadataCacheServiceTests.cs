using FluentAssertions;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Caching.Tests;

/// <summary>
/// Tests for FeedMetadataCacheService - caches per-user feed metadata (lastBlockIndex) in Redis.
/// Tests cover HGETALL, HSET, HMSET, and HDEL operations with JSON serialization (FEAT-060).
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedMetadataCacheServiceTests
{
    private const string KeyPrefix = "HushFeeds:";
    private const string TestUserId = "0x1234567890abcdef";
    private static readonly FeedId TestFeedId = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly FeedId TestFeedId2 = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly string ExpectedHashKey = $"{KeyPrefix}user:{TestUserId}:feed_meta";

    #region GetAllLastBlockIndexesAsync Tests (HGETALL + JSON)

    [Fact]
    public async Task GetAllLastBlockIndexesAsync_WhenCacheHit_ReturnsParsedDictionary()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var entries = new HashEntry[]
        {
            new(TestFeedId.ToString(), "{\"lastBlockIndex\":500}"),
            new(TestFeedId2.ToString(), "{\"lastBlockIndex\":300}")
        };

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ReturnsAsync(entries);

        // Act
        var result = await sut.GetAllLastBlockIndexesAsync(TestUserId);

        // Assert
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result[TestFeedId].Value.Should().Be(500);
        result[TestFeedId2].Value.Should().Be(300);
    }

    [Fact]
    public async Task GetAllLastBlockIndexesAsync_WhenCacheMiss_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ReturnsAsync(Array.Empty<HashEntry>());

        // Act
        var result = await sut.GetAllLastBlockIndexesAsync(TestUserId);

        // Assert
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetAllLastBlockIndexesAsync_WhenRedisThrows_ReturnsNullAndLogsWarning()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.GetAllLastBlockIndexesAsync(TestUserId);

        // Assert
        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetAllLastBlockIndexesAsync_WhenJsonInvalid_SkipsEntry()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var entries = new HashEntry[]
        {
            new(TestFeedId.ToString(), "{\"lastBlockIndex\":500}"),
            new(TestFeedId2.ToString(), "not-valid-json")
        };

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ReturnsAsync(entries);

        // Act
        var result = await sut.GetAllLastBlockIndexesAsync(TestUserId);

        // Assert
        result.Should().NotBeNull();
        result!.Count.Should().Be(1);
        result[TestFeedId].Value.Should().Be(500);
    }

    [Fact]
    public async Task GetAllLastBlockIndexesAsync_WhenUserIdIsNull_ReturnsNull()
    {
        // Arrange
        var (sut, _) = CreateCacheService();

        // Act
        var result = await sut.GetAllLastBlockIndexesAsync(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllLastBlockIndexesAsync_IncrementsCacheHitsOnHit()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var entries = new HashEntry[]
        {
            new(TestFeedId.ToString(), "{\"lastBlockIndex\":100}")
        };

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ReturnsAsync(entries);

        // Act
        await sut.GetAllLastBlockIndexesAsync(TestUserId);

        // Assert
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetAllLastBlockIndexesAsync_IncrementsCacheMissesOnMiss()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ReturnsAsync(Array.Empty<HashEntry>());

        // Act
        await sut.GetAllLastBlockIndexesAsync(TestUserId);

        // Assert
        sut.CacheMisses.Should().Be(1);
    }

    #endregion

    #region SetLastBlockIndexAsync Tests (HSET + JSON)

    [Fact]
    public async Task SetLastBlockIndexAsync_SetsJsonValueWithTtl()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        databaseMock
            .Setup(x => x.HashSetAsync(
                ExpectedHashKey,
                TestFeedId.ToString(),
                It.Is<RedisValue>(v => v.ToString().Contains("500")),
                It.IsAny<When>(),
                CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.KeyExpireAsync(
                ExpectedHashKey,
                FeedMetadataCacheConstants.CacheTtl,
                CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.SetLastBlockIndexAsync(TestUserId, TestFeedId, blockIndex);

        // Assert
        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
    }

    [Fact]
    public async Task SetLastBlockIndexAsync_WhenRedisThrows_ReturnsFalseAndLogsWarning()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        databaseMock
            .Setup(x => x.HashSetAsync(
                ExpectedHashKey,
                TestFeedId.ToString(),
                It.IsAny<RedisValue>(),
                It.IsAny<When>(),
                CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.SetLastBlockIndexAsync(TestUserId, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    [Fact]
    public async Task SetLastBlockIndexAsync_WhenUserIdIsNull_ReturnsFalse()
    {
        // Arrange
        var (sut, _) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        // Act
        var result = await sut.SetLastBlockIndexAsync(null!, TestFeedId, blockIndex);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetLastBlockIndexAsync_IncrementsWriteOperationsCounter()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var blockIndex = new BlockIndex(500);

        databaseMock
            .Setup(x => x.HashSetAsync(
                ExpectedHashKey,
                TestFeedId.ToString(),
                It.IsAny<RedisValue>(),
                It.IsAny<When>(),
                CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.KeyExpireAsync(
                ExpectedHashKey,
                FeedMetadataCacheConstants.CacheTtl,
                CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.SetLastBlockIndexAsync(TestUserId, TestFeedId, blockIndex);
        await sut.SetLastBlockIndexAsync(TestUserId, TestFeedId2, new BlockIndex(300));

        // Assert
        sut.WriteOperations.Should().Be(2);
    }

    #endregion

    #region SetMultipleLastBlockIndexesAsync Tests (HMSET)

    [Fact]
    public async Task SetMultipleLastBlockIndexesAsync_SetsAllEntriesWithTtl()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var blockIndexes = new Dictionary<FeedId, BlockIndex>
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
                FeedMetadataCacheConstants.CacheTtl,
                CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.SetMultipleLastBlockIndexesAsync(TestUserId, blockIndexes);

        // Assert
        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
        databaseMock.Verify(
            x => x.HashSetAsync(
                ExpectedHashKey,
                It.Is<HashEntry[]>(e => e.Length == 2),
                CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task SetMultipleLastBlockIndexesAsync_WhenEmpty_ReturnsFalse()
    {
        // Arrange
        var (sut, _) = CreateCacheService();
        var blockIndexes = new Dictionary<FeedId, BlockIndex>();

        // Act
        var result = await sut.SetMultipleLastBlockIndexesAsync(TestUserId, blockIndexes);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetMultipleLastBlockIndexesAsync_WhenRedisThrows_ReturnsFalseAndLogsError()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var blockIndexes = new Dictionary<FeedId, BlockIndex>
        {
            { TestFeedId, new BlockIndex(500) }
        };

        databaseMock
            .Setup(x => x.HashSetAsync(ExpectedHashKey, It.IsAny<HashEntry[]>(), CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.SetMultipleLastBlockIndexesAsync(TestUserId, blockIndexes);

        // Assert
        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region RemoveFeedMetaAsync Tests (HDEL)

    [Fact]
    public async Task RemoveFeedMetaAsync_DeletesHashField()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.HashDeleteAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.RemoveFeedMetaAsync(TestUserId, TestFeedId);

        // Assert
        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
        databaseMock.Verify(
            x => x.HashDeleteAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task RemoveFeedMetaAsync_WhenFieldDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.HashDeleteAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        var result = await sut.RemoveFeedMetaAsync(TestUserId, TestFeedId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveFeedMetaAsync_WhenRedisThrows_ReturnsFalse()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        databaseMock
            .Setup(x => x.HashDeleteAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.RemoveFeedMetaAsync(TestUserId, TestFeedId);

        // Assert
        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    [Fact]
    public async Task RemoveFeedMetaAsync_WhenUserIdIsNull_ReturnsFalse()
    {
        // Arrange
        var (sut, _) = CreateCacheService();

        // Act
        var result = await sut.RemoveFeedMetaAsync(null!, TestFeedId);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static (FeedMetadataCacheService sut, Mock<IDatabase> databaseMock)
        CreateCacheService()
    {
        var connectionMultiplexerMock = new Mock<IConnectionMultiplexer>();
        var databaseMock = new Mock<IDatabase>();
        var loggerMock = new Mock<ILogger<FeedMetadataCacheService>>();

        connectionMultiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        var sut = new FeedMetadataCacheService(
            connectionMultiplexerMock.Object,
            KeyPrefix,
            loggerMock.Object);

        return (sut, databaseMock);
    }

    #endregion
}
