using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using Olimpo;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// Step definitions for Chat Feed creation and messaging scenarios.
/// </summary>
[Binding]
public sealed class ChatFeedSteps
{
    private readonly ScenarioContext _scenarioContext;

    public ChatFeedSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"user ""(.*)"" is registered with a personal feed")]
    public async Task GivenUserIsRegisteredWithAPersonalFeed(string userName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();

        // Check if already registered
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var hasPersonalFeed = await feedClient.HasPersonalFeedAsync(new HasPersonalFeedRequest
        {
            PublicPublicKey = identity.PublicSigningAddress
        });

        if (!hasPersonalFeed.FeedAvailable)
        {
            var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

            // Step 1: Register the identity
            var identityTxJson = TestTransactionFactory.CreateIdentityRegistration(identity);
            var identityResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
            {
                SignedTransaction = identityTxJson
            });
            identityResponse.Successfull.Should().BeTrue($"Identity registration for {userName} should succeed");

            // Step 2: Produce block to commit identity first
            await blockControl.ProduceBlockAsync();

            // Step 3: Create personal feed (in separate block to avoid serialization conflict)
            var personalFeedTxJson = TestTransactionFactory.CreatePersonalFeed(identity);
            var feedResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
            {
                SignedTransaction = personalFeedTxJson
            });
            feedResponse.Successfull.Should().BeTrue($"Personal feed creation for {userName} should succeed");

            // Step 4: Produce block to commit personal feed
            await blockControl.ProduceBlockAsync();
        }

        // Store identity in context for later steps
        _scenarioContext[$"Identity_{userName}"] = identity;
    }

    [When(@"(.*) requests a ChatFeed with (.*) via gRPC")]
    public async Task WhenUserRequestsChatFeedWithOtherViaGrpc(string initiatorName, string recipientName)
    {
        var initiator = GetTestIdentity(initiatorName);
        var recipient = GetTestIdentity(recipientName);
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        // Create and submit signed chat feed creation transaction
        var (signedTransactionJson, feedId, aesKey) = TestTransactionFactory.CreateChatFeed(initiator, recipient);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransactionJson
        });

        response.Successfull.Should().BeTrue($"Chat feed creation should succeed: {response.Message}");

        // Store feed info for later steps
        var chatKey = GetChatFeedKey(initiatorName, recipientName);
        _scenarioContext[$"ChatFeed_{chatKey}"] = feedId;
        _scenarioContext[$"ChatFeedAesKey_{chatKey}"] = aesKey;
    }

    [Given(@"(.*) has a ChatFeed with (.*)")]
    public async Task GivenUserHasChatFeedWithOther(string initiatorName, string recipientName)
    {
        var chatKey = GetChatFeedKey(initiatorName, recipientName);

        // Check if chat feed already exists in context
        if (!_scenarioContext.ContainsKey($"ChatFeed_{chatKey}"))
        {
            // Create the chat feed
            await WhenUserRequestsChatFeedWithOtherViaGrpc(initiatorName, recipientName);

            // Produce a block to commit
            var blockControl = GetBlockControl();
            await blockControl.ProduceBlockAsync();
        }
    }

    [Then(@"(.*) should have a ChatFeed with (.*)")]
    public async Task ThenUserShouldHaveChatFeedWithOther(string userName, string otherUserName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        // Get all feeds for the user
        var response = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0
        });

        // Find a chat feed that includes the other user
        var otherIdentity = GetTestIdentity(otherUserName);
        var chatFeeds = response.Feeds
            .Where(f => f.FeedType == 1) // FeedType.Chat = 1
            .Where(f => f.FeedParticipants.Any(p => p.ParticipantPublicAddress == otherIdentity.PublicSigningAddress))
            .ToList();

        chatFeeds.Should().NotBeEmpty($"{userName} should have a chat feed with {otherUserName}");
    }

    [When(@"(.*) sends message ""(.*)"" to the ChatFeed via gRPC")]
    public async Task WhenUserSendsMessageToChatFeedViaGrpc(string senderName, string message)
    {
        var sender = GetTestIdentity(senderName);
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        // Find the chat feed and its key
        var chatFeedKey = FindChatFeedKeyForUser(senderName);
        var feedId = (FeedId)_scenarioContext[$"ChatFeed_{chatFeedKey}"];
        var aesKey = (string)_scenarioContext[$"ChatFeedAesKey_{chatFeedKey}"];

        // Create and submit the message transaction
        var signedTransactionJson = TestTransactionFactory.CreateFeedMessage(sender, feedId, message, aesKey);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransactionJson
        });

        response.Successfull.Should().BeTrue($"Message submission should succeed: {response.Message}");

        // Store the message for verification
        _scenarioContext[$"LastMessage_{chatFeedKey}"] = message;
    }

    [Then(@"(.*) should see the message ""(.*)"" in the ChatFeed")]
    public async Task ThenUserShouldSeeMessageInChatFeed(string userName, string expectedMessage)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        // Get all messages for the user
        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0,
            LastReactionTallyVersion = 0
        });

        // Find the chat feed key and decrypt the messages
        var chatFeedKey = FindChatFeedKeyForUser(userName);
        var feedId = (FeedId)_scenarioContext[$"ChatFeed_{chatFeedKey}"];
        var aesKey = (string)_scenarioContext[$"ChatFeedAesKey_{chatFeedKey}"];

        // Find messages in our chat feed
        var chatMessages = response.Messages
            .Where(m => m.FeedId == feedId.ToString())
            .ToList();

        chatMessages.Should().NotBeEmpty($"{userName} should have messages in the ChatFeed");

        // Decrypt and check for expected message
        var decryptedMessages = chatMessages
            .Select(m => EncryptKeys.AesDecrypt(m.MessageContent, aesKey))
            .ToList();

        decryptedMessages.Should().Contain(expectedMessage,
            $"{userName} should see the message '{expectedMessage}' in the ChatFeed");
    }

    private TestIdentity GetTestIdentity(string userName)
    {
        // Check if already in context
        var contextKey = $"Identity_{userName}";
        if (_scenarioContext.TryGetValue(contextKey, out var identityObj)
            && identityObj is TestIdentity identity)
        {
            return identity;
        }

        // Map user name to predefined test identity
        return userName.ToLowerInvariant() switch
        {
            "alice" => TestIdentities.Alice,
            "bob" => TestIdentities.Bob,
            "charlie" => TestIdentities.Charlie,
            "blockproducer" => TestIdentities.BlockProducer,
            _ => throw new ArgumentException($"Unknown test user: {userName}")
        };
    }

    private static string GetChatFeedKey(string user1, string user2)
    {
        // Create a consistent key regardless of order
        var names = new[] { user1.ToLowerInvariant(), user2.ToLowerInvariant() };
        Array.Sort(names);
        return $"{names[0]}_{names[1]}";
    }

    private string FindChatFeedKeyForUser(string userName)
    {
        var lowerName = userName.ToLowerInvariant();

        // Search for any chat feed key containing this user
        foreach (var key in _scenarioContext.Keys)
        {
            if (key.StartsWith("ChatFeed_") && !key.Contains("AesKey"))
            {
                var chatKey = key["ChatFeed_".Length..];
                if (chatKey.Contains(lowerName))
                {
                    return chatKey;
                }
            }
        }

        throw new InvalidOperationException($"No chat feed found for user {userName}");
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
}
