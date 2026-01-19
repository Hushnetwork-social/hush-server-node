using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;
using HushShared.Converters;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// Step definitions for Genesis Block creation scenarios.
/// </summary>
[Binding]
public sealed class GenesisBlockSteps
{
    private readonly ScenarioContext _scenarioContext;

    public GenesisBlockSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"a fresh HushServerNode without any blocks")]
    public async Task GivenAFreshHushServerNodeWithoutAnyBlocks()
    {
        // The node is started fresh by BeforeScenario hook with reset database
        // Note: The node automatically creates the genesis block during initialization
        // So a "fresh" node will actually be at block 1 (genesis), not block 0
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        // Initial state should be block 1 (genesis block auto-created)
        var response = await blockchainClient.GetBlockchainHeightAsync(new GetBlockchainHeightRequest());
        response.Index.Should().Be(1, "fresh node should have genesis block at index 1");
    }

    [Given(@"BlockProducer credentials are configured")]
    public void GivenBlockProducerCredentialsAreConfigured()
    {
        // The node is configured with TestIdentities.BlockProducer by HushServerNodeCore.CreateForTesting
        // We just verify the test identity exists
        TestIdentities.BlockProducer.Should().NotBeNull();
        TestIdentities.BlockProducer.PublicSigningAddress.Should().NotBeNullOrWhiteSpace();
    }

    [When(@"a block is produced")]
    public async Task WhenABlockIsProduced()
    {
        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();
    }

    [Then(@"the genesis block should exist at index (.*)")]
    public async Task ThenTheGenesisBlockShouldExistAtIndex(int expectedIndex)
    {
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var response = await blockchainClient.GetBlockchainHeightAsync(new GetBlockchainHeightRequest());
        response.Index.Should().Be(expectedIndex, $"block height should be {expectedIndex} after genesis block");
    }

    [Then(@"the blockchain should be at index (.*)")]
    public async Task ThenTheBlockchainShouldBeAtIndex(int expectedIndex)
    {
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var response = await blockchainClient.GetBlockchainHeightAsync(new GetBlockchainHeightRequest());
        response.Index.Should().Be(expectedIndex, $"block height should be {expectedIndex}");
    }

    [Then(@"the BlockProducer should have (.*) HUSH balance")]
    public async Task ThenTheBlockProducerShouldHaveHUSHBalance(int expectedBalance)
    {
        var grpcFactory = GetGrpcFactory();
        var bankClient = grpcFactory.CreateClient<HushBank.HushBankClient>();

        var response = await bankClient.GetAddressBalanceAsync(new GetAddressBalanceRequest
        {
            Token = "HUSH",
            Address = TestIdentities.BlockProducer.PublicSigningAddress
        });

        DecimalStringConverter.StringToDecimal(response.Balance).Should().Be(expectedBalance,
            $"BlockProducer should have {expectedBalance} HUSH after producing a block");
    }

    [Then(@"the BlockProducer should have a personal feed")]
    public async Task ThenTheBlockProducerShouldHaveAPersonalFeed()
    {
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.HasPersonalFeedAsync(new HasPersonalFeedRequest
        {
            PublicPublicKey = TestIdentities.BlockProducer.PublicSigningAddress
        });

        response.FeedAvailable.Should().BeTrue("BlockProducer should have a personal feed after identity registration");
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
