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
/// FEAT-060: Step definitions for Redis-First Caching scenarios.
/// Tests feed_meta Hash (lastBlockIndex) and read positions Hash integration.
/// </summary>
[Binding]
public sealed class RedisCachingSteps
{
    private readonly ScenarioContext _scenarioContext;

    public RedisCachingSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    #region Feed Metadata (feed_meta Hash) Steps

    [Then(@"the Redis feed_meta Hash for (.*) should contain lastBlockIndex for the ChatFeed")]
    public async Task ThenTheRedisFeedMetaHashForUserShouldContainLastBlockIndexForTheChatFeed(string userName)
    {
        var identity = GetTestIdentity(userName);
        var feedId = GetChatFeedId();
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var hashKey = $"HushTest:{FeedMetadataCacheConstants.GetFeedMetaHashKey(identity.PublicSigningAddress)}";
        var value = await redisDb.HashGetAsync(hashKey, feedId.ToString());

        value.IsNullOrEmpty.Should().BeFalse(
            $"Redis feed_meta Hash for {userName} should contain an entry for feed {feedId}");

        // Verify JSON contains lastBlockIndex
        var json = value.ToString();
        json.Should().Contain("lastBlockIndex",
            "Feed meta value should be JSON with lastBlockIndex field");
    }

    [Then(@"the ChatFeed response should have a non-zero lastBlockIndex")]
    public void ThenTheChatFeedResponseShouldHaveANonZeroLastBlockIndex()
    {
        var feedId = GetChatFeedId();
        var response = (GetFeedForAddressReply)_scenarioContext["LastFeedsResponse"];

        var chatFeed = response.Feeds.FirstOrDefault(f => f.FeedId == feedId.ToString());
        chatFeed.Should().NotBeNull("Chat feed should be in response");
        chatFeed!.BlockIndex.Should().BeGreaterThan(0,
            "BlockIndex should be non-zero after message finalization");
    }

    [Then(@"the ChatFeed response should have lastBlockIndex greater than (.*)")]
    public void ThenTheChatFeedResponseShouldHaveLastBlockIndexGreaterThan(int minValue)
    {
        var feedId = GetChatFeedId();
        var response = (GetFeedForAddressReply)_scenarioContext["LastFeedsResponse"];

        var chatFeed = response.Feeds.FirstOrDefault(f => f.FeedId == feedId.ToString());
        chatFeed.Should().NotBeNull("Chat feed should be in response");
        chatFeed!.BlockIndex.Should().BeGreaterThan(minValue,
            $"BlockIndex should be greater than {minValue} after multiple messages");
    }

    #endregion

    #region Graceful Degradation Steps

    [Then(@"the response should contain the ChatFeed")]
    public void ThenTheResponseShouldContainTheChatFeed()
    {
        var feedId = GetChatFeedId();
        var response = (GetFeedForAddressReply)_scenarioContext["LastFeedsResponse"];

        var chatFeed = response.Feeds.FirstOrDefault(f => f.FeedId == feedId.ToString());
        chatFeed.Should().NotBeNull(
            "Response should contain the ChatFeed even after Redis flush (PostgreSQL fallback)");
    }

    [Then(@"no error is returned to the client")]
    public void ThenNoErrorIsReturnedToTheClient()
    {
        // The gRPC call itself completed without throwing - stored in ScenarioContext
        _scenarioContext.ContainsKey("LastFeedsResponse").Should().BeTrue(
            "GetFeedsForAddress should complete without error even when Redis is unavailable");
    }

    #endregion

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
