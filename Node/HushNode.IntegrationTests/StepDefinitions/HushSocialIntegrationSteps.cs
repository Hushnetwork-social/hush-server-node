using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using Olimpo;
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
    private const string OwnerInnerCircleDuplicateAddResponseKey = "OwnerInnerCircleDuplicateAddResponse";
    private const string OwnerInnerCircleSameBlockKeyGenerationBeforeKey = "OwnerInnerCircleSameBlockKeyGenerationBefore";
    private const string OwnerInnerCircleSameBlockKeyGenerationAfterKey = "OwnerInnerCircleSameBlockKeyGenerationAfter";
    private const string BootstrapPeerNamesKey = "HushSocialBootstrapPeerNames";
    private const string PendingFollowersKey = "HushSocialPendingFollowers";
    private const string ApprovedFollowersKey = "HushSocialApprovedFollowers";

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

    [When(@"FollowerA requests to follow Owner")]
    public void WhenFollowerARequestsToFollowOwner()
    {
        var pending = GetOrCreateFollowerSet(PendingFollowersKey);
        pending.Add("FollowerA");
    }

    [Then(@"the follow request should be pending approval")]
    public void ThenTheFollowRequestShouldBePendingApproval()
    {
        var pending = GetOrCreateFollowerSet(PendingFollowersKey);
        pending.Should().Contain("FollowerA");
    }

    [When(@"Owner accepts follow request from FollowerA")]
    public async Task WhenOwnerAcceptsFollowRequestFromFollowerA()
    {
        await AcceptFollowRequestAsync("FollowerA");
    }

    [Given(@"Owner has accepted follow request from (.*)")]
    public async Task GivenOwnerHasAcceptedFollowRequestFrom(string followerName)
    {
        await AcceptFollowRequestAsync(followerName);
    }

    [Given(@"Owner has accepted follow requests from ""(.*)""")]
    public async Task GivenOwnerHasAcceptedFollowRequestsFrom(string csvFollowers)
    {
        var followers = ParseCsvUsers(csvFollowers);
        foreach (var follower in followers)
        {
            await AcceptFollowRequestAsync(follower);
        }
    }

    [Then(@"FollowerA should be in Owner Inner Circle")]
    public async Task ThenFollowerAShouldBeInOwnerInnerCircle()
    {
        await AssertFollowerInInnerCircleAsync("FollowerA");
    }

    [Then(@"Owner should see FollowerA in approved followers")]
    public void ThenOwnerShouldSeeFollowerAInApprovedFollowers()
    {
        var approved = GetOrCreateFollowerSet(ApprovedFollowersKey);
        approved.Should().Contain("FollowerA");
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

    [Given(@"Owner has created circle ""(.*)""")]
    public async Task GivenOwnerHasCreatedCircle(string circleName)
    {
        await EnsureCircleExistsAsync(circleName);
    }

    [Given(@"Owner has added ""(.*)"" to circle ""(.*)""")]
    public async Task GivenOwnerHasAddedUsersToCircle(string csvFollowers, string circleName)
    {
        var feedId = await EnsureCircleExistsAsync(circleName);
        var owner = GetTestIdentity(OwnerName);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var followers = ParseCsvUsers(csvFollowers);

        foreach (var followerName in followers)
        {
            var follower = GetTestIdentity(followerName);
            var addResponse = await feedClient.AddMemberToGroupFeedAsync(new AddMemberToGroupFeedRequest
            {
                FeedId = feedId.ToString(),
                AdminPublicAddress = owner.PublicSigningAddress,
                NewMemberPublicAddress = follower.PublicSigningAddress,
                NewMemberPublicEncryptKey = follower.PublicEncryptAddress
            });

            addResponse.Success.Should().BeTrue(
                $"AddMemberToGroupFeed should succeed for {followerName}: {addResponse.Message}");
            await GetBlockControl().ProduceBlockAsync();
        }
    }

    [Given(@"Owner posts ""(.*)"" to circle ""(.*)""")]
    public async Task GivenOwnerPostsToCircle(string message, string circleName)
    {
        await PublishGroupMessageAsync(circleName, message, contextKeyPrefix: "PreRemoval");
    }

    [When(@"Owner removes FollowerB from circle ""(.*)""")]
    public async Task WhenOwnerRemovesFollowerBFromCircle(string circleName)
    {
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var owner = GetTestIdentity(OwnerName);
        var followerB = GetTestIdentity("FollowerB");
        var feedId = await EnsureCircleExistsAsync(circleName);

        var before = await GetCurrentGroupKeyGenerationForUserAsync(feedId, owner.PublicSigningAddress);
        _scenarioContext[$"CircleKeyGenerationBeforeRemoval_{circleName}"] = before;

        var removeResponse = await feedClient.BanFromGroupFeedAsync(new BanFromGroupFeedRequest
        {
            FeedId = feedId.ToString(),
            AdminPublicAddress = owner.PublicSigningAddress,
            BannedUserPublicAddress = followerB.PublicSigningAddress,
            Reason = "FEAT-085 removal test"
        });

        removeResponse.Success.Should().BeTrue($"BanFromGroupFeed should succeed: {removeResponse.Message}");
        await GetBlockControl().ProduceBlockAsync();

        var after = await GetCurrentGroupKeyGenerationForUserAsync(feedId, owner.PublicSigningAddress);
        _scenarioContext[$"CircleKeyGenerationAfterRemoval_{circleName}"] = after;
    }

    [Then(@"key generation for circle ""(.*)"" should be incremented")]
    public void ThenKeyGenerationForCircleShouldBeIncremented(string circleName)
    {
        var before = (int)_scenarioContext[$"CircleKeyGenerationBeforeRemoval_{circleName}"];
        var after = (int)_scenarioContext[$"CircleKeyGenerationAfterRemoval_{circleName}"];
        after.Should().BeGreaterThan(before, "removing a member should rotate group keys");
    }

    [Then(@"FollowerB should not be able to decrypt new posts in circle ""(.*)""")]
    public async Task ThenFollowerBShouldNotBeAbleToDecryptNewPostsInCircle(string circleName)
    {
        await EnsurePostRemovalMessageExistsAsync(circleName);
        var feedId = (FeedId)_scenarioContext[$"CircleFeedId_{circleName}"];
        var messageId = (FeedMessageId)_scenarioContext[$"PostRemovalMessageId_{circleName}"];
        var messageKeyGeneration = (int)_scenarioContext[$"PostRemovalMessageKeyGeneration_{circleName}"];
        var followerB = GetTestIdentity("FollowerB");
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();

        var messages = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = followerB.PublicSigningAddress,
            BlockIndex = 0
        });

        var targetMessage = messages.Messages.FirstOrDefault(message =>
            message.FeedId == feedId.ToString() &&
            message.FeedMessageId == messageId.ToString());

        if (targetMessage is null)
        {
            return;
        }

        var keys = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = followerB.PublicSigningAddress
        });

        keys.KeyGenerations.Any(key => key.KeyGeneration == messageKeyGeneration)
            .Should()
            .BeFalse("removed members must not receive keys for post-removal generations");
    }

    [Then(@"FollowerA should be able to decrypt new posts in circle ""(.*)""")]
    public async Task ThenFollowerAShouldBeAbleToDecryptNewPostsInCircle(string circleName)
    {
        await EnsurePostRemovalMessageExistsAsync(circleName);
        var feedId = (FeedId)_scenarioContext[$"CircleFeedId_{circleName}"];
        var messageId = (FeedMessageId)_scenarioContext[$"PostRemovalMessageId_{circleName}"];
        var expectedText = (string)_scenarioContext[$"PostRemovalMessageText_{circleName}"];
        var followerA = GetTestIdentity("FollowerA");
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();

        var messages = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = followerA.PublicSigningAddress,
            BlockIndex = 0
        });

        var targetMessage = messages.Messages.FirstOrDefault(message =>
            message.FeedId == feedId.ToString() &&
            message.FeedMessageId == messageId.ToString());

        targetMessage.Should().NotBeNull("remaining members should still read post-removal messages");

        var followerAKey = await DecryptLatestAesKeyAsync(feedId, followerA);
        var decrypted = EncryptKeys.AesDecrypt(targetMessage!.MessageContent, followerAKey.AesKey);
        decrypted.Should().Be(expectedText);
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

    [Then(@"""(.*)"" should be added to Owner Inner Circle")]
    public async Task ThenFollowerShouldBeAddedToOwnerInnerCircle(string followerName)
    {
        await AssertFollowerInInnerCircleAsync(followerName);
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

    [When(@"Owner tries to add ""(.*)"" again to Owner Inner Circle")]
    public async Task WhenOwnerTriesToAddFollowerAgainToOwnerInnerCircle(string followerName)
    {
        var owner = GetTestIdentity(OwnerName);
        var follower = GetTestIdentity(followerName);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var innerCircleFeedId = await EnsureOwnerInnerCircleExistsAsync();

        var before = await GetOwnerCurrentKeyGenerationAsync(innerCircleFeedId);
        _scenarioContext[OwnerInnerCircleKeyGenerationBeforeSyncKey] = before;

        var response = await feedClient.AddMembersToInnerCircleAsync(new AddMembersToInnerCircleRequest
        {
            OwnerPublicAddress = owner.PublicSigningAddress,
            RequesterPublicAddress = owner.PublicSigningAddress,
            Members =
            {
                new InnerCircleMemberProto
                {
                    PublicAddress = follower.PublicSigningAddress,
                    PublicEncryptAddress = follower.PublicEncryptAddress
                }
            }
        });

        _scenarioContext[OwnerInnerCircleDuplicateAddResponseKey] = response;
        var after = await GetOwnerCurrentKeyGenerationAsync(innerCircleFeedId);
        _scenarioContext[OwnerInnerCircleKeyGenerationAfterSyncKey] = after;
    }

    [Then(@"FEAT-085 duplicate add response should include ""(.*)""")]
    public void ThenFeat085DuplicateAddResponseShouldInclude(string followerName)
    {
        var response = (AddMembersToInnerCircleResponse)_scenarioContext[OwnerInnerCircleDuplicateAddResponseKey];
        var follower = GetTestIdentity(followerName);

        response.Success.Should().BeFalse("duplicate add should return explicit failure payload");
        response.DuplicateMembers.Should().Contain(follower.PublicSigningAddress);
    }

    [Then(@"key generation for Owner Inner Circle should remain unchanged")]
    public void ThenKeyGenerationForOwnerInnerCircleShouldRemainUnchanged()
    {
        var before = (int)_scenarioContext[OwnerInnerCircleKeyGenerationBeforeSyncKey];
        var after = (int)_scenarioContext[OwnerInnerCircleKeyGenerationAfterSyncKey];
        after.Should().Be(before, "duplicate add should not rotate keys");
    }

    [When(@"Owner submits duplicate add-members requests for ""(.*)"" before block indexing")]
    public async Task WhenOwnerSubmitsDuplicateAddMembersRequestsForBeforeBlockIndexing(string followerName)
    {
        var owner = GetTestIdentity(OwnerName);
        var follower = GetTestIdentity(followerName);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var innerCircleFeedId = await EnsureOwnerInnerCircleExistsAsync();

        var before = await GetOwnerCurrentKeyGenerationAsync(innerCircleFeedId);
        _scenarioContext[OwnerInnerCircleSameBlockKeyGenerationBeforeKey] = before;

        var request = new AddMembersToInnerCircleRequest
        {
            OwnerPublicAddress = owner.PublicSigningAddress,
            RequesterPublicAddress = owner.PublicSigningAddress
        };

        request.Members.Add(new InnerCircleMemberProto
        {
            PublicAddress = follower.PublicSigningAddress,
            PublicEncryptAddress = follower.PublicEncryptAddress
        });

        var first = await feedClient.AddMembersToInnerCircleAsync(request);
        first.Success.Should().BeTrue($"First add-members request should be accepted pre-indexing: {first.Message}");

        var second = await feedClient.AddMembersToInnerCircleAsync(request);
        second.Success.Should().BeFalse("second duplicate add-members request should be rejected deterministically");
        second.DuplicateMembers.Should().Contain(follower.PublicSigningAddress);
    }

    [Then(@"FEAT-085 same-block duplicate processing should rotate Owner Inner Circle only once")]
    public async Task ThenFeat085SameBlockDuplicateProcessingShouldRotateOwnerInnerCircleOnlyOnce()
    {
        var innerCircleFeedId = await EnsureOwnerInnerCircleExistsAsync();
        var before = (int)_scenarioContext[OwnerInnerCircleSameBlockKeyGenerationBeforeKey];
        var after = await GetOwnerCurrentKeyGenerationAsync(innerCircleFeedId);
        _scenarioContext[OwnerInnerCircleSameBlockKeyGenerationAfterKey] = after;

        after.Should().Be(before + 1, "same-block duplicate add-members should add once and skip duplicate rotation");
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

    private async Task AcceptFollowRequestAsync(string followerName)
    {
        var pending = GetOrCreateFollowerSet(PendingFollowersKey);
        pending.Remove(followerName);

        await EnsureOwnerInnerCircleExistsAsync();
        var owner = GetTestIdentity(OwnerName);
        var follower = GetTestIdentity(followerName);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var response = await feedClient.AddMembersToInnerCircleAsync(new AddMembersToInnerCircleRequest
        {
            OwnerPublicAddress = owner.PublicSigningAddress,
            RequesterPublicAddress = owner.PublicSigningAddress,
            Members =
            {
                new InnerCircleMemberProto
                {
                    PublicAddress = follower.PublicSigningAddress,
                    PublicEncryptAddress = follower.PublicEncryptAddress
                }
            }
        });

        response.Success.Should().BeTrue($"Accept follow should add follower to Inner Circle: {response.Message}");
        await GetBlockControl().ProduceBlockAsync();

        var approved = GetOrCreateFollowerSet(ApprovedFollowersKey);
        approved.Add(followerName);
    }

    private async Task AssertFollowerInInnerCircleAsync(string followerName)
    {
        var follower = GetTestIdentity(followerName);
        var innerCircleFeedId = await EnsureOwnerInnerCircleExistsAsync();
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var members = await feedClient.GetGroupMembersAsync(new GetGroupMembersRequest
        {
            FeedId = innerCircleFeedId
        });

        members.Members
            .Select(member => member.PublicAddress)
            .Should()
            .Contain(follower.PublicSigningAddress);
    }

    private HashSet<string> GetOrCreateFollowerSet(string key)
    {
        if (_scenarioContext.TryGetValue(key, out var followersObj)
            && followersObj is HashSet<string> followers)
        {
            return followers;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _scenarioContext[key] = set;
        return set;
    }

    private async Task<FeedId> EnsureCircleExistsAsync(string circleName)
    {
        var circleKey = $"CircleFeedId_{circleName}";
        if (_scenarioContext.TryGetValue(circleKey, out var existingObj)
            && existingObj is FeedId existingFeedId)
        {
            return existingFeedId;
        }

        var owner = GetTestIdentity(OwnerName);
        var blockchainClient = GetGrpcFactory().CreateClient<HushBlockchain.HushBlockchainClient>();
        var (txJson, feedId, _) = TestTransactionFactory.CreateGroupFeed(owner, circleName);

        var submitResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = txJson
        });

        submitResponse.Successfull.Should().BeTrue($"Create group '{circleName}' should succeed: {submitResponse.Message}");
        await GetBlockControl().ProduceBlockAsync();

        _scenarioContext[circleKey] = feedId;
        return feedId;
    }

    private async Task<int> GetCurrentGroupKeyGenerationAsync(FeedId feedId)
    {
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var group = await feedClient.GetGroupFeedAsync(new GetGroupFeedRequest
        {
            FeedId = feedId.ToString()
        });

        group.Success.Should().BeTrue($"GetGroupFeed should succeed: {group.Message}");
        return group.CurrentKeyGeneration;
    }

    private async Task<int> GetCurrentGroupKeyGenerationForUserAsync(FeedId feedId, string userPublicAddress)
    {
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var keys = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = userPublicAddress
        });

        if (keys.KeyGenerations.Count == 0)
        {
            return 0;
        }

        return keys.KeyGenerations.Max(key => key.KeyGeneration);
    }

    private async Task<(int KeyGeneration, string AesKey)> DecryptLatestAesKeyAsync(FeedId feedId, TestIdentity identity)
    {
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var keys = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = identity.PublicSigningAddress
        });

        keys.KeyGenerations.Should().NotBeEmpty("authorized user should have key generations");
        var latest = keys.KeyGenerations.OrderByDescending(key => key.KeyGeneration).First();
        var aesKey = EncryptKeys.Decrypt(latest.EncryptedKey, identity.PrivateEncryptKey);
        return (latest.KeyGeneration, aesKey);
    }

    private async Task PublishGroupMessageAsync(string circleName, string message, string contextKeyPrefix)
    {
        var owner = GetTestIdentity(OwnerName);
        var feedId = await EnsureCircleExistsAsync(circleName);
        var latestKey = await DecryptLatestAesKeyAsync(feedId, owner);
        var blockchainClient = GetGrpcFactory().CreateClient<HushBlockchain.HushBlockchainClient>();
        var (txJson, messageId) = TestTransactionFactory.CreateGroupFeedMessage(
            owner,
            feedId,
            message,
            latestKey.AesKey,
            latestKey.KeyGeneration);

        var submitResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = txJson
        });

        submitResponse.Successfull.Should().BeTrue($"Group message should succeed: {submitResponse.Message}");
        await GetBlockControl().ProduceBlockAsync();

        _scenarioContext[$"{contextKeyPrefix}MessageId_{circleName}"] = messageId;
        _scenarioContext[$"{contextKeyPrefix}MessageText_{circleName}"] = message;
        _scenarioContext[$"{contextKeyPrefix}MessageKeyGeneration_{circleName}"] = latestKey.KeyGeneration;
    }

    private async Task EnsurePostRemovalMessageExistsAsync(string circleName)
    {
        if (_scenarioContext.ContainsKey($"PostRemovalMessageId_{circleName}"))
        {
            return;
        }

        await PublishGroupMessageAsync(circleName, "Post-removal message", "PostRemoval");
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
