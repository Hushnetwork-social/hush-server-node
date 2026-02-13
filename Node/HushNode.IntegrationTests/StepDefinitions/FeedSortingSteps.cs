using FluentAssertions;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using System.Text.Json;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-062: Step definitions for Feed Sorting by blockIndex scenarios.
/// Tests that GetFeedsForAddress returns correct blockIndex values and that
/// Redis feed_meta Hash is consistent with gRPC responses.
/// </summary>
[Binding]
public sealed class FeedSortingSteps
{
    private readonly ScenarioContext _scenarioContext;

    public FeedSortingSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    #region Given Steps

    // "Alice has a ChatFeed with Bob" and "Alice has a ChatFeed with Charlie" are
    // already provided by ChatFeedSteps. No new Given steps needed.

    #endregion

    #region When Steps

    /// <summary>
    /// Sends a message to a specific ChatFeed identified by both participants.
    /// Extends the existing pattern to support specifying which ChatFeed when a user has multiple.
    /// </summary>
    [When(@"(.*) sends message ""(.*)"" to ChatFeed\((.*),(.*)\) via gRPC")]
    public async Task WhenUserSendsMessageToSpecificChatFeedViaGrpc(
        string senderName, string message, string participant1, string participant2)
    {
        var sender = GetTestIdentity(senderName);
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var chatKey = GetChatFeedKey(participant1, participant2);
        var feedId = (FeedId)_scenarioContext[$"ChatFeed_{chatKey}"];
        var aesKey = (string)_scenarioContext[$"ChatFeedAesKey_{chatKey}"];

        var signedTransactionJson = TestTransactionFactory.CreateFeedMessage(sender, feedId, message, aesKey);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransactionJson
        });

        response.Successfull.Should().BeTrue($"Message submission should succeed: {response.Message}");
    }

    /// <summary>
    /// Records the current ChatFeed BlockIndex for later comparison.
    /// Must be called after a GetFeedsForAddress request.
    /// </summary>
    [When(@"the ChatFeed BlockIndex is recorded as ""(.*)""")]
    public async Task WhenTheChatFeedBlockIndexIsRecordedAs(string key)
    {
        // We need to call GetFeedsForAddress to get the current blockIndex
        var identity = GetTestIdentity("Alice");
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0
        });

        var feedId = GetSingleChatFeedId();
        var chatFeed = response.Feeds.FirstOrDefault(f => f.FeedId == feedId.ToString());
        chatFeed.Should().NotBeNull("Chat feed should be in response");

        _scenarioContext[$"RecordedBlockIndex_{key}"] = chatFeed!.BlockIndex;
    }

    /// <summary>
    /// User requests feeds and stores the response under a named key.
    /// Supports multi-user comparison in the same scenario.
    /// </summary>
    [When(@"(.*) requests (?:her|his) feeds and stores as ""(.*)""")]
    public async Task WhenUserRequestsFeedsAndStoresAs(string userName, string responseKey)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0
        });

        _scenarioContext[$"NamedFeedsResponse_{responseKey}"] = response;
    }

    #endregion

    #region Then Steps

    /// <summary>
    /// Verifies that one ChatFeed has a higher BlockIndex than another.
    /// Used to confirm that feeds with more recent messages have higher blockIndex.
    /// </summary>
    [Then(@"ChatFeed\((.*),(.*)\) should have a higher BlockIndex than ChatFeed\((.*),(.*)\)")]
    public void ThenChatFeedShouldHaveHigherBlockIndexThanOther(
        string higher1, string higher2, string lower1, string lower2)
    {
        var response = (GetFeedForAddressReply)_scenarioContext["LastFeedsResponse"];

        var higherKey = GetChatFeedKey(higher1, higher2);
        var lowerKey = GetChatFeedKey(lower1, lower2);

        var higherFeedId = (FeedId)_scenarioContext[$"ChatFeed_{higherKey}"];
        var lowerFeedId = (FeedId)_scenarioContext[$"ChatFeed_{lowerKey}"];

        var higherFeed = response.Feeds.FirstOrDefault(f => f.FeedId == higherFeedId.ToString());
        var lowerFeed = response.Feeds.FirstOrDefault(f => f.FeedId == lowerFeedId.ToString());

        higherFeed.Should().NotBeNull($"ChatFeed({higher1},{higher2}) should be in response");
        lowerFeed.Should().NotBeNull($"ChatFeed({lower1},{lower2}) should be in response");

        higherFeed!.BlockIndex.Should().BeGreaterThan(lowerFeed!.BlockIndex,
            $"ChatFeed({higher1},{higher2}) should have a higher BlockIndex than ChatFeed({lower1},{lower2}) " +
            $"because it received a message in a later block. " +
            $"Higher: {higherFeed.BlockIndex}, Lower: {lowerFeed.BlockIndex}");
    }

    /// <summary>
    /// Verifies Redis feed_meta Hash contains lastBlockIndex for a specific ChatFeed.
    /// Extends the existing pattern to support specifying which ChatFeed.
    /// </summary>
    [Then(@"the Redis feed_meta Hash for (.*) should contain lastBlockIndex for ChatFeed\((.*),(.*)\)")]
    public async Task ThenRedisContainsLastBlockIndexForSpecificChatFeed(
        string userName, string participant1, string participant2)
    {
        var identity = GetTestIdentity(userName);
        var chatKey = GetChatFeedKey(participant1, participant2);
        var feedId = (FeedId)_scenarioContext[$"ChatFeed_{chatKey}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var hashKey = $"HushTest:{FeedMetadataCacheConstants.GetFeedMetaHashKey(identity.PublicSigningAddress)}";
        var value = await redisDb.HashGetAsync(hashKey, feedId.ToString());

        value.IsNullOrEmpty.Should().BeFalse(
            $"Redis feed_meta Hash for {userName} should contain an entry for ChatFeed({participant1},{participant2})");

        var json = value.ToString();
        json.Should().Contain("lastBlockIndex",
            "Feed meta value should be JSON with lastBlockIndex field");
    }

    /// <summary>
    /// Verifies that the ChatFeed BlockIndex is greater than a previously recorded value.
    /// </summary>
    [Then(@"the ChatFeed BlockIndex should be greater than ""(.*)""")]
    public void ThenTheChatFeedBlockIndexShouldBeGreaterThan(string key)
    {
        var response = (GetFeedForAddressReply)_scenarioContext["LastFeedsResponse"];
        var recordedBlockIndex = (long)_scenarioContext[$"RecordedBlockIndex_{key}"];

        var feedId = GetSingleChatFeedId();
        var chatFeed = response.Feeds.FirstOrDefault(f => f.FeedId == feedId.ToString());
        chatFeed.Should().NotBeNull("Chat feed should be in response");

        chatFeed!.BlockIndex.Should().BeGreaterThan(recordedBlockIndex,
            $"BlockIndex should be greater than recorded value '{key}' ({recordedBlockIndex}) " +
            $"after additional messages. Current: {chatFeed.BlockIndex}");
    }

    /// <summary>
    /// Verifies that the Redis feed_meta Hash lastBlockIndex is consistent with the gRPC response.
    /// The gRPC response uses effectiveBlockIndex = MAX(feedBlockIndex, participantProfileBlockIndex),
    /// so it may be >= the Redis cached lastBlockIndex (which only stores feed message activity).
    /// </summary>
    [Then(@"the Redis feed_meta Hash lastBlockIndex should be consistent with the response BlockIndex")]
    public async Task ThenRedisLastBlockIndexShouldBeConsistentWithResponseBlockIndex()
    {
        var response = (GetFeedForAddressReply)_scenarioContext["LastFeedsResponse"];
        var identity = GetTestIdentity("Alice");
        var feedId = GetSingleChatFeedId();

        var chatFeed = response.Feeds.FirstOrDefault(f => f.FeedId == feedId.ToString());
        chatFeed.Should().NotBeNull("Chat feed should be in response");

        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var hashKey = $"HushTest:{FeedMetadataCacheConstants.GetFeedMetaHashKey(identity.PublicSigningAddress)}";
        var value = await redisDb.HashGetAsync(hashKey, feedId.ToString());

        value.IsNullOrEmpty.Should().BeFalse("Redis should have a cached entry for the feed");

        var json = value.ToString();
        var doc = JsonDocument.Parse(json);
        var cachedBlockIndex = doc.RootElement.GetProperty("lastBlockIndex").GetInt64();

        // The gRPC response effectiveBlockIndex >= Redis lastBlockIndex because
        // effectiveBlockIndex = MAX(feedBlockIndex, participantProfileBlockIndex)
        chatFeed!.BlockIndex.Should().BeGreaterOrEqualTo(cachedBlockIndex,
            $"gRPC response BlockIndex ({chatFeed.BlockIndex}) should be >= Redis cached lastBlockIndex ({cachedBlockIndex}). " +
            $"effectiveBlockIndex accounts for participant profile updates in addition to feed message activity.");

        cachedBlockIndex.Should().BeGreaterThan(0,
            "Redis cached lastBlockIndex should be positive after a message was sent and block produced");
    }

    /// <summary>
    /// Verifies that the response BlockIndex is >= the Redis cached value.
    /// The gRPC response uses effectiveBlockIndex = MAX(feedBlockIndex, participantProfileBlockIndex),
    /// so it may exceed the raw Redis lastBlockIndex when a participant was registered at a later block.
    /// </summary>
    [Then(@"the response BlockIndex should be greater than or equal to the Redis cached value")]
    public async Task ThenResponseBlockIndexShouldBeGteRedisCachedValue()
    {
        var response = (GetFeedForAddressReply)_scenarioContext["LastFeedsResponse"];
        var identity = GetTestIdentity("Alice");
        var feedId = GetSingleChatFeedId();

        var chatFeed = response.Feeds.FirstOrDefault(f => f.FeedId == feedId.ToString());
        chatFeed.Should().NotBeNull("Chat feed should be in response");

        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var hashKey = $"HushTest:{FeedMetadataCacheConstants.GetFeedMetaHashKey(identity.PublicSigningAddress)}";
        var value = await redisDb.HashGetAsync(hashKey, feedId.ToString());

        value.IsNullOrEmpty.Should().BeFalse("Redis should have a cached entry for the feed");

        var json = value.ToString();
        var doc = JsonDocument.Parse(json);
        var cachedBlockIndex = doc.RootElement.GetProperty("lastBlockIndex").GetInt64();

        chatFeed!.BlockIndex.Should().BeGreaterOrEqualTo(cachedBlockIndex,
            $"gRPC response BlockIndex ({chatFeed.BlockIndex}) should be >= Redis lastBlockIndex ({cachedBlockIndex}). " +
            $"effectiveBlockIndex = MAX(feedBlockIndex, participantProfileBlockIndex)");

        cachedBlockIndex.Should().BeGreaterThan(0,
            "Redis cached lastBlockIndex should be positive after message finalization");
    }

    /// <summary>
    /// Verifies that two named feed responses have the same BlockIndex for the shared ChatFeed.
    /// </summary>
    [Then(@"both ""(.*)"" and ""(.*)"" should have the same BlockIndex for the shared ChatFeed")]
    public void ThenBothResponsesShouldHaveSameBlockIndexForSharedChatFeed(string responseKey1, string responseKey2)
    {
        var response1 = (GetFeedForAddressReply)_scenarioContext[$"NamedFeedsResponse_{responseKey1}"];
        var response2 = (GetFeedForAddressReply)_scenarioContext[$"NamedFeedsResponse_{responseKey2}"];

        var feedId = GetSingleChatFeedId();

        var feed1 = response1.Feeds.FirstOrDefault(f => f.FeedId == feedId.ToString());
        var feed2 = response2.Feeds.FirstOrDefault(f => f.FeedId == feedId.ToString());

        feed1.Should().NotBeNull("First response should contain the shared ChatFeed");
        feed2.Should().NotBeNull("Second response should contain the shared ChatFeed");

        feed1!.BlockIndex.Should().Be(feed2!.BlockIndex,
            $"Both participants should see the same BlockIndex for the shared ChatFeed. " +
            $"{responseKey1}: {feed1.BlockIndex}, {responseKey2}: {feed2.BlockIndex}");
    }

    #endregion

    #region Helper Methods

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

    private static string GetChatFeedKey(string user1, string user2)
    {
        var names = new[] { user1.Trim().ToLowerInvariant(), user2.Trim().ToLowerInvariant() };
        Array.Sort(names);
        return $"{names[0]}_{names[1]}";
    }

    /// <summary>
    /// Gets the single chat feed ID when there's only one in context.
    /// For scenarios with a single ChatFeed.
    /// </summary>
    private FeedId GetSingleChatFeedId()
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
