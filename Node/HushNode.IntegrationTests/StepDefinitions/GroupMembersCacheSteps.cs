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
/// Step definitions for Group Members Cache scenarios.
/// Tests cache-aside pattern for group members with display names.
/// </summary>
[Binding]
public sealed class GroupMembersCacheSteps
{
    private readonly ScenarioContext _scenarioContext;

    public GroupMembersCacheSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"the Redis group members cache for ""(.*)"" is empty")]
    public async Task GivenTheRedisGroupMembersCacheIsEmpty(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        await ClearGroupMembersCache(feedId);
    }

    [Given(@"the group members for ""(.*)"" have been cached")]
    public async Task GivenTheGroupMembersHaveBeenCached(string groupName)
    {
        await WhenTheGroupMembersAreLookedUpViaGrpc(groupName);
    }

    [When(@"the group members for ""(.*)"" are looked up via gRPC")]
    public async Task WhenTheGroupMembersAreLookedUpViaGrpc(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetGroupMembersAsync(new GetGroupMembersRequest
        {
            FeedId = feedId.ToString()
        });

        _scenarioContext[$"LastGroupMembersResponse_{groupName}"] = response;
        _scenarioContext["LastGroupFeedName"] = groupName;
    }

    [When(@"the group members for ""(.*)"" are looked up via gRPC again")]
    public async Task WhenTheGroupMembersAreLookedUpViaGrpcAgain(string groupName)
    {
        await WhenTheGroupMembersAreLookedUpViaGrpc(groupName);
    }

    [Then(@"the group members should be in the Redis cache for ""(.*)""")]
    public async Task ThenTheGroupMembersShouldBeInTheRedisCacheFor(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:group:{feedId.Value}:members";

        // Allow some time for async cache population
        var maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var exists = await redisDb.KeyExistsAsync(cacheKey);
            if (exists) return;
            await Task.Delay(100);
        }

        var finalExists = await redisDb.KeyExistsAsync(cacheKey);
        finalExists.Should().BeTrue($"Group members should be in Redis cache for {groupName}");
    }

    [Then(@"the cached members should include display names")]
    public async Task ThenTheCachedMembersShouldIncludeDisplayNames()
    {
        var groupName = (string)_scenarioContext["LastGroupFeedName"];
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:group:{feedId.Value}:members";
        var cachedValue = await redisDb.StringGetAsync(cacheKey);

        cachedValue.IsNullOrEmpty.Should().BeFalse("Cache should contain members JSON");
        cachedValue.ToString().Should().Contain("DisplayName",
            "Cached members should include DisplayName field");
    }

    [Then(@"the response should contain group members with display names")]
    public void ThenTheResponseShouldContainGroupMembersWithDisplayNames()
    {
        var groupName = (string)_scenarioContext["LastGroupFeedName"];
        var response = (GetGroupMembersResponse)_scenarioContext[$"LastGroupMembersResponse_{groupName}"];

        response.Members.Should().NotBeEmpty("Response should contain group members");

        foreach (var member in response.Members)
        {
            member.DisplayName.Should().NotBeNullOrEmpty(
                $"Member {member.PublicAddress.Substring(0, 10)}... should have a display name");
        }
    }

    #region Helper Methods

    private async Task ClearGroupMembersCache(FeedId feedId)
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:group:{feedId.Value}:members";
        await redisDb.KeyDeleteAsync(cacheKey);

        var exists = await redisDb.KeyExistsAsync(cacheKey);
        exists.Should().BeFalse("Group members cache should be empty after deletion");
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
