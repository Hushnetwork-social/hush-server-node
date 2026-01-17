using FluentAssertions;
using HushNode.Notifications;
using HushNode.UrlMetadata;
using HushNode.UrlMetadata.Models;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace HushNode.UrlMetadata.Tests;

/// <summary>
/// Tests for UrlMetadataCacheService - Redis caching functionality.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class UrlMetadataCacheServiceTests
{
    #region Cache hit tests

    [Fact]
    public async Task GetAsync_WithCachedEntry_ReturnsMetadata()
    {
        // Arrange
        var (service, mockDatabase) = CreateServiceWithMock();
        var url = "https://example.com/article";
        var cachedJson = """{"Url":"https://example.com/article","Success":true,"Title":"Test","Description":null,"ImageUrl":null,"ImageBase64":null,"Domain":"example.com","FetchedAt":"2026-01-17T00:00:00Z","ErrorMessage":null}""";

        mockDatabase
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)cachedJson);

        // Act
        var result = await service.GetAsync(url);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Test");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrFetchAsync_WithCachedEntry_ReturnsCachedWithoutFetching()
    {
        // Arrange
        var (service, mockDatabase) = CreateServiceWithMock();
        var url = "https://example.com/article";
        var cachedJson = """{"Url":"https://example.com/article","Success":true,"Title":"Cached","Description":null,"ImageUrl":null,"ImageBase64":null,"Domain":"example.com","FetchedAt":"2026-01-17T00:00:00Z","ErrorMessage":null}""";
        var fetchCalled = false;

        mockDatabase
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)cachedJson);

        // Act
        var result = await service.GetOrFetchAsync(url, () =>
        {
            fetchCalled = true;
            return Task.FromResult(UrlMetadataResult.CreateSuccess(url, "example.com", "Fresh", null, null, null));
        });

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Cached");
        fetchCalled.Should().BeFalse("fetch function should not be called when cache hit");
    }

    #endregion

    #region Cache miss tests

    [Fact]
    public async Task GetAsync_WithNoCachedEntry_ReturnsNull()
    {
        // Arrange
        var (service, mockDatabase) = CreateServiceWithMock();

        mockDatabase
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await service.GetAsync("https://example.com/missing");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrFetchAsync_WithNoCachedEntry_FetchesAndCaches()
    {
        // Arrange
        var (service, mockDatabase) = CreateServiceWithMock();
        var url = "https://example.com/article";
        var fetchCalled = false;

        mockDatabase
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        mockDatabase
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await service.GetOrFetchAsync(url, () =>
        {
            fetchCalled = true;
            return Task.FromResult(UrlMetadataResult.CreateSuccess(url, "example.com", "Fresh", null, null, null));
        });

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Fresh");
        fetchCalled.Should().BeTrue("fetch function should be called on cache miss");

        // Verify caching occurred
        mockDatabase.Verify(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(t => t!.Value.TotalHours == 24),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    #endregion

    #region Cache key normalization tests

    [Theory]
    [InlineData("https://example.com/page/", "https://example.com/page")]
    [InlineData("https://EXAMPLE.COM/PAGE", "https://example.com/page")]
    public async Task SetAsync_NormalizesUrlForCacheKey(string url1, string url2)
    {
        // Arrange
        var (service, mockDatabase) = CreateServiceWithMock();
        RedisKey? capturedKey1 = null;
        RedisKey? capturedKey2 = null;

        mockDatabase
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((k, v, t, b, w, c) =>
            {
                if (!capturedKey1.HasValue)
                    capturedKey1 = k;
                else
                    capturedKey2 = k;
            })
            .ReturnsAsync(true);

        // Act
        await service.SetAsync(UrlMetadataResult.CreateSuccess(url1, "example.com", "Test1", null, null, null));
        await service.SetAsync(UrlMetadataResult.CreateSuccess(url2, "example.com", "Test2", null, null, null));

        // Assert
        capturedKey1.HasValue.Should().BeTrue();
        capturedKey2.HasValue.Should().BeTrue();
        capturedKey1!.Value.ToString().Should().Be(capturedKey2!.Value.ToString(),
            "normalized URLs should produce the same cache key");
    }

    #endregion

    #region Redis error handling tests

    [Fact]
    public async Task GetAsync_WhenRedisThrows_ReturnsNull()
    {
        // Arrange
        var (service, mockDatabase) = CreateServiceWithMock();

        mockDatabase
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var result = await service.GetAsync("https://example.com/article");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_WhenRedisThrows_DoesNotThrow()
    {
        // Arrange
        var (service, mockDatabase) = CreateServiceWithMock();

        mockDatabase
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test error"));

        // Act
        var action = () => service.SetAsync(
            UrlMetadataResult.CreateSuccess("https://example.com", "example.com", "Test", null, null, null));

        // Assert
        await action.Should().NotThrowAsync();
    }

    #endregion

    #region Helper methods

    private static (UrlMetadataCacheService service, Mock<IDatabase> mockDatabase) CreateServiceWithMock()
    {
        var mockDatabase = new Mock<IDatabase>();
        var mockRedis = new Mock<RedisConnectionManager>(
            Mock.Of<Microsoft.Extensions.Options.IOptions<HushNode.Notifications.Models.RedisSettings>>(),
            Mock.Of<ILogger<RedisConnectionManager>>());

        mockRedis.Setup(x => x.Database).Returns(mockDatabase.Object);
        mockRedis.Setup(x => x.KeyPrefix).Returns("HushFeeds:");

        var logger = Mock.Of<ILogger<UrlMetadataCacheService>>();
        var service = new UrlMetadataCacheService(mockRedis.Object, logger);

        return (service, mockDatabase);
    }

    #endregion
}
