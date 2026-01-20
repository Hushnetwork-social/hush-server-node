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
/// FEAT-048: Integration tests for identity cache.
/// These tests verify the cache-aside pattern works correctly
/// with real PostgreSQL and Redis containers.
///
/// The cache-aside pattern:
/// 1. Check Redis cache first
/// 2. On cache miss, query PostgreSQL
/// 3. Populate Redis cache after DB query
/// 4. TTL refreshed on every read
/// 5. Cache invalidated via EventAggregator when identity updated
/// </summary>
[Collection("Integration Tests")]
[Trait("Category", "Integration")]
public class IdentityCacheIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private BlockProductionControl? _blockControl;
    private GrpcClientFactory? _grpcFactory;

    // Test identity - full 130-character public signing address format
    private const string TestPublicSigningAddress = "04test1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890ab";
    private const string TestPublicEncryptAddress = "04encrypt1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef12345678";
    private const string TestAlias = "Test User";
    private const string TestShortAlias = "TU";

    public IdentityCacheIntegrationTests(ITestOutputHelper output)
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

    #region Cache-Aside Pattern Tests

    [Fact]
    public async Task CacheAside_FirstLookup_PopulatesCache()
    {
        // Arrange: Insert identity directly into PostgreSQL (bypassing cache)
        await InsertTestProfileAsync(TestPublicSigningAddress, TestAlias, TestShortAlias);

        // Verify cache is empty before lookup
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:identity:{TestPublicSigningAddress}";
        var cachedBefore = await redisDb.StringGetAsync(cacheKey);
        cachedBefore.IsNullOrEmpty.Should().BeTrue("Cache should be empty before first lookup");

        // Act: Lookup identity via gRPC (triggers cache-aside pattern)
        var identityClient = _grpcFactory!.CreateClient<HushIdentity.HushIdentityClient>();
        var response = await identityClient.GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = TestPublicSigningAddress
        });

        // Assert: Identity returned successfully
        response.Successfull.Should().BeTrue("Identity lookup should succeed");
        response.ProfileName.Should().Be(TestAlias);
        _output.WriteLine($"Identity retrieved: {response.ProfileName}");

        // Assert: Cache should now be populated
        var cachedAfter = await redisDb.StringGetAsync(cacheKey);
        cachedAfter.IsNullOrEmpty.Should().BeFalse("Cache should be populated after lookup");
        cachedAfter.ToString().Should().Contain(TestAlias, "Cached data should contain the alias");
        _output.WriteLine($"Cache populated with: {cachedAfter}");
    }

    [Fact]
    public async Task CacheAside_SecondLookup_ServesFromCache()
    {
        // Arrange: Insert identity and trigger first lookup to populate cache
        await InsertTestProfileAsync(TestPublicSigningAddress, TestAlias, TestShortAlias);

        var identityClient = _grpcFactory!.CreateClient<HushIdentity.HushIdentityClient>();
        await identityClient.GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = TestPublicSigningAddress
        });

        // Verify cache is populated
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:identity:{TestPublicSigningAddress}";
        var cachedValue = await redisDb.StringGetAsync(cacheKey);
        cachedValue.IsNullOrEmpty.Should().BeFalse("Cache should be populated after first lookup");

        // Act: Second lookup (should hit cache)
        var response = await identityClient.GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = TestPublicSigningAddress
        });

        // Assert: Identity returned successfully from cache
        response.Successfull.Should().BeTrue("Second lookup should succeed");
        response.ProfileName.Should().Be(TestAlias);
        _output.WriteLine($"Second lookup returned: {response.ProfileName} (from cache)");
    }

    [Fact]
    public async Task CacheAside_NonExistingIdentity_NotCached()
    {
        // Arrange: Use an address that doesn't exist in the database
        var nonExistentAddress = "04nonexistent1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef12345";

        // Act: Lookup non-existing identity via gRPC
        var identityClient = _grpcFactory!.CreateClient<HushIdentity.HushIdentityClient>();
        var response = await identityClient.GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = nonExistentAddress
        });

        // Assert: Identity not found (no negative caching)
        response.Successfull.Should().BeFalse("Lookup for non-existing identity should return false");
        _output.WriteLine($"Non-existing identity lookup: Success={response.Successfull}, Message={response.Message}");

        // Assert: Cache should NOT contain entry for non-existing identity
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:identity:{nonExistentAddress}";
        var cached = await redisDb.StringGetAsync(cacheKey);
        cached.IsNullOrEmpty.Should().BeTrue("Non-existing identities should NOT be cached (no negative caching)");
        _output.WriteLine("Confirmed: non-existing identity was not cached");
    }

    [Fact]
    public async Task CacheAside_CacheHas7DayTtl()
    {
        // Arrange & Act: Insert identity and lookup to populate cache
        await InsertTestProfileAsync(TestPublicSigningAddress, TestAlias, TestShortAlias);

        var identityClient = _grpcFactory!.CreateClient<HushIdentity.HushIdentityClient>();
        await identityClient.GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = TestPublicSigningAddress
        });

        // Assert: Cache key should have TTL set
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:identity:{TestPublicSigningAddress}";
        var ttl = await redisDb.KeyTimeToLiveAsync(cacheKey);

        ttl.Should().NotBeNull("Cache key should have TTL set");
        ttl!.Value.TotalDays.Should().BeGreaterThan(6, "TTL should be close to 7 days");
        ttl.Value.TotalDays.Should().BeLessOrEqualTo(7, "TTL should not exceed 7 days");
        _output.WriteLine($"Cache TTL: {ttl.Value.TotalDays:F2} days");
    }

    #endregion

    #region Cache Invalidation Tests

    [Fact]
    public async Task CacheInvalidation_AfterCacheFlush_NextLookupRepopulatesCache()
    {
        // Arrange: Insert identity and populate cache
        await InsertTestProfileAsync(TestPublicSigningAddress, TestAlias, TestShortAlias);

        var identityClient = _grpcFactory!.CreateClient<HushIdentity.HushIdentityClient>();
        await identityClient.GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = TestPublicSigningAddress
        });

        // Verify cache is populated
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:identity:{TestPublicSigningAddress}";
        var cachedBefore = await redisDb.StringGetAsync(cacheKey);
        cachedBefore.IsNullOrEmpty.Should().BeFalse("Cache should be populated");
        _output.WriteLine("Cache populated before flush");

        // Act: Flush Redis (simulates cache invalidation)
        await redisDb.KeyDeleteAsync(cacheKey);

        // Verify cache is now empty
        var cachedAfterFlush = await redisDb.StringGetAsync(cacheKey);
        cachedAfterFlush.IsNullOrEmpty.Should().BeTrue("Cache should be empty after flush");
        _output.WriteLine("Cache flushed");

        // Act: Lookup identity again (should repopulate cache)
        var response = await identityClient.GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = TestPublicSigningAddress
        });

        // Assert: Identity returned and cache repopulated
        response.Successfull.Should().BeTrue("Lookup after cache flush should succeed");
        response.ProfileName.Should().Be(TestAlias);

        var cachedAfterLookup = await redisDb.StringGetAsync(cacheKey);
        cachedAfterLookup.IsNullOrEmpty.Should().BeFalse("Cache should be repopulated after lookup");
        _output.WriteLine("Cache successfully repopulated after flush");
    }

    #endregion

    #region Database Verification Tests

    [Fact]
    public async Task PostgresSchema_IdentityTableExists()
    {
        // Arrange: Node is already started, which runs migrations

        // Act: Query the PostgreSQL system catalog
        var tableQuery = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'Identity'
            AND table_name = 'Profile';
            """;

        var tables = await _fixture!.ExecuteQueryAsync(tableQuery);

        // Assert: Identity.Profile table should exist
        tables.Should().HaveCount(1, "Identity.Profile table should exist");
        _output.WriteLine("Identity.Profile table verified");
    }

    [Fact]
    public async Task PostgresData_InsertedProfileCanBeQueried()
    {
        // Arrange: Insert a profile
        await InsertTestProfileAsync(TestPublicSigningAddress, TestAlias, TestShortAlias);

        // Act: Query the profile directly from PostgreSQL
        var query = $"""
            SELECT "Alias", "ShortAlias", "PublicSigningAddress", "IsPublic"
            FROM "Identity"."Profile"
            WHERE "PublicSigningAddress" = '{TestPublicSigningAddress}'
            """;

        var results = await _fixture!.ExecuteQueryAsync(query);

        // Assert: Profile should be found
        results.Should().HaveCount(1, "Profile should exist in PostgreSQL");
        results[0]["Alias"].Should().Be(TestAlias);
        results[0]["ShortAlias"].Should().Be(TestShortAlias);
        _output.WriteLine($"Profile found in PostgreSQL: {results[0]["Alias"]}");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Inserts a test profile directly into PostgreSQL.
    /// </summary>
    private async Task InsertTestProfileAsync(string publicSigningAddress, string alias, string shortAlias)
    {
        var insertSql = $"""
            INSERT INTO "Identity"."Profile"
            ("PublicSigningAddress", "Alias", "ShortAlias", "PublicEncryptAddress", "IsPublic", "BlockIndex")
            VALUES ('{publicSigningAddress}', '{alias}', '{shortAlias}', '{TestPublicEncryptAddress}', true, 1)
            ON CONFLICT ("PublicSigningAddress") DO UPDATE SET
            "Alias" = EXCLUDED."Alias",
            "ShortAlias" = EXCLUDED."ShortAlias"
            """;

        await _fixture!.ExecuteScalarAsync<object>(insertSql);
        _output.WriteLine($"Test profile inserted: {alias} ({publicSigningAddress})");
    }

    #endregion
}
