using System.Text.Json;
using FluentAssertions;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Caching.Tests;

/// <summary>
/// Tests for FeedParticipantsCacheService - caches feed participants (SET) and key generations (JSON).
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class FeedParticipantsCacheServiceTests
{
    private const string KeyPrefix = "HushFeeds:";
    private static readonly FeedId TestFeedId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly string ParticipantsKey = $"{KeyPrefix}feed:{TestFeedId.Value}:participants";
    private static readonly string KeyGenerationsKey = $"{KeyPrefix}feed:{TestFeedId.Value}:keys";

    #region GetParticipantsAsync Tests

    [Fact]
    public async Task GetParticipantsAsync_CacheMiss_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ParticipantsKey, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        var result = await sut.GetParticipantsAsync(TestFeedId);

        // Assert
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetParticipantsAsync_CacheHit_ReturnsParticipants()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var participant1 = "address-alice-123";
        var participant2 = "address-bob-456";

        databaseMock
            .Setup(x => x.KeyExistsAsync(ParticipantsKey, CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.SetMembersAsync(ParticipantsKey, CommandFlags.None))
            .ReturnsAsync(new RedisValue[] { participant1, participant2 });

        databaseMock
            .Setup(x => x.KeyExpireAsync(ParticipantsKey, FeedParticipantsCacheConstants.CacheTtl, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.GetParticipantsAsync(TestFeedId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain(participant1);
        result.Should().Contain(participant2);
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetParticipantsAsync_CacheHitEmptySet_ReturnsEmptyCollection()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ParticipantsKey, CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.SetMembersAsync(ParticipantsKey, CommandFlags.None))
            .ReturnsAsync(Array.Empty<RedisValue>());

        databaseMock
            .Setup(x => x.KeyExpireAsync(ParticipantsKey, FeedParticipantsCacheConstants.CacheTtl, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.GetParticipantsAsync(TestFeedId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetParticipantsAsync_OnRedisFailure_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ParticipantsKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.GetParticipantsAsync(TestFeedId);

        // Assert
        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetParticipantsAsync_RefreshesTtlOnCacheHit()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ParticipantsKey, CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.SetMembersAsync(ParticipantsKey, CommandFlags.None))
            .ReturnsAsync(new RedisValue[] { "address-1" });

        databaseMock
            .Setup(x => x.KeyExpireAsync(ParticipantsKey, FeedParticipantsCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.GetParticipantsAsync(TestFeedId);

        // Assert
        databaseMock.Verify(
            x => x.KeyExpireAsync(ParticipantsKey, FeedParticipantsCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    #endregion

    #region SetParticipantsAsync Tests

    [Fact]
    public async Task SetParticipantsAsync_SetsParticipantsInCache()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var participants = new[] { "address-alice", "address-bob" };

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.SetParticipantsAsync(TestFeedId, participants);

        // Assert
        transactionMock.Verify(
            x => x.KeyDeleteAsync(ParticipantsKey, CommandFlags.None),
            Times.Once);
        transactionMock.Verify(
            x => x.SetAddAsync(ParticipantsKey, It.IsAny<RedisValue[]>(), CommandFlags.None),
            Times.Once);
        sut.WriteOperations.Should().Be(1);
    }

    [Fact]
    public async Task SetParticipantsAsync_SetsTtl()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var participants = new[] { "address-alice" };

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.SetParticipantsAsync(TestFeedId, participants);

        // Assert
        transactionMock.Verify(
            x => x.KeyExpireAsync(ParticipantsKey, FeedParticipantsCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task SetParticipantsAsync_WithEmptyList_CreatesEmptyCache()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var participants = Array.Empty<string>();

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        databaseMock
            .Setup(x => x.SetAddAsync(ParticipantsKey, "__placeholder__", CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.SetRemoveAsync(ParticipantsKey, "__placeholder__", CommandFlags.None))
            .ReturnsAsync(true);

        databaseMock
            .Setup(x => x.KeyExpireAsync(ParticipantsKey, FeedParticipantsCacheConstants.CacheTtl, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.SetParticipantsAsync(TestFeedId, participants);

        // Assert
        sut.WriteOperations.Should().Be(1);
    }

    [Fact]
    public async Task SetParticipantsAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var participants = new[] { "address-alice" };

        databaseMock
            .Setup(x => x.CreateTransaction(null))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.SetParticipantsAsync(TestFeedId, participants);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region AddParticipantAsync Tests

    [Fact]
    public async Task AddParticipantAsync_WhenCacheExists_AddsParticipant()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var participant = "address-charlie";

        databaseMock
            .Setup(x => x.KeyExistsAsync(ParticipantsKey, CommandFlags.None))
            .ReturnsAsync(true);

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.AddParticipantAsync(TestFeedId, participant);

        // Assert
        transactionMock.Verify(
            x => x.SetAddAsync(ParticipantsKey, participant, CommandFlags.None),
            Times.Once);
        sut.WriteOperations.Should().Be(1);
    }

    [Fact]
    public async Task AddParticipantAsync_RefreshesTtl()
    {
        // Arrange
        var (sut, databaseMock, transactionMock) = CreateCacheService();
        var participant = "address-charlie";

        databaseMock
            .Setup(x => x.KeyExistsAsync(ParticipantsKey, CommandFlags.None))
            .ReturnsAsync(true);

        SetupSuccessfulTransaction(databaseMock, transactionMock);

        // Act
        await sut.AddParticipantAsync(TestFeedId, participant);

        // Assert
        transactionMock.Verify(
            x => x.KeyExpireAsync(ParticipantsKey, FeedParticipantsCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task AddParticipantAsync_WhenCacheNotPopulated_SkipsAdd()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var participant = "address-charlie";

        databaseMock
            .Setup(x => x.KeyExistsAsync(ParticipantsKey, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        await sut.AddParticipantAsync(TestFeedId, participant);

        // Assert
        databaseMock.Verify(
            x => x.CreateTransaction(null),
            Times.Never);
        sut.WriteOperations.Should().Be(0);
    }

    [Fact]
    public async Task AddParticipantAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var participant = "address-charlie";

        databaseMock
            .Setup(x => x.KeyExistsAsync(ParticipantsKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.AddParticipantAsync(TestFeedId, participant);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region RemoveParticipantAsync Tests

    [Fact]
    public async Task RemoveParticipantAsync_RemovesParticipant()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var participant = "address-charlie";

        databaseMock
            .Setup(x => x.SetRemoveAsync(ParticipantsKey, participant, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.RemoveParticipantAsync(TestFeedId, participant);

        // Assert
        databaseMock.Verify(
            x => x.SetRemoveAsync(ParticipantsKey, participant, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task RemoveParticipantAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var participant = "address-charlie";

        databaseMock
            .Setup(x => x.SetRemoveAsync(ParticipantsKey, participant, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.RemoveParticipantAsync(TestFeedId, participant);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetKeyGenerationsAsync Tests

    [Fact]
    public async Task GetKeyGenerationsAsync_CacheMiss_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.StringGetAsync(KeyGenerationsKey, CommandFlags.None))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await sut.GetKeyGenerationsAsync(TestFeedId);

        // Assert
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetKeyGenerationsAsync_CacheHit_ReturnsKeyGenerations()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var cached = new CachedKeyGenerations
        {
            KeyGenerations = new List<KeyGenerationCacheDto>
            {
                new()
                {
                    Version = 1,
                    ValidFromBlock = 100,
                    ValidToBlock = 149,
                    EncryptedKeysByMember = new Dictionary<string, string>
                    {
                        ["alice"] = "key-for-alice"
                    }
                }
            }
        };
        var json = JsonSerializer.Serialize(cached);

        databaseMock
            .Setup(x => x.StringGetAsync(KeyGenerationsKey, CommandFlags.None))
            .ReturnsAsync(json);

        databaseMock
            .Setup(x => x.KeyExpireAsync(KeyGenerationsKey, FeedParticipantsCacheConstants.CacheTtl, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.GetKeyGenerationsAsync(TestFeedId);

        // Assert
        result.Should().NotBeNull();
        result!.KeyGenerations.Should().HaveCount(1);
        result.KeyGenerations[0].Version.Should().Be(1);
        result.KeyGenerations[0].EncryptedKeysByMember["alice"].Should().Be("key-for-alice");
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetKeyGenerationsAsync_InvalidJson_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.StringGetAsync(KeyGenerationsKey, CommandFlags.None))
            .ReturnsAsync("{ invalid json }");

        // Act
        var result = await sut.GetKeyGenerationsAsync(TestFeedId);

        // Assert
        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetKeyGenerationsAsync_OnRedisFailure_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.StringGetAsync(KeyGenerationsKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.GetKeyGenerationsAsync(TestFeedId);

        // Assert
        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetKeyGenerationsAsync_RefreshesTtlOnCacheHit()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var cached = new CachedKeyGenerations { KeyGenerations = new List<KeyGenerationCacheDto>() };
        var json = JsonSerializer.Serialize(cached);

        databaseMock
            .Setup(x => x.StringGetAsync(KeyGenerationsKey, CommandFlags.None))
            .ReturnsAsync(json);

        databaseMock
            .Setup(x => x.KeyExpireAsync(KeyGenerationsKey, FeedParticipantsCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.GetKeyGenerationsAsync(TestFeedId);

        // Assert
        databaseMock.Verify(
            x => x.KeyExpireAsync(KeyGenerationsKey, FeedParticipantsCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    #endregion

    #region SetKeyGenerationsAsync Tests

    [Fact]
    public async Task SetKeyGenerationsAsync_SetsJsonInCache()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var cached = new CachedKeyGenerations
        {
            KeyGenerations = new List<KeyGenerationCacheDto>
            {
                new()
                {
                    Version = 1,
                    ValidFromBlock = 100,
                    ValidToBlock = null,
                    EncryptedKeysByMember = new Dictionary<string, string>()
                }
            }
        };

        databaseMock
            .Setup(x => x.StringSetAsync(
                KeyGenerationsKey,
                It.IsAny<RedisValue>(),
                FeedParticipantsCacheConstants.CacheTtl,
                false,
                When.Always,
                CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.SetKeyGenerationsAsync(TestFeedId, cached);

        // Assert
        databaseMock.Verify(
            x => x.StringSetAsync(
                KeyGenerationsKey,
                It.Is<RedisValue>(v => v.ToString().Contains("\"version\":1")),
                FeedParticipantsCacheConstants.CacheTtl,
                false,
                When.Always,
                CommandFlags.None),
            Times.Once);
        sut.WriteOperations.Should().Be(1);
    }

    [Fact]
    public async Task SetKeyGenerationsAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var cached = new CachedKeyGenerations { KeyGenerations = new List<KeyGenerationCacheDto>() };

        databaseMock
            .Setup(x => x.StringSetAsync(
                KeyGenerationsKey,
                It.IsAny<RedisValue>(),
                FeedParticipantsCacheConstants.CacheTtl,
                false,
                When.Always,
                CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.SetKeyGenerationsAsync(TestFeedId, cached);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region InvalidateKeyGenerationsAsync Tests

    [Fact]
    public async Task InvalidateKeyGenerationsAsync_DeletesCacheKey()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyDeleteAsync(KeyGenerationsKey, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.InvalidateKeyGenerationsAsync(TestFeedId);

        // Assert
        databaseMock.Verify(
            x => x.KeyDeleteAsync(KeyGenerationsKey, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task InvalidateKeyGenerationsAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyDeleteAsync(KeyGenerationsKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.InvalidateKeyGenerationsAsync(TestFeedId);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Key Prefix Tests

    [Fact]
    public async Task GetParticipantsAsync_UsesCorrectKeyWithPrefix()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ParticipantsKey, CommandFlags.None))
            .ReturnsAsync(false);

        // Act
        await sut.GetParticipantsAsync(TestFeedId);

        // Assert - verify the correct key was used
        databaseMock.Verify(
            x => x.KeyExistsAsync(ParticipantsKey, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task GetKeyGenerationsAsync_UsesCorrectKeyWithPrefix()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.StringGetAsync(KeyGenerationsKey, CommandFlags.None))
            .ReturnsAsync(RedisValue.Null);

        // Act
        await sut.GetKeyGenerationsAsync(TestFeedId);

        // Assert - verify the correct key was used
        databaseMock.Verify(
            x => x.StringGetAsync(KeyGenerationsKey, CommandFlags.None),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private static (FeedParticipantsCacheService sut, Mock<IDatabase> databaseMock, Mock<ITransaction> transactionMock)
        CreateCacheService()
    {
        var connectionMultiplexerMock = new Mock<IConnectionMultiplexer>();
        var databaseMock = new Mock<IDatabase>();
        var transactionMock = new Mock<ITransaction>();
        var loggerMock = new Mock<ILogger<FeedParticipantsCacheService>>();

        connectionMultiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        databaseMock
            .Setup(x => x.CreateTransaction(null))
            .Returns(transactionMock.Object);

        var sut = new FeedParticipantsCacheService(
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
