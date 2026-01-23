using FluentAssertions;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Caching.Tests;

/// <summary>
/// Tests for FeedMessageCacheService - caches feed messages in Redis.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedMessageCacheServiceTests
{
    private const string KeyPrefix = "HushFeeds:";
    private static readonly FeedId TestFeedId = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly string ExpectedKey = $"{KeyPrefix}feed:{TestFeedId}:messages";

    #region AddMessageAsync Tests

    [Fact]
    public async Task AddMessageAsync_AddsMessageToCache()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var message = CreateTestMessage(1);

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.AddMessageAsync(TestFeedId, message);

        // Assert
        transactionMock.Verify(
            x => x.ListLeftPushAsync(ExpectedKey, It.IsAny<RedisValue>(), When.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task AddMessageAsync_TrimsToMaxMessages()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var message = CreateTestMessage(1);

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.AddMessageAsync(TestFeedId, message);

        // Assert
        transactionMock.Verify(
            x => x.ListTrimAsync(ExpectedKey, 0, FeedMessageCacheConstants.MaxMessagesPerFeed - 1, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task AddMessageAsync_RefreshesTtl()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var message = CreateTestMessage(1);

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.AddMessageAsync(TestFeedId, message);

        // Assert
        transactionMock.Verify(
            x => x.KeyExpireAsync(ExpectedKey, FeedMessageCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task AddMessageAsync_OnRedisFailure_LogsAndContinues()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var message = CreateTestMessage(1);

        databaseMock
            .Setup(x => x.CreateTransaction(null))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.AddMessageAsync(TestFeedId, message);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
        sut.WriteErrors.Should().Be(1);
    }

    [Fact]
    public async Task AddMessageAsync_IncrementsWriteOperations()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var message = CreateTestMessage(1);

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.AddMessageAsync(TestFeedId, message);

        // Assert
        sut.WriteOperations.Should().Be(1);
    }

    #endregion

    #region GetMessagesAsync Tests

    [Fact]
    public async Task GetMessagesAsync_CacheMiss_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        var result = await sut.GetMessagesAsync(TestFeedId);

        // Assert
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetMessagesAsync_CacheHit_ReturnsMessages()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var message1 = CreateTestMessage(1);
        var message2 = CreateTestMessage(2);

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        var serializedMessages = new RedisValue[]
        {
            SerializeMessage(message1),
            SerializeMessage(message2)
        };

        databaseMock
            .Setup(x => x.ListRangeAsync(ExpectedKey, 0, -1, CommandFlags.None))
            .ReturnsAsync(serializedMessages);

        // Act
        var result = await sut.GetMessagesAsync(TestFeedId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetMessagesAsync_CacheHitEmptyList_ReturnsEmptyCollection()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.ListRangeAsync(ExpectedKey, 0, -1, CommandFlags.None))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        var result = await sut.GetMessagesAsync(TestFeedId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetMessagesAsync_WithBlockIndexFilter_FiltersCorrectly()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var message1 = CreateTestMessage(1);  // BlockIndex = 1
        var message2 = CreateTestMessage(2);  // BlockIndex = 2
        var message3 = CreateTestMessage(3);  // BlockIndex = 3

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        var serializedMessages = new RedisValue[]
        {
            SerializeMessage(message1),
            SerializeMessage(message2),
            SerializeMessage(message3)
        };

        databaseMock
            .Setup(x => x.ListRangeAsync(ExpectedKey, 0, -1, CommandFlags.None))
            .ReturnsAsync(serializedMessages);

        var sinceBlockIndex = new BlockIndex(2); // Only return messages >= 2

        // Act
        var result = await sut.GetMessagesAsync(TestFeedId, sinceBlockIndex);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2); // Only message2 and message3 (filter uses >=)
        result!.All(m => m.BlockIndex >= sinceBlockIndex).Should().BeTrue();
    }

    [Fact]
    public async Task GetMessagesAsync_OnRedisFailure_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.GetMessagesAsync(TestFeedId);

        // Assert
        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    #endregion

    #region InvalidateCacheAsync Tests

    [Fact]
    public async Task InvalidateCacheAsync_DeletesKey()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.InvalidateCacheAsync(TestFeedId);

        // Assert
        databaseMock.Verify(
            x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task InvalidateCacheAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.InvalidateCacheAsync(TestFeedId);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region PopulateCacheAsync Tests

    [Fact]
    public async Task PopulateCacheAsync_PopulatesEmptyCache()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var messages = new[] { CreateTestMessage(1), CreateTestMessage(2) };

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.PopulateCacheAsync(TestFeedId, messages);

        // Assert
        transactionMock.Verify(
            x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None),
            Times.Once);
        transactionMock.Verify(
            x => x.ListRightPushAsync(ExpectedKey, It.IsAny<RedisValue[]>(), When.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task PopulateCacheAsync_SetsTtl()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var messages = new[] { CreateTestMessage(1) };

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.PopulateCacheAsync(TestFeedId, messages);

        // Assert
        transactionMock.Verify(
            x => x.KeyExpireAsync(ExpectedKey, FeedMessageCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task PopulateCacheAsync_TrimsToMaxMessages()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var messages = new[] { CreateTestMessage(1) };

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.PopulateCacheAsync(TestFeedId, messages);

        // Assert
        transactionMock.Verify(
            x => x.ListTrimAsync(ExpectedKey, -FeedMessageCacheConstants.MaxMessagesPerFeed, -1, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task PopulateCacheAsync_WithEmptyList_DoesNothing()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var messages = Array.Empty<FeedMessage>();

        // Act
        await sut.PopulateCacheAsync(TestFeedId, messages);

        // Assert
        databaseMock.Verify(
            x => x.CreateTransaction(null),
            Times.Never);
    }

    [Fact]
    public async Task PopulateCacheAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var messages = new[] { CreateTestMessage(1) };

        databaseMock
            .Setup(x => x.CreateTransaction(null))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.PopulateCacheAsync(TestFeedId, messages);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region Helper Methods

    private static (FeedMessageCacheService sut, Mock<IDatabase> databaseMock, Mock<ITransaction> transactionMock)
        CreateCacheService()
    {
        var connectionMultiplexerMock = new Mock<IConnectionMultiplexer>();
        var databaseMock = new Mock<IDatabase>();
        var transactionMock = new Mock<ITransaction>();
        var loggerMock = new Mock<ILogger<FeedMessageCacheService>>();

        connectionMultiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        databaseMock
            .Setup(x => x.CreateTransaction(null))
            .Returns(transactionMock.Object);

        var sut = new FeedMessageCacheService(
            connectionMultiplexerMock.Object,
            KeyPrefix,
            loggerMock.Object);

        return (sut, databaseMock, transactionMock);
    }

    private static void SetupSuccessfulTransaction(Mock<IDatabase> databaseMock, Mock<ITransaction> transactionMock)
    {
        transactionMock
            .Setup(x => x.ListLeftPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), When.Always, CommandFlags.None))
            .ReturnsAsync(1);

        transactionMock
            .Setup(x => x.ListRightPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue[]>(), When.Always, CommandFlags.None))
            .ReturnsAsync(1);

        transactionMock
            .Setup(x => x.ListTrimAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), CommandFlags.None))
            .Returns(Task.CompletedTask);

        transactionMock
            .Setup(x => x.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan>(), ExpireWhen.Always, CommandFlags.None))
            .ReturnsAsync(true);

        transactionMock
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), CommandFlags.None))
            .ReturnsAsync(true);

        transactionMock
            .Setup(x => x.ExecuteAsync(CommandFlags.None))
            .ReturnsAsync(true);
    }

    private static FeedMessage CreateTestMessage(long blockIndex)
    {
        return new FeedMessage(
            FeedMessageId: new FeedMessageId(Guid.NewGuid()),
            FeedId: TestFeedId,
            MessageContent: $"Test message at block {blockIndex}",
            IssuerPublicAddress: "test-issuer-address",
            Timestamp: new Timestamp(DateTime.UtcNow),
            BlockIndex: new BlockIndex(blockIndex),
            AuthorCommitment: null,
            ReplyToMessageId: null,
            KeyGeneration: null);
    }

    private static string SerializeMessage(FeedMessage message)
    {
        return System.Text.Json.JsonSerializer.Serialize(message);
    }

    #endregion
}
