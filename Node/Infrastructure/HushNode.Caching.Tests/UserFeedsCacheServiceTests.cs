using FluentAssertions;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Caching.Tests;

/// <summary>
/// Tests for UserFeedsCacheService - caches user feed IDs in Redis SET.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class UserFeedsCacheServiceTests
{
    private const string KeyPrefix = "HushFeeds:";
    private const string TestUserId = "test-user-123";
    private static readonly string ExpectedKey = $"{KeyPrefix}user:{TestUserId}:feeds";

    #region GetUserFeedsAsync Tests

    [Fact]
    public async Task GetUserFeedsAsync_CacheMiss_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        var result = await sut.GetUserFeedsAsync(TestUserId);

        // Assert
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetUserFeedsAsync_CacheHit_ReturnsFeedIds()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var feedId1 = Guid.NewGuid();
        var feedId2 = Guid.NewGuid();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.SetMembersAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(new RedisValue[] { feedId1.ToString(), feedId2.ToString() });

        databaseMock
            .Setup(x => x.KeyExpireAsync(ExpectedKey, UserFeedsCacheConstants.CacheTtl, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.GetUserFeedsAsync(TestUserId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain(x => x.Value == feedId1);
        result.Should().Contain(x => x.Value == feedId2);
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetUserFeedsAsync_CacheHitEmptySet_ReturnsEmptyCollection()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.SetMembersAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(Array.Empty<RedisValue>());

        databaseMock
            .Setup(x => x.KeyExpireAsync(ExpectedKey, UserFeedsCacheConstants.CacheTtl, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.GetUserFeedsAsync(TestUserId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetUserFeedsAsync_OnRedisFailure_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.GetUserFeedsAsync(TestUserId);

        // Assert
        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetUserFeedsAsync_WithInvalidGuid_SkipsInvalidEntries()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var validFeedId = Guid.NewGuid();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.SetMembersAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(new RedisValue[] { validFeedId.ToString(), "not-a-valid-guid" });

        databaseMock
            .Setup(x => x.KeyExpireAsync(ExpectedKey, UserFeedsCacheConstants.CacheTtl, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.GetUserFeedsAsync(TestUserId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1); // Only valid feed ID returned
        result!.First().Value.Should().Be(validFeedId);
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetUserFeedsAsync_RefreshesTtlOnCacheHit()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var feedId = Guid.NewGuid();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.SetMembersAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(new RedisValue[] { feedId.ToString() });

        databaseMock
            .Setup(x => x.KeyExpireAsync(ExpectedKey, UserFeedsCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.GetUserFeedsAsync(TestUserId);

        // Assert
        databaseMock.Verify(
            x => x.KeyExpireAsync(ExpectedKey, UserFeedsCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    #endregion

    #region SetUserFeedsAsync Tests

    [Fact]
    public async Task SetUserFeedsAsync_SetsFeedIdsInCache()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var feedIds = new[] { new FeedId(Guid.NewGuid()), new FeedId(Guid.NewGuid()) };

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.SetUserFeedsAsync(TestUserId, feedIds);

        // Assert
        transactionMock.Verify(
            x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None),
            Times.Once);
        transactionMock.Verify(
            x => x.SetAddAsync(ExpectedKey, It.IsAny<RedisValue[]>(), CommandFlags.None),
            Times.Once);
        sut.WriteOperations.Should().Be(1);
    }

    [Fact]
    public async Task SetUserFeedsAsync_SetsTtl()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var feedIds = new[] { new FeedId(Guid.NewGuid()) };

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.SetUserFeedsAsync(TestUserId, feedIds);

        // Assert
        transactionMock.Verify(
            x => x.KeyExpireAsync(ExpectedKey, UserFeedsCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task SetUserFeedsAsync_WithEmptyList_DeletesKey()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var feedIds = Array.Empty<FeedId>();

        databaseMock
            .Setup(x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.SetUserFeedsAsync(TestUserId, feedIds);

        // Assert
        databaseMock.Verify(
            x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None),
            Times.Once);
        databaseMock.Verify(
            x => x.CreateTransaction(null),
            Times.Never);
    }

    [Fact]
    public async Task SetUserFeedsAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var feedIds = new[] { new FeedId(Guid.NewGuid()) };

        databaseMock
            .Setup(x => x.CreateTransaction(null))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.SetUserFeedsAsync(TestUserId, feedIds);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region AddFeedToUserCacheAsync Tests

    [Fact]
    public async Task AddFeedToUserCacheAsync_WhenCacheExists_AddsFeedId()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var feedId = new FeedId(Guid.NewGuid());

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.AddFeedToUserCacheAsync(TestUserId, feedId);

        // Assert
        transactionMock.Verify(
            x => x.SetAddAsync(ExpectedKey, feedId.Value.ToString(), CommandFlags.None),
            Times.Once);
        sut.WriteOperations.Should().Be(1);
    }

    [Fact]
    public async Task AddFeedToUserCacheAsync_RefreshesTtl()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var feedId = new FeedId(Guid.NewGuid());

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.AddFeedToUserCacheAsync(TestUserId, feedId);

        // Assert
        transactionMock.Verify(
            x => x.KeyExpireAsync(ExpectedKey, UserFeedsCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task AddFeedToUserCacheAsync_WhenCacheNotPopulated_SkipsAdd()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var feedId = new FeedId(Guid.NewGuid());

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        await sut.AddFeedToUserCacheAsync(TestUserId, feedId);

        // Assert
        databaseMock.Verify(
            x => x.CreateTransaction(null),
            Times.Never);
        sut.WriteOperations.Should().Be(0);
    }

    [Fact]
    public async Task AddFeedToUserCacheAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var feedId = new FeedId(Guid.NewGuid());

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.AddFeedToUserCacheAsync(TestUserId, feedId);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region RemoveFeedFromUserCacheAsync Tests

    [Fact]
    public async Task RemoveFeedFromUserCacheAsync_RemovesFeedId()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var feedId = new FeedId(Guid.NewGuid());

        databaseMock
            .Setup(x => x.SetRemoveAsync(ExpectedKey, feedId.Value.ToString(), CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.RemoveFeedFromUserCacheAsync(TestUserId, feedId);

        // Assert
        databaseMock.Verify(
            x => x.SetRemoveAsync(ExpectedKey, feedId.Value.ToString(), CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task RemoveFeedFromUserCacheAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var feedId = new FeedId(Guid.NewGuid());

        databaseMock
            .Setup(x => x.SetRemoveAsync(ExpectedKey, feedId.Value.ToString(), CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.RemoveFeedFromUserCacheAsync(TestUserId, feedId);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Key Prefix Tests

    [Fact]
    public async Task GetUserFeedsAsync_UsesCorrectKeyWithPrefix()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        await sut.GetUserFeedsAsync(TestUserId);

        // Assert - verify the correct key was used
        databaseMock.Verify(
            x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private static (UserFeedsCacheService sut, Mock<IDatabase> databaseMock, Mock<ITransaction> transactionMock)
        CreateCacheService()
    {
        var connectionMultiplexerMock = new Mock<IConnectionMultiplexer>();
        var databaseMock = new Mock<IDatabase>();
        var transactionMock = new Mock<ITransaction>();
        var loggerMock = new Mock<ILogger<UserFeedsCacheService>>();

        connectionMultiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        databaseMock
            .Setup(x => x.CreateTransaction(null))
            .Returns(transactionMock.Object);

        var sut = new UserFeedsCacheService(
            connectionMultiplexerMock.Object,
            KeyPrefix,
            loggerMock.Object);

        return (sut, databaseMock, transactionMock);
    }

    private static void SetupSuccessfulTransaction(Mock<IDatabase> databaseMock, Mock<ITransaction> transactionMock)
    {
        transactionMock
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), CommandFlags.None))
            .ReturnsAsync(true);

        transactionMock
            .Setup(x => x.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue[]>(), CommandFlags.None))
            .ReturnsAsync(0);

        transactionMock
            .Setup(x => x.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), CommandFlags.None))
            .ReturnsAsync(true);

        transactionMock
            .Setup(x => x.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan>(), CommandFlags.None))
            .ReturnsAsync(true);

        transactionMock
            .Setup(x => x.ExecuteAsync(CommandFlags.None))
            .ReturnsAsync(true);
    }

    #endregion
}
