using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.Caching.Tests;

public class ElectionCastIdempotencyCacheServiceTests
{
    private const string KeyPrefix = "Hush:";
    private const string ElectionId = "election-123";
    private const string IdempotencyKeyHash = "idem-hash-123";
    private static readonly string ExpectedKey =
        $"{KeyPrefix}{ElectionCastIdempotencyCacheConstants.GetCommittedMarkerKey(ElectionId, IdempotencyKeyHash)}";

    [Fact]
    public async Task ExistsAsync_CacheMiss_ReturnsNull()
    {
        var (sut, databaseMock) = CreateService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(false);

        var result = await sut.ExistsAsync(ElectionId, IdempotencyKeyHash);

        result.Should().BeNull();
        databaseMock.Verify(
            x => x.KeyExpireAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public async Task ExistsAsync_CacheHit_ReturnsTrueAndRefreshesTtl()
    {
        var (sut, databaseMock) = CreateService();

        databaseMock
            .Setup(x => x.KeyExistsAsync(ExpectedKey, CommandFlags.None))
            .ReturnsAsync(true);
        databaseMock
            .Setup(x => x.KeyExpireAsync(
                ExpectedKey,
                ElectionCastIdempotencyCacheConstants.CacheTtl,
                ExpireWhen.Always,
                CommandFlags.None))
            .ReturnsAsync(true);

        var result = await sut.ExistsAsync(ElectionId, IdempotencyKeyHash);

        result.Should().BeTrue();
        databaseMock.Verify(
            x => x.KeyExpireAsync(
                ExpectedKey,
                ElectionCastIdempotencyCacheConstants.CacheTtl,
                ExpireWhen.Always,
                CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task SetAsync_WritesMarkerWithTtl()
    {
        var (sut, databaseMock) = CreateService();

        databaseMock
            .Setup(x => x.StringSetAsync(
                ExpectedKey,
                "1",
                ElectionCastIdempotencyCacheConstants.CacheTtl,
                false,
                When.Always,
                CommandFlags.None))
            .ReturnsAsync(true);

        await sut.SetAsync(ElectionId, IdempotencyKeyHash);

        databaseMock.Verify(
            x => x.StringSetAsync(
                ExpectedKey,
                "1",
                ElectionCastIdempotencyCacheConstants.CacheTtl,
                false,
                When.Always,
                CommandFlags.None),
            Times.Once);
    }

    private static (ElectionCastIdempotencyCacheService sut, Mock<IDatabase> databaseMock) CreateService()
    {
        var databaseMock = new Mock<IDatabase>();
        var multiplexerMock = new Mock<IConnectionMultiplexer>();
        multiplexerMock
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        var loggerMock = new Mock<ILogger<ElectionCastIdempotencyCacheService>>();

        var sut = new ElectionCastIdempotencyCacheService(
            multiplexerMock.Object,
            KeyPrefix,
            loggerMock.Object);

        return (sut, databaseMock);
    }
}
