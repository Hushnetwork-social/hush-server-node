using System.Text.Json;
using FluentAssertions;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Caching.Tests;

/// <summary>
/// Tests for FeedMetadataCacheService - caches per-user feed metadata in Redis.
/// FEAT-060 tests cover legacy lastBlockIndex methods.
/// FEAT-065 tests cover full 6-field metadata methods.
/// Each test follows AAA pattern with isolated factory method.
/// </summary>
public class FeedMetadataCacheServiceTests
{
    private const string KeyPrefix = "HushFeeds:";
    private const string TestUserId = "0x1234567890abcdef";
    private static readonly FeedId TestFeedId = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly FeedId TestFeedId2 = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly string ExpectedHashKey = $"{KeyPrefix}user:{TestUserId}:feed_meta";

    #region FEAT-060 Legacy Tests — GetAllLastBlockIndexesAsync

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
        databaseMock.Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None)).ReturnsAsync(entries);

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
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None)).ReturnsAsync(Array.Empty<HashEntry>());

        var result = await sut.GetAllLastBlockIndexesAsync(TestUserId);

        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetAllLastBlockIndexesAsync_WhenRedisThrows_ReturnsNull()
    {
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test"));

        var result = await sut.GetAllLastBlockIndexesAsync(TestUserId);

        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetAllLastBlockIndexesAsync_WhenUserIdIsNull_ReturnsNull()
    {
        var (sut, _) = CreateCacheService();
        var result = await sut.GetAllLastBlockIndexesAsync(null!);
        result.Should().BeNull();
    }

    #endregion

    #region FEAT-065 — GetAllFeedMetadataAsync

    [Fact]
    public async Task GetAllFeedMetadataAsync_WhenCacheHit_ReturnsFullMetadata()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var entry1 = CreateTestEntry("Chat with Bob", 1, 500, new[] { "0xalice", "0xbob" }, 10, null);
        var entry2 = CreateTestEntry("Dev Group", 2, 300, new[] { "0xalice", "0xbob", "0xcarol" }, 5, 2);
        var entries = new HashEntry[]
        {
            new(TestFeedId.ToString(), JsonSerializer.Serialize(entry1)),
            new(TestFeedId2.ToString(), JsonSerializer.Serialize(entry2))
        };
        databaseMock.Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None)).ReturnsAsync(entries);

        // Act
        var result = await sut.GetAllFeedMetadataAsync(TestUserId);

        // Assert
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result[TestFeedId].Title.Should().Be("Chat with Bob");
        result[TestFeedId].Type.Should().Be(1);
        result[TestFeedId].LastBlockIndex.Should().Be(500);
        result[TestFeedId].Participants.Should().HaveCount(2);
        result[TestFeedId].CreatedAtBlock.Should().Be(10);
        result[TestFeedId].CurrentKeyGeneration.Should().BeNull();
        result[TestFeedId2].Title.Should().Be("Dev Group");
        result[TestFeedId2].CurrentKeyGeneration.Should().Be(2);
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetAllFeedMetadataAsync_WhenCacheMiss_ReturnsNull()
    {
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None)).ReturnsAsync(Array.Empty<HashEntry>());

        var result = await sut.GetAllFeedMetadataAsync(TestUserId);

        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetAllFeedMetadataAsync_WhenLegacyFormat_ReturnsNullForLazyMigration()
    {
        // Arrange — FEAT-060 format: {"lastBlockIndex": 500} (no title, no participants)
        var (sut, databaseMock) = CreateCacheService();
        var entries = new HashEntry[]
        {
            new(TestFeedId.ToString(), "{\"lastBlockIndex\":500}")
        };
        databaseMock.Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None)).ReturnsAsync(entries);

        // Act
        var result = await sut.GetAllFeedMetadataAsync(TestUserId);

        // Assert — legacy format triggers cache miss for lazy migration
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetAllFeedMetadataAsync_WhenRedisThrows_ReturnsNull()
    {
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test"));

        var result = await sut.GetAllFeedMetadataAsync(TestUserId);

        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetAllFeedMetadataAsync_WhenInvalidJson_SkipsEntry()
    {
        var (sut, databaseMock) = CreateCacheService();
        var validEntry = CreateTestEntry("Chat", 1, 100, new[] { "0xa" }, 1, null);
        var entries = new HashEntry[]
        {
            new(TestFeedId.ToString(), JsonSerializer.Serialize(validEntry)),
            new(TestFeedId2.ToString(), "not-valid-json")
        };
        databaseMock.Setup(x => x.HashGetAllAsync(ExpectedHashKey, CommandFlags.None)).ReturnsAsync(entries);

        var result = await sut.GetAllFeedMetadataAsync(TestUserId);

        result.Should().NotBeNull();
        result!.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetAllFeedMetadataAsync_WhenUserIdIsNull_ReturnsNull()
    {
        var (sut, _) = CreateCacheService();
        var result = await sut.GetAllFeedMetadataAsync(null!);
        result.Should().BeNull();
    }

    #endregion

    #region FEAT-065 — SetFeedMetadataAsync

    [Fact]
    public async Task SetFeedMetadataAsync_WritesFullJsonAndRefreshesTtl()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var entry = CreateTestEntry("Chat with Bob", 1, 500, new[] { "0xalice", "0xbob" }, 10, null);

        databaseMock.Setup(x => x.HashSetAsync(ExpectedHashKey, TestFeedId.ToString(),
            It.IsAny<RedisValue>(), It.IsAny<When>(), CommandFlags.None)).ReturnsAsync(true);
        databaseMock.Setup(x => x.KeyExpireAsync(ExpectedHashKey,
            FeedMetadataCacheConstants.CacheTtl, It.IsAny<ExpireWhen>(), CommandFlags.None)).ReturnsAsync(true);

        // Act
        var result = await sut.SetFeedMetadataAsync(TestUserId, TestFeedId, entry);

        // Assert
        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
        databaseMock.Verify(x => x.KeyExpireAsync(ExpectedHashKey,
            FeedMetadataCacheConstants.CacheTtl, It.IsAny<ExpireWhen>(), CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task SetFeedMetadataAsync_WhenRedisThrows_ReturnsFalse()
    {
        var (sut, databaseMock) = CreateCacheService();
        var entry = CreateTestEntry("Chat", 1, 100, new[] { "0xa" }, 1, null);

        databaseMock.Setup(x => x.HashSetAsync(ExpectedHashKey, TestFeedId.ToString(),
            It.IsAny<RedisValue>(), It.IsAny<When>(), CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test"));

        var result = await sut.SetFeedMetadataAsync(TestUserId, TestFeedId, entry);

        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    [Fact]
    public async Task SetFeedMetadataAsync_WhenUserIdIsNull_ReturnsFalse()
    {
        var (sut, _) = CreateCacheService();
        var entry = CreateTestEntry("Chat", 1, 100, new[] { "0xa" }, 1, null);
        var result = await sut.SetFeedMetadataAsync(null!, TestFeedId, entry);
        result.Should().BeFalse();
    }

    #endregion

    #region FEAT-065 — SetMultipleFeedMetadataAsync

    [Fact]
    public async Task SetMultipleFeedMetadataAsync_WritesAllEntriesWithTtl()
    {
        var (sut, databaseMock) = CreateCacheService();
        var entries = new Dictionary<FeedId, FeedMetadataEntry>
        {
            { TestFeedId, CreateTestEntry("Chat", 1, 500, new[] { "0xa" }, 10, null) },
            { TestFeedId2, CreateTestEntry("Group", 2, 300, new[] { "0xa", "0xb" }, 5, 1) }
        };

        databaseMock.Setup(x => x.HashSetAsync(ExpectedHashKey, It.IsAny<HashEntry[]>(), CommandFlags.None)).Returns(Task.CompletedTask);
        databaseMock.Setup(x => x.KeyExpireAsync(ExpectedHashKey, FeedMetadataCacheConstants.CacheTtl, It.IsAny<ExpireWhen>(), CommandFlags.None)).ReturnsAsync(true);

        var result = await sut.SetMultipleFeedMetadataAsync(TestUserId, entries);

        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
        databaseMock.Verify(x => x.HashSetAsync(ExpectedHashKey, It.Is<HashEntry[]>(e => e.Length == 2), CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task SetMultipleFeedMetadataAsync_WhenEmpty_ReturnsFalse()
    {
        var (sut, _) = CreateCacheService();
        var result = await sut.SetMultipleFeedMetadataAsync(TestUserId, new Dictionary<FeedId, FeedMetadataEntry>());
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetMultipleFeedMetadataAsync_WhenRedisThrows_ReturnsFalse()
    {
        var (sut, databaseMock) = CreateCacheService();
        var entries = new Dictionary<FeedId, FeedMetadataEntry>
        {
            { TestFeedId, CreateTestEntry("Chat", 1, 100, new[] { "0xa" }, 1, null) }
        };
        databaseMock.Setup(x => x.HashSetAsync(ExpectedHashKey, It.IsAny<HashEntry[]>(), CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test"));

        var result = await sut.SetMultipleFeedMetadataAsync(TestUserId, entries);

        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region FEAT-065 — UpdateFeedTitleAsync

    [Fact]
    public async Task UpdateFeedTitleAsync_ReadsModifiesAndWritesBack()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var existingEntry = CreateTestEntry("Bob", 1, 500, new[] { "0xalice", "0xbob" }, 10, null);
        var existingJson = JsonSerializer.Serialize(existingEntry);

        databaseMock.Setup(x => x.HashGetAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None))
            .ReturnsAsync((RedisValue)existingJson);
        databaseMock.Setup(x => x.HashSetAsync(ExpectedHashKey, TestFeedId.ToString(),
            It.IsAny<RedisValue>(), It.IsAny<When>(), CommandFlags.None)).ReturnsAsync(true);
        databaseMock.Setup(x => x.KeyExpireAsync(ExpectedHashKey,
            FeedMetadataCacheConstants.CacheTtl, It.IsAny<ExpireWhen>(), CommandFlags.None)).ReturnsAsync(true);

        // Act
        var result = await sut.UpdateFeedTitleAsync(TestUserId, TestFeedId, "Robert");

        // Assert
        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
        databaseMock.Verify(x => x.HashSetAsync(ExpectedHashKey, TestFeedId.ToString(),
            It.Is<RedisValue>(v => v.ToString().Contains("Robert") && v.ToString().Contains("0xalice")),
            It.IsAny<When>(), CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task UpdateFeedTitleAsync_WhenEntryNotFound_ReturnsFalse()
    {
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashGetAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None))
            .ReturnsAsync(RedisValue.Null);

        var result = await sut.UpdateFeedTitleAsync(TestUserId, TestFeedId, "Robert");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateFeedTitleAsync_WhenLegacyEntry_ReturnsFalse()
    {
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashGetAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None))
            .ReturnsAsync((RedisValue)"{\"lastBlockIndex\":500}");

        var result = await sut.UpdateFeedTitleAsync(TestUserId, TestFeedId, "Robert");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateFeedTitleAsync_PreservesAllOtherFields()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var existingEntry = CreateTestEntry("Bob", 1, 500, new[] { "0xalice", "0xbob" }, 10, null);
        var existingJson = JsonSerializer.Serialize(existingEntry);
        string? capturedJson = null;

        databaseMock.Setup(x => x.HashGetAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None))
            .ReturnsAsync((RedisValue)existingJson);
        databaseMock.Setup(x => x.HashSetAsync(ExpectedHashKey, TestFeedId.ToString(),
            It.IsAny<RedisValue>(), It.IsAny<When>(), CommandFlags.None))
            .Callback<RedisKey, RedisValue, RedisValue, When, CommandFlags>((_, _, v, _, _) => capturedJson = v.ToString())
            .ReturnsAsync(true);
        databaseMock.Setup(x => x.KeyExpireAsync(ExpectedHashKey,
            FeedMetadataCacheConstants.CacheTtl, It.IsAny<ExpireWhen>(), CommandFlags.None)).ReturnsAsync(true);

        // Act
        await sut.UpdateFeedTitleAsync(TestUserId, TestFeedId, "Robert");

        // Assert — verify all fields preserved, only title changed
        capturedJson.Should().NotBeNull();
        var updatedEntry = JsonSerializer.Deserialize<FeedMetadataEntry>(capturedJson!);
        updatedEntry!.Title.Should().Be("Robert");
        updatedEntry.Type.Should().Be(1);
        updatedEntry.LastBlockIndex.Should().Be(500);
        updatedEntry.Participants.Should().BeEquivalentTo(new[] { "0xalice", "0xbob" });
        updatedEntry.CreatedAtBlock.Should().Be(10);
        updatedEntry.CurrentKeyGeneration.Should().BeNull();
    }

    [Fact]
    public async Task UpdateFeedTitleAsync_WhenRedisThrows_ReturnsFalse()
    {
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashGetAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test"));

        var result = await sut.UpdateFeedTitleAsync(TestUserId, TestFeedId, "Robert");

        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region FEAT-065 — RemoveFeedMetadataAsync

    [Fact]
    public async Task RemoveFeedMetadataAsync_DeletesHashField()
    {
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashDeleteAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None)).ReturnsAsync(true);

        var result = await sut.RemoveFeedMetadataAsync(TestUserId, TestFeedId);

        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
    }

    [Fact]
    public async Task RemoveFeedMetadataAsync_WhenFieldDoesNotExist_ReturnsFalse()
    {
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashDeleteAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None)).ReturnsAsync(false);

        var result = await sut.RemoveFeedMetadataAsync(TestUserId, TestFeedId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveFeedMetadataAsync_WhenRedisThrows_ReturnsFalse()
    {
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashDeleteAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test"));

        var result = await sut.RemoveFeedMetadataAsync(TestUserId, TestFeedId);

        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    [Fact]
    public async Task RemoveFeedMetadataAsync_WhenUserIdIsNull_ReturnsFalse()
    {
        var (sut, _) = CreateCacheService();
        var result = await sut.RemoveFeedMetadataAsync(null!, TestFeedId);
        result.Should().BeFalse();
    }

    #endregion

    #region FEAT-060 Legacy — RemoveFeedMetaAsync

    [Fact]
    public async Task RemoveFeedMetaAsync_DeletesHashField()
    {
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashDeleteAsync(ExpectedHashKey, TestFeedId.ToString(), CommandFlags.None)).ReturnsAsync(true);

        var result = await sut.RemoveFeedMetaAsync(TestUserId, TestFeedId);

        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
    }

    #endregion

    #region Helper Methods

    private static (FeedMetadataCacheService sut, Mock<IDatabase> databaseMock) CreateCacheService()
    {
        var connectionMultiplexerMock = new Mock<IConnectionMultiplexer>();
        var databaseMock = new Mock<IDatabase>();
        var loggerMock = new Mock<ILogger<FeedMetadataCacheService>>();

        connectionMultiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        var sut = new FeedMetadataCacheService(
            connectionMultiplexerMock.Object, KeyPrefix, loggerMock.Object);

        return (sut, databaseMock);
    }

    private static FeedMetadataEntry CreateTestEntry(
        string title, int type, long lastBlockIndex,
        string[] participants, long createdAtBlock, int? currentKeyGeneration)
    {
        return new FeedMetadataEntry
        {
            Title = title,
            Type = type,
            LastBlockIndex = lastBlockIndex,
            Participants = participants.ToList(),
            CreatedAtBlock = createdAtBlock,
            CurrentKeyGeneration = currentKeyGeneration
        };
    }

    #endregion
}
