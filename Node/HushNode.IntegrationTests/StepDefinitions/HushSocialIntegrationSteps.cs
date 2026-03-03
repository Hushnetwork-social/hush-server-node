using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode.Testing;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

[Binding]
[Scope(Feature = "HushSocial server integration rules")]
public sealed class HushSocialIntegrationSteps
{
    private const string OwnerName = "Owner";
    private const string OwnerInnerCircleFeedIdKey = "OwnerInnerCircleFeedId";
    private const string OwnerInnerCircleKeyGenerationBeforeSyncKey = "OwnerInnerCircleKeyGenerationBeforeSync";
    private const string OwnerInnerCircleKeyGenerationAfterSyncKey = "OwnerInnerCircleKeyGenerationAfterSync";
    private const string BootstrapPeerNamesKey = "HushSocialBootstrapPeerNames";

    private readonly ScenarioContext _scenarioContext;

    public HushSocialIntegrationSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"user ""(.*)"" is not authenticated")]
    public void GivenUserIsNotAuthenticated(string userName)
    {
        _scenarioContext[$"IsAuthenticated_{userName}"] = false;
    }

    [Given(@"Owner has HushSocial enabled")]
    public void GivenOwnerHasHushSocialEnabled()
    {
        _scenarioContext["OwnerHushSocialEnabled"] = true;
    }

    [Given(@"Owner profile mode is Close")]
    public void GivenOwnerProfileModeIsClose()
    {
        _scenarioContext["OwnerProfileMode"] = "Close";
    }

    [Given(@"Owner has existing chat feeds with ""(.*)""")]
    public async Task GivenOwnerHasExistingChatFeedsWith(string csvFollowers)
    {
        var followers = ParseCsvUsers(csvFollowers);
        _scenarioContext[BootstrapPeerNamesKey] = followers;

        foreach (var follower in followers)
        {
            await EnsureChatFeedExistsAsync(OwnerName, follower);
        }
    }

    [Given(@"Owner does not have an Inner Circle yet")]
    public async Task GivenOwnerDoesNotHaveAnInnerCircleYet()
    {
        var owner = GetTestIdentity(OwnerName);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetInnerCircleAsync(new GetInnerCircleRequest
        {
            OwnerPublicAddress = owner.PublicSigningAddress
        });

        response.Success.Should().BeTrue($"GetInnerCircle should succeed: {response.Message}");
        response.Exists.Should().BeFalse("Owner should not have an Inner Circle before bootstrap");
    }

    [When(@"Owner opens HushFeeds and personal feed bootstrap runs")]
    public async Task WhenOwnerOpensHushFeedsAndPersonalFeedBootstrapRuns()
    {
        await RunFeat085BootstrapAsync();
    }

    [Then(@"Owner Inner Circle should be created")]
    public async Task ThenOwnerInnerCircleShouldBeCreated()
    {
        var innerCircleFeedId = await EnsureOwnerInnerCircleExistsAsync();
        innerCircleFeedId.Should().NotBeNullOrWhiteSpace("Owner Inner Circle should exist after bootstrap");
    }

    [Then(@"""(.*)"" should be members of Owner Inner Circle")]
    public async Task ThenUsersShouldBeMembersOfOwnerInnerCircle(string csvFollowers)
    {
        var expectedFollowers = ParseCsvUsers(csvFollowers)
            .Select(GetTestIdentity)
            .Select(identity => identity.PublicSigningAddress)
            .ToHashSet(StringComparer.Ordinal);

        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var innerCircleFeedId = await EnsureOwnerInnerCircleExistsAsync();
        var membersResponse = await feedClient.GetGroupMembersAsync(new GetGroupMembersRequest
        {
            FeedId = innerCircleFeedId
        });

        var memberAddresses = membersResponse.Members
            .Select(member => member.PublicAddress)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expectedFollower in expectedFollowers)
        {
            memberAddresses.Should().Contain(expectedFollower);
        }
    }

    [Given(@"Owner Inner Circle already exists")]
    public async Task GivenOwnerInnerCircleAlreadyExists()
    {
        var innerCircleFeedId = await EnsureOwnerInnerCircleExistsAsync();
        var beforeKeyGeneration = await GetOwnerCurrentKeyGenerationAsync(innerCircleFeedId);

        _scenarioContext[OwnerInnerCircleKeyGenerationBeforeSyncKey] = beforeKeyGeneration;
    }

    [Given(@"Owner starts a new chat feed with (.*)")]
    public async Task GivenOwnerStartsANewChatFeedWith(string followerName)
    {
        await EnsureChatFeedExistsAsync(OwnerName, followerName);
    }

    [When(@"FEAT-085 background sync runs after chat creation")]
    public async Task WhenFeat085BackgroundSyncRunsAfterChatCreation()
    {
        if (!_scenarioContext.ContainsKey(OwnerInnerCircleKeyGenerationBeforeSyncKey))
        {
            var innerCircleFeedId = await EnsureOwnerInnerCircleExistsAsync();
            var beforeKeyGeneration = await GetOwnerCurrentKeyGenerationAsync(innerCircleFeedId);
            _scenarioContext[OwnerInnerCircleKeyGenerationBeforeSyncKey] = beforeKeyGeneration;
        }

        await RunFeat085BootstrapAsync();

        var currentFeedId = await EnsureOwnerInnerCircleExistsAsync();
        var afterKeyGeneration = await GetOwnerCurrentKeyGenerationAsync(currentFeedId);
        _scenarioContext[OwnerInnerCircleKeyGenerationAfterSyncKey] = afterKeyGeneration;
    }

    [Then(@"FollowerC should be added to Owner Inner Circle")]
    public async Task ThenFollowerCShouldBeAddedToOwnerInnerCircle()
    {
        var followerC = GetTestIdentity("FollowerC");
        var innerCircleFeedId = await EnsureOwnerInnerCircleExistsAsync();
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();

        var membersResponse = await feedClient.GetGroupMembersAsync(new GetGroupMembersRequest
        {
            FeedId = innerCircleFeedId
        });

        membersResponse.Members
            .Select(member => member.PublicAddress)
            .Should()
            .Contain(followerC.PublicSigningAddress);
    }

    [Then(@"key generation for Owner Inner Circle should be incremented")]
    public void ThenKeyGenerationForOwnerInnerCircleShouldBeIncremented()
    {
        _scenarioContext.TryGetValue(OwnerInnerCircleKeyGenerationBeforeSyncKey, out var beforeObj)
            .Should()
            .BeTrue("Expected stored key generation before background sync");

        _scenarioContext.TryGetValue(OwnerInnerCircleKeyGenerationAfterSyncKey, out var afterObj)
            .Should()
            .BeTrue("Expected stored key generation after background sync");

        var before = (int)beforeObj!;
        var after = (int)afterObj!;

        after.Should().BeGreaterThan(before, "adding new inner circle members should rotate the key");
    }

    private async Task RunFeat085BootstrapAsync()
    {
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var owner = GetTestIdentity(OwnerName);

        var innerCircleFeedId = await EnsureOwnerInnerCircleExistsAsync();

        if (!_scenarioContext.TryGetValue(BootstrapPeerNamesKey, out var peerNamesObj)
            || peerNamesObj is not string[] configuredPeers
            || configuredPeers.Length == 0)
        {
            configuredPeers = await ResolveChatPeerNamesForOwnerAsync();
        }

        if (configuredPeers.Length == 0)
        {
            _scenarioContext[OwnerInnerCircleFeedIdKey] = innerCircleFeedId;
            return;
        }

        var members = configuredPeers
            .Select(GetTestIdentity)
            .Select(identity => new InnerCircleMemberProto
            {
                PublicAddress = identity.PublicSigningAddress,
                PublicEncryptAddress = identity.PublicEncryptAddress
            })
            .ToList();

        var addMembersResponse = await feedClient.AddMembersToInnerCircleAsync(new AddMembersToInnerCircleRequest
        {
            OwnerPublicAddress = owner.PublicSigningAddress,
            RequesterPublicAddress = owner.PublicSigningAddress,
            Members = { members }
        });

        addMembersResponse.Success.Should().BeTrue(
            $"AddMembersToInnerCircle should succeed: {addMembersResponse.Message}");

        await GetBlockControl().ProduceBlockAsync();
        _scenarioContext[OwnerInnerCircleFeedIdKey] = innerCircleFeedId;
    }

    private async Task<string> EnsureOwnerInnerCircleExistsAsync()
    {
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var owner = GetTestIdentity(OwnerName);

        var innerCircle = await feedClient.GetInnerCircleAsync(new GetInnerCircleRequest
        {
            OwnerPublicAddress = owner.PublicSigningAddress
        });

        innerCircle.Success.Should().BeTrue($"GetInnerCircle should succeed: {innerCircle.Message}");

        if (innerCircle.Exists)
        {
            _scenarioContext[OwnerInnerCircleFeedIdKey] = innerCircle.FeedId;
            return innerCircle.FeedId;
        }

        var createResponse = await feedClient.CreateInnerCircleAsync(new CreateInnerCircleRequest
        {
            OwnerPublicAddress = owner.PublicSigningAddress,
            RequesterPublicAddress = owner.PublicSigningAddress
        });

        createResponse.Success.Should().BeTrue($"CreateInnerCircle should succeed: {createResponse.Message}");

        await GetBlockControl().ProduceBlockAsync();

        var afterCreate = await feedClient.GetInnerCircleAsync(new GetInnerCircleRequest
        {
            OwnerPublicAddress = owner.PublicSigningAddress
        });

        afterCreate.Success.Should().BeTrue($"GetInnerCircle after create should succeed: {afterCreate.Message}");
        afterCreate.Exists.Should().BeTrue("Inner Circle should exist after create");
        afterCreate.FeedId.Should().NotBeNullOrWhiteSpace("Inner Circle FeedId should be returned after create");

        _scenarioContext[OwnerInnerCircleFeedIdKey] = afterCreate.FeedId;
        return afterCreate.FeedId;
    }

    private async Task<int> GetOwnerCurrentKeyGenerationAsync(string feedId)
    {
        var owner = GetTestIdentity(OwnerName);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
        {
            FeedId = feedId,
            UserPublicAddress = owner.PublicSigningAddress
        });

        if (response.KeyGenerations.Count == 0)
        {
            return 0;
        }

        return response.KeyGenerations.Max(keyGeneration => keyGeneration.KeyGeneration);
    }

    private async Task EnsureChatFeedExistsAsync(string initiatorName, string recipientName)
    {
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var initiator = GetTestIdentity(initiatorName);
        var recipient = GetTestIdentity(recipientName);

        var feedsResponse = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = initiator.PublicSigningAddress,
            BlockIndex = 0
        });

        var chatAlreadyExists = feedsResponse.Feeds.Any(feed =>
            feed.FeedType == 1 &&
            feed.FeedParticipants.Any(participant =>
                participant.ParticipantPublicAddress == recipient.PublicSigningAddress));

        if (chatAlreadyExists)
        {
            return;
        }

        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();
        var (signedTransactionJson, _, _) = TestTransactionFactory.CreateChatFeed(initiator, recipient);

        var submitResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransactionJson
        });

        submitResponse.Successfull.Should().BeTrue($"Chat feed creation should succeed: {submitResponse.Message}");
        await GetBlockControl().ProduceBlockAsync();
    }

    private async Task<string[]> ResolveChatPeerNamesForOwnerAsync()
    {
        var owner = GetTestIdentity(OwnerName);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var feedsResponse = await feedClient.GetFeedsForAddressAsync(new GetFeedForAddressRequest
        {
            ProfilePublicKey = owner.PublicSigningAddress,
            BlockIndex = 0
        });

        var peerAddresses = feedsResponse.Feeds
            .Where(feed => feed.FeedType == 1)
            .SelectMany(feed => feed.FeedParticipants)
            .Select(participant => participant.ParticipantPublicAddress)
            .Where(publicAddress => publicAddress != owner.PublicSigningAddress)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return _scenarioContext.Keys
            .Where(key => key.StartsWith("Identity_", StringComparison.Ordinal))
            .Select(key => key["Identity_".Length..])
            .Where(userName =>
            {
                var identity = GetTestIdentity(userName);
                return peerAddresses.Contains(identity.PublicSigningAddress, StringComparer.Ordinal);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private TestIdentity GetTestIdentity(string userName)
    {
        var contextKey = $"Identity_{userName}";
        if (_scenarioContext.TryGetValue(contextKey, out var identityObj)
            && identityObj is TestIdentity existingIdentity)
        {
            return existingIdentity;
        }

        var generatedIdentity = TestIdentities.GenerateFromSeed(
            $"TEST_{new string(userName.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray())}_V1",
            userName);

        _scenarioContext[contextKey] = generatedIdentity;
        return generatedIdentity;
    }

    private string[] ParseCsvUsers(string csvUsers) =>
        csvUsers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(user => !string.IsNullOrWhiteSpace(user))
            .ToArray();

    private GrpcClientFactory GetGrpcFactory()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            && factoryObj is GrpcClientFactory grpcFactory)
        {
            return grpcFactory;
        }

        throw new InvalidOperationException("GrpcClientFactory not found in ScenarioContext.");
    }

    private HushServerNode.Testing.BlockProductionControl GetBlockControl()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.BlockControlKey, out var controlObj)
            && controlObj is HushServerNode.Testing.BlockProductionControl blockControl)
        {
            return blockControl;
        }

        throw new InvalidOperationException("BlockProductionControl not found in ScenarioContext.");
    }
}
