using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-047: Integration tests for push token caching.
/// These tests verify the write-through pattern works correctly
/// with real PostgreSQL and Redis containers.
///
/// Note: Cache-aside pattern (reading from cache) is tested via unit tests
/// in PushDeliveryServiceTests because the gRPC endpoint GetActiveDeviceTokens
/// always queries PostgreSQL directly. The cache-aside pattern is used internally
/// by PushDeliveryService.SendPushAsync when delivering push notifications.
/// </summary>
[Collection("Integration Tests")]
[Trait("Category", "Integration")]
public class PushTokenCacheIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private BlockProductionControl? _blockControl;
    private GrpcClientFactory? _grpcFactory;

    // Test user IDs (varchar(100) max in database)
    private const string TestUserId = "04abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
    private const string TestUser2Id = "04user2_abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
    private const string TestUser3Id = "04user3_abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";

    public PushTokenCacheIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _fixture = new HushTestFixture();
        await _fixture.InitializeAsync();
        await _fixture.ResetAllAsync();

        var (node, blockControl, grpcFactory) = await _fixture.StartNodeAsync();
        _node = node;
        _blockControl = blockControl;
        _grpcFactory = grpcFactory;
    }

    public async Task DisposeAsync()
    {
        _grpcFactory?.Dispose();

        if (_node != null)
        {
            await _node.DisposeAsync();
        }

        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    #region Write-Through Pattern Tests

    [Fact]
    public async Task WriteThrough_TokenRegistration_TokenAppearsInBothPostgresAndRedis()
    {
        // Arrange
        var notificationClient = _grpcFactory!.CreateClient<HushNotification.HushNotificationClient>();
        var testToken = $"fcm_token_{Guid.NewGuid():N}";
        var deviceName = "Test Device";

        // Act: Register a device token
        var response = await notificationClient.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = PushPlatform.Android,
            Token = testToken,
            DeviceName = deviceName
        });

        // Assert: Registration should succeed
        response.Success.Should().BeTrue($"Token registration should succeed: {response.Message}");

        // Assert: Token should be in PostgreSQL
        var dbResults = await _fixture!.ExecuteQueryAsync(
            $"SELECT \"Token\", \"Platform\", \"DeviceName\", \"IsActive\" FROM \"Notifications\".\"DeviceTokens\" WHERE \"UserId\" = '{TestUserId}' AND \"Token\" = '{testToken}'");

        dbResults.Should().HaveCount(1, "Token should exist in PostgreSQL");
        dbResults[0]["IsActive"].Should().Be(true, "Token should be active in PostgreSQL");
        _output.WriteLine($"Token found in PostgreSQL: {dbResults[0]["Token"]}");

        // Assert: Token should be in Redis cache
        var redisDb = _fixture.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:push:v1:user:{TestUserId}";
        var cachedTokens = await redisDb.HashGetAllAsync(cacheKey);

        cachedTokens.Should().NotBeEmpty("Token should be cached in Redis after write-through");
        _output.WriteLine($"Found {cachedTokens.Length} token(s) in Redis cache");

        // Verify at least one cached token contains our test token
        var cachedJson = cachedTokens.Select(e => e.Value.ToString()).ToList();
        cachedJson.Any(j => j.Contains(testToken)).Should().BeTrue("Cached data should contain our test token");
    }

    [Fact]
    public async Task WriteThrough_TokenUpdate_CacheIsUpdated()
    {
        // Arrange: Register a token first
        var notificationClient = _grpcFactory!.CreateClient<HushNotification.HushNotificationClient>();
        var testToken = $"fcm_token_{Guid.NewGuid():N}";

        var initialResponse = await notificationClient.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = PushPlatform.Android,
            Token = testToken,
            DeviceName = "Initial Device"
        });
        initialResponse.Success.Should().BeTrue("Initial registration should succeed");

        // Act: Re-register the same token (refresh) with updated device name
        var refreshResponse = await notificationClient.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = PushPlatform.Android,
            Token = testToken,
            DeviceName = "Updated Device"
        });

        // Assert: Refresh should succeed
        refreshResponse.Success.Should().BeTrue("Token refresh should succeed");

        // Assert: Redis cache should contain the updated token with new device name
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:push:v1:user:{TestUserId}";
        var cachedTokens = await redisDb.HashGetAllAsync(cacheKey);

        cachedTokens.Should().NotBeEmpty("Cache should contain the token after refresh");
        var cachedJson = cachedTokens.Select(e => e.Value.ToString()).ToList();
        cachedJson.Any(j => j.Contains("Updated Device")).Should().BeTrue("Cache should have updated device name");
        _output.WriteLine("Cache successfully updated after token refresh");
    }

    [Fact]
    public async Task WriteThrough_TokenUnregistration_TokenRemovedFromCache()
    {
        // Arrange: Register a token first
        var notificationClient = _grpcFactory!.CreateClient<HushNotification.HushNotificationClient>();
        var testToken = $"fcm_token_{Guid.NewGuid():N}";

        await notificationClient.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = PushPlatform.Android,
            Token = testToken,
            DeviceName = "To Be Unregistered"
        });

        // Verify token is in cache
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:push:v1:user:{TestUserId}";
        var cachedBefore = await redisDb.HashGetAllAsync(cacheKey);
        cachedBefore.Should().NotBeEmpty("Token should be in cache before unregistration");

        // Act: Unregister the token
        var unregisterResponse = await notificationClient.UnregisterDeviceTokenAsync(new UnregisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Token = testToken
        });

        // Assert: Unregistration should succeed
        unregisterResponse.Success.Should().BeTrue($"Unregistration should succeed: {unregisterResponse.Message}");

        // Assert: Token should be removed from cache
        var cachedAfter = await redisDb.HashGetAllAsync(cacheKey);
        var tokenStillInCache = cachedAfter.Any(e => e.Value.ToString().Contains(testToken));
        tokenStillInCache.Should().BeFalse("Token should be removed from cache after unregistration");
        _output.WriteLine("Token successfully removed from cache after unregistration");
    }

    [Fact]
    public async Task WriteThrough_TokenReassignment_UpdatesBothUserCaches()
    {
        // Arrange: Register token for user2
        var notificationClient = _grpcFactory!.CreateClient<HushNotification.HushNotificationClient>();
        var testToken = $"fcm_token_{Guid.NewGuid():N}";

        await notificationClient.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            UserId = TestUser2Id,
            Platform = PushPlatform.Android,
            Token = testToken,
            DeviceName = "Shared Device"
        });

        // Verify user2 has the token cached
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var user2CacheKey = $"HushTest:push:v1:user:{TestUser2Id}";
        var user2Before = await redisDb.HashGetAllAsync(user2CacheKey);
        user2Before.Any(e => e.Value.ToString().Contains(testToken)).Should().BeTrue("User2 should have token cached");

        // Act: Register the same token for user3 (reassignment)
        var reassignResponse = await notificationClient.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            UserId = TestUser3Id,
            Platform = PushPlatform.Android,
            Token = testToken,
            DeviceName = "Shared Device"
        });

        // Assert: Reassignment should succeed
        reassignResponse.Success.Should().BeTrue("Token reassignment should succeed");

        // Assert: User3 should now have the token cached
        var user3CacheKey = $"HushTest:push:v1:user:{TestUser3Id}";
        var user3After = await redisDb.HashGetAllAsync(user3CacheKey);
        user3After.Any(e => e.Value.ToString().Contains(testToken)).Should().BeTrue("User3 should have token cached after reassignment");

        // Assert: User2 should no longer have the token cached
        var user2After = await redisDb.HashGetAllAsync(user2CacheKey);
        var user2StillHasToken = user2After.Any(e => e.Value.ToString().Contains(testToken));
        user2StillHasToken.Should().BeFalse("User2 should not have token in cache after reassignment");

        _output.WriteLine("Token successfully reassigned: removed from user2 cache, added to user3 cache");
    }

    [Fact]
    public async Task WriteThrough_CacheHas7DayTtl()
    {
        // Arrange & Act: Register a token
        var notificationClient = _grpcFactory!.CreateClient<HushNotification.HushNotificationClient>();
        var testToken = $"fcm_token_{Guid.NewGuid():N}";

        await notificationClient.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = PushPlatform.Android,
            Token = testToken,
            DeviceName = "TTL Test Device"
        });

        // Assert: Cache key should have TTL set
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:push:v1:user:{TestUserId}";
        var ttl = await redisDb.KeyTimeToLiveAsync(cacheKey);

        ttl.Should().NotBeNull("Cache key should have TTL set");
        ttl!.Value.TotalDays.Should().BeGreaterThan(6, "TTL should be close to 7 days");
        ttl.Value.TotalDays.Should().BeLessOrEqualTo(7, "TTL should not exceed 7 days");
        _output.WriteLine($"Cache TTL: {ttl.Value.TotalDays:F2} days");
    }

    #endregion

    #region GetActiveDeviceTokens Tests (PostgreSQL Direct Query)

    [Fact]
    public async Task GetActiveDeviceTokens_ReturnsRegisteredTokens()
    {
        // Arrange: Register a token
        var notificationClient = _grpcFactory!.CreateClient<HushNotification.HushNotificationClient>();
        var testToken = $"fcm_token_{Guid.NewGuid():N}";

        await notificationClient.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = PushPlatform.Android,
            Token = testToken,
            DeviceName = "Get Tokens Test Device"
        });

        // Act: Get active tokens (queries PostgreSQL directly)
        var tokensResponse = await notificationClient.GetActiveDeviceTokensAsync(new GetActiveDeviceTokensRequest
        {
            UserId = TestUserId
        });

        // Assert: Token should be returned
        tokensResponse.Tokens.Should().NotBeEmpty("Should return registered token");
        tokensResponse.Tokens.Should().Contain(t => t.Token == testToken, "Should contain our test token");
        _output.WriteLine($"GetActiveDeviceTokens returned {tokensResponse.Tokens.Count} token(s)");
    }

    [Fact]
    public async Task GetActiveDeviceTokens_DoesNotReturnUnregisteredTokens()
    {
        // Arrange: Register then unregister a token
        var notificationClient = _grpcFactory!.CreateClient<HushNotification.HushNotificationClient>();
        var testToken = $"fcm_token_{Guid.NewGuid():N}";

        await notificationClient.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = PushPlatform.Android,
            Token = testToken,
            DeviceName = "Unregister Test"
        });

        await notificationClient.UnregisterDeviceTokenAsync(new UnregisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Token = testToken
        });

        // Act: Get active tokens
        var tokensResponse = await notificationClient.GetActiveDeviceTokensAsync(new GetActiveDeviceTokensRequest
        {
            UserId = TestUserId
        });

        // Assert: Unregistered token should not be returned
        tokensResponse.Tokens.Should().NotContain(t => t.Token == testToken,
            "Unregistered token should not be returned");
        _output.WriteLine("Unregistered token correctly excluded from active tokens");
    }

    #endregion

    #region Fallback Behavior Tests

    [Fact]
    public async Task Fallback_TokenRegistration_StillWorksAfterCacheFlush()
    {
        // Arrange: Flush Redis first to start with empty cache
        await _fixture!.FlushRedisAsync();

        // Act: Register token (write-through pattern should still save to PostgreSQL and cache)
        var notificationClient = _grpcFactory!.CreateClient<HushNotification.HushNotificationClient>();
        var testToken = $"fcm_token_{Guid.NewGuid():N}";

        var response = await notificationClient.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequest
        {
            UserId = TestUserId,
            Platform = PushPlatform.Android,
            Token = testToken,
            DeviceName = "Flush Recovery Test"
        });

        // Assert: Registration should succeed
        response.Success.Should().BeTrue("Token registration should succeed even after cache flush");

        // Verify token is in PostgreSQL
        var dbResults = await _fixture.ExecuteQueryAsync(
            $"SELECT \"Token\" FROM \"Notifications\".\"DeviceTokens\" WHERE \"UserId\" = '{TestUserId}' AND \"Token\" = '{testToken}'");
        dbResults.Should().HaveCount(1, "Token should be in PostgreSQL");

        // Verify cache is repopulated
        var redisDb = _fixture.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:push:v1:user:{TestUserId}";
        var cachedTokens = await redisDb.HashGetAllAsync(cacheKey);
        cachedTokens.Should().NotBeEmpty("Cache should be repopulated after registration");

        _output.WriteLine("Token registration successful and cache repopulated after flush");
    }

    #endregion

    #region Database Index Tests

    [Fact]
    public async Task PostgresIndexes_ExistForPushTokenQueries()
    {
        // Arrange: Node is already started, which runs migrations and creates indexes

        // Act: Query the PostgreSQL system catalog to verify indexes exist
        var indexQuery = """
            SELECT indexname, tablename
            FROM pg_indexes
            WHERE schemaname = 'Notifications'
            AND tablename = 'DeviceTokens';
            """;

        var indexes = await _fixture!.ExecuteQueryAsync(indexQuery);

        // Assert: DeviceToken table should have indexes
        _output.WriteLine($"Found {indexes.Count} DeviceTokens indexes:");
        foreach (var index in indexes)
        {
            _output.WriteLine($"  - {index["indexname"]} on {index["tablename"]}");
        }

        // The table should have at least the primary key index and the UserId index
        indexes.Should().HaveCountGreaterOrEqualTo(2, "DeviceTokens table should have indexes for efficient queries");

        var indexNames = indexes.Select(i => i["indexname"]?.ToString()).ToList();
        indexNames.Should().Contain("IX_DeviceTokens_UserId", "UserId index should exist for user lookup");
    }

    #endregion
}
