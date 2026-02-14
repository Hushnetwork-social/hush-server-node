using System.Text.Json;
using FluentAssertions;
using HushNode.Caching;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-065 E1: Step definitions for full feed metadata cache integration tests.
/// Tests FeedMetadataCacheService against real Redis via TestContainers.
/// </summary>
[Binding]
public sealed class FeedMetadataFullCacheSteps
{
    private const string KeyPrefix = "HushTest:";
    private readonly ScenarioContext _scenarioContext;
    private FeedMetadataCacheService? _cacheService;
    private IReadOnlyDictionary<FeedId, FeedMetadataEntry>? _lastResult;

    public FeedMetadataFullCacheSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    #region Given Steps

    [Given(@"feed metadata for user ""(.*)"" is populated in Redis with (\d+) feeds?")]
    [When(@"feed metadata for user ""(.*)"" is populated in Redis with (\d+) feeds?")]
    public async Task GivenFeedMetadataForUserIsPopulatedInRedis(string userName, int feedCount)
    {
        var sut = GetOrCreateCacheService();
        var userId = GetUserId(userName);
        var entries = new Dictionary<FeedId, FeedMetadataEntry>();

        for (int i = 1; i <= feedCount; i++)
        {
            var feedId = FeedId.NewFeedId;
            entries[feedId] = new FeedMetadataEntry
            {
                Title = $"Feed {i}",
                Type = i % 2 == 0 ? 2 : 1, // Alternate Chat(1) and Group(2)
                LastBlockIndex = 100 * i,
                Participants = new List<string> { "0xalice", $"0xuser{i}" },
                CreatedAtBlock = i,
                CurrentKeyGeneration = i > 1 ? i : null
            };
        }

        var result = await sut.SetMultipleFeedMetadataAsync(userId, entries);
        result.Should().BeTrue("SetMultipleFeedMetadataAsync should succeed with real Redis");

        // Store for later verification
        _scenarioContext[$"PopulatedFeeds_{userName}"] = entries;
    }

    [Given(@"legacy FEAT-060 feed metadata is written for user ""(.*)""")]
    public async Task GivenLegacyFeat060FeedMetadataIsWrittenForUser(string userName)
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();
        var userId = GetUserId(userName);
        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(userId)}";

        // Write legacy FEAT-060 format: {"lastBlockIndex": 500}
        var feedId = FeedId.NewFeedId;
        var legacyJson = JsonSerializer.Serialize(new { lastBlockIndex = 500 });
        await redisDb.HashSetAsync(hashKey, feedId.ToString(), legacyJson);
    }

    #endregion

    #region When Steps

    [When(@"GetAllFeedMetadataAsync is called for user ""(.*)""")]
    public async Task WhenGetAllFeedMetadataAsyncIsCalledForUser(string userName)
    {
        var sut = GetOrCreateCacheService();
        var userId = GetUserId(userName);
        _lastResult = await sut.GetAllFeedMetadataAsync(userId);
        _scenarioContext["LastFeedMetadataResult"] = (object?)_lastResult ?? "null";
    }

    #endregion

    #region Then Steps — Result Assertions

    [Then(@"a dictionary of (\d+) entries is returned")]
    public void ThenADictionaryOfEntriesIsReturned(int expectedCount)
    {
        _lastResult.Should().NotBeNull("GetAllFeedMetadataAsync should return a non-null result");
        _lastResult!.Count.Should().Be(expectedCount,
            $"Expected {expectedCount} entries in the result dictionary");
    }

    [Then(@"each FeedMetadataEntry has title, type, lastBlockIndex, participants, createdAtBlock fields")]
    public void ThenEachFeedMetadataEntryHasAllFields()
    {
        _lastResult.Should().NotBeNull();

        foreach (var (feedId, entry) in _lastResult!)
        {
            entry.Title.Should().NotBeNullOrEmpty($"Feed {feedId} should have a title");
            entry.Type.Should().BeGreaterThan(0, $"Feed {feedId} should have a non-zero type");
            entry.LastBlockIndex.Should().BeGreaterThan(0, $"Feed {feedId} should have a lastBlockIndex");
            entry.Participants.Should().NotBeNull($"Feed {feedId} should have participants");
            entry.Participants!.Count.Should().BeGreaterThan(0, $"Feed {feedId} should have at least 1 participant");
            entry.CreatedAtBlock.Should().BeGreaterThan(0, $"Feed {feedId} should have a createdAtBlock");
        }
    }

    [Then(@"the result should be null")]
    public void ThenTheResultShouldBeNull()
    {
        _lastResult.Should().BeNull("GetAllFeedMetadataAsync should return null on cache miss");
    }

    [Then(@"the CacheHits counter should be (\d+)")]
    public void ThenTheCacheHitsCounterShouldBe(int expectedHits)
    {
        var sut = GetOrCreateCacheService();
        sut.CacheHits.Should().Be(expectedHits);
    }

    [Then(@"the CacheMisses counter should be (\d+)")]
    public void ThenTheCacheMissesCounterShouldBe(int expectedMisses)
    {
        var sut = GetOrCreateCacheService();
        sut.CacheMisses.Should().Be(expectedMisses);
    }

    [Then(@"the CacheMisses counter should be at least (\d+)")]
    public void ThenTheCacheMissesCounterShouldBeAtLeast(int minMisses)
    {
        var sut = GetOrCreateCacheService();
        sut.CacheMisses.Should().BeGreaterThanOrEqualTo(minMisses);
    }

    #endregion

    #region Then Steps — Direct Redis Assertions

    [Then(@"the Redis feed_meta Hash for ""(.*)"" should have (\d+) fields?")]
    public async Task ThenTheRedisFeedMetaHashForUserShouldHaveFields(string userName, int expectedFields)
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();
        var userId = GetUserId(userName);
        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(userId)}";

        var entries = await redisDb.HashGetAllAsync(hashKey);
        entries.Length.Should().Be(expectedFields,
            $"Redis Hash for {userName} should have {expectedFields} fields");
    }

    [Then(@"each field JSON should contain keys title, type, lastBlockIndex, participants, createdAtBlock")]
    public async Task ThenEachFieldJsonShouldContainExpectedKeys()
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();
        // Get Alice's hash (used in Background)
        var userId = GetUserId("Alice");
        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(userId)}";

        var entries = await redisDb.HashGetAllAsync(hashKey);
        entries.Should().NotBeEmpty("Hash should have entries");

        foreach (var entry in entries)
        {
            var json = entry.Value.ToString();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            root.TryGetProperty("title", out _).Should().BeTrue($"JSON should have 'title' key: {json}");
            root.TryGetProperty("type", out _).Should().BeTrue($"JSON should have 'type' key: {json}");
            root.TryGetProperty("lastBlockIndex", out _).Should().BeTrue($"JSON should have 'lastBlockIndex' key: {json}");
            root.TryGetProperty("participants", out _).Should().BeTrue($"JSON should have 'participants' key: {json}");
            root.TryGetProperty("createdAtBlock", out _).Should().BeTrue($"JSON should have 'createdAtBlock' key: {json}");
        }
    }

    [Then(@"the Redis feed_meta Hash for ""(.*)"" should have a TTL between (\d+) and (\d+) hours")]
    public async Task ThenTheRedisFeedMetaHashShouldHaveTtlBetween(string userName, int minHours, int maxHours)
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();
        var userId = GetUserId(userName);
        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(userId)}";

        var ttl = await redisDb.KeyTimeToLiveAsync(hashKey);
        ttl.Should().NotBeNull("Hash should have a TTL set");
        ttl!.Value.TotalHours.Should().BeInRange(minHours, maxHours,
            $"TTL should be between {minHours} and {maxHours} hours");
    }

    #endregion

    #region Helper Methods

    private FeedMetadataCacheService GetOrCreateCacheService()
    {
        if (_cacheService != null)
            return _cacheService;

        var fixture = GetFixture();
        _cacheService = new FeedMetadataCacheService(
            fixture.RedisConnection,
            KeyPrefix,
            NullLogger<FeedMetadataCacheService>.Instance);

        return _cacheService;
    }

    private static string GetUserId(string userName)
    {
        return userName.ToLowerInvariant() switch
        {
            "alice" => "0xalice_test",
            "bob" => "0xbob_test",
            "charlie" => "0xcharlie_test",
            "nonexistent" => "0xnonexistent",
            _ => $"0x{userName.ToLowerInvariant()}"
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
