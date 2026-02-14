using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Caching.Tests;

/// <summary>
/// Tests for IdentityDisplayNameCacheService - caches identity display names in a global Redis Hash (FEAT-065 E2).
/// Each test follows AAA pattern with isolated factory method.
/// </summary>
public class IdentityDisplayNameCacheServiceTests
{
    private const string KeyPrefix = "HushFeeds:";
    private static readonly string ExpectedHashKey = $"{KeyPrefix}identities:display_names";

    #region GetDisplayNamesAsync Tests

    [Fact]
    public async Task GetDisplayNamesAsync_WhenAllCached_ReturnsAllNames()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var addresses = new[] { "0xalice", "0xbob" };
        var fields = new RedisValue[] { "0xalice", "0xbob" };
        var values = new RedisValue[] { "Alice", "Bob" };

        databaseMock.Setup(x => x.HashGetAsync(ExpectedHashKey, It.Is<RedisValue[]>(f => f.Length == 2), CommandFlags.None))
            .ReturnsAsync(values);

        // Act
        var result = await sut.GetDisplayNamesAsync(addresses);

        // Assert
        result.Should().NotBeNull();
        result!["0xalice"].Should().Be("Alice");
        result["0xbob"].Should().Be("Bob");
        sut.CacheHits.Should().Be(2);
        sut.CacheMisses.Should().Be(0);
    }

    [Fact]
    public async Task GetDisplayNamesAsync_WhenPartialHit_ReturnsNullForMisses()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var addresses = new[] { "0xalice", "0xcarol" };
        var values = new RedisValue[] { "Alice", RedisValue.Null };

        databaseMock.Setup(x => x.HashGetAsync(ExpectedHashKey, It.Is<RedisValue[]>(f => f.Length == 2), CommandFlags.None))
            .ReturnsAsync(values);

        // Act
        var result = await sut.GetDisplayNamesAsync(addresses);

        // Assert
        result.Should().NotBeNull();
        result!["0xalice"].Should().Be("Alice");
        result["0xcarol"].Should().BeNull();
        sut.CacheHits.Should().Be(1);
        sut.CacheMisses.Should().Be(1);
    }

    [Fact]
    public async Task GetDisplayNamesAsync_WhenAllMiss_ReturnsAllNull()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var addresses = new[] { "0xunknown1", "0xunknown2" };
        var values = new RedisValue[] { RedisValue.Null, RedisValue.Null };

        databaseMock.Setup(x => x.HashGetAsync(ExpectedHashKey, It.Is<RedisValue[]>(f => f.Length == 2), CommandFlags.None))
            .ReturnsAsync(values);

        // Act
        var result = await sut.GetDisplayNamesAsync(addresses);

        // Assert
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result.Values.Should().AllBeEquivalentTo((string?)null);
        sut.CacheMisses.Should().Be(2);
    }

    [Fact]
    public async Task GetDisplayNamesAsync_WhenEmptyInput_ReturnsEmptyDictionary()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();

        // Act
        var result = await sut.GetDisplayNamesAsync(Array.Empty<string>());

        // Assert
        result.Should().NotBeNull();
        result!.Count.Should().Be(0);
        databaseMock.Verify(x => x.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task GetDisplayNamesAsync_WhenRedisThrows_ReturnsNull()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashGetAsync(ExpectedHashKey, It.IsAny<RedisValue[]>(), CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test"));

        // Act
        var result = await sut.GetDisplayNamesAsync(new[] { "0xalice" });

        // Assert
        result.Should().BeNull();
        sut.ReadErrors.Should().Be(1);
    }

    [Fact]
    public async Task GetDisplayNamesAsync_IncrementsCacheHitsPerNonNull()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var addresses = new[] { "0xa", "0xb", "0xc" };
        var values = new RedisValue[] { "A", "B", RedisValue.Null };

        databaseMock.Setup(x => x.HashGetAsync(ExpectedHashKey, It.IsAny<RedisValue[]>(), CommandFlags.None))
            .ReturnsAsync(values);

        // Act
        await sut.GetDisplayNamesAsync(addresses);

        // Assert
        sut.CacheHits.Should().Be(2);
        sut.CacheMisses.Should().Be(1);
    }

    #endregion

    #region SetDisplayNameAsync Tests

    [Fact]
    public async Task SetDisplayNameAsync_WritesToCorrectHashField()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashSetAsync(ExpectedHashKey, "0xdave", "Dave", It.IsAny<When>(), CommandFlags.None))
            .ReturnsAsync(true);

        // Act
        var result = await sut.SetDisplayNameAsync("0xdave", "Dave");

        // Assert
        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
        databaseMock.Verify(x => x.HashSetAsync(ExpectedHashKey, "0xdave", "Dave", It.IsAny<When>(), CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task SetDisplayNameAsync_WhenRedisThrows_ReturnsFalse()
    {
        var (sut, databaseMock) = CreateCacheService();
        databaseMock.Setup(x => x.HashSetAsync(ExpectedHashKey, "0xdave", "Dave", It.IsAny<When>(), CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test"));

        var result = await sut.SetDisplayNameAsync("0xdave", "Dave");

        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    [Fact]
    public async Task SetDisplayNameAsync_WhenEmptyAddress_ReturnsFalse()
    {
        var (sut, _) = CreateCacheService();
        var result = await sut.SetDisplayNameAsync("", "Dave");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetDisplayNameAsync_WhenEmptyName_ReturnsFalse()
    {
        var (sut, _) = CreateCacheService();
        var result = await sut.SetDisplayNameAsync("0xdave", "");
        result.Should().BeFalse();
    }

    #endregion

    #region SetMultipleDisplayNamesAsync Tests

    [Fact]
    public async Task SetMultipleDisplayNamesAsync_WritesAllEntries()
    {
        // Arrange
        var (sut, databaseMock) = CreateCacheService();
        var names = new Dictionary<string, string>
        {
            { "0xalice", "Alice" },
            { "0xbob", "Bob" }
        };

        databaseMock.Setup(x => x.HashSetAsync(ExpectedHashKey, It.IsAny<HashEntry[]>(), CommandFlags.None))
            .Returns(Task.CompletedTask);

        // Act
        var result = await sut.SetMultipleDisplayNamesAsync(names);

        // Assert
        result.Should().BeTrue();
        sut.WriteOperations.Should().Be(1);
        databaseMock.Verify(x => x.HashSetAsync(ExpectedHashKey, It.Is<HashEntry[]>(e => e.Length == 2), CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task SetMultipleDisplayNamesAsync_WhenEmpty_ReturnsFalse()
    {
        var (sut, _) = CreateCacheService();
        var result = await sut.SetMultipleDisplayNamesAsync(new Dictionary<string, string>());
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetMultipleDisplayNamesAsync_WhenRedisThrows_ReturnsFalse()
    {
        var (sut, databaseMock) = CreateCacheService();
        var names = new Dictionary<string, string> { { "0xalice", "Alice" } };

        databaseMock.Setup(x => x.HashSetAsync(ExpectedHashKey, It.IsAny<HashEntry[]>(), CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test"));

        var result = await sut.SetMultipleDisplayNamesAsync(names);

        result.Should().BeFalse();
        sut.WriteErrors.Should().Be(1);
    }

    #endregion

    #region Helper Methods

    private static (IdentityDisplayNameCacheService sut, Mock<IDatabase> databaseMock) CreateCacheService()
    {
        var connectionMultiplexerMock = new Mock<IConnectionMultiplexer>();
        var databaseMock = new Mock<IDatabase>();
        var loggerMock = new Mock<ILogger<IdentityDisplayNameCacheService>>();

        connectionMultiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        var sut = new IdentityDisplayNameCacheService(
            connectionMultiplexerMock.Object, KeyPrefix, loggerMock.Object);

        return (sut, databaseMock);
    }

    #endregion
}
