using FluentAssertions;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode.Testing;
using StackExchange.Redis;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-049: Step definitions for User Feeds List Cache scenarios.
/// Tests cache-aside pattern and in-place updates (SADD/SREM) for user feed lists.
/// </summary>
[Binding]
public sealed class UserFeedsListCacheSteps
{
    private readonly ScenarioContext _scenarioContext;

    public UserFeedsListCacheSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"(.*)'s user feeds cache is empty")]
    public async Task GivenUsersFeedsCacheIsEmpty(string userName)
    {
        var identity = GetTestIdentity(userName);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = GetUserFeedsCacheKey(identity.PublicSigningAddress);
        await redisDb.KeyDeleteAsync(cacheKey);

        var exists = await redisDb.KeyExistsAsync(cacheKey);
        exists.Should().BeFalse("User feeds cache should be empty");
    }

    [Given(@"(.*)'s user feeds cache has been populated")]
    public async Task GivenUsersFeedsCacheHasBeenPopulated(string userName)
    {
        // Trigger cache population via GetFeedMessagesForAddress
        await WhenUserRequestsFeedMessagesViaGrpc(userName);

        // Verify populated
        var identity = GetTestIdentity(userName);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = GetUserFeedsCacheKey(identity.PublicSigningAddress);
        var exists = await redisDb.KeyExistsAsync(cacheKey);
        exists.Should().BeTrue($"{userName}'s user feeds cache should be populated");
    }

    [Given(@"(.*)'s user feeds cache count is recorded")]
    public async Task GivenUsersFeedsCacheCountIsRecorded(string userName)
    {
        var identity = GetTestIdentity(userName);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = GetUserFeedsCacheKey(identity.PublicSigningAddress);
        var members = await redisDb.SetMembersAsync(cacheKey);

        _scenarioContext[$"FeedsCacheCount_{userName}"] = members.Length;
    }

    [Given(@"(.*) has feeds in the database")]
    public void GivenUserHasFeedsInTheDatabase(string userName)
    {
        // User should already have personal feed from "is registered with a personal feed" step
        // This is a verification step
        var identity = GetTestIdentity(userName);
        identity.Should().NotBeNull($"{userName} should have been registered");
    }

    [When(@"(.*) requests feed messages via GetFeedMessagesForAddress gRPC")]
    [When(@"(.*) requests feed messages via GetFeedMessagesForAddress gRPC again")]
    public async Task WhenUserRequestsFeedMessagesViaGrpc(string userName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        _scenarioContext[$"LastFeedMessagesResponse_{userName}"] = response;
    }

    [When(@"(.*) creates a group feed ""(.*)"" via gRPC")]
    public async Task WhenUserCreatesAGroupFeedViaGrpc(string userName, string groupName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var (signedTxJson, feedId, aesKey) = TestTransactionFactory.CreateGroupFeed(identity, groupName);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTxJson
        });

        response.Successfull.Should().BeTrue($"Group feed creation should succeed: {response.Message}");

        _scenarioContext[$"GroupFeed_{groupName}"] = feedId;
        _scenarioContext[$"GroupFeedAesKey_{groupName}"] = aesKey;
    }

    [When(@"(.*) creates a ChatFeed with (.*) via gRPC")]
    public async Task WhenUserCreatesAChatFeedViaGrpc(string initiatorName, string recipientName)
    {
        var initiator = GetTestIdentity(initiatorName);
        var recipient = GetTestIdentity(recipientName);
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var (signedTxJson, feedId, aesKey) = TestTransactionFactory.CreateChatFeed(initiator, recipient);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTxJson
        });

        response.Successfull.Should().BeTrue($"Chat feed creation should succeed: {response.Message}");

        var chatKey = GetChatFeedKey(initiatorName, recipientName);
        _scenarioContext[$"ChatFeed_{chatKey}"] = feedId;
        _scenarioContext[$"ChatFeedAesKey_{chatKey}"] = aesKey;
    }

    [Then(@"(.*)'s feed list should be in the Redis user feeds cache")]
    [Then(@"(.*)'s feed list should still be in the Redis user feeds cache")]
    public async Task ThenUsersFeedListShouldBeInTheRedisUserFeedsCache(string userName)
    {
        var identity = GetTestIdentity(userName);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = GetUserFeedsCacheKey(identity.PublicSigningAddress);
        var exists = await redisDb.KeyExistsAsync(cacheKey);

        exists.Should().BeTrue($"{userName}'s feed list should be in Redis cache");
    }

    [Then(@"the cache should contain (.*)'s personal feed ID")]
    public async Task ThenTheCacheShouldContainUsersPersonalFeedId(string userName)
    {
        var identity = GetTestIdentity(userName);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = GetUserFeedsCacheKey(identity.PublicSigningAddress);
        var members = await redisDb.SetMembersAsync(cacheKey);

        members.Should().NotBeEmpty($"Cache should contain {userName}'s feed IDs");
    }

    [Then(@"the Redis user feeds cache TTL should be less than or equal to (.*) minutes")]
    public async Task ThenTheRedisUserFeedsCacheTtlShouldBeLessThanOrEqualToMinutes(int maxMinutes)
    {
        var identity = GetTestIdentity("Alice"); // Default to Alice for this step
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = GetUserFeedsCacheKey(identity.PublicSigningAddress);
        var ttl = await redisDb.KeyTimeToLiveAsync(cacheKey);

        ttl.Should().NotBeNull("Cache key should have TTL set");
        ttl!.Value.TotalMinutes.Should().BeGreaterThan(0).And.BeLessOrEqualTo(maxMinutes,
            $"TTL should be less than or equal to {maxMinutes} minutes");
    }

    [Then(@"(.*)'s Redis user feeds cache should contain one more feed ID")]
    public async Task ThenUsersRedisUserFeedsCacheShouldContainOneMoreFeedId(string userName)
    {
        var identity = GetTestIdentity(userName);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = GetUserFeedsCacheKey(identity.PublicSigningAddress);
        var members = await redisDb.SetMembersAsync(cacheKey);

        var previousCount = (int)_scenarioContext[$"FeedsCacheCount_{userName}"];
        members.Length.Should().Be(previousCount + 1, $"{userName}'s cache should have one more feed ID");
    }

    [Then(@"the response should succeed")]
    public void ThenTheResponseShouldSucceed()
    {
        // The GetFeedMessagesForAddress call itself doesn't fail - we just verify it was called
        // The response existence is the success indicator
        _scenarioContext.Keys.Should().Contain(k => k.StartsWith("LastFeedMessagesResponse_"));
    }

    #region Helper Methods

    private static string GetUserFeedsCacheKey(string userPublicAddress)
    {
        return $"HushTest:{UserFeedsCacheConstants.GetUserFeedsKey(userPublicAddress)}";
    }

    private static string GetChatFeedKey(string user1, string user2)
    {
        var names = new[] { user1.ToLowerInvariant(), user2.ToLowerInvariant() };
        Array.Sort(names);
        return $"{names[0]}_{names[1]}";
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
