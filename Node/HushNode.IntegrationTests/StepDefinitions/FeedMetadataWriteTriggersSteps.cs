using System.Text.Json;
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
/// FEAT-065 Phase 4: Step definitions for feed metadata write trigger integration tests.
/// Verifies that transaction handlers correctly write full metadata to Redis.
/// </summary>
[Binding]
public sealed class FeedMetadataWriteTriggersSteps
{
    private const string KeyPrefix = "HushTest:";
    private readonly ScenarioContext _scenarioContext;

    public FeedMetadataWriteTriggersSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    #region When Steps

    [When(@"(.*) changes display name to ""(.*)"" via gRPC")]
    public async Task WhenUserChangesDisplayNameViaGrpc(string userName, string newAlias)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var signedTxJson = TestTransactionFactory.CreateIdentityUpdate(identity, newAlias);
        var response = await blockchainClient.SubmitSignedTransactionAsync(
            new SubmitSignedTransactionRequest { SignedTransaction = signedTxJson });

        response.Successfull.Should().BeTrue($"Identity update for {userName} should succeed: {response.Message}");
    }

    [When(@"Alice's feed_meta Hash is flushed")]
    public async Task WhenAliceFeedMetaHashIsFlushed()
    {
        var alice = TestIdentities.Alice;
        var redisDb = GetRedisDatabase();
        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(alice.PublicSigningAddress)}";
        await redisDb.KeyDeleteAsync(hashKey);
    }

    [When(@"GetFeedsForAddress is called for Alice")]
    [Then(@"GetFeedsForAddress is called for Alice")]
    public async Task WhenGetFeedsForAddressIsCalledForAlice()
    {
        var alice = TestIdentities.Alice;
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = alice.PublicSigningAddress,
            BlockIndex = 0
        });
    }

    #endregion

    #region Then Steps — F6-003 Feed Creation Triggers

    [Then(@"(.*)'s feed_meta Hash should contain the ChatFeed with title ""(.*)""")]
    public async Task ThenUserFeedMetaHashShouldContainChatFeedWithTitle(string userName, string expectedTitle)
    {
        var identity = GetTestIdentity(userName);
        var feedId = GetChatFeedId();
        var redisDb = GetRedisDatabase();

        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(identity.PublicSigningAddress)}";
        var value = await redisDb.HashGetAsync(hashKey, feedId.ToString());

        value.IsNullOrEmpty.Should().BeFalse(
            $"{userName}'s feed_meta Hash should contain entry for ChatFeed {feedId}");

        var entry = JsonSerializer.Deserialize<FeedMetadataEntry>(value.ToString());
        entry.Should().NotBeNull();
        entry!.Title.Should().Be(expectedTitle,
            $"{userName}'s ChatFeed title should be '{expectedTitle}'");
    }

    [Then(@"(.*)'s feed_meta entry should have type Chat and correct participants")]
    public async Task ThenUserFeedMetaEntryShouldHaveTypeChatAndCorrectParticipants(string userName)
    {
        var identity = GetTestIdentity(userName);
        var feedId = GetChatFeedId();
        var redisDb = GetRedisDatabase();

        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(identity.PublicSigningAddress)}";
        var value = await redisDb.HashGetAsync(hashKey, feedId.ToString());

        var entry = JsonSerializer.Deserialize<FeedMetadataEntry>(value.ToString());
        entry.Should().NotBeNull();
        entry!.Type.Should().Be((int)FeedType.Chat, "Feed type should be Chat");
        entry.Participants.Should().NotBeNull().And.HaveCount(2, "Chat feed should have 2 participants");
        entry.Participants.Should().Contain(TestIdentities.Alice.PublicSigningAddress);
        entry.Participants.Should().Contain(TestIdentities.Bob.PublicSigningAddress);
        entry.CreatedAtBlock.Should().BeGreaterThan(0, "CreatedAtBlock should be set");
        entry.LastBlockIndex.Should().BeGreaterThan(0, "LastBlockIndex should be set");
    }

    #endregion

    #region Then Steps — F6-002 Message Triggers

    [Then(@"(.*)'s feed_meta Hash entry for the ChatFeed should have an updated lastBlockIndex")]
    public async Task ThenUserFeedMetaHashEntryShouldHaveUpdatedLastBlockIndex(string userName)
    {
        var identity = GetTestIdentity(userName);
        var feedId = GetChatFeedId();
        var redisDb = GetRedisDatabase();

        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(identity.PublicSigningAddress)}";
        var value = await redisDb.HashGetAsync(hashKey, feedId.ToString());

        value.IsNullOrEmpty.Should().BeFalse(
            $"{userName}'s feed_meta Hash should contain entry for ChatFeed after message");

        var entry = JsonSerializer.Deserialize<FeedMetadataEntry>(value.ToString());
        entry.Should().NotBeNull();
        entry!.LastBlockIndex.Should().BeGreaterThan(0,
            $"{userName}'s lastBlockIndex should be updated after message");
        // Verify it's a full FEAT-065 entry (not legacy)
        entry.Title.Should().NotBeNullOrEmpty("Entry should have a title (not legacy format)");
        entry.Participants.Should().NotBeNull("Entry should have participants (not legacy format)");
    }

    #endregion

    #region Then Steps — F6-008 Identity Display Name Cache

    [Then(@"the identity display names Hash should contain ""(.*)"" for (.*)'s address")]
    public async Task ThenIdentityDisplayNamesHashShouldContainValueForAddress(string expectedName, string userName)
    {
        var identity = GetTestIdentity(userName);
        var redisDb = GetRedisDatabase();

        var hashKey = $"{KeyPrefix}{IdentityDisplayNameCacheConstants.DisplayNamesHashKey}";
        var value = await redisDb.HashGetAsync(hashKey, identity.PublicSigningAddress);

        value.IsNullOrEmpty.Should().BeFalse(
            $"Identity display names Hash should contain entry for {userName}");
        value.ToString().Should().Be(expectedName,
            $"{userName}'s display name should be '{expectedName}'");
    }

    #endregion

    #region Then Steps — Cache Repopulation

    [Then(@"Alice's feed_meta Hash should be repopulated with the ChatFeed")]
    public async Task ThenAliceFeedMetaHashShouldBeRepopulatedWithChatFeed()
    {
        var alice = TestIdentities.Alice;
        var feedId = GetChatFeedId();
        var redisDb = GetRedisDatabase();

        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(alice.PublicSigningAddress)}";
        var value = await redisDb.HashGetAsync(hashKey, feedId.ToString());

        value.IsNullOrEmpty.Should().BeFalse(
            "Alice's feed_meta Hash should be repopulated after GetFeedsForAddress");

        var entry = JsonSerializer.Deserialize<FeedMetadataEntry>(value.ToString());
        entry.Should().NotBeNull();
        entry!.Title.Should().NotBeNullOrEmpty("Repopulated entry should have a resolved title");
        entry.Type.Should().Be((int)FeedType.Chat);
    }

    #endregion

    #region Helper Methods

    private FeedId GetChatFeedId()
    {
        // Find the chat feed ID from ScenarioContext (set by ChatFeedSteps)
        foreach (var key in _scenarioContext.Keys)
        {
            if (key.StartsWith("ChatFeed_") && !key.Contains("AesKey"))
            {
                return (FeedId)_scenarioContext[key];
            }
        }
        throw new InvalidOperationException("No ChatFeed found in ScenarioContext.");
    }

    private TestIdentity GetTestIdentity(string userName)
    {
        var contextKey = $"Identity_{userName}";
        if (_scenarioContext.TryGetValue(contextKey, out var identityObj)
            && identityObj is TestIdentity identity)
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

    private IDatabase GetRedisDatabase()
    {
        var fixture = GetFixture();
        return fixture.RedisConnection.GetDatabase();
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

    private GrpcClientFactory GetGrpcFactory()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            && factoryObj is GrpcClientFactory grpcFactory)
        {
            return grpcFactory;
        }
        throw new InvalidOperationException("GrpcClientFactory not found in ScenarioContext.");
    }

    #endregion
}
