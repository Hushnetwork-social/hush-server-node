using FluentAssertions;
using HushNode.Events;
using HushShared.Blockchain.BlockModel;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Olimpo;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Caching.Tests;

/// <summary>
/// Tests for IdentityCacheService - caches identity profiles in Redis.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class IdentityCacheServiceTests
{
    private const string KeyPrefix = "HushFeeds:";
    private const string TestAddress = "04abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
    private static readonly string ExpectedKey = $"{KeyPrefix}identity:{TestAddress}";

    #region GetIdentityAsync Tests

    [Fact]
    public async Task GetIdentityAsync_CacheMiss_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.StringGetAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await sut.GetIdentityAsync(TestAddress);

        // Assert
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetIdentityAsync_CacheHit_ReturnsProfile()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var profile = CreateTestProfile();

        databaseMock
            .Setup(x => x.StringGetAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(SerializeProfile(profile));

        databaseMock
            .Setup(x => x.KeyExpireAsync(ExpectedKey, IdentityCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.GetIdentityAsync(TestAddress);

        // Assert
        result.Should().NotBeNull();
        result!.PublicSigningAddress.Should().Be(profile.PublicSigningAddress);
        result.Alias.Should().Be(profile.Alias);
        sut.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetIdentityAsync_CacheHit_RefreshesTtl()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var profile = CreateTestProfile();

        databaseMock
            .Setup(x => x.StringGetAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(SerializeProfile(profile));

        databaseMock
            .Setup(x => x.KeyExpireAsync(ExpectedKey, IdentityCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.GetIdentityAsync(TestAddress);

        // Assert
        databaseMock.Verify(
            x => x.KeyExpireAsync(ExpectedKey, IdentityCacheConstants.CacheTtl, ExpireWhen.Always, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task GetIdentityAsync_OnRedisFailure_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.StringGetAsync(ExpectedKey, CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await sut.GetIdentityAsync(TestAddress);

        // Assert
        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetIdentityAsync_InvalidJson_ReturnsMiss()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();

        databaseMock
            .Setup(x => x.StringGetAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync("invalid json {{{");

        // Act
        var result = await sut.GetIdentityAsync(TestAddress);

        // Assert
        result.Should().BeNull();
        sut.CacheMisses.Should().Be(1);
    }

    #endregion

    #region SetIdentityAsync Tests

    [Fact]
    public async Task SetIdentityAsync_StoresProfileWithTtl()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var profile = CreateTestProfile();

        databaseMock
            .Setup(x => x.StringSetAsync(
                ExpectedKey,
                It.IsAny<RedisValue>(),
                IdentityCacheConstants.CacheTtl,
                false,
                When.Always,
                CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.SetIdentityAsync(TestAddress, profile);

        // Assert
        databaseMock.Verify(
            x => x.StringSetAsync(
                ExpectedKey,
                It.IsAny<RedisValue>(),
                IdentityCacheConstants.CacheTtl,
                false,
                When.Always,
                CommandFlags.None),
            Times.Once);
        sut.WriteOperations.Should().Be(1);
    }

    [Fact]
    public async Task SetIdentityAsync_OnRedisFailure_DoesNotThrow()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var profile = CreateTestProfile();

        databaseMock
            .Setup(x => x.StringSetAsync(
                ExpectedKey,
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var act = async () => await sut.SetIdentityAsync(TestAddress, profile);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
        sut.WriteErrors.Should().Be(1);
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
        await sut.InvalidateCacheAsync(TestAddress);

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
        var act = async () => await sut.InvalidateCacheAsync(TestAddress);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Event Subscription Tests

    [Fact]
    public async Task HandleAsync_IdentityUpdatedEvent_InvalidatesCache()
    {
        // Arrange
        var (sut, databaseMock, _) = CreateCacheService();
        var identityUpdatedEvent = new IdentityUpdatedEvent(TestAddress);

        databaseMock
            .Setup(x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        await sut.HandleAsync(identityUpdatedEvent);

        // Assert
        databaseMock.Verify(
            x => x.KeyDeleteAsync(ExpectedKey, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public void Constructor_SubscribesToEventAggregator()
    {
        // Arrange
        var connectionMultiplexerMock = new Mock<IConnectionMultiplexer>();
        var databaseMock = new Mock<IDatabase>();
        var eventAggregatorMock = new Mock<IEventAggregator>();
        var loggerMock = new Mock<ILogger<IdentityCacheService>>();

        connectionMultiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        // Act
        var sut = new IdentityCacheService(
            connectionMultiplexerMock.Object,
            KeyPrefix,
            eventAggregatorMock.Object,
            loggerMock.Object);

        // Assert
        eventAggregatorMock.Verify(
            x => x.Subscribe(sut),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private static (IdentityCacheService sut, Mock<IDatabase> databaseMock, Mock<IEventAggregator> eventAggregatorMock)
        CreateCacheService()
    {
        var connectionMultiplexerMock = new Mock<IConnectionMultiplexer>();
        var databaseMock = new Mock<IDatabase>();
        var eventAggregatorMock = new Mock<IEventAggregator>();
        var loggerMock = new Mock<ILogger<IdentityCacheService>>();

        connectionMultiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        var sut = new IdentityCacheService(
            connectionMultiplexerMock.Object,
            KeyPrefix,
            eventAggregatorMock.Object,
            loggerMock.Object);

        return (sut, databaseMock, eventAggregatorMock);
    }

    private static Profile CreateTestProfile()
    {
        return new Profile(
            Alias: "TestUser",
            ShortAlias: "TU",
            PublicSigningAddress: TestAddress,
            PublicEncryptAddress: "encrypt-address-123",
            IsPublic: true,
            BlockIndex: new BlockIndex(100));
    }

    private static string SerializeProfile(Profile profile)
    {
        return System.Text.Json.JsonSerializer.Serialize(profile);
    }

    #endregion
}
