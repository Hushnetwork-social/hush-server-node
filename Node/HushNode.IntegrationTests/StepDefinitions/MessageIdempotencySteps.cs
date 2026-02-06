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
/// Step definitions for FEAT-057: Message Idempotency tests.
/// Tests the server's ability to detect duplicate message submissions.
/// </summary>
[Binding]
public sealed class MessageIdempotencySteps
{
    private readonly ScenarioContext _scenarioContext;
    private SubmitSignedTransactionReply? _lastResponse;

    public MessageIdempotencySteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"(.*) creates a message with a specific message ID ""(.*)""")]
    public void GivenUserCreatesMessageWithSpecificId(string userName, string messageIdSuffix)
    {
        var sender = GetTestIdentity(userName);
        var chatFeedKey = FindChatFeedKeyForUser(userName);
        var feedId = (FeedId)_scenarioContext[$"ChatFeed_{chatFeedKey}"];
        var aesKey = (string)_scenarioContext[$"ChatFeedAesKey_{chatFeedKey}"];

        // Create a deterministic message ID from the suffix
        // This allows us to resubmit the exact same message
        var messageId = CreateDeterministicMessageId(messageIdSuffix);
        var message = $"Test message for idempotency: {messageIdSuffix}";

        // Create the transaction JSON
        var transactionJson = TestTransactionFactory.CreateFeedMessageWithId(
            sender, feedId, messageId, message, aesKey);

        // Store for later submission
        _scenarioContext[$"IdempotencyMessage_{messageIdSuffix}"] = transactionJson;
        _scenarioContext[$"IdempotencyMessageId_{messageIdSuffix}"] = messageId;
        _scenarioContext["CurrentIdempotencyMessageSuffix"] = messageIdSuffix;
    }

    [When(@"(.*) submits the message to the ChatFeed via gRPC")]
    public async Task WhenUserSubmitsMessageToChatFeedViaGrpc(string userName)
    {
        var suffix = (string)_scenarioContext["CurrentIdempotencyMessageSuffix"];
        var transactionJson = (string)_scenarioContext[$"IdempotencyMessage_{suffix}"];

        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        _lastResponse = await blockchainClient.SubmitSignedTransactionAsync(
            new SubmitSignedTransactionRequest
            {
                SignedTransaction = transactionJson
            });
    }

    [When(@"(.*) submits the same message again via gRPC")]
    public async Task WhenUserSubmitsSameMessageAgainViaGrpc(string userName)
    {
        // Resubmit the same transaction
        await WhenUserSubmitsMessageToChatFeedViaGrpc(userName);
    }

    [When(@"(.*) submits message ""(.*)"" again via gRPC")]
    public async Task WhenUserSubmitsSpecificMessageAgainViaGrpc(string userName, string messageIdSuffix)
    {
        var transactionJson = (string)_scenarioContext[$"IdempotencyMessage_{messageIdSuffix}"];

        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        _lastResponse = await blockchainClient.SubmitSignedTransactionAsync(
            new SubmitSignedTransactionRequest
            {
                SignedTransaction = transactionJson
            });
    }

    // Note: WhenUserRegistersIdentityViaGrpc step is already defined in PersonalFeedSteps.cs
    // Note: GivenUserIsNotRegistered step is already defined in PersonalFeedSteps.cs
    // We reuse those step definitions instead of duplicating them here

    [Then(@"the response status should be ""(.*)""")]
    public void ThenResponseStatusShouldBe(string expectedStatus)
    {
        _lastResponse.Should().NotBeNull("Response should exist");

        var expectedEnum = expectedStatus switch
        {
            "ACCEPTED" => TransactionStatus.Accepted,
            "PENDING" => TransactionStatus.Pending,
            "ALREADY_EXISTS" => TransactionStatus.AlreadyExists,
            "REJECTED" => TransactionStatus.Rejected,
            _ => throw new ArgumentException($"Unknown status: {expectedStatus}")
        };

        _lastResponse!.Status.Should().Be(expectedEnum,
            $"Expected status {expectedStatus} but got {_lastResponse.Status}. Message: {_lastResponse.Message}");
    }

    [Then(@"the response should be successful")]
    public void ThenResponseShouldBeSuccessful()
    {
        _lastResponse.Should().NotBeNull("Response should exist");
        _lastResponse!.Successfull.Should().BeTrue(
            $"Response should be successful. Status: {_lastResponse.Status}, Message: {_lastResponse.Message}");
    }

    /// <summary>
    /// Creates a deterministic FeedMessageId from a string suffix.
    /// This allows tests to create the same message ID across multiple submissions.
    /// </summary>
    private static FeedMessageId CreateDeterministicMessageId(string suffix)
    {
        // Use a hash of the suffix to create a deterministic GUID
        var hashBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"FEAT-057-TEST-{suffix}"));

        // Take first 16 bytes for GUID
        var guidBytes = hashBytes[..16];
        var guid = new Guid(guidBytes);

        return new FeedMessageId(guid);
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
}
