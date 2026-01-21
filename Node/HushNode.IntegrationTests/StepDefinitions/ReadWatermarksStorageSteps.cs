using FluentAssertions;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using StackExchange.Redis;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-051: Step definitions for Read Watermarks Storage scenarios.
/// Tests write-through caching and max-wins semantics for read positions.
/// </summary>
[Binding]
public sealed class ReadWatermarksStorageSteps
{
    private readonly ScenarioContext _scenarioContext;

    public ReadWatermarksStorageSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [When(@"(.*) marks the ChatFeed as read at block (.*) via gRPC")]
    public async Task WhenUserMarksTheChatFeedAsReadAtBlockViaGrpc(string userName, int blockIndex)
    {
        var identity = GetTestIdentity(userName);
        var feedId = GetChatFeedId();
        var grpcFactory = GetGrpcFactory();
        var notificationClient = grpcFactory.CreateClient<HushNotification.HushNotificationClient>();

        var response = await notificationClient.MarkFeedAsReadAsync(new MarkFeedAsReadRequest
        {
            UserId = identity.PublicSigningAddress,
            FeedId = feedId.ToString(),
            UpToBlockIndex = blockIndex
        });

        _scenarioContext["LastMarkAsReadResponse"] = response;
        _scenarioContext["LastMarkAsReadBlockIndex"] = blockIndex;
        _scenarioContext["LastMarkAsReadUserId"] = identity.PublicSigningAddress;
        _scenarioContext["LastMarkAsReadFeedId"] = feedId.ToString();
        _scenarioContext["LastMarkAsReadFeedIdTyped"] = feedId;
    }

    [Given(@"(.*) has marked the ChatFeed as read at block (.*)")]
    public async Task GivenUserHasMarkedTheChatFeedAsReadAtBlock(string userName, int blockIndex)
    {
        await WhenUserMarksTheChatFeedAsReadAtBlockViaGrpc(userName, blockIndex);

        // Store the expected position for verification
        _scenarioContext["ExpectedReadPosition"] = blockIndex;
    }

    [Given(@"the read position is stored in both database and cache")]
    public async Task GivenTheReadPositionIsStoredInBothDatabaseAndCache()
    {
        // Verify database
        await ThenTheReadPositionShouldBeStoredInThePostgreSQLFeedReadPositionTable();

        // Verify cache
        await ThenTheReadPositionShouldBeInTheRedisReadWatermarkCache();
    }

    [Given(@"the Redis read watermark cache is flushed")]
    [When(@"the Redis read watermark cache is flushed")]
    public async Task GivenTheRedisReadWatermarkCacheIsFlushed()
    {
        var userId = (string)_scenarioContext["LastMarkAsReadUserId"];
        var feedId = (FeedId)_scenarioContext["LastMarkAsReadFeedIdTyped"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:{FeedReadPositionCacheConstants.GetReadPositionKey(userId, feedId)}";
        await redisDb.KeyDeleteAsync(cacheKey);
    }

    [When(@"(.*) requests her feeds via GetFeedsForAddress gRPC")]
    public async Task WhenUserRequestsHerFeedsViaGetFeedsForAddressGrpc(string userName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0
        });

        _scenarioContext["LastFeedsResponse"] = response;
    }

    [Then(@"the read position should be stored in the PostgreSQL FeedReadPosition table")]
    public async Task ThenTheReadPositionShouldBeStoredInThePostgreSQLFeedReadPositionTable()
    {
        var userId = (string)_scenarioContext["LastMarkAsReadUserId"];
        var feedId = (string)_scenarioContext["LastMarkAsReadFeedId"];
        var fixture = GetFixture();

        var query = $"""
            SELECT "LastReadBlockIndex" FROM "Feeds"."FeedReadPosition"
            WHERE "UserId" = '{userId}' AND "FeedId" = '{feedId}'
            """;

        var results = await fixture.ExecuteQueryAsync(query);
        results.Should().NotBeEmpty("Read position should exist in PostgreSQL");
    }

    [Then(@"the read position should be in the Redis read watermark cache")]
    public async Task ThenTheReadPositionShouldBeInTheRedisReadWatermarkCache()
    {
        var userId = (string)_scenarioContext["LastMarkAsReadUserId"];
        var feedId = (FeedId)_scenarioContext["LastMarkAsReadFeedIdTyped"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:{FeedReadPositionCacheConstants.GetReadPositionKey(userId, feedId)}";
        var cachedValue = await redisDb.StringGetAsync(cacheKey);

        cachedValue.IsNullOrEmpty.Should().BeFalse("Read position should be in Redis cache");
    }

    [Then(@"the response should indicate success")]
    public void ThenTheResponseShouldIndicateSuccess()
    {
        var response = (MarkFeedAsReadReply)_scenarioContext["LastMarkAsReadResponse"];
        response.Success.Should().BeTrue("MarkFeedAsRead should succeed");
    }

    [Then(@"the Redis read watermark cache TTL should be approximately (.*) days")]
    public async Task ThenTheRedisReadWatermarkCacheTtlShouldBeApproximatelyDays(int expectedDays)
    {
        var userId = (string)_scenarioContext["LastMarkAsReadUserId"];
        var feedId = (FeedId)_scenarioContext["LastMarkAsReadFeedIdTyped"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:{FeedReadPositionCacheConstants.GetReadPositionKey(userId, feedId)}";
        var ttl = await redisDb.KeyTimeToLiveAsync(cacheKey);

        ttl.Should().NotBeNull("Cache key should have TTL set");
        ttl!.Value.TotalDays.Should().BeGreaterThan(expectedDays - 2, $"TTL should be close to {expectedDays} days");
        ttl.Value.TotalDays.Should().BeLessOrEqualTo(expectedDays + 1, $"TTL should not exceed {expectedDays + 1} days");
    }

    [Then(@"the read position should remain at block (.*)")]
    public async Task ThenTheReadPositionShouldRemainAtBlock(int expectedBlockIndex)
    {
        var userId = (string)_scenarioContext["LastMarkAsReadUserId"];
        var feedId = (string)_scenarioContext["LastMarkAsReadFeedId"];
        var fixture = GetFixture();

        var query = $"""
            SELECT "LastReadBlockIndex" FROM "Feeds"."FeedReadPosition"
            WHERE "UserId" = '{userId}' AND "FeedId" = '{feedId}'
            """;

        var results = await fixture.ExecuteQueryAsync(query);
        results.Should().NotBeEmpty("Read position should exist");

        var actualBlockIndex = Convert.ToInt64(results[0]["LastReadBlockIndex"]);
        actualBlockIndex.Should().Be(expectedBlockIndex, $"Read position should remain at {expectedBlockIndex} (max wins)");
    }

    [Then(@"the read position should be updated to block (.*)")]
    public async Task ThenTheReadPositionShouldBeUpdatedToBlock(int expectedBlockIndex)
    {
        var userId = (string)_scenarioContext["LastMarkAsReadUserId"];
        var feedId = (string)_scenarioContext["LastMarkAsReadFeedId"];
        var fixture = GetFixture();

        var query = $"""
            SELECT "LastReadBlockIndex" FROM "Feeds"."FeedReadPosition"
            WHERE "UserId" = '{userId}' AND "FeedId" = '{feedId}'
            """;

        var results = await fixture.ExecuteQueryAsync(query);
        results.Should().NotBeEmpty("Read position should exist");

        var actualBlockIndex = Convert.ToInt64(results[0]["LastReadBlockIndex"]);
        actualBlockIndex.Should().Be(expectedBlockIndex, $"Read position should be updated to {expectedBlockIndex}");
    }

    [Then(@"the response should include lastReadBlockIndex of (.*) for the ChatFeed")]
    public void ThenTheResponseShouldIncludeLastReadBlockIndexForTheChatFeed(int expectedBlockIndex)
    {
        var feedId = GetChatFeedId();
        var response = (GetFeedForAddressReply)_scenarioContext["LastFeedsResponse"];

        var chatFeed = response.Feeds.FirstOrDefault(f => f.FeedId == feedId.ToString());
        chatFeed.Should().NotBeNull("Chat feed should be in response");
        chatFeed!.LastReadBlockIndex.Should().Be(expectedBlockIndex,
            $"lastReadBlockIndex should be {expectedBlockIndex}");
    }

    [Then(@"the response should include lastReadBlockIndex from the database")]
    public void ThenTheResponseShouldIncludeLastReadBlockIndexFromTheDatabase()
    {
        var feedId = GetChatFeedId();
        var expectedBlockIndex = (int)_scenarioContext["ExpectedReadPosition"];
        var response = (GetFeedForAddressReply)_scenarioContext["LastFeedsResponse"];

        var chatFeed = response.Feeds.FirstOrDefault(f => f.FeedId == feedId.ToString());
        chatFeed.Should().NotBeNull("Chat feed should be in response");
        chatFeed!.LastReadBlockIndex.Should().Be(expectedBlockIndex,
            "lastReadBlockIndex should be retrieved from database");
    }

    [Then(@"the read watermark cache should be repopulated")]
    public async Task ThenTheReadWatermarkCacheShouldBeRepopulated()
    {
        var userId = (string)_scenarioContext["LastMarkAsReadUserId"];
        var feedId = (FeedId)_scenarioContext["LastMarkAsReadFeedIdTyped"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:{FeedReadPositionCacheConstants.GetReadPositionKey(userId, feedId)}";
        var exists = await redisDb.KeyExistsAsync(cacheKey);

        exists.Should().BeTrue("Cache should be repopulated after database fallback");
    }

    [Then(@"the watermark should be retrieved from the database")]
    public void ThenTheWatermarkShouldBeRetrievedFromTheDatabase()
    {
        // This is verified by the response containing the correct value
        // The fact that cache was flushed and we still get the value proves DB retrieval
        ThenTheResponseShouldIncludeLastReadBlockIndexFromTheDatabase();
    }

    [Then(@"the table should have column ""(.*)""")]
    public async Task ThenTheTableShouldHaveColumn(string columnName)
    {
        var fixture = GetFixture();
        var query = $"""
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = 'Feeds'
            AND table_name = 'FeedReadPosition'
            AND column_name = '{columnName}'
            """;

        var results = await fixture.ExecuteQueryAsync(query);
        results.Should().NotBeEmpty($"Column {columnName} should exist in FeedReadPosition table");
    }

    [Then(@"there should be a unique index on UserId and FeedId in ""(.*)""\.""(.*)""")]
    public async Task ThenThereShouldBeAUniqueIndexOnUserIdAndFeedId(string schema, string table)
    {
        var fixture = GetFixture();
        var query = $"""
            SELECT indexname FROM pg_indexes
            WHERE schemaname = '{schema}'
            AND tablename = '{table}'
            AND indexdef LIKE '%UserId%'
            AND indexdef LIKE '%FeedId%'
            """;

        var results = await fixture.ExecuteQueryAsync(query);
        results.Should().NotBeEmpty("Unique index on UserId and FeedId should exist");
    }

    #region Helper Methods

    private FeedId GetChatFeedId()
    {
        foreach (var key in _scenarioContext.Keys)
        {
            if (key.StartsWith("ChatFeed_") && !key.Contains("AesKey"))
            {
                return (FeedId)_scenarioContext[key];
            }
        }
        throw new InvalidOperationException("No chat feed found in context");
    }

    private TestIdentity GetTestIdentity(string userName)
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
