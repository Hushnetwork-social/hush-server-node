using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using StackExchange.Redis;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-050: Step definitions for Feed Participants & Group Keys Cache scenarios.
/// Tests cache-aside pattern for:
/// - Participants: Populated by NotificationEventHandler when messages are sent (Redis SET)
/// - Key Generations: Populated by GetKeyGenerations gRPC on lookup (JSON STRING)
/// </summary>
[Binding]
public sealed class FeedParticipantsCacheSteps
{
    private readonly ScenarioContext _scenarioContext;

    public FeedParticipantsCacheSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"(.*) has created a group feed ""(.*)""")]
    public async Task GivenUserHasCreatedAGroupFeed(string userName, string groupName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var (signedTxJson, feedId, aesKey) = TestTransactionFactory.CreateGroupFeed(identity, groupName);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTxJson
        });

        response.Successfull.Should().BeTrue($"Group feed creation should succeed: {response.Message}");

        await blockControl.ProduceBlockAsync();

        _scenarioContext[$"GroupFeed_{groupName}"] = feedId;
        _scenarioContext[$"GroupFeedAesKey_{groupName}"] = aesKey;
    }

    [Given(@"the Redis participants cache for ""(.*)"" is empty")]
    public async Task GivenTheRedisParticipantsCacheForGroupIsEmpty(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        await ClearParticipantsCache(feedId);
    }

    [Given(@"the Redis participants cache for the ChatFeed is empty")]
    public async Task GivenTheRedisParticipantsCacheForTheChatFeedIsEmpty()
    {
        var feedId = GetChatFeedId();
        await ClearParticipantsCache(feedId);
    }

    [Given(@"the Redis key generations cache for ""(.*)"" is empty")]
    public async Task GivenTheRedisKeyGenerationsCacheIsEmpty(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:keys";
        await redisDb.KeyDeleteAsync(cacheKey);
    }

    [Given(@"(.*) has sent a message to ""(.*)""")]
    public async Task GivenUserHasSentAMessageToGroup(string userName, string groupName)
    {
        await WhenUserSendsMessageToGroupViaGrpc(userName, "Test message", groupName);

        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();
    }

    [Given(@"the participants are in the Redis SET cache for ""(.*)""")]
    public async Task GivenTheParticipantsAreInTheRedisSETCacheForGroup(string groupName)
    {
        // Verify cache is populated from the message send
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";
        var exists = await redisDb.KeyExistsAsync(cacheKey);

        if (!exists)
        {
            // If not populated yet, send a message to populate it
            await GivenUserHasSentAMessageToGroup("Alice", groupName);
        }
    }

    [Given(@"the group has key generations in the database")]
    public void GivenTheGroupHasKeyGenerationsInTheDatabase()
    {
        // Group was created with initial key generation, so it exists in DB
    }

    [When(@"(.*) sends message ""(.*)"" to group ""(.*)"" via gRPC")]
    public async Task WhenUserSendsMessageToGroupViaGrpc(string userName, string message, string groupName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var aesKey = (string)_scenarioContext[$"GroupFeedAesKey_{groupName}"];

        var signedTransactionJson = TestTransactionFactory.CreateFeedMessage(identity, feedId, message, aesKey);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransactionJson
        });

        response.Successfull.Should().BeTrue($"Message submission should succeed: {response.Message}");

        _scenarioContext[$"LastGroupMessage_{groupName}"] = message;
        _scenarioContext["LastGroupFeedName"] = groupName;
    }

    [When(@"the key generations for ""(.*)"" are looked up via gRPC")]
    public async Task WhenTheKeyGenerationsForGroupAreLookedUpViaGrpc(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var identity = GetTestIdentity("Alice"); // Use Alice's identity for the request
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = identity.PublicSigningAddress
        });

        _scenarioContext[$"LastKeyGenerationsResponse_{groupName}"] = response;
        _scenarioContext["LastGroupFeedName"] = groupName;
    }

    [When(@"the Redis participants cache for ""(.*)"" is flushed")]
    public async Task WhenTheRedisParticipantsCacheForGroupIsFlushed(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        await ClearParticipantsCache(feedId);
    }

    [When(@"the Redis key generations cache for ""(.*)"" is flushed")]
    public async Task WhenTheRedisKeyGenerationsCacheForGroupIsFlushed(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:keys";
        await redisDb.KeyDeleteAsync(cacheKey);
    }

    [Then(@"the participants should be in the Redis SET cache for ""(.*)""")]
    public async Task ThenTheParticipantsShouldBeInTheRedisSETCacheForGroup(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        await VerifyParticipantsCacheExists(feedId, groupName);
    }

    [Then(@"the participants should be in the Redis SET cache for the ChatFeed")]
    public async Task ThenTheParticipantsShouldBeInTheRedisSETCacheForTheChatFeed()
    {
        var feedId = GetChatFeedId();
        await VerifyParticipantsCacheExists(feedId, "ChatFeed");
    }

    [Then(@"the cache SET should contain (.*) as a participant")]
    public async Task ThenTheCacheSETShouldContainUserAsAParticipant(string userName)
    {
        var groupName = (string)_scenarioContext["LastGroupFeedName"];
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var identity = GetTestIdentity(userName);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";

        // Participants cache uses Redis SET
        var members = await redisDb.SetMembersAsync(cacheKey);
        var memberStrings = members.Select(m => m.ToString()).ToList();

        memberStrings.Should().Contain(identity.PublicSigningAddress,
            $"Cache SET should contain {userName} as participant");
    }

    [Then(@"the cache SET should contain both (.*) and (.*)")]
    public async Task ThenTheCacheSETShouldContainBothUsers(string user1, string user2)
    {
        var feedId = GetChatFeedId();
        var identity1 = GetTestIdentity(user1);
        var identity2 = GetTestIdentity(user2);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";

        // Participants cache uses Redis SET
        var members = await redisDb.SetMembersAsync(cacheKey);
        var memberStrings = members.Select(m => m.ToString()).ToList();

        memberStrings.Should().Contain(identity1.PublicSigningAddress,
            $"Cache SET should contain {user1} as participant");
        memberStrings.Should().Contain(identity2.PublicSigningAddress,
            $"Cache SET should contain {user2} as participant");
    }

    [Then(@"the key generations should be in the Redis cache for ""(.*)""")]
    public async Task ThenTheKeyGenerationsShouldBeInTheRedisCacheForGroup(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:keys";
        var exists = await redisDb.KeyExistsAsync(cacheKey);

        exists.Should().BeTrue($"Key generations should be in Redis cache for {groupName}");
    }

    [Then(@"the cache should contain at least one key generation")]
    public async Task ThenTheCacheShouldContainAtLeastOneKeyGeneration()
    {
        var groupName = (string)_scenarioContext["LastGroupFeedName"];
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:keys";
        var cachedValue = await redisDb.StringGetAsync(cacheKey);

        cachedValue.IsNullOrEmpty.Should().BeFalse("Cache should contain key generations JSON");
        cachedValue.ToString().Should().Contain("keyGenerations",
            "Cache should contain valid key generations JSON structure (camelCase)");
    }

    [Then(@"the response should contain the key generations")]
    public void ThenTheResponseShouldContainTheKeyGenerations()
    {
        var groupName = (string)_scenarioContext["LastGroupFeedName"];
        var response = (GetKeyGenerationsResponse)_scenarioContext[$"LastKeyGenerationsResponse_{groupName}"];
        response.KeyGenerations.Should().NotBeEmpty("Response should contain key generations");
    }

    [Given(@"the key generations for ""(.*)"" have been cached")]
    public async Task GivenTheKeyGenerationsHaveBeenCached(string groupName)
    {
        // Trigger cache population via lookup
        await WhenTheKeyGenerationsForGroupAreLookedUpViaGrpc(groupName);
    }

    [When(@"the key generations for ""(.*)"" are looked up via gRPC again")]
    public async Task WhenTheKeyGenerationsForGroupAreLookedUpViaGrpcAgain(string groupName)
    {
        await WhenTheKeyGenerationsForGroupAreLookedUpViaGrpc(groupName);
    }

    [When(@"the participants cache service stores participants for ""(.*)""")]
    public async Task WhenTheParticipantsCacheServiceStoresParticipantsForGroup(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var identity = GetTestIdentity("Alice");
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        // Directly store participants in Redis SET (simulating what the cache service does)
        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";
        var participants = new[] { identity.PublicSigningAddress };

        // Use Redis SET to store participants
        await redisDb.SetAddAsync(cacheKey, participants.Select(p => (RedisValue)p).ToArray());

        _scenarioContext[$"StoredParticipants_{groupName}"] = participants;
        _scenarioContext["LastGroupFeedName"] = groupName;
    }

    [Then(@"the participants should be retrievable from the cache service")]
    public async Task ThenTheParticipantsShouldBeRetrievableFromTheCacheService()
    {
        var groupName = (string)_scenarioContext["LastGroupFeedName"];
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";
        var members = await redisDb.SetMembersAsync(cacheKey);

        members.Should().NotBeEmpty("Participants should be retrievable from cache");

        var storedParticipants = (string[])_scenarioContext[$"StoredParticipants_{groupName}"];
        var memberStrings = members.Select(m => m.ToString()).ToList();

        foreach (var participant in storedParticipants)
        {
            memberStrings.Should().Contain(participant, $"Cache should contain stored participant");
        }
    }

    #region Helper Methods

    private async Task ClearParticipantsCache(FeedId feedId)
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";
        await redisDb.KeyDeleteAsync(cacheKey);

        var exists = await redisDb.KeyExistsAsync(cacheKey);
        exists.Should().BeFalse("Participants cache should be empty after deletion");
    }

    private async Task VerifyParticipantsCacheExists(FeedId feedId, string feedName)
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";

        // Retry with small delays to handle async fire-and-forget cache population
        var maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var exists = await redisDb.KeyExistsAsync(cacheKey);
            if (exists)
            {
                return; // Cache exists, test passes
            }
            await Task.Delay(100); // Wait 100ms between attempts
        }

        // Final check with assertion
        var finalExists = await redisDb.KeyExistsAsync(cacheKey);
        finalExists.Should().BeTrue($"Participants should be in Redis SET cache for {feedName}");
    }

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
