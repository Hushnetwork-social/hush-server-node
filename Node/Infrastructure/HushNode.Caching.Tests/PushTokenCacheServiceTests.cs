using FluentAssertions;
using HushNode.Interfaces.Models;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Caching.Tests;

/// <summary>
/// Tests for PushTokenCacheService - caches push notification device tokens in Redis.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class PushTokenCacheServiceTests
{
    private const string KeyPrefix = "HushFeeds:";
    private const string TestUserId = "test-user-123";
    private static readonly string ExpectedKey = $"{KeyPrefix}push:v1:user:{TestUserId}";

    #region GetTokensAsync Tests

    [Fact]
    public async Task GetTokensAsync_CacheMiss_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        var result = await sut.GetTokensAsync(TestUserId);

        // Assert
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetTokensAsync_CacheHit_ReturnsTokens()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var token1 = CreateTestToken("token-1");
        var token2 = CreateTestToken("token-2");

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        var hashEntries = new HashEntry[]
        {
            new(token1.Id, SerializeToken(token1)),
            new(token2.Id, SerializeToken(token2))
        };

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(hashEntries);

        // Act
        var result = await sut.GetTokensAsync(TestUserId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetTokensAsync_CacheHitEmptyHash_ReturnsEmptyCollection()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(Array.Empty<HashEntry>());

        // Act
        var result = await sut.GetTokensAsync(TestUserId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetTokensAsync_OnRedisFailure_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.GetTokensAsync(TestUserId);

        // Assert
        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetTokensAsync_WithInvalidJson_SkipsInvalidEntries()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var validToken = CreateTestToken("valid-token");

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        var hashEntries = new HashEntry[]
        {
            new(validToken.Id, SerializeToken(validToken)),
            new("invalid-id", "{ invalid json }")
        };

        databaseMock
            .Setup(x => x.HashGetAllAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(hashEntries);

        // Act
        var result = await sut.GetTokensAsync(TestUserId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1); // Only valid token returned
        sut.CacheHits.Should().Be(1);
    }

    #endregion

    #region SetTokensAsync Tests

    [Fact]
    public async Task SetTokensAsync_SetsTokensInCache()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var tokens = new[] { CreateTestToken("token-1"), CreateTestToken("token-2") };

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.SetTokensAsync(TestUserId, tokens);

        // Assert
        transactionMock.Verify(
            x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None),
            Times.Once);
        transactionMock.Verify(
            x => x.HashSetAsync(ExpectedKey, It.IsAny<HashEntry[]>(), CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task SetTokensAsync_SetsTtl()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var tokens = new[] { CreateTestToken("token-1") };

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.SetTokensAsync(TestUserId, tokens);

        // Assert
        transactionMock.Verify(
            x => x.KeyExpireAsync(ExpectedKey, PushTokenCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task SetTokensAsync_IncrementsWriteOperations()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var tokens = new[] { CreateTestToken("token-1") };

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.SetTokensAsync(TestUserId, tokens);

        // Assert
        sut.WriteOperations.Should().Be(1);
    }

    [Fact]
    public async Task SetTokensAsync_WithEmptyList_DoesNothing()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var tokens = Array.Empty<DeviceToken>();

        // Act
        await sut.SetTokensAsync(TestUserId, tokens);

        // Assert
        databaseMock.Verify(
            x => x.CreateTransaction(null),
            Times.Never);
        sut.WriteOperations.Should().Be(0);
    }

    [Fact]
    public async Task SetTokensAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var tokens = new[] { CreateTestToken("token-1") };

        databaseMock
            .Setup(x => x.CreateTransaction(null))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.SetTokensAsync(TestUserId, tokens);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region AddOrUpdateTokenAsync Tests

    [Fact]
    public async Task AddOrUpdateTokenAsync_AddsTokenToCache()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var token = CreateTestToken("token-1");

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.AddOrUpdateTokenAsync(TestUserId, token);

        // Assert
        transactionMock.Verify(
            x => x.HashSetAsync(ExpectedKey, token.Id, It.IsAny<RedisValue>(), When.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task AddOrUpdateTokenAsync_RefreshesTtl()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var token = CreateTestToken("token-1");

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.AddOrUpdateTokenAsync(TestUserId, token);

        // Assert
        transactionMock.Verify(
            x => x.KeyExpireAsync(ExpectedKey, PushTokenCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task AddOrUpdateTokenAsync_IncrementsWriteOperations()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var token = CreateTestToken("token-1");

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.AddOrUpdateTokenAsync(TestUserId, token);

        // Assert
        sut.WriteOperations.Should().Be(1);
    }

    [Fact]
    public async Task AddOrUpdateTokenAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var token = CreateTestToken("token-1");

        databaseMock
            .Setup(x => x.CreateTransaction(null))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.AddOrUpdateTokenAsync(TestUserId, token);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region RemoveTokenAsync Tests

    [Fact]
    public async Task RemoveTokenAsync_RemovesTokenFromCache()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var tokenId = "token-to-remove";

        databaseMock
            .Setup(x => x.HashDeleteAsync(ExpectedKey, tokenId, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.RemoveTokenAsync(TestUserId, tokenId);

        // Assert
        databaseMock.Verify(
            x => x.HashDeleteAsync(ExpectedKey, tokenId, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task RemoveTokenAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var tokenId = "token-to-remove";

        databaseMock
            .Setup(x => x.HashDeleteAsync(ExpectedKey, tokenId, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.RemoveTokenAsync(TestUserId, tokenId);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region InvalidateUserCacheAsync Tests

    [Fact]
    public async Task InvalidateUserCacheAsync_DeletesKey()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.InvalidateUserCacheAsync(TestUserId);

        // Assert
        databaseMock.Verify(
            x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task InvalidateUserCacheAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.InvalidateUserCacheAsync(TestUserId);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Helper Methods

    private static (PushTokenCacheService sut, Mock<IDatabase> databaseMock, Mock<ITransaction> transactionMock)
        CreateCacheService()
    {
        var connectionMultiplexerMock = new Mock<IConnectionMultiplexer>();
        var databaseMock = new Mock<IDatabase>();
        var transactionMock = new Mock<ITransaction>();
        var loggerMock = new Mock<ILogger<PushTokenCacheService>>();

        connectionMultiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        databaseMock
            .Setup(x => x.CreateTransaction(null))
            .Returns(transactionMock.Object);

        var sut = new PushTokenCacheService(
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
            .Setup(x => x.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), CommandFlags.None))
            .Returns(Task.CompletedTask);

        transactionMock
            .Setup(x => x.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(), When.Always, CommandFlags.None))
            .ReturnsAsync(true);

        transactionMock
            .Setup(x => x.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan>(), ExpireWhen.Always, CommandFlags.None))
            .ReturnsAsync(true);

        transactionMock
            .Setup(x => x.ExecuteAsync(CommandFlags.None))
            .ReturnsAsync(true);
    }

    private static DeviceToken CreateTestToken(string tokenId)
    {
        return new DeviceToken
        {
            Id = tokenId,
            UserId = TestUserId,
            Platform = PushPlatform.Android,
            Token = $"fcm-token-{tokenId}",
            DeviceName = "Test Device",
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    private static string SerializeToken(DeviceToken token)
    {
        return System.Text.Json.JsonSerializer.Serialize(token);
    }

    #endregion
}
