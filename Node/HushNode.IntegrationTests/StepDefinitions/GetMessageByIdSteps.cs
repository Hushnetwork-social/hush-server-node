using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-052: Step definitions for GetMessageById scenarios.
/// Tests fetching individual messages by ID.
/// </summary>
[Binding]
public sealed class GetMessageByIdSteps
{
    private readonly ScenarioContext _scenarioContext;

    public GetMessageByIdSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    #region Given Steps - Message Creation

    [Given(@"the ChatFeed contains message ""(.*)"" with content ""(.*)"" at block (\d+)")]
    public async Task GivenTheChatFeedContainsMessageWithContentAtBlock(string messageAlias, string content, int blockIndex)
    {
        var feedId = GetChatFeedId();
        var identity = GetStoredIdentity("Alice");
        var aesKey = GetChatFeedAesKey();
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var signedTx = TestTransactionFactory.CreateFeedMessage(identity, feedId, content, aesKey);
        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTx
        });
        response.Successfull.Should().BeTrue("Message should be submitted successfully");

        await blockControl.ProduceBlockAsync();

        // Get the most recent message (the one we just created)
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var messagesResponse = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            FetchLatest = true,
            Limit = 1,
            LastReactionTallyVersion = 0
        });

        // Get the most recent message from the target feed
        var message = messagesResponse.Messages.FirstOrDefault(m => m.FeedId == feedId.ToString());
        message.Should().NotBeNull($"Message in feed {feedId} should exist");

        _scenarioContext[$"MessageId_{messageAlias}"] = message!.FeedMessageId;
        _scenarioContext[$"MessageBlockIndex_{messageAlias}"] = message.BlockIndex;
        _scenarioContext[$"MessageContent_{messageAlias}"] = content; // Store original plaintext for assertions
    }

    [Given(@"the ChatFeed contains message ""(.*)"" sent by Bob with content ""(.*)""")]
    public async Task GivenTheChatFeedContainsMessageSentByBobWithContent(string messageAlias, string content)
    {
        var feedId = GetChatFeedId();
        var bobIdentity = TestIdentities.Bob;
        var aesKey = GetChatFeedAesKey();
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var signedTx = TestTransactionFactory.CreateFeedMessage(bobIdentity, feedId, content, aesKey);
        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTx
        });
        response.Successfull.Should().BeTrue("Message from Bob should be submitted successfully");

        await blockControl.ProduceBlockAsync();

        // Get the most recent message from Bob in this feed
        var aliceIdentity = GetStoredIdentity("Alice");
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var messagesResponse = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = aliceIdentity.PublicSigningAddress,
            BlockIndex = 0,
            FetchLatest = true,
            Limit = 10,
            LastReactionTallyVersion = 0
        });

        // Find the message by sender (Bob) and feed ID
        var message = messagesResponse.Messages
            .Where(m => m.FeedId == feedId.ToString())
            .FirstOrDefault(m => m.IssuerPublicAddress == bobIdentity.PublicSigningAddress);
        message.Should().NotBeNull($"Message from Bob in feed {feedId} should exist");

        _scenarioContext[$"MessageId_{messageAlias}"] = message!.FeedMessageId;
        _scenarioContext[$"MessageSenderKey_{messageAlias}"] = bobIdentity.PublicSigningAddress;
    }

    [Given(@"a Charlie-Bob ChatFeed exists with message ""(.*)""")]
    public async Task GivenCharlieHasChatFeedWithBobContainingMessage(string messageAlias)
    {
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var charlieIdentity = TestIdentities.Charlie;
        var bobIdentity = TestIdentities.Bob;

        // Create chat feed between Charlie and Bob
        var (signedTxJson, feedId, aesKey) = TestTransactionFactory.CreateChatFeed(charlieIdentity, bobIdentity);
        var createResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTxJson
        });
        createResponse.Successfull.Should().BeTrue("Charlie-Bob chat feed should be created");

        await blockControl.ProduceBlockAsync();

        _scenarioContext["CharlieBobFeedId"] = feedId;
        _scenarioContext["CharlieBobAesKey"] = aesKey;

        // Send a message
        var signedTx = TestTransactionFactory.CreateFeedMessage(
            charlieIdentity,
            feedId,
            "Secret message from Charlie",
            aesKey);

        await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTx
        });

        await blockControl.ProduceBlockAsync();

        // Get the message ID
        var messagesResponse = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = charlieIdentity.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        var message = messagesResponse.Messages.FirstOrDefault();
        message.Should().NotBeNull("Charlie's message should exist");

        _scenarioContext[$"MessageId_{messageAlias}"] = message!.FeedMessageId;
    }

    #endregion

    #region When Steps - GetMessageById

    [When(@"Alice requests message by ID ""(.*)"" via gRPC")]
    public async Task WhenAliceRequestsMessageByIdViaGrpc(string messageAlias)
    {
        var feedId = GetChatFeedId();
        var messageId = GetStoredMessageId(messageAlias);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetMessageByIdAsync(new GetMessageByIdRequest
        {
            FeedId = feedId.ToString(),
            MessageId = messageId
        });

        _scenarioContext["LastGetMessageByIdResponse"] = response;
    }

    [When(@"Alice requests message by ID ""(.*)"" from Charlie-Bob feed via gRPC")]
    public async Task WhenAliceRequestsMessageByIdFromCharlieBobFeedViaGrpc(string messageAlias)
    {
        var feedId = (FeedId)_scenarioContext["CharlieBobFeedId"];
        var messageId = GetStoredMessageId(messageAlias);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetMessageByIdAsync(new GetMessageByIdRequest
        {
            FeedId = feedId.ToString(),
            MessageId = messageId
        });

        _scenarioContext["LastGetMessageByIdResponse"] = response;
    }

    #endregion

    #region Then Steps - Assertions

    [Then(@"the GetMessageById response should be successful")]
    public void ThenTheGetMessageByIdResponseShouldBeSuccessful()
    {
        var response = GetLastGetMessageByIdResponse();
        response.Success.Should().BeTrue("GetMessageById should succeed");
    }

    [Then(@"the GetMessageById response should indicate failure")]
    public void ThenTheGetMessageByIdResponseShouldIndicateFailure()
    {
        var response = GetLastGetMessageByIdResponse();
        response.Success.Should().BeFalse("GetMessageById should fail");
    }

    [Then(@"the GetMessageById response should contain message content ""(.*)""")]
    public void ThenTheGetMessageByIdResponseShouldContainMessageContent(string expectedContent)
    {
        var response = GetLastGetMessageByIdResponse();
        response.Message.Should().NotBeNull("Response should contain a message");
        response.Message.MessageContent.Should().Be(expectedContent,
            $"Message content should be '{expectedContent}'");
    }

    [Then(@"the GetMessageById response should include block_index (\d+)")]
    public void ThenTheGetMessageByIdResponseShouldIncludeBlockIndex(long expectedBlockIndex)
    {
        var response = GetLastGetMessageByIdResponse();
        response.Message.Should().NotBeNull("Response should contain a message");
        response.Message.BlockIndex.Should().Be(expectedBlockIndex,
            $"Block index should be {expectedBlockIndex}");
    }

    [Then(@"the GetMessageById response should include a valid block_index")]
    public void ThenTheGetMessageByIdResponseShouldIncludeAValidBlockIndex()
    {
        var response = GetLastGetMessageByIdResponse();
        response.Message.Should().NotBeNull("Response should contain a message");
        response.Message.BlockIndex.Should().BeGreaterThan(0,
            "Block index should be greater than 0");
    }

    [Then(@"the GetMessageById response should include the sender public key for Bob")]
    public void ThenTheGetMessageByIdResponseShouldIncludeTheSenderPublicKeyForBob()
    {
        var response = GetLastGetMessageByIdResponse();
        response.Message.Should().NotBeNull("Response should contain a message");
        response.Message.IssuerPublicAddress.Should().Be(TestIdentities.Bob.PublicSigningAddress,
            "Sender public key should be Bob's");
    }

    [Then(@"the GetMessageById error message should contain ""(.*)""")]
    public void ThenTheGetMessageByIdErrorMessageShouldContain(string expectedText)
    {
        var response = GetLastGetMessageByIdResponse();
        response.Error.Should().Contain(expectedText,
            $"Error message should contain '{expectedText}'");
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

    private string GetStoredMessageId(string messageAlias)
    {
        var contextKey = $"MessageId_{messageAlias}";
        if (_scenarioContext.TryGetValue(contextKey, out var messageIdObj) && messageIdObj is string messageId)
        {
            return messageId;
        }

        // If not stored, assume it's meant to be a non-existent ID
        return Guid.NewGuid().ToString();
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

    private GetMessageByIdResponse GetLastGetMessageByIdResponse()
    {
        if (_scenarioContext.TryGetValue("LastGetMessageByIdResponse", out var responseObj)
            && responseObj is GetMessageByIdResponse response)
        {
            return response;
        }
        throw new InvalidOperationException("LastGetMessageByIdResponse not found in ScenarioContext.");
    }

    #endregion
}
