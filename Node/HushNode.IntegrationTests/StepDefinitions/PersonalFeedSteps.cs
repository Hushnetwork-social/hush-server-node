using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode.Testing;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// Step definitions for Personal Feed creation scenarios.
/// </summary>
[Binding]
public sealed class PersonalFeedSteps
{
    private readonly ScenarioContext _scenarioContext;

    public PersonalFeedSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"a HushServerNode at block (.*)")]
    public async Task GivenAHushServerNodeAtBlock(int targetBlockIndex)
    {
        var blockControl = GetBlockControl();
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        // Produce blocks until we reach the target index
        var currentHeight = (await blockchainClient.GetBlockchainHeightAsync(new GetBlockchainHeightRequest())).Index;

        while (currentHeight < targetBlockIndex)
        {
            await blockControl.ProduceBlockAsync();
            currentHeight = (await blockchainClient.GetBlockchainHeightAsync(new GetBlockchainHeightRequest())).Index;
        }

        currentHeight.Should().BeGreaterThanOrEqualTo(targetBlockIndex);
    }

    [Given(@"user ""(.*)"" is not registered")]
    public async Task GivenUserIsNotRegistered(string userName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        // Verify user doesn't have a personal feed yet
        var response = await feedClient.HasPersonalFeedAsync(new HasPersonalFeedRequest
        {
            PublicPublicKey = identity.PublicSigningAddress
        });

        response.FeedAvailable.Should().BeFalse($"{userName} should not have a personal feed before registration");

        // Store identity in context for later steps
        _scenarioContext[$"Identity_{userName}"] = identity;
    }

    [When(@"(.*) registers her identity via gRPC")]
    [When(@"(.*) registers his identity via gRPC")]
    public async Task WhenUserRegistersIdentityViaGrpc(string userName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        // Step 1: Submit identity transaction
        var identityTxJson = TestTransactionFactory.CreateIdentityRegistration(identity);
        var identityResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = identityTxJson
        });
        identityResponse.Successfull.Should().BeTrue($"Identity registration for {userName} should succeed: {identityResponse.Message}");

        // Step 2: Produce a block to commit identity (required before personal feed can reference it)
        await blockControl.ProduceBlockAsync();

        // Step 3: Submit personal feed transaction (must be in separate block to avoid serialization conflict)
        var personalFeedTxJson = TestTransactionFactory.CreatePersonalFeed(identity);
        var feedResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = personalFeedTxJson
        });
        feedResponse.Successfull.Should().BeTrue($"Personal feed creation for {userName} should succeed: {feedResponse.Message}");
    }

    [Then(@"([A-Za-z]+) should have a personal feed")]
    public async Task ThenUserShouldHaveAPersonalFeed(string userName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.HasPersonalFeedAsync(new HasPersonalFeedRequest
        {
            PublicPublicKey = identity.PublicSigningAddress
        });

        response.FeedAvailable.Should().BeTrue($"{userName} should have a personal feed after registration");
    }

    [Then(@"(.*)'s display name should be ""(.*)""")]
    public async Task ThenDisplayNameShouldBe(string userName, string expectedDisplayName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var identityClient = grpcFactory.CreateClient<HushIdentity.HushIdentityClient>();

        var response = await identityClient.GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = identity.PublicSigningAddress
        });

        response.Successfull.Should().BeTrue($"Should be able to retrieve {userName}'s identity");
        response.ProfileName.Should().Be(expectedDisplayName);
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
