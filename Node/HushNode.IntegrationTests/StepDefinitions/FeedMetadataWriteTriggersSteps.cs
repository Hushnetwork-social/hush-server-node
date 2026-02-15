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

    [Given(@"(.*)'s feed_meta Hash is flushed")]
    [When(@"(.*)'s feed_meta Hash is flushed")]
    public async Task WhenUserFeedMetaHashIsFlushed(string userName)
    {
        var identity = GetTestIdentity(userName);
        var redisDb = GetRedisDatabase();
        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(identity.PublicSigningAddress)}";
        await redisDb.KeyDeleteAsync(hashKey);
    }

    [Given(@"GetFeedsForAddress is called for Alice")]
    [When(@"GetFeedsForAddress is called for Alice")]
    [Then(@"GetFeedsForAddress is called for Alice")]
    public async Task WhenGetFeedsForAddressIsCalledForAlice()
    {
        await GetFeedsForAddressAsync("Alice");
    }

    [Given(@"GetFeedsForAddress is called for Bob")]
    [When(@"GetFeedsForAddress is called for Bob")]
    public async Task WhenGetFeedsForAddressIsCalledForBob()
    {
        await GetFeedsForAddressAsync("Bob");
    }

    [When(@"(.*) changes group ""(.*)"" title to ""(.*)"" via gRPC")]
    public async Task WhenUserChangesGroupTitleViaGrpc(string userName, string groupName, string newTitle)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];

        var response = await feedClient.UpdateGroupFeedTitleAsync(new UpdateGroupFeedTitleRequest
        {
            FeedId = feedId.ToString(),
            AdminPublicAddress = identity.PublicSigningAddress,
            NewTitle = newTitle
        });

        response.Success.Should().BeTrue($"Group title change should succeed: {response.Message}");
    }

    private async Task GetFeedsForAddressAsync(string userName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0
        });

        _scenarioContext[$"LastFeedsResponse_{userName}"] = response;
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

    #region Then Steps — F6-008 Identity Name Change Feed Title Cascades

    [Then(@"(.*)'s feed_meta Hash should contain his Personal feed with title ""(.*)""")]
    public async Task ThenUserFeedMetaHashShouldContainPersonalFeedWithTitle(string userName, string expectedTitle)
    {
        var identity = GetTestIdentity(userName);
        var redisDb = GetRedisDatabase();

        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(identity.PublicSigningAddress)}";

        // Retry with small delays to handle fire-and-forget cache writes
        FeedMetadataEntry? personalEntry = null;
        string diagnosticInfo = "";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var allEntries = await redisDb.HashGetAllAsync(hashKey);
            diagnosticInfo = $"Hash '{hashKey}' has {allEntries.Length} entries: [{string.Join(", ", allEntries.Select(e => { var m = JsonSerializer.Deserialize<FeedMetadataEntry>(e.Value.ToString()); return $"{{feedId={e.Name}, type={m?.Type}, title='{m?.Title}'}}"; }))}]";

            foreach (var entry in allEntries)
            {
                var metadata = JsonSerializer.Deserialize<FeedMetadataEntry>(entry.Value.ToString());
                if (metadata != null && metadata.Type == (int)FeedType.Personal
                    && metadata.Title == expectedTitle)
                {
                    personalEntry = metadata;
                    break;
                }
            }

            if (personalEntry != null) break;
            await Task.Delay(200);
        }

        personalEntry.Should().NotBeNull(
            $"{userName}'s feed_meta Hash should contain a Personal feed entry with title '{expectedTitle}'. {diagnosticInfo}");
    }

    [Then(@"GetFeedsForAddress for (.*) should return the ChatFeed with title ""(.*)""")]
    public async Task ThenGetFeedsForAddressForUserShouldReturnChatFeedWithTitle(string userName, string expectedTitle)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0
        });

        var chatFeedId = GetChatFeedId();
        var chatFeed = response.Feeds.FirstOrDefault(f => f.FeedId == chatFeedId.ToString());

        chatFeed.Should().NotBeNull(
            $"GetFeedsForAddress for {userName} should return the ChatFeed");
        chatFeed!.FeedTitle.Should().Be(expectedTitle,
            $"ChatFeed title in gRPC response should be '{expectedTitle}'");
    }

    #endregion

    #region Then Steps — Group Title Change Cascades

    [Then(@"(.*)'s feed_meta Hash should contain group ""(.*)"" with title ""(.*)""")]
    public async Task ThenUserFeedMetaHashShouldContainGroupWithTitle(string userName, string groupName, string expectedTitle)
    {
        var identity = GetTestIdentity(userName);
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var redisDb = GetRedisDatabase();

        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(identity.PublicSigningAddress)}";

        // Retry with small delays to handle fire-and-forget cache writes
        FeedMetadataEntry? matchingEntry = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var value = await redisDb.HashGetAsync(hashKey, feedId.ToString());
            if (!value.IsNullOrEmpty)
            {
                var entry = JsonSerializer.Deserialize<FeedMetadataEntry>(value.ToString());
                if (entry != null && entry.Title == expectedTitle)
                {
                    matchingEntry = entry;
                    break;
                }
            }
            await Task.Delay(100);
        }

        matchingEntry.Should().NotBeNull(
            $"{userName}'s feed_meta Hash should contain entry for group '{groupName}' with title '{expectedTitle}'");
    }

    [Then(@"GetFeedsForAddress for (.*) should return group feed with title ""(.*)""")]
    public async Task ThenGetFeedsForAddressForUserShouldReturnGroupFeedWithTitle(string userName, string expectedTitle)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0
        });

        var groupFeed = response.Feeds.FirstOrDefault(f =>
            f.FeedType == (int)FeedType.Group && f.FeedTitle == expectedTitle);

        groupFeed.Should().NotBeNull(
            $"GetFeedsForAddress for {userName} should return a group feed with title '{expectedTitle}'. " +
            $"Found feeds: [{string.Join(", ", response.Feeds.Select(f => $"{f.FeedTitle}(type={f.FeedType})"))}]");
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
