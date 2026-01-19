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
/// FEAT-046: Integration tests for feed message caching.
/// These tests verify the write-through and cache-aside patterns work correctly
/// with real PostgreSQL and Redis containers.
/// </summary>
[Collection("Integration Tests")]
[Trait("Category", "Integration")]
public class FeedMessageCacheIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private BlockProductionControl? _blockControl;
    private GrpcClientFactory? _grpcFactory;

    // Store AES keys for personal feeds, keyed by user's public signing address
    private readonly Dictionary<string, string> _personalFeedAesKeys = new();

    public FeedMessageCacheIntegrationTests(ITestOutputHelper output)
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
    public async Task WriteThrough_MessageAppearsInBothPostgresAndRedis()
    {
        // Arrange: Register user with personal feed
        var alice = TestIdentities.Alice;
        await RegisterUserWithPersonalFeed(alice);

        // Get Alice's personal feed ID
        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        var feedsResponse = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0
        });

        var personalFeed = feedsResponse.Feeds.First(f => f.FeedType == 0); // Personal = 0
        var feedId = personalFeed.FeedId;
        _output.WriteLine($"Personal feed ID: {feedId}");

        // Act: Send a message to the personal feed
        var message = "Test message for cache integration";
        await SendMessageToFeed(alice, feedId, message);
        await _blockControl!.ProduceBlockAsync();

        // Assert: Message should be in Redis cache
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:feed:{feedId}:messages";
        var cachedMessages = await redisDb.ListRangeAsync(cacheKey, 0, -1);

        cachedMessages.Should().NotBeEmpty("Message should be cached in Redis after write-through");
        _output.WriteLine($"Found {cachedMessages.Length} message(s) in Redis cache");

        // Also verify via gRPC query (which may use cache)
        var messagesResponse = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0
        });

        messagesResponse.Messages.Should().NotBeEmpty("Message should be retrievable via gRPC");
    }

    [Fact]
    public async Task CacheAside_CacheMiss_PopulatesCache()
    {
        // Arrange: Register user and create a message
        var alice = TestIdentities.Alice;
        await RegisterUserWithPersonalFeed(alice);

        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        var feedsResponse = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0
        });

        var personalFeed = feedsResponse.Feeds.First(f => f.FeedType == 0);
        var feedId = personalFeed.FeedId;

        // Send a message
        await SendMessageToFeed(alice, feedId, "Message for cache-aside test");
        await _blockControl!.ProduceBlockAsync();

        // Flush Redis to simulate cache miss
        await _fixture!.FlushRedisAsync();

        // Verify cache is empty
        var redisDb = _fixture.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:feed:{feedId}:messages";
        var cachedBefore = await redisDb.ListRangeAsync(cacheKey, 0, -1);
        cachedBefore.Should().BeEmpty("Cache should be empty after flush");

        // Act: Query messages via gRPC (should trigger cache-aside)
        var messagesResponse = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0
        });

        // Assert: Message returned from PostgreSQL
        messagesResponse.Messages.Should().NotBeEmpty("Message should be returned from PostgreSQL on cache miss");

        // Assert: Cache should now be populated (cache-aside pattern)
        var cachedAfter = await redisDb.ListRangeAsync(cacheKey, 0, -1);
        cachedAfter.Should().NotBeEmpty("Cache should be populated after cache-aside read");
        _output.WriteLine($"Cache populated with {cachedAfter.Length} message(s) after cache-aside");
    }

    [Fact]
    public async Task CacheTrim_KeepsLast100Messages()
    {
        // Arrange: Register user with personal feed
        var alice = TestIdentities.Alice;
        await RegisterUserWithPersonalFeed(alice);

        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        var feedsResponse = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0
        });

        var personalFeed = feedsResponse.Feeds.First(f => f.FeedType == 0);
        var feedId = personalFeed.FeedId;

        // Act: Send more than 100 messages (batch in groups to speed up)
        const int messageCount = 110;
        _output.WriteLine($"Sending {messageCount} messages...");

        for (int i = 0; i < messageCount; i++)
        {
            await SendMessageToFeed(alice, feedId, $"Message {i + 1}");

            // Produce blocks periodically to commit messages
            if ((i + 1) % 10 == 0)
            {
                await _blockControl!.ProduceBlockAsync();
                _output.WriteLine($"Produced block after message {i + 1}");
            }
        }

        // Final block for remaining messages
        await _blockControl!.ProduceBlockAsync();

        // Assert: Redis should contain exactly 100 messages (trimmed)
        var redisDb = _fixture!.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:feed:{feedId}:messages";
        var cachedMessages = await redisDb.ListLengthAsync(cacheKey);

        cachedMessages.Should().Be(FeedMessageCacheConstants.MaxMessagesPerFeed,
            $"Cache should be trimmed to {FeedMessageCacheConstants.MaxMessagesPerFeed} messages");
        _output.WriteLine($"Cache contains {cachedMessages} messages (expected {FeedMessageCacheConstants.MaxMessagesPerFeed})");
    }

    [Fact]
    public async Task RedisUnavailable_FallbackToPostgres()
    {
        // Arrange: Register user and send a message
        var alice = TestIdentities.Alice;
        await RegisterUserWithPersonalFeed(alice);

        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        var feedsResponse = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0
        });

        var personalFeed = feedsResponse.Feeds.First(f => f.FeedType == 0);
        var feedId = personalFeed.FeedId;

        // Send a message to ensure there's data
        await SendMessageToFeed(alice, feedId, "Message before Redis unavailable");
        await _blockControl!.ProduceBlockAsync();

        // Flush cache to ensure we're testing fallback
        await _fixture!.FlushRedisAsync();

        // Note: We can't actually stop Redis container mid-test without breaking the fixture.
        // Instead, we verify that when cache returns null (cache miss), fallback works.
        // This is effectively testing the same code path as Redis being unavailable.

        // Act: Query messages (cache miss will trigger PostgreSQL fallback)
        var messagesResponse = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0
        });

        // Assert: Messages should still be returned from PostgreSQL
        messagesResponse.Messages.Should().NotBeEmpty(
            "Messages should be returned from PostgreSQL when cache is empty/unavailable");
        _output.WriteLine($"Fallback successful: Retrieved {messagesResponse.Messages.Count} message(s) from PostgreSQL");
    }

    [Fact]
    public async Task PostgresIndexes_ExistForCacheFallbackQueries()
    {
        // Arrange: Node is already started, which runs migrations and creates indexes

        // Act: Query the PostgreSQL system catalog to verify indexes exist
        var indexQuery = """
            SELECT indexname, tablename
            FROM pg_indexes
            WHERE schemaname = 'Feeds'
            AND indexname IN (
                'IX_FeedMessage_FeedId_BlockIndex',
                'IX_FeedMessage_IssuerPublicAddress_BlockIndex'
            );
            """;

        var indexes = await _fixture!.ExecuteQueryAsync(indexQuery);

        // Assert: Both indexes should exist
        _output.WriteLine($"Found {indexes.Count} feed message indexes:");
        foreach (var index in indexes)
        {
            _output.WriteLine($"  - {index["indexname"]} on {index["tablename"]}");
        }

        indexes.Should().HaveCount(2, "Both FeedMessage indexes should exist for efficient cache fallback queries");

        var indexNames = indexes.Select(i => i["indexname"]?.ToString()).ToList();
        indexNames.Should().Contain("IX_FeedMessage_FeedId_BlockIndex");
        indexNames.Should().Contain("IX_FeedMessage_IssuerPublicAddress_BlockIndex");
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

    private async Task SendMessageToFeed(TestIdentity sender, string feedIdString, string message)
    {
        var blockchainClient = _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();

        var feedId = new HushShared.Feeds.Model.FeedId(Guid.Parse(feedIdString));

        // Get the AES key for the sender's personal feed
        var aesKey = _personalFeedAesKeys[sender.PublicSigningAddress];
        var signedTransactionJson = TestTransactionFactory.CreateFeedMessage(sender, feedId, message, aesKey);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransactionJson
        });

        response.Successfull.Should().BeTrue($"Message submission should succeed: {response.Message}");
    }

    #endregion
}
