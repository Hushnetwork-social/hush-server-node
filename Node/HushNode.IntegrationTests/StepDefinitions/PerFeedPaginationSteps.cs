using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-059: Step definitions for Per-Feed Pagination (GetFeedMessagesById) scenarios.
/// Tests the new per-feed pagination API for scroll-based prefetch.
/// </summary>
[Binding]
public sealed class PerFeedPaginationSteps
{
    private readonly ScenarioContext _scenarioContext;

    public PerFeedPaginationSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    #region When Steps - GetFeedMessagesById Requests

    [When(@"Alice calls GetFeedMessagesById for the ChatFeed via gRPC")]
    public async Task WhenAliceCallsGetFeedMessagesByIdForTheChatFeedViaGrpc()
    {
        var identity = GetStoredIdentity("Alice");
        var feedId = GetChatFeedId();
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesByIdAsync(new GetFeedMessagesByIdRequest
        {
            FeedId = feedId.ToString(),
            UserAddress = identity.PublicSigningAddress
        });

        _scenarioContext["LastPerFeedResponse"] = response;
    }

    [When(@"Alice calls GetFeedMessagesById with limit (\d+) via gRPC")]
    public async Task WhenAliceCallsGetFeedMessagesByIdWithLimitViaGrpc(int limit)
    {
        var identity = GetStoredIdentity("Alice");
        var feedId = GetChatFeedId();
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesByIdAsync(new GetFeedMessagesByIdRequest
        {
            FeedId = feedId.ToString(),
            UserAddress = identity.PublicSigningAddress,
            Limit = limit
        });

        _scenarioContext["LastPerFeedResponse"] = response;
    }

    [When(@"Alice calls GetFeedMessagesById with beforeBlockIndex via gRPC")]
    public async Task WhenAliceCallsGetFeedMessagesByIdWithBeforeBlockIndexViaGrpc()
    {
        var identity = GetStoredIdentity("Alice");
        var feedId = GetChatFeedId();
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var recordedOldestBlockIndex = (long)_scenarioContext["PerFeedRecordedOldestBlockIndex"];

        var response = await feedClient.GetFeedMessagesByIdAsync(new GetFeedMessagesByIdRequest
        {
            FeedId = feedId.ToString(),
            UserAddress = identity.PublicSigningAddress,
            BeforeBlockIndex = recordedOldestBlockIndex
        });

        _scenarioContext["LastPerFeedResponse"] = response;
    }

    [When(@"Charlie calls GetFeedMessagesById for the Alice-Bob ChatFeed via gRPC")]
    public async Task WhenCharlieCallsGetFeedMessagesByIdForTheAliceBobChatFeedViaGrpc()
    {
        var identity = GetStoredIdentity("Charlie");
        var feedId = GetChatFeedId(); // Alice-Bob chat feed
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesByIdAsync(new GetFeedMessagesByIdRequest
        {
            FeedId = feedId.ToString(),
            UserAddress = identity.PublicSigningAddress
        });

        _scenarioContext["LastPerFeedResponse"] = response;
    }

    [When(@"Alice calls GetFeedMessagesById for a non-existent feed via gRPC")]
    public async Task WhenAliceCallsGetFeedMessagesByIdForANonExistentFeedViaGrpc()
    {
        var identity = GetStoredIdentity("Alice");
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        // Use a valid GUID format that doesn't exist in the database
        var nonExistentFeedId = "00000000-0000-0000-0000-000000000000";

        var response = await feedClient.GetFeedMessagesByIdAsync(new GetFeedMessagesByIdRequest
        {
            FeedId = nonExistentFeedId,
            UserAddress = identity.PublicSigningAddress
        });

        _scenarioContext["LastPerFeedResponse"] = response;
    }

    #endregion

    #region Then Steps - Assertions

    [Then(@"the per-feed response should contain exactly (\d+) messages")]
    public void ThenThePerFeedResponseShouldContainExactlyMessages(int expectedCount)
    {
        var response = GetLastPerFeedResponse();
        response.Messages.Should().HaveCount(expectedCount,
            $"Per-feed response should contain exactly {expectedCount} messages");
    }

    [Then(@"the per-feed response has_more_messages should be (true|false)")]
    public void ThenThePerFeedResponseHasMoreMessagesShouldBe(string expectedValue)
    {
        var response = GetLastPerFeedResponse();
        var expected = bool.Parse(expectedValue);
        response.HasMoreMessages.Should().Be(expected,
            $"HasMoreMessages should be {expectedValue}");
    }

    [Then(@"all per-feed messages should belong to the ChatFeed")]
    public void ThenAllPerFeedMessagesShouldBelongToTheChatFeed()
    {
        var response = GetLastPerFeedResponse();
        var feedId = GetChatFeedId();

        foreach (var message in response.Messages)
        {
            message.FeedId.Should().Be(feedId.ToString(),
                "All messages should belong to the requested ChatFeed");
        }
    }

    [Then(@"Alice records the per-feed oldest_block_index from the response")]
    public void ThenAliceRecordsThePerFeedOldestBlockIndexFromTheResponse()
    {
        var response = GetLastPerFeedResponse();
        _scenarioContext["PerFeedRecordedOldestBlockIndex"] = response.OldestBlockIndex;
    }

    [Then(@"the per-feed response oldest_block_index should be less than newest_block_index")]
    public void ThenThePerFeedResponseOldestBlockIndexShouldBeLessThanNewestBlockIndex()
    {
        var response = GetLastPerFeedResponse();
        if (response.Messages.Count > 1)
        {
            response.OldestBlockIndex.Should().BeLessThanOrEqualTo(response.NewestBlockIndex,
                "OldestBlockIndex should be less than or equal to NewestBlockIndex");
        }
    }

    [Then(@"the per-feed response newest_block_index should be the block of the newest message")]
    public void ThenThePerFeedResponseNewestBlockIndexShouldBeTheBlockOfTheNewestMessage()
    {
        var response = GetLastPerFeedResponse();
        if (response.Messages.Count > 0)
        {
            var newestMessageBlockIndex = response.Messages.Max(m => m.BlockIndex);
            response.NewestBlockIndex.Should().Be(newestMessageBlockIndex,
                "NewestBlockIndex should match the block index of the newest message");
        }
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

    private TestIdentity GetStoredIdentity(string userName)
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

    private GetFeedMessagesByIdReply GetLastPerFeedResponse()
    {
        if (_scenarioContext.TryGetValue("LastPerFeedResponse", out var responseObj)
            && responseObj is GetFeedMessagesByIdReply response)
        {
            return response;
        }
        throw new InvalidOperationException("LastPerFeedResponse not found in ScenarioContext.");
    }

    #endregion
}
