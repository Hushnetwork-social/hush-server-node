using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-052: Step definitions for Message Pagination scenarios.
/// Tests pagination parameters and response fields.
/// </summary>
[Binding]
public sealed class MessagePaginationSteps
{
    private readonly ScenarioContext _scenarioContext;

    public MessagePaginationSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    #region Given Steps - Message Population

    [Given(@"the ChatFeed contains (\d+) messages")]
    public async Task GivenTheChatFeedContainsMessages(int messageCount)
    {
        await PopulateChatFeedWithMessages(messageCount, 10);
    }

    [Given(@"the ChatFeed has no messages")]
    public void GivenTheChatFeedHasNoMessages()
    {
        // No messages to add - feed is empty by default
    }

    [Given(@"Alice records the current block height")]
    public async Task GivenAliceRecordsTheCurrentBlockHeight()
    {
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var response = await blockchainClient.GetBlockchainHeightAsync(new GetBlockchainHeightRequest());
        _scenarioContext["RecordedBlockHeight"] = response.Index;
    }

    [Given(@"another (\d+) messages are added to the ChatFeed")]
    public async Task GivenAnotherMessagesAreAddedToTheChatFeed(int messageCount)
    {
        await PopulateChatFeedWithMessages(messageCount, 0);
    }

    [Given(@"a Charlie-Bob ChatFeed exists with (\d+) messages")]
    public async Task GivenCharlieHasChatFeedWithBobContainingMessages(int messageCount)
    {
        // Create Charlie-Bob chat feed and populate with messages
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        // Create chat feed
        var charlieIdentity = TestIdentities.Charlie;
        var bobIdentity = TestIdentities.Bob;

        var (signedTxJson, feedId, aesKey) = TestTransactionFactory.CreateChatFeed(charlieIdentity, bobIdentity);
        var createResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTxJson
        });
        createResponse.Successfull.Should().BeTrue("Charlie-Bob chat feed should be created");

        await blockControl.ProduceBlockAsync();

        _scenarioContext["CharlieBobFeedId"] = feedId;
        _scenarioContext["CharlieBobAesKey"] = aesKey;

        // Populate with messages
        for (int i = 0; i < messageCount; i++)
        {
            var signedTx = TestTransactionFactory.CreateFeedMessage(
                charlieIdentity,
                feedId,
                $"Charlie message {i + 1}",
                aesKey);

            await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
            {
                SignedTransaction = signedTx
            });

            if ((i + 1) % 10 == 0)
            {
                await blockControl.ProduceBlockAsync();
            }
        }

        await blockControl.ProduceBlockAsync();
    }

    #endregion

    #region When Steps - Request Messages

    [When(@"Alice requests messages with limit (\d+) via gRPC")]
    public async Task WhenAliceRequestsMessagesWithLimitViaGrpc(int limit)
    {
        var identity = GetStoredIdentity("Alice");
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            Limit = limit,
            FetchLatest = false,
            LastReactionTallyVersion = 0
        });

        _scenarioContext["LastMessagesResponse"] = response;
    }

    [When(@"Alice requests messages with fetch_latest true via gRPC")]
    public async Task WhenAliceRequestsMessagesWithFetchLatestViaGrpc()
    {
        var identity = GetStoredIdentity("Alice");
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            FetchLatest = true,
            LastReactionTallyVersion = 0
        });

        _scenarioContext["LastMessagesResponse"] = response;
    }

    [When(@"Alice requests messages with fetch_latest true and limit (\d+) via gRPC")]
    public async Task WhenAliceRequestsMessagesWithFetchLatestAndLimitViaGrpc(int limit)
    {
        var identity = GetStoredIdentity("Alice");
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            Limit = limit,
            FetchLatest = true,
            LastReactionTallyVersion = 0
        });

        _scenarioContext["LastMessagesResponse"] = response;
    }

    [When(@"Alice requests messages since the recorded block via gRPC")]
    public async Task WhenAliceRequestsMessagesSinceTheRecordedBlockViaGrpc()
    {
        var identity = GetStoredIdentity("Alice");
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var recordedBlockHeight = (long)_scenarioContext["RecordedBlockHeight"];

        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = recordedBlockHeight + 1, // Messages after the recorded block
            FetchLatest = false,
            LastReactionTallyVersion = 0
        });

        _scenarioContext["LastMessagesResponse"] = response;
    }

    [When(@"Alice requests older messages before the recorded oldest_block_index via gRPC")]
    public async Task WhenAliceRequestsOlderMessagesBeforeTheRecordedOldestBlockIndexViaGrpc()
    {
        var identity = GetStoredIdentity("Alice");
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var recordedOldestBlockIndex = (long)_scenarioContext["RecordedOldestBlockIndex"];

        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            FetchLatest = false,
            Limit = 100,
            BeforeBlockIndex = recordedOldestBlockIndex, // FEAT-052: backward pagination
            LastReactionTallyVersion = 0
        });

        _scenarioContext["LastMessagesResponse"] = response;
    }

    #endregion

    #region Then Steps - Assertions

    [Then(@"the response should contain exactly (\d+) messages")]
    public void ThenTheResponseShouldContainExactlyMessages(int expectedCount)
    {
        var response = GetLastMessagesResponse();
        response.Messages.Should().HaveCount(expectedCount,
            $"Response should contain exactly {expectedCount} messages");
    }

    [Then(@"the response should contain at most (\d+) messages")]
    public void ThenTheResponseShouldContainAtMostMessages(int maxCount)
    {
        var response = GetLastMessagesResponse();
        response.Messages.Count.Should().BeLessOrEqualTo(maxCount,
            $"Response should contain at most {maxCount} messages");
    }

    [Then(@"the response should contain at least (\d+) messages")]
    public void ThenTheResponseShouldContainAtLeastMessages(int minCount)
    {
        var response = GetLastMessagesResponse();
        response.Messages.Count.Should().BeGreaterOrEqualTo(minCount,
            $"Response should contain at least {minCount} messages");
    }

    [Then(@"the response has_more_messages should be (true|false)")]
    public void ThenTheResponseHasMoreMessagesShouldBe(string expectedValue)
    {
        var response = GetLastMessagesResponse();
        var expected = bool.Parse(expectedValue);
        response.HasMoreMessages.Should().Be(expected,
            $"HasMoreMessages should be {expectedValue}");
    }

    [Then(@"the messages should be ordered by block_index ascending")]
    public void ThenTheMessagesShouldBeOrderedByBlockIndexAscending()
    {
        var response = GetLastMessagesResponse();
        var blockIndexes = response.Messages.Select(m => m.BlockIndex).ToList();

        blockIndexes.Should().BeInAscendingOrder(
            "Messages should be ordered by block_index ascending");
    }



    [Then(@"all messages should belong to the ChatFeed with Bob")]
    public void ThenAllMessagesShouldBelongToTheChatFeedWithBob()
    {
        var response = GetLastMessagesResponse();
        var aliceBobFeedId = GetChatFeedId();

        // Check that no messages are from Charlie-Bob feed (if it exists)
        if (_scenarioContext.TryGetValue("CharlieBobFeedId", out var charlieBobFeedObj)
            && charlieBobFeedObj is FeedId charlieBobFeedId)
        {
            response.Messages.Should().NotContain(m => m.FeedId == charlieBobFeedId.ToString(),
                "No messages should come from Charlie-Bob feed that Alice is not part of");
        }

        // Verify the chat feed messages are present (at least some from Alice-Bob feed)
        response.Messages.Should().Contain(m => m.FeedId == aliceBobFeedId.ToString(),
            "Messages from Alice-Bob chat feed should be present");
    }

    [Then(@"Alice records the oldest_block_index from the response")]
    public void ThenAliceRecordsTheOldestBlockIndexFromTheResponse()
    {
        var response = GetLastMessagesResponse();
        _scenarioContext["RecordedOldestBlockIndex"] = response.OldestBlockIndex;
    }

    #endregion

    #region Helper Methods

    private async Task PopulateChatFeedWithMessages(int messageCount, int startBlock)
    {
        var feedId = GetChatFeedId();
        var identity = GetStoredIdentity("Alice");
        var aesKey = GetChatFeedAesKey();
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        // Send messages to populate feed
        for (int i = 0; i < messageCount; i++)
        {
            var signedTx = TestTransactionFactory.CreateFeedMessage(
                identity,
                feedId,
                $"Message {i + 1}",
                aesKey);

            var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
            {
                SignedTransaction = signedTx
            });
            response.Successfull.Should().BeTrue($"Message {i + 1} should be submitted successfully");

            // Produce blocks in batches for efficiency
            if ((i + 1) % 10 == 0)
            {
                await blockControl.ProduceBlockAsync();
            }
        }

        // Final block to commit any remaining messages
        await blockControl.ProduceBlockAsync();
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

    private string GetChatFeedAesKey()
    {
        foreach (var key in _scenarioContext.Keys)
        {
            if (key.StartsWith("ChatFeedAesKey_"))
            {
                return (string)_scenarioContext[key];
            }
        }
        throw new InvalidOperationException("No chat feed AES key found in context");
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

    private BlockProductionControl GetBlockControl()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.BlockControlKey, out var controlObj)
            && controlObj is BlockProductionControl blockControl)
        {
            return blockControl;
        }
        throw new InvalidOperationException("BlockProductionControl not found in ScenarioContext.");
    }

    private GetFeedMessagesForAddressReply GetLastMessagesResponse()
    {
        if (_scenarioContext.TryGetValue("LastMessagesResponse", out var responseObj)
            && responseObj is GetFeedMessagesForAddressReply response)
        {
            return response;
        }
        throw new InvalidOperationException("LastMessagesResponse not found in ScenarioContext.");
    }

    #endregion
}
