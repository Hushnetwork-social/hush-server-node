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
/// FEAT-065 Phase 5: Step definitions for feed metadata edge case integration tests.
/// Verifies graceful degradation, cache repopulation, and warm-cache behavior.
/// </summary>
[Binding]
public sealed class FeedMetadataEdgeCasesSteps
{
    private const string KeyPrefix = "HushTest:";
    private readonly ScenarioContext _scenarioContext;

    public FeedMetadataEdgeCasesSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    #region Then Steps

    [Then(@"Alice's feed_meta Hash should have more than (\d+) entries")]
    public async Task ThenAliceFeedMetaHashShouldHaveMoreThanEntries(int minEntries)
    {
        var alice = TestIdentities.Alice;
        var redisDb = GetRedisDatabase();
        var hashKey = $"{KeyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(alice.PublicSigningAddress)}";

        var entries = await redisDb.HashGetAllAsync(hashKey);
        entries.Length.Should().BeGreaterThan(minEntries,
            $"Alice's feed_meta Hash should have more than {minEntries} entries");
    }

    #endregion

    #region When Steps — Full Sync

    [When(@"Alice performs a full sync via GetFeedMessagesForAddress")]
    public async Task WhenAlicePerformsFullSyncViaGetFeedMessagesForAddress()
    {
        var alice = TestIdentities.Alice;
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesForAddressAsync(
            new GetFeedMessagesForAddressRequest
            {
                ProfilePublicKey = alice.PublicSigningAddress,
                BlockIndex = 0
            });

        _scenarioContext["LastMessagesResponse"] = response;
    }

    #endregion

    #region Then Steps — Response Assertions

    [Then(@"the response should contain messages with resolved display names")]
    public void ThenResponseShouldContainMessagesWithResolvedDisplayNames()
    {
        var response = (GetFeedMessagesForAddressReply)_scenarioContext["LastMessagesResponse"];

        response.Messages.Should().NotBeEmpty("Response should contain messages");

        foreach (var message in response.Messages)
        {
            message.IssuerName.Should().NotBeNullOrEmpty(
                $"Message {message.FeedMessageId} should have a resolved display name");
            // Display name should not be a raw address (should be resolved)
            message.IssuerName.Should().NotStartWith("04",
                "Display name should be resolved, not a raw public key");
        }
    }

    #endregion

    #region Helper Methods

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
