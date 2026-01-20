using FluentAssertions;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-049: Integration tests for user feeds list caching.
/// These tests verify the cache-aside pattern and in-place updates (SADD/SREM)
/// work correctly with real PostgreSQL and Redis containers.
///
/// Note: The cache is integrated with GetFeedIdsForUserAsync, which is called
/// by GetFeedMessagesForAddress (for reaction tallies), not GetFeedsForAddress.
/// </summary>
[Collection("Integration Tests")]
[Trait("Category", "Integration")]
public class UserFeedsCacheIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private BlockProductionControl? _blockControl;
    private GrpcClientFactory? _grpcFactory;

    // Store AES keys for personal feeds, keyed by user's public signing address
    private readonly Dictionary<string, string> _personalFeedAesKeys = new();

    public UserFeedsCacheIntegrationTests(ITestOutputHelper output)
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

    [Fact]
    public async Task CacheAside_GetFeedMessagesForAddress_PopulatesCacheOnMiss()
    {
        // Arrange: Register Alice with personal feed
        var alice = TestIdentities.Alice;
        await RegisterUserWithPersonalFeed(alice);

        // Flush cache to ensure cache miss
        await _fixture!.FlushRedisAsync();

        // Verify cache is empty
        var redisDb = _fixture.RedisConnection.GetDatabase();
        var cacheKey = GetUserFeedsCacheKey(alice.PublicSigningAddress);
        var existsBefore = await redisDb.KeyExistsAsync(cacheKey);
        existsBefore.Should().BeFalse("Cache should be empty after flush");

        // Act: Query feed messages for address (should trigger cache-aside via AddReactionTallies)
        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        var messagesResponse = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        _output.WriteLine($"GetFeedMessagesForAddress returned {messagesResponse.Messages.Count} message(s)");

        // Assert: Cache should now be populated (via AddReactionTallies calling GetFeedIdsForUserAsync)
        var existsAfter = await redisDb.KeyExistsAsync(cacheKey);
        existsAfter.Should().BeTrue("Cache should be populated after cache-aside read via GetFeedMessagesForAddress");

        var cachedFeedIds = await redisDb.SetMembersAsync(cacheKey);
        cachedFeedIds.Should().NotBeEmpty("Cache should contain feed IDs");
        _output.WriteLine($"Cache populated with {cachedFeedIds.Length} feed ID(s)");
    }

    [Fact]
    public async Task CacheHit_GetFeedMessagesForAddress_ReturnsCachedData()
    {
        // Arrange: Register Alice with personal feed
        var alice = TestIdentities.Alice;
        await RegisterUserWithPersonalFeed(alice);

        // First call to populate cache
        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        _output.WriteLine("First call completed - cache should be populated");

        // Verify cache is populated
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = GetUserFeedsCacheKey(alice.PublicSigningAddress);
        var cachedBefore = await redisDb.SetMembersAsync(cacheKey);
        cachedBefore.Should().NotBeEmpty("Cache should be populated after first call");
        _output.WriteLine($"Cache contains {cachedBefore.Length} feed ID(s)");

        // Act: Second call (should hit cache)
        var secondResponse = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        // Assert: Cache still populated (not cleared)
        var cachedAfter = await redisDb.SetMembersAsync(cacheKey);
        cachedAfter.Length.Should().Be(cachedBefore.Length,
            "Second call should use same cached data");
        _output.WriteLine($"Second call completed - cache still has {cachedAfter.Length} feed ID(s)");
    }

    [Fact]
    public async Task InPlaceUpdate_JoinGroupFeed_AddsFeedToCache()
    {
        // Arrange: Register Alice
        var alice = TestIdentities.Alice;
        await RegisterUserWithPersonalFeed(alice);

        // Populate Alice's cache first via GetFeedMessagesForAddress
        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = GetUserFeedsCacheKey(alice.PublicSigningAddress);
        var cachedBefore = await redisDb.SetMembersAsync(cacheKey);
        _output.WriteLine($"Cache before group join: {cachedBefore.Length} feed(s)");
        cachedBefore.Should().NotBeEmpty("Cache should be populated before testing in-place update");

        // Act: Alice creates a group feed (creator is automatically a participant)
        await CreateGroupFeed(alice, "TestGroup");
        await _blockControl!.ProduceBlockAsync();

        // Assert: Cache should be updated with new feed (in-place SADD)
        var cachedAfter = await redisDb.SetMembersAsync(cacheKey);
        cachedAfter.Length.Should().Be(cachedBefore.Length + 1,
            "Cache should have one more feed after creating group feed");
        _output.WriteLine($"Cache after group join: {cachedAfter.Length} feed(s)");
    }

    [Fact]
    public async Task CacheTtl_IsSetOnCachePopulation()
    {
        // Arrange: Register Alice with personal feed
        var alice = TestIdentities.Alice;
        await RegisterUserWithPersonalFeed(alice);

        // Flush cache to ensure we test fresh population
        await _fixture!.FlushRedisAsync();

        // Act: Query feed messages (should populate cache with TTL)
        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        // Assert: Cache key should have TTL set
        var redisDb = _fixture.RedisConnection.GetDatabase();
        var cacheKey = GetUserFeedsCacheKey(alice.PublicSigningAddress);
        var ttl = await redisDb.KeyTimeToLiveAsync(cacheKey);

        ttl.Should().NotBeNull("Cache key should have a TTL set");
        ttl!.Value.TotalMinutes.Should().BeGreaterThan(0).And.BeLessOrEqualTo(5,
            "TTL should be less than or equal to 5 minutes (configured TTL)");
        _output.WriteLine($"Cache TTL: {ttl.Value.TotalSeconds:F1} seconds");
    }

    [Fact]
    public async Task RedisFallback_CacheMiss_QueriesPostgres()
    {
        // Arrange: Register Alice with personal feed
        var alice = TestIdentities.Alice;
        await RegisterUserWithPersonalFeed(alice);

        // Flush cache to ensure cache miss (simulates Redis unavailable scenario)
        await _fixture!.FlushRedisAsync();

        // Verify cache is empty
        var redisDb = _fixture.RedisConnection.GetDatabase();
        var cacheKey = GetUserFeedsCacheKey(alice.PublicSigningAddress);
        var existsBefore = await redisDb.KeyExistsAsync(cacheKey);
        existsBefore.Should().BeFalse("Cache should be empty after flush");

        // Act: Query feed messages (should fallback to PostgreSQL for feed IDs)
        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        var messagesResponse = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        // Assert: Request should succeed (data retrieved from PostgreSQL)
        // The cache should also be populated after the fallback
        var existsAfter = await redisDb.KeyExistsAsync(cacheKey);
        existsAfter.Should().BeTrue("Cache should be populated after PostgreSQL fallback");
        _output.WriteLine($"Fallback successful: Cache populated after PostgreSQL query");
    }

    [Fact]
    public async Task ChatFeedCreation_UpdatesCacheForBothParticipants()
    {
        // Arrange: Register Alice and Bob with personal feeds
        var alice = TestIdentities.Alice;
        var bob = TestIdentities.Bob;
        await RegisterUserWithPersonalFeed(alice);
        await RegisterUserWithPersonalFeed(bob);

        // Populate both users' caches via GetFeedMessagesForAddress
        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });
        await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = bob.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var aliceCacheKey = GetUserFeedsCacheKey(alice.PublicSigningAddress);
        var bobCacheKey = GetUserFeedsCacheKey(bob.PublicSigningAddress);

        var aliceFeedsBefore = await redisDb.SetMembersAsync(aliceCacheKey);
        var bobFeedsBefore = await redisDb.SetMembersAsync(bobCacheKey);
        _output.WriteLine($"Before chat: Alice has {aliceFeedsBefore.Length}, Bob has {bobFeedsBefore.Length} feeds");
        aliceFeedsBefore.Should().NotBeEmpty("Alice's cache should be populated");
        bobFeedsBefore.Should().NotBeEmpty("Bob's cache should be populated");

        // Act: Create a chat feed between Alice and Bob
        await CreateChatFeed(alice, bob);
        await _blockControl!.ProduceBlockAsync();

        // Assert: Both caches should be updated (in-place SADD)
        var aliceFeedsAfter = await redisDb.SetMembersAsync(aliceCacheKey);
        var bobFeedsAfter = await redisDb.SetMembersAsync(bobCacheKey);

        aliceFeedsAfter.Length.Should().Be(aliceFeedsBefore.Length + 1,
            "Alice's cache should have one more feed after chat creation");
        bobFeedsAfter.Length.Should().Be(bobFeedsBefore.Length + 1,
            "Bob's cache should have one more feed after chat creation");
        _output.WriteLine($"After chat: Alice has {aliceFeedsAfter.Length}, Bob has {bobFeedsAfter.Length} feeds");
    }

    #region Helper Methods

    private async Task RegisterUserWithPersonalFeed(TestIdentity identity)
    {
        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        var blockchainClient = _grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        // Check if already registered
        var hasPersonalFeed = await feedClient.HasPersonalFeedAsync(new HasPersonalFeedRequest
        {
            PublicPublicKey = identity.PublicSigningAddress
        });

        if (!hasPersonalFeed.FeedAvailable)
        {
            // Register identity
            var identityTxJson = TestTransactionFactory.CreateIdentityRegistration(identity);
            var identityResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
            {
                SignedTransaction = identityTxJson
            });
            identityResponse.Successfull.Should().BeTrue($"Identity registration should succeed: {identityResponse.Message}");
            await _blockControl!.ProduceBlockAsync();

            // Create personal feed and store the AES key
            var (personalFeedTxJson, aesKey) = TestTransactionFactory.CreatePersonalFeedWithKey(identity);
            _personalFeedAesKeys[identity.PublicSigningAddress] = aesKey;

            var feedResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
            {
                SignedTransaction = personalFeedTxJson
            });
            feedResponse.Successfull.Should().BeTrue($"Personal feed creation should succeed: {feedResponse.Message}");
            await _blockControl.ProduceBlockAsync();
        }
    }

    private async Task CreateGroupFeed(TestIdentity creator, string groupName)
    {
        var blockchainClient = _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();

        var (groupFeedTxJson, _, _) = TestTransactionFactory.CreateGroupFeed(creator, groupName);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = groupFeedTxJson
        });

        response.Successfull.Should().BeTrue($"Group feed creation should succeed: {response.Message}");
    }

    private async Task CreateChatFeed(TestIdentity initiator, TestIdentity partner)
    {
        var blockchainClient = _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();

        var (chatFeedTxJson, _, _) = TestTransactionFactory.CreateChatFeed(initiator, partner);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = chatFeedTxJson
        });

        response.Successfull.Should().BeTrue($"Chat feed creation should succeed: {response.Message}");
    }

    /// <summary>
    /// Gets the Redis cache key for a user's feed list.
    /// Pattern: {prefix}user:{userPublicAddress}:feeds
    /// </summary>
    private static string GetUserFeedsCacheKey(string userPublicAddress)
    {
        // HushTest: prefix is used by HushServerNodeCore.CreateForTesting
        return $"HushTest:{UserFeedsCacheConstants.GetUserFeedsKey(userPublicAddress)}";
    }

    #endregion
}
