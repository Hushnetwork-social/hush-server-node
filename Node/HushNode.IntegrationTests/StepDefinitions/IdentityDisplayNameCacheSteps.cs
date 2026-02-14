using FluentAssertions;
using HushNode.Caching;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-065 E2: Step definitions for identity display name cache integration tests.
/// Tests IdentityDisplayNameCacheService against real Redis via TestContainers.
/// </summary>
[Binding]
public sealed class IdentityDisplayNameCacheSteps
{
    private const string KeyPrefix = "HushTest:";
    private readonly ScenarioContext _scenarioContext;
    private IdentityDisplayNameCacheService? _cacheService;
    private IReadOnlyDictionary<string, string?>? _lastResult;
    private IReadOnlyDictionary<string, string?>? _secondResult;

    public IdentityDisplayNameCacheSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    #region Given Steps

    [Given(@"display names are cached for ""(.*)"" as ""(.*)"" and ""(.*)"" as ""(.*)""")]
    public async Task GivenDisplayNamesAreCachedForTwoAddresses(
        string addr1, string name1, string addr2, string name2)
    {
        var sut = GetOrCreateCacheService();
        var names = new Dictionary<string, string>
        {
            { addr1, name1 },
            { addr2, name2 }
        };
        var result = await sut.SetMultipleDisplayNamesAsync(names);
        result.Should().BeTrue("SetMultipleDisplayNamesAsync should succeed with real Redis");
    }

    [Given(@"a single display name is cached for ""(.*)"" as ""(.*)""")]
    public async Task GivenDisplayNameIsCachedForSingleAddress(string address, string displayName)
    {
        var sut = GetOrCreateCacheService();
        var result = await sut.SetDisplayNameAsync(address, displayName);
        result.Should().BeTrue("SetDisplayNameAsync should succeed with real Redis");
    }

    #endregion

    #region When Steps

    [When(@"GetDisplayNamesAsync is called for addresses ""(.*)"" and ""(.*)""")]
    public async Task WhenGetDisplayNamesAsyncIsCalledForAddresses(string addr1, string addr2)
    {
        var sut = GetOrCreateCacheService();
        _lastResult = await sut.GetDisplayNamesAsync(new[] { addr1, addr2 });
    }

    [When(@"display names are written for ""(.*)"" as ""(.*)"" and ""(.*)"" as ""(.*)""")]
    public async Task WhenDisplayNamesAreWrittenForTwoAddresses(
        string addr1, string name1, string addr2, string name2)
    {
        var sut = GetOrCreateCacheService();
        var names = new Dictionary<string, string>
        {
            { addr1, name1 },
            { addr2, name2 }
        };
        var result = await sut.SetMultipleDisplayNamesAsync(names);
        result.Should().BeTrue("SetMultipleDisplayNamesAsync should succeed");
    }

    [When(@"GetDisplayNamesAsync is called again for addresses ""(.*)"" and ""(.*)""")]
    public async Task WhenGetDisplayNamesAsyncIsCalledAgainForAddresses(string addr1, string addr2)
    {
        // Create a fresh service instance to get clean counters
        var fixture = GetFixture();
        var freshService = new IdentityDisplayNameCacheService(
            fixture.RedisConnection,
            KeyPrefix,
            NullLogger<IdentityDisplayNameCacheService>.Instance);

        _secondResult = await freshService.GetDisplayNamesAsync(new[] { addr1, addr2 });
    }

    #endregion

    #region Then Steps — Result Assertions

    [Then(@"the result should contain ""(.*)"" for ""(.*)"" and ""(.*)"" for ""(.*)""")]
    public void ThenTheResultShouldContainValuesForAddresses(
        string name1, string addr1, string name2, string addr2)
    {
        _lastResult.Should().NotBeNull("GetDisplayNamesAsync should return a non-null result");
        AssertDisplayNameValue(_lastResult!, addr1, name1);
        AssertDisplayNameValue(_lastResult!, addr2, name2);
    }

    [Then(@"the result should contain ""(.*)"" for ""(.*)"" and null for ""(.*)""")]
    public void ThenTheResultShouldContainValueAndNullForAddresses(
        string name1, string addr1, string addrNull)
    {
        _lastResult.Should().NotBeNull();
        _lastResult![addr1].Should().Be(name1, $"Address {addr1} should have name '{name1}'");
        _lastResult[addrNull].Should().BeNull($"Address {addrNull} should be null (cache miss)");
    }

    [Then(@"the result should contain null for ""(.*)"" and null for ""(.*)""")]
    public void ThenTheResultShouldContainNullForBothAddresses(string addr1, string addr2)
    {
        _lastResult.Should().NotBeNull();
        _lastResult![addr1].Should().BeNull($"Address {addr1} should be null");
        _lastResult[addr2].Should().BeNull($"Address {addr2} should be null");
    }

    [Then(@"the second result should contain ""(.*)"" for ""(.*)"" and ""(.*)"" for ""(.*)""")]
    public void ThenTheSecondResultShouldContainValuesForAddresses(
        string name1, string addr1, string name2, string addr2)
    {
        _secondResult.Should().NotBeNull("Second GetDisplayNamesAsync should return non-null");
        _secondResult![addr1].Should().Be(name1);
        _secondResult[addr2].Should().Be(name2);
    }

    [Then(@"the identity CacheHits counter should be (\d+)")]
    public void ThenTheIdentityCacheHitsCounterShouldBe(int expectedHits)
    {
        var sut = GetOrCreateCacheService();
        sut.CacheHits.Should().Be(expectedHits);
    }

    [Then(@"the identity CacheMisses counter should be (\d+)")]
    public void ThenTheIdentityCacheMissesCounterShouldBe(int expectedMisses)
    {
        var sut = GetOrCreateCacheService();
        sut.CacheMisses.Should().Be(expectedMisses);
    }

    #endregion

    #region Then Steps — Direct Redis Assertions

    [Then(@"the Redis identities:display_names Hash should contain ""(.*)"" for field ""(.*)""")]
    public async Task ThenTheRedisHashShouldContainValueForField(string expectedName, string field)
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();
        var hashKey = $"{KeyPrefix}{IdentityDisplayNameCacheConstants.DisplayNamesHashKey}";

        var value = await redisDb.HashGetAsync(hashKey, field);
        value.IsNullOrEmpty.Should().BeFalse($"Hash field '{field}' should exist");
        value.ToString().Should().Be(expectedName,
            $"Hash field '{field}' should contain '{expectedName}'");
    }

    [Then(@"the Redis identities:display_names Hash should have no TTL")]
    public async Task ThenTheRedisHashShouldHaveNoTtl()
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();
        var hashKey = $"{KeyPrefix}{IdentityDisplayNameCacheConstants.DisplayNamesHashKey}";

        var ttl = await redisDb.KeyTimeToLiveAsync(hashKey);
        ttl.Should().BeNull("Identity display names Hash should have no TTL (persistent)");
    }

    #endregion

    #region Helper Methods

    private IdentityDisplayNameCacheService GetOrCreateCacheService()
    {
        if (_cacheService != null)
            return _cacheService;

        var fixture = GetFixture();
        _cacheService = new IdentityDisplayNameCacheService(
            fixture.RedisConnection,
            KeyPrefix,
            NullLogger<IdentityDisplayNameCacheService>.Instance);

        return _cacheService;
    }

    private static void AssertDisplayNameValue(
        IReadOnlyDictionary<string, string?> result, string address, string expectedValue)
    {
        result.Should().ContainKey(address, $"Result should contain address '{address}'");
        if (expectedValue == "null")
            result[address].Should().BeNull($"Address '{address}' should be null");
        else
            result[address].Should().Be(expectedValue, $"Address '{address}' should be '{expectedValue}'");
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
