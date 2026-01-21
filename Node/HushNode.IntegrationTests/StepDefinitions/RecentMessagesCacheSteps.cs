using FluentAssertions;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using Olimpo;
using StackExchange.Redis;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-046: Step definitions for Recent Messages Cache scenarios.
/// Tests write-through caching and cache-aside patterns for feed messages.
/// </summary>
[Binding]
public sealed class RecentMessagesCacheSteps
{
    private readonly ScenarioContext _scenarioContext;

    public RecentMessagesCacheSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"Redis cache is available")]
    public void GivenRedisCacheIsAvailable()
    {
        // Redis is started by the test fixture and available via ScenarioContext
        var fixture = GetFixture();
        fixture.RedisConnection.Should().NotBeNull("Redis should be available");
        fixture.RedisConnection.IsConnected.Should().BeTrue("Redis should be connected");
    }

    [Given(@"the Redis message cache for the ChatFeed is empty")]
    public async Task GivenTheRedisMessageCacheForTheChatFeedIsEmpty()
    {
        var feedId = GetChatFeedId();
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId}:messages";
        await redisDb.KeyDeleteAsync(cacheKey);

        var exists = await redisDb.KeyExistsAsync(cacheKey);
        exists.Should().BeFalse("Message cache should be empty");
    }

    [Given(@"the ChatFeed has (.*) messages in cache")]
    public async Task GivenTheChatFeedHasMessagesInCache(int messageCount)
    {
        var feedId = GetChatFeedId();
        var identity = GetStoredIdentity("Alice");
        var aesKey = GetChatFeedAesKey();
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        // Send messages to populate cache
        for (int i = 0; i < messageCount; i++)
        {
            var signedTx = TestTransactionFactory.CreateFeedMessage(identity, feedId, $"Message {i + 1}", aesKey);
            var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
            {
                SignedTransaction = signedTx
            });
            response.Successfull.Should().BeTrue($"Message {i + 1} should be submitted successfully");

            // Produce blocks in batches for efficiency
            if ((i + 1) % 10 == 0)
            {
                await blockControl.ProduceBlockAsync();
            }
        }

        // Final block to commit any remaining messages
        await blockControl.ProduceBlockAsync();
    }

    [Given(@"the Redis cache is flushed")]
    [When(@"the Redis cache is flushed")]
    public async Task GivenTheRedisCacheIsFlushed()
    {
        var fixture = GetFixture();
        await fixture.FlushRedisAsync();
    }

    [When(@"Alice requests messages for the ChatFeed via gRPC")]
    public async Task WhenAliceRequestsMessagesForTheChatFeedViaGrpc()
    {
        var identity = GetStoredIdentity("Alice");
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        _scenarioContext["LastMessagesResponse"] = response;
    }

    [Then(@"the message should be stored in the PostgreSQL database")]
    public async Task ThenTheMessageShouldBeStoredInThePostgreSQLDatabase()
    {
        var feedId = GetChatFeedId();
        var fixture = GetFixture();

        var query = $"SELECT COUNT(*) FROM \"Feeds\".\"FeedMessage\" WHERE \"FeedId\" = '{feedId}'";
        var count = await fixture.ExecuteScalarAsync<long>(query);

        count.Should().BeGreaterThan(0, "Message should exist in PostgreSQL");
    }

    [Then(@"the message should be in the Redis message cache for the ChatFeed")]
    public async Task ThenTheMessageShouldBeInTheRedisMessageCacheForTheChatFeed()
    {
        var feedId = GetChatFeedId();
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId}:messages";
        var cachedMessages = await redisDb.ListRangeAsync(cacheKey, 0, -1);

        cachedMessages.Should().NotBeEmpty("Message should be in Redis cache");
    }

    [Then(@"the Redis message cache should contain exactly (.*) messages")]
    public async Task ThenTheRedisMessageCacheShouldContainExactlyMessages(int expectedCount)
    {
        var feedId = GetChatFeedId();
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId}:messages";
        var length = await redisDb.ListLengthAsync(cacheKey);

        length.Should().Be(expectedCount, $"Cache should contain exactly {expectedCount} messages");
    }

    [Then(@"the response should contain the message")]
    public void ThenTheResponseShouldContainTheMessage()
    {
        var response = (GetFeedMessagesForAddressReply)_scenarioContext["LastMessagesResponse"];
        response.Messages.Should().NotBeEmpty("Response should contain messages");
    }

    [Then(@"the Redis message cache should be populated")]
    public async Task ThenTheRedisMessageCacheShouldBePopulated()
    {
        var feedId = GetChatFeedId();
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId}:messages";
        var exists = await redisDb.KeyExistsAsync(cacheKey);

        exists.Should().BeTrue("Cache should be populated after cache-aside read");
    }

    [Then(@"the PostgreSQL index ""(.*)"" should exist on ""(.*)""\.""(.*)""")]
    public async Task ThenThePostgreSQLIndexShouldExistOnTable(string indexName, string schema, string table)
    {
        var fixture = GetFixture();
        var query = $"""
            SELECT indexname FROM pg_indexes
            WHERE schemaname = '{schema}'
            AND tablename = '{table}'
            AND indexname = '{indexName}'
            """;

        var results = await fixture.ExecuteQueryAsync(query);
        results.Should().NotBeEmpty($"Index {indexName} should exist on {schema}.{table}");
    }

    #region Helper Methods

    private FeedId GetChatFeedId()
    {
        // Find the chat feed from context
        foreach (var key in _scenarioContext.Keys)
        {
            if (key.StartsWith("ChatFeed_") && !key.Contains("AesKey"))
            {
                return (FeedId)_scenarioContext[key];
            }
        }
        throw new InvalidOperationException("No chat feed found in context");
    }

    private string GetChatFeedAesKey()
    {
        foreach (var key in _scenarioContext.Keys)
        {
            if (key.StartsWith("ChatFeedAesKey_"))
            {
                return (string)_scenarioContext[key];
            }
        }
        throw new InvalidOperationException("No chat feed AES key found in context");
    }

    private TestIdentity GetStoredIdentity(string userName)
    {
        var contextKey = $"Identity_{userName}";
        if (_scenarioContext.TryGetValue(contextKey, out var identityObj) && identityObj is TestIdentity identity)
        {
            return identity;
        }

        return userName.ToLowerInvariant() switch
        {
            "alice" => TestIdentities.Alice,
            "bob" => TestIdentities.Bob,
            "charlie" => TestIdentities.Charlie,
            _ => throw new ArgumentException($"Unknown test user: {userName}")
        };
    }

    private GrpcClientFactory GetGrpcFactory()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            && factoryObj is GrpcClientFactory grpcFactory)
        {
            return grpcFactory;
        }
        throw new InvalidOperationException("GrpcClientFactory not found in ScenarioContext.");
    }

    private BlockProductionControl GetBlockControl()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.BlockControlKey, out var controlObj)
            && controlObj is BlockProductionControl blockControl)
        {
            return blockControl;
        }
        throw new InvalidOperationException("BlockProductionControl not found in ScenarioContext.");
    }

    private HushTestFixture GetFixture()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.FixtureKey, out var fixtureObj)
            && fixtureObj is HushTestFixture fixture)
        {
            return fixture;
        }
        throw new InvalidOperationException("HushTestFixture not found in ScenarioContext.");
    }

    #endregion
}
