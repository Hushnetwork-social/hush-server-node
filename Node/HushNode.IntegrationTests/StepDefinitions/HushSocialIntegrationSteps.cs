using FluentAssertions;
using Google.Protobuf;
using System.Text;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.Storage;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;
using HushServerNode.Testing;
using Microsoft.Extensions.DependencyInjection;
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
    private const string SameBlockInnerCircleOwnerNamesKey = "SameBlockInnerCircleOwnerNames";
    private const string SameBlockInnerCircleCreateResponsesKey = "SameBlockInnerCircleCreateResponses";
    private const string BootstrapPeerNamesKey = "HushSocialBootstrapPeerNames";
    private const string PendingFollowersKey = "HushSocialPendingFollowers";
    private const string ApprovedFollowersKey = "HushSocialApprovedFollowers";
    private const string SocialPostsByTitleKey = "Feat086SocialPostsByTitle";
    private const string LastSocialPostCreateResponseKey = "Feat086LastSocialPostCreateResponse";
    private const string LastSocialPostPermalinkResponseKey = "Feat086LastSocialPostPermalinkResponse";
    private const string LastSocialComposerContractResponseKey = "Feat086LastSocialComposerContractResponse";
    private const string LastSocialReactionTallyResponseKey = "Feat087LastSocialReactionTallyResponse";
    private const string CurrentReactionPostTitleKey = "Feat087CurrentReactionPostTitle";
    private const string SocialThreadEntriesByContentKey = "Feat088SocialThreadEntriesByContent";
    private const string SocialThreadPostTitlesByContentKey = "Feat088SocialThreadPostTitlesByContent";
    private const string LastSocialCommentsPageResponseKey = "Feat088LastSocialCommentsPageResponse";
    private const string LastSocialThreadRepliesResponseKey = "Feat088LastSocialThreadRepliesResponse";

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

    [Given(@"(.*) FEAT-085 owners are registered with personal feeds")]
    public async Task GivenFeat085OwnersAreRegisteredWithPersonalFeeds(int ownerCount)
    {
        ownerCount.Should().BeGreaterThan(0);

        var ownerNames = Enumerable.Range(1, ownerCount)
            .Select(i => $"RaceOwner{i}")
            .ToArray();

        foreach (var ownerName in ownerNames)
        {
            await EnsureUserRegisteredWithPersonalFeedAsync(ownerName);
        }

        _scenarioContext[SameBlockInnerCircleOwnerNamesKey] = ownerNames;
    }

    [When(@"all FEAT-085 owners submit CreateInnerCircle before block indexing")]
    public async Task WhenAllFeat085OwnersSubmitCreateInnerCircleBeforeBlockIndexing()
    {
        _scenarioContext.TryGetValue(SameBlockInnerCircleOwnerNamesKey, out var ownerNamesObj)
            .Should()
            .BeTrue("expected owner names prepared by previous step");
        ownerNamesObj.Should().BeOfType<string[]>();
        var ownerNames = (string[])ownerNamesObj!;

        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var responses = new Dictionary<string, CreateInnerCircleResponse>(StringComparer.Ordinal);

        foreach (var ownerName in ownerNames)
        {
            var owner = GetTestIdentity(ownerName);
            var response = await feedClient.CreateInnerCircleAsync(new CreateInnerCircleRequest
            {
                OwnerPublicAddress = owner.PublicSigningAddress,
                RequesterPublicAddress = owner.PublicSigningAddress
            });

            responses[ownerName] = response;
        }

        _scenarioContext[SameBlockInnerCircleCreateResponsesKey] = responses;
    }

    [Then(@"all FEAT-085 create responses should be accepted pre-indexing")]
    public void ThenAllFeat085CreateResponsesShouldBeAcceptedPreIndexing()
    {
        _scenarioContext.TryGetValue(SameBlockInnerCircleCreateResponsesKey, out var responsesObj)
            .Should()
            .BeTrue("expected captured CreateInnerCircle responses");
        responsesObj.Should().BeOfType<Dictionary<string, CreateInnerCircleResponse>>();
        var responses = (Dictionary<string, CreateInnerCircleResponse>)responsesObj!;

        responses.Should().NotBeEmpty();
        foreach (var (ownerName, response) in responses)
        {
            response.Success.Should().BeTrue(
                $"CreateInnerCircle should be accepted pre-indexing for {ownerName}: {response.Message}");
        }
    }

    [Then(@"all FEAT-085 owners should have exactly one Inner Circle")]
    public async Task ThenAllFeat085OwnersShouldHaveExactlyOneInnerCircle()
    {
        _scenarioContext.TryGetValue(SameBlockInnerCircleOwnerNamesKey, out var ownerNamesObj)
            .Should()
            .BeTrue("expected owner names prepared by previous step");
        ownerNamesObj.Should().BeOfType<string[]>();
        var ownerNames = (string[])ownerNamesObj!;

        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var seenFeedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ownerName in ownerNames)
        {
            var owner = GetTestIdentity(ownerName);
            var response = await feedClient.GetInnerCircleAsync(new GetInnerCircleRequest
            {
                OwnerPublicAddress = owner.PublicSigningAddress
            });

            response.Success.Should().BeTrue($"GetInnerCircle should succeed for {ownerName}: {response.Message}");
            response.Exists.Should().BeTrue($"Inner Circle should exist for {ownerName}");
            response.FeedId.Should().NotBeNullOrWhiteSpace($"Inner Circle FeedId should exist for {ownerName}");
            seenFeedIds.Add(response.FeedId!);
        }

        seenFeedIds.Count.Should().Be(ownerNames.Length, "each owner should have an isolated Inner Circle feed");
    }

    [Given(@"Owner has created an Open post ""(.*)""")]
    public async Task GivenOwnerHasCreatedAnOpenPost(string postTitle)
    {
        var owner = GetTestIdentity(OwnerName);
        var response = await SubmitSocialPostAsync(
            owner,
            postTitle,
            SocialPostVisibility.Open);

        response.Success.Should().BeTrue($"Open post creation should succeed: {response.Message}");
        StoreSocialPost(postTitle, response);
        _scenarioContext[LastSocialPostCreateResponseKey] = response;
    }

    [Given(@"Owner has created a Close post ""(.*)"" for Inner Circle")]
    public async Task GivenOwnerHasCreatedAClosePostForInnerCircle(string postTitle)
    {
        var owner = GetTestIdentity(OwnerName);
        var innerCircleFeedId = await EnsureOwnerInnerCircleExistsAsync();
        var response = await SubmitSocialPostAsync(
            owner,
            postTitle,
            SocialPostVisibility.Private,
            [FeedIdHandler.CreateFromString(innerCircleFeedId)]);

        response.Success.Should().BeTrue($"Private post creation should succeed: {response.Message}");
        StoreSocialPost(postTitle, response);
        _scenarioContext[LastSocialPostCreateResponseKey] = response;
    }

    [When(@"(.*) reacts to ""(.*)"" with ""(.*)""")]
    public async Task WhenUserReactsToPostWith(string userName, string postTitle, string emojiName)
    {
        await SubmitSocialReactionAsync(userName, postTitle, emojiName);
    }

    [When(@"(.*) changes reaction on ""(.*)"" to ""(.*)""")]
    public async Task WhenUserChangesReactionOnPostTo(string userName, string postTitle, string emojiName)
    {
        await SubmitSocialReactionAsync(userName, postTitle, emojiName);
    }

    [Then(@"authorized viewers should see reaction tally updates on ""(.*)""")]
    public async Task ThenAuthorizedViewersShouldSeeReactionTallyUpdatesOn(string postTitle)
    {
        var follower = GetTestIdentity("FollowerA");
        await OpenPermalinkAsAuthenticatedUserAsync(postTitle, follower.PublicSigningAddress);

        var permalinkResponse = GetLastPermalinkResponse();
        permalinkResponse.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateAllowed);
        permalinkResponse.ReactionScopeId.Should().NotBeNullOrWhiteSpace();

        var tallyResponse = await GetSocialReactionTalliesAsync(postTitle);
        tallyResponse.Should().ContainSingle();

        var tally = tallyResponse.Single();
        tally.TotalCount.Should().Be(2);

        var counts = DecodeToyCounts(tally);
        counts[ResolveReactionIndex("thumbs_up")].Should().Be(1);
        counts[ResolveReactionIndex("fire")].Should().Be(1);

        _scenarioContext[LastSocialReactionTallyResponseKey] = tallyResponse;
        _scenarioContext[CurrentReactionPostTitleKey] = postTitle;
    }

    [Then(@"the backend should not expose exact reaction choice per individual user to other viewers")]
    public async Task ThenTheBackendShouldNotExposeExactReactionChoicePerIndividualUserToOtherViewers()
    {
        _scenarioContext.TryGetValue(CurrentReactionPostTitleKey, out var postTitleObj)
            .Should().BeTrue("expected a previously asserted FEAT-087 post title");
        postTitleObj.Should().BeOfType<string>();

        var owner = GetTestIdentity(OwnerName);
        await OpenPermalinkAsAuthenticatedUserAsync((string)postTitleObj!, owner.PublicSigningAddress);

        var permalinkResponse = GetLastPermalinkResponse();
        var permalinkJson = JsonFormatter.Default.Format(permalinkResponse);
        var followerA = GetTestIdentity("FollowerA");
        var followerB = GetTestIdentity("FollowerB");

        permalinkJson.Should().NotContain(followerA.PublicSigningAddress);
        permalinkJson.Should().NotContain(followerB.PublicSigningAddress);
        permalinkJson.Should().NotContain("thumbs_up");
        permalinkJson.Should().NotContain("heart");
        permalinkJson.Should().NotContain("fire");

        if (_scenarioContext.TryGetValue(LastSocialReactionTallyResponseKey, out var tallyObj)
            && tallyObj is IReadOnlyList<MessageReactionTally> tallyResponse)
        {
            var tallyJson = string.Join(
                "\n",
                tallyResponse.Select(t => $"{Convert.ToHexString(t.MessageId.Value.ToByteArray())}:{t.TotalCount}"));
            tallyJson.Should().NotContain(followerA.PublicSigningAddress);
            tallyJson.Should().NotContain(followerB.PublicSigningAddress);
            tallyJson.Should().NotContain("thumbs_up");
            tallyJson.Should().NotContain("heart");
            tallyJson.Should().NotContain("fire");
        }
    }

    [Then(@"only one active reaction should exist for FollowerA on ""(.*)""")]
    public async Task ThenOnlyOneActiveReactionShouldExistForFollowerAOn(string postTitle)
    {
        var tallyResponse = await GetSocialReactionTalliesAsync(postTitle);
        var tally = tallyResponse.Single();

        tally.TotalCount.Should().Be(2, "updating a reaction should replace the previous vote, not add a third one");

        var counts = DecodeToyCounts(tally);
        counts[ResolveReactionIndex("thumbs_up")].Should().Be(0);
        counts[ResolveReactionIndex("heart")].Should().Be(1);
        counts[ResolveReactionIndex("fire")].Should().Be(1);

        var nullifierExists = await GetReactionService()
            .NullifierExistsAsync(GetSocialReactionNullifier("FollowerA", postTitle));

        nullifierExists.Should().BeTrue("FollowerA should still have exactly one tracked nullifier after updating the reaction");
    }

    [When(@"(.*) comments ""(.*)"" on post ""(.*)""")]
    public async Task WhenUserCommentsOnPost(string userName, string commentText, string postTitle)
    {
        await SubmitSocialThreadEntryAsync(userName, postTitle, commentText);
    }

    [When(@"(.*) replies ""(.*)"" to comment ""(.*)""")]
    public async Task WhenUserRepliesToComment(string userName, string replyText, string commentText)
    {
        var threadEntries = GetOrCreateSocialThreadEntriesByContent();
        threadEntries.TryGetValue(commentText, out var commentId)
            .Should()
            .BeTrue($"comment '{commentText}' should exist before replying");

        await SubmitSocialThreadEntryAsync(
            userName,
            FindPostTitleForThreadEntry(new FeedMessageId(commentId!.Value)),
            replyText,
            commentId);
    }

    [When(@"(.*) reacts to comment ""(.*)"" with ""(.*)""")]
    public async Task WhenUserReactsToComment(string userName, string commentText, string emojiName)
    {
        await SubmitSocialThreadReactionAsync(userName, commentText, emojiName);
    }

    [When(@"(.*) reacts to reply ""(.*)"" with ""(.*)""")]
    public async Task WhenUserReactsToReply(string userName, string replyText, string emojiName)
    {
        await SubmitSocialThreadReactionAsync(userName, replyText, emojiName);
    }

    [Then(@"FollowerA should see comment ""(.*)"" and reply ""(.*)""")]
    public async Task ThenFollowerAShouldSeeCommentAndReply(string commentText, string replyText)
    {
        var follower = GetTestIdentity("FollowerA");
        await AssertAuthorizedThreadViewAsync(follower.PublicSigningAddress, "Architecture thread", commentText, replyText);
    }

    [When(@"FollowerC opens post ""(.*)""")]
    public async Task WhenFollowerCOpensPost(string postTitle)
    {
        var follower = GetTestIdentity("FollowerC");
        await LoadCommentsPageAsync(postTitle, follower.PublicSigningAddress, true);

        var threadEntries = GetOrCreateSocialThreadEntriesByContent();
        if (threadEntries.TryGetValue("Looks good", out var commentId))
        {
            await LoadRepliesPageAsync(postTitle, commentId, follower.PublicSigningAddress, true);
        }
    }

    [Then(@"authorized viewers should see reaction tally updates on comment ""(.*)""")]
    public async Task ThenAuthorizedViewersShouldSeeReactionTallyUpdatesOnComment(string commentText)
    {
        await AssertAuthorizedThreadReactionVisibleAsync("Architecture thread", commentText, "FollowerA");
        await AssertAuthorizedThreadReactionVisibleAsync("Architecture thread", commentText, "Owner");
    }

    [Then(@"authorized viewers should see reaction tally updates on reply ""(.*)""")]
    public async Task ThenAuthorizedViewersShouldSeeReactionTallyUpdatesOnReply(string replyText)
    {
        await AssertAuthorizedThreadReactionVisibleAsync("Architecture thread", replyText, "FollowerA");
        await AssertAuthorizedThreadReactionVisibleAsync("Architecture thread", replyText, "Owner");
    }

    [Then(@"FollowerC should receive access denied for post comments")]
    public void ThenFollowerCShouldReceiveAccessDeniedForPostComments()
    {
        var response = GetLastSocialCommentsPageResponse();
        response.Success.Should().BeFalse();
        response.Message.Should().Be("You do not have permission to access this private thread.");
    }

    [Then(@"FollowerC should receive access denied for post replies")]
    public void ThenFollowerCShouldReceiveAccessDeniedForPostReplies()
    {
        var response = GetLastSocialThreadRepliesResponse();
        response.Success.Should().BeFalse();
        response.Message.Should().Be("You do not have permission to access this private thread.");
    }

    [When(@"Owner requests social composer contract in private mode")]
    public async Task WhenOwnerRequestsSocialComposerContractInPrivateMode()
    {
        var owner = GetTestIdentity(OwnerName);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var response = await feedClient.GetSocialComposerContractAsync(new GetSocialComposerContractRequest
        {
            OwnerPublicAddress = owner.PublicSigningAddress,
            PreferredVisibility = SocialPostVisibilityProto.SocialPostVisibilityPrivate
        });

        _scenarioContext[LastSocialComposerContractResponseKey] = response;
    }

    [Then(@"social composer default visibility should be private")]
    public void ThenSocialComposerDefaultVisibilityShouldBePrivate()
    {
        var response = GetLastSocialComposerContractResponse();
        response.Success.Should().BeTrue($"Composer contract should succeed: {response.Message}");
        response.DefaultVisibility.Should().Be(SocialPostVisibilityProto.SocialPostVisibilityPrivate);
    }

    [Then(@"social composer should select Owner Inner Circle by default")]
    public async Task ThenSocialComposerShouldSelectOwnerInnerCircleByDefault()
    {
        var response = GetLastSocialComposerContractResponse();
        var ownerInnerCircle = await EnsureOwnerInnerCircleExistsAsync();
        response.SelectedCircleFeedIds.Should().Contain(ownerInnerCircle);

        var innerCircle = response.AvailableCircles.SingleOrDefault(x => x.FeedId == ownerInnerCircle);
        innerCircle.Should().NotBeNull("inner circle should be present in available circles");
        innerCircle!.IsSelectedByDefault.Should().BeTrue();
    }

    [Then(@"social composer should lock the last selected private circle")]
    public void ThenSocialComposerShouldLockTheLastSelectedPrivateCircle()
    {
        var response = GetLastSocialComposerContractResponse();
        response.SelectedCircleFeedIds.Should().ContainSingle();
        var selectedFeedId = response.SelectedCircleFeedIds.Single();
        var selectedCircle = response.AvailableCircles.Single(x => x.FeedId == selectedFeedId);
        selectedCircle.IsRemovable.Should().BeFalse("last selected private circle must be locked");
    }

    [Then(@"social composer submit should be allowed")]
    public void ThenSocialComposerSubmitShouldBeAllowed()
    {
        var response = GetLastSocialComposerContractResponse();
        response.CanSubmit.Should().BeTrue();
    }

    [When(@"an unauthenticated user opens permalink for post ""(.*)""")]
    public async Task WhenAnUnauthenticatedUserOpensPermalinkForPost(string postTitle)
    {
        var postId = GetStoredPostId(postTitle);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var response = await feedClient.GetSocialPostPermalinkAsync(new GetSocialPostPermalinkRequest
        {
            PostId = postId,
            IsAuthenticated = false
        });

        _scenarioContext[LastSocialPostPermalinkResponseKey] = response;
    }

    [When(@"FollowerC opens permalink for post ""(.*)""")]
    public async Task WhenFollowerCOpensPermalinkForPost(string postTitle)
    {
        var follower = GetTestIdentity("FollowerC");
        await OpenPermalinkAsAuthenticatedUserAsync(postTitle, follower.PublicSigningAddress);
    }

    [When(@"FollowerA opens permalink for post ""(.*)""")]
    public async Task WhenFollowerAOpensPermalinkForPost(string postTitle)
    {
        var follower = GetTestIdentity("FollowerA");
        await OpenPermalinkAsAuthenticatedUserAsync(postTitle, follower.PublicSigningAddress);
    }

    [Then(@"the post content should be visible")]
    public void ThenThePostContentShouldBeVisible()
    {
        var response = GetLastPermalinkResponse();
        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateAllowed);
        response.Content.Should().NotBeNullOrWhiteSpace();
    }

    [Then(@"the public permalink should remain read-only for guests")]
    public void ThenThePublicPermalinkShouldRemainReadOnlyForGuests()
    {
        var response = GetLastPermalinkResponse();
        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateAllowed);
        response.CanInteract.Should().BeFalse();
        response.DenialKind.Should().Be(SocialPermalinkDenialKindProto.SocialPermalinkDenialKindNone);
    }

    [Then(@"a generic denial message should be returned")]
    public void ThenAGenericDenialMessageShouldBeReturned()
    {
        var response = GetLastPermalinkResponse();
        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateUnauthorizedDenied);
        response.OpenGraph.IsGenericPrivate.Should().BeTrue();
        response.OpenGraph.CacheControl.Should().Be("no-store");
    }

    [Then(@"no private metadata should be returned")]
    public void ThenNoPrivateMetadataShouldBeReturned()
    {
        var response = GetLastPermalinkResponse();
        response.AuthorPublicAddress.Should().BeNullOrEmpty();
        response.Content.Should().BeNullOrEmpty();
        response.CircleFeedIds.Should().BeEmpty();
        response.CreatedAtBlock.Should().Be(0);
    }

    [Then(@"the permalink denial contract should target guest account creation")]
    public void ThenThePermalinkDenialContractShouldTargetGuestAccountCreation()
    {
        var response = GetLastPermalinkResponse();
        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateGuestDenied);
        response.DenialKind.Should().Be(SocialPermalinkDenialKindProto.SocialPermalinkDenialKindGuestCreateAccount);
        response.PrimaryCtaLabel.Should().Be("Create account");
        response.PrimaryCtaRoute.Should().Be($"/auth?returnTo=%2Fsocial%2Fpost%2F{response.PostId}");
    }

    [Then(@"the permalink denial contract should request access from owner")]
    public void ThenThePermalinkDenialContractShouldRequestAccessFromOwner()
    {
        var response = GetLastPermalinkResponse();
        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateUnauthorizedDenied);
        response.DenialKind.Should().Be(SocialPermalinkDenialKindProto.SocialPermalinkDenialKindUnauthorizedRequestAccess);
        response.PrimaryCtaLabel.Should().Be("Request access");
        response.PrimaryCtaRoute.Should().Be("/social/following");
    }

    [When(@"Owner submits an Open post with 5 valid media attachments")]
    public async Task WhenOwnerSubmitsAnOpenPostWithFiveValidMediaAttachments()
    {
        var owner = GetTestIdentity(OwnerName);
        var attachments = Enumerable.Range(0, 5)
            .Select(i => new SocialPostAttachment(
                AttachmentId: $"att-{i + 1}",
                MimeType: i % 2 == 0 ? "image/png" : "video/mp4",
                Size: 1024 * (i + 1),
                FileName: $"file-{i + 1}.bin",
                Hash: $"hash-{i + 1}",
                Kind: i % 2 == 0 ? SocialPostAttachmentKind.Image : SocialPostAttachmentKind.Video))
            .ToArray();

        var response = await SubmitSocialPostAsync(
            owner,
            "Post with max valid attachments",
            SocialPostVisibility.Open,
            null,
            attachments);
        _scenarioContext[LastSocialPostCreateResponseKey] = response;
    }

    [Then(@"the submission should be accepted")]
    public void ThenTheSubmissionShouldBeAccepted()
    {
        var response = (CreateSocialPostResponse)_scenarioContext[LastSocialPostCreateResponseKey];
        response.Success.Should().BeTrue($"Expected social post submission to succeed: {response.Message}");
    }

    [When(@"Owner submits an Open post with 6 media attachments")]
    public async Task WhenOwnerSubmitsAnOpenPostWithSixMediaAttachments()
    {
        var owner = GetTestIdentity(OwnerName);
        var attachments = Enumerable.Range(0, 6)
            .Select(i => new SocialPostAttachment(
                AttachmentId: $"att-over-{i + 1}",
                MimeType: "image/png",
                Size: 1024,
                FileName: $"over-{i + 1}.png",
                Hash: $"hash-over-{i + 1}",
                Kind: SocialPostAttachmentKind.Image))
            .ToArray();

        var response = await SubmitSocialPostAsync(
            owner,
            "Post exceeding attachment count",
            SocialPostVisibility.Open,
            null,
            attachments);
        _scenarioContext[LastSocialPostCreateResponseKey] = response;
    }

    [Then(@"the submission should be rejected with a count limit error")]
    public void ThenTheSubmissionShouldBeRejectedWithACountLimitError()
    {
        var response = (CreateSocialPostResponse)_scenarioContext[LastSocialPostCreateResponseKey];
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(SocialPostContractErrorCode.AttachmentCountExceeded.ToString());
    }

    [When(@"Owner submits an Open post with a media attachment over max size")]
    public async Task WhenOwnerSubmitsAnOpenPostWithAMediaAttachmentOverMaxSize()
    {
        var owner = GetTestIdentity(OwnerName);
        var response = await SubmitSocialPostAsync(
            owner,
            "Post exceeding size",
            SocialPostVisibility.Open,
            null,
            [
                new SocialPostAttachment(
                    AttachmentId: "oversize-1",
                    MimeType: "video/mp4",
                    Size: SocialPostContractRules.MaxAttachmentSizeBytes + 1,
                    FileName: "oversize.mp4",
                    Hash: "oversize-hash",
                    Kind: SocialPostAttachmentKind.Video)
            ]);

        _scenarioContext[LastSocialPostCreateResponseKey] = response;
    }

    [Then(@"the submission should be rejected with a size limit error")]
    public void ThenTheSubmissionShouldBeRejectedWithASizeLimitError()
    {
        var response = (CreateSocialPostResponse)_scenarioContext[LastSocialPostCreateResponseKey];
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(SocialPostContractErrorCode.AttachmentSizeExceeded.ToString());
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

    private async Task EnsureUserRegisteredWithPersonalFeedAsync(string userName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var hasPersonalFeed = await feedClient.HasPersonalFeedAsync(new HasPersonalFeedRequest
        {
            PublicPublicKey = identity.PublicSigningAddress
        });

        if (hasPersonalFeed.FeedAvailable)
        {
            _scenarioContext[$"Identity_{userName}"] = identity;
            return;
        }

        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();
        var blockControl = GetBlockControl();

        var identityTxJson = TestTransactionFactory.CreateIdentityRegistration(identity);
        var identityResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = identityTxJson
        });
        identityResponse.Successfull.Should().BeTrue($"Identity registration for {userName} should succeed");
        await blockControl.ProduceBlockAsync();

        var personalFeedTxJson = TestTransactionFactory.CreatePersonalFeed(identity);
        var personalFeedResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = personalFeedTxJson
        });
        personalFeedResponse.Successfull.Should().BeTrue($"Personal feed creation for {userName} should succeed");
        await blockControl.ProduceBlockAsync();

        _scenarioContext[$"Identity_{userName}"] = identity;
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

    private void StoreSocialPost(string title, CreateSocialPostResponse response)
    {
        var postsByTitle = GetOrCreateSocialPostsByTitle();
        response.Permalink.Should().NotBeNullOrWhiteSpace("successful post creation should return permalink");

        var postId = response.Permalink!.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        postsByTitle[title] = postId;
    }

    private string GetStoredPostId(string title)
    {
        var postsByTitle = GetOrCreateSocialPostsByTitle();
        postsByTitle.TryGetValue(title, out var postId)
            .Should()
            .BeTrue($"post '{title}' should have been created and stored");
        return postId!;
    }

    private async Task SubmitSocialThreadEntryAsync(
        string userName,
        string postTitle,
        string content,
        FeedMessageId? replyToMessageId = null)
    {
        var identity = GetTestIdentity(userName);
        var postId = Guid.Parse(GetStoredPostId(postTitle));
        var blockchainClient = GetGrpcFactory().CreateClient<HushBlockchain.HushBlockchainClient>();
        var (signedTransaction, messageId) = TestTransactionFactory.CreateSocialThreadEntry(
            identity,
            postId,
            content,
            replyToMessageId);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction
        });

        response.Successfull.Should().BeTrue($"Social thread entry should succeed: {response.Message}");
        await GetBlockControl().ProduceBlockAsync();

        GetOrCreateSocialThreadEntriesByContent()[content] = messageId;
        GetOrCreateSocialThreadPostTitlesByContent()[content] = postTitle;
    }

    private async Task AssertAuthorizedThreadViewAsync(
        string requesterPublicAddress,
        string postTitle,
        string commentText,
        string replyText)
    {
        var commentsResponse = await LoadCommentsPageAsync(postTitle, requesterPublicAddress, true);
        commentsResponse.Success.Should().BeTrue($"Authorized comment retrieval should succeed: {commentsResponse.Message}");
        commentsResponse.Comments.Should().ContainSingle(comment => comment.Content == commentText);

        var comment = commentsResponse.Comments.Single(comment => comment.Content == commentText);
        comment.Kind.Should().Be(SocialThreadEntryKindProto.SocialThreadEntryKindComment);

        var repliesResponse = await LoadRepliesPageAsync(postTitle, Guid.Parse(comment.EntryId), requesterPublicAddress, true);
        repliesResponse.Success.Should().BeTrue($"Authorized reply retrieval should succeed: {repliesResponse.Message}");
        repliesResponse.Replies.Should().ContainSingle(reply => reply.Content == replyText);

        var reply = repliesResponse.Replies.Single(entry => entry.Content == replyText);
        reply.Kind.Should().Be(SocialThreadEntryKindProto.SocialThreadEntryKindReply);
        reply.ThreadRootId.Should().Be(comment.EntryId);
    }

    private async Task<GetSocialCommentsPageResponse> LoadCommentsPageAsync(
        string postTitle,
        string? requesterPublicAddress,
        bool isAuthenticated)
    {
        var postId = GetStoredPostId(postTitle);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var response = await feedClient.GetSocialCommentsPageAsync(new GetSocialCommentsPageRequest
        {
            PostId = postId,
            RequesterPublicAddress = requesterPublicAddress ?? string.Empty,
            IsAuthenticated = isAuthenticated,
            Limit = 10
        });

        _scenarioContext[LastSocialCommentsPageResponseKey] = response;
        return response;
    }

    private async Task<GetSocialThreadRepliesResponse> LoadRepliesPageAsync(
        string postTitle,
        FeedMessageId threadRootId,
        string? requesterPublicAddress,
        bool isAuthenticated)
    {
        return await LoadRepliesPageAsync(postTitle, threadRootId.Value, requesterPublicAddress, isAuthenticated);
    }

    private async Task<GetSocialThreadRepliesResponse> LoadRepliesPageAsync(
        string postTitle,
        Guid threadRootId,
        string? requesterPublicAddress,
        bool isAuthenticated)
    {
        var postId = GetStoredPostId(postTitle);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var response = await feedClient.GetSocialThreadRepliesAsync(new GetSocialThreadRepliesRequest
        {
            PostId = postId,
            ThreadRootId = threadRootId.ToString("D"),
            RequesterPublicAddress = requesterPublicAddress ?? string.Empty,
            IsAuthenticated = isAuthenticated,
            Limit = 5
        });

        _scenarioContext[LastSocialThreadRepliesResponseKey] = response;
        return response;
    }

    private string FindPostTitleForThreadEntry(FeedMessageId threadEntryId)
    {
        var threadEntries = GetOrCreateSocialThreadEntriesByContent();
        var postTitles = GetOrCreateSocialThreadPostTitlesByContent();
        foreach (var (content, entryId) in threadEntries)
        {
            if (entryId == threadEntryId && postTitles.TryGetValue(content, out var postTitle))
            {
                return postTitle;
            }
        }

        throw new InvalidOperationException($"Unable to resolve post title for thread entry {threadEntryId}.");
    }

    private Dictionary<string, FeedMessageId> GetOrCreateSocialThreadEntriesByContent()
    {
        if (_scenarioContext.TryGetValue(SocialThreadEntriesByContentKey, out var existing)
            && existing is Dictionary<string, FeedMessageId> map)
        {
            return map;
        }

        var created = new Dictionary<string, FeedMessageId>(StringComparer.Ordinal);
        _scenarioContext[SocialThreadEntriesByContentKey] = created;
        return created;
    }

    private Dictionary<string, string> GetOrCreateSocialThreadPostTitlesByContent()
    {
        if (_scenarioContext.TryGetValue(SocialThreadPostTitlesByContentKey, out var existing)
            && existing is Dictionary<string, string> map)
        {
            return map;
        }

        var created = new Dictionary<string, string>(StringComparer.Ordinal);
        _scenarioContext[SocialThreadPostTitlesByContentKey] = created;
        return created;
    }

    private Dictionary<string, string> GetOrCreateSocialPostsByTitle()
    {
        if (_scenarioContext.TryGetValue(SocialPostsByTitleKey, out var existing)
            && existing is Dictionary<string, string> map)
        {
            return map;
        }

        var created = new Dictionary<string, string>(StringComparer.Ordinal);
        _scenarioContext[SocialPostsByTitleKey] = created;
        return created;
    }

    private async Task<CreateSocialPostResponse> SubmitSocialPostAsync(
        TestIdentity author,
        string content,
        SocialPostVisibility visibility,
        IReadOnlyCollection<FeedId>? circleFeedIds = null,
        IReadOnlyCollection<SocialPostAttachment>? attachments = null)
    {
        var blockchainClient = GetGrpcFactory().CreateClient<HushBlockchain.HushBlockchainClient>();
        var (signedTransaction, postId) = TestTransactionFactory.CreateSocialPost(
            author,
            content,
            visibility,
            circleFeedIds,
            attachments);

        var request = new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction
        };

        if (attachments is { Count: > 0 })
        {
            request.Attachments.AddRange(attachments.Select(CreateAttachmentBlob));
        }

        var submitResponse = await blockchainClient.SubmitSignedTransactionAsync(request);

        if (submitResponse.Successfull)
        {
            await GetBlockControl().ProduceBlockAsync();
            return new CreateSocialPostResponse
            {
                Success = true,
                Message = submitResponse.Message,
                Permalink = $"/social/post/{postId:D}"
            };
        }

        return new CreateSocialPostResponse
        {
            Success = false,
            Message = submitResponse.Message,
            ErrorCode = ResolveCreateSocialPostErrorCode(content, visibility, circleFeedIds, attachments)
        };
    }

    private static string ResolveCreateSocialPostErrorCode(
        string content,
        SocialPostVisibility visibility,
        IReadOnlyCollection<FeedId>? circleFeedIds,
        IReadOnlyCollection<SocialPostAttachment>? attachments)
    {
        var audienceValidation = SocialPostContractRules.ValidateAudience(new SocialPostAudience(
            visibility,
            (circleFeedIds ?? Array.Empty<FeedId>()).Select(x => x.ToString()).ToArray()));
        if (!audienceValidation.IsValid)
        {
            return audienceValidation.ErrorCode.ToString();
        }

        var attachmentValidation = SocialPostContractRules.ValidateAttachments(
            (attachments ?? Array.Empty<SocialPostAttachment>()).ToArray());
        if (!attachmentValidation.IsValid)
        {
            return attachmentValidation.ErrorCode.ToString();
        }

        return string.IsNullOrWhiteSpace(content) ? "SOCIAL_POST_EMPTY_CONTENT" : "SOCIAL_POST_REJECTED";
    }

    private async Task OpenPermalinkAsAuthenticatedUserAsync(string postTitle, string requesterPublicAddress)
    {
        var postId = GetStoredPostId(postTitle);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var response = await feedClient.GetSocialPostPermalinkAsync(new GetSocialPostPermalinkRequest
        {
            PostId = postId,
            IsAuthenticated = true,
            RequesterPublicAddress = requesterPublicAddress
        });

        _scenarioContext[LastSocialPostPermalinkResponseKey] = response;
    }

    private async Task SubmitSocialReactionAsync(string userName, string postTitle, string emojiName)
    {
        var identity = GetTestIdentity(userName);
        var postId = Guid.Parse(GetStoredPostId(postTitle));
        var reactionScopeId = new FeedId(postId);
        var messageId = new FeedMessageId(postId);
        var nullifier = GetSocialReactionNullifier(userName, postTitle);
        var signedTransaction = TestTransactionFactory.CreateDevModeReaction(
            identity,
            reactionScopeId,
            messageId,
            nullifier,
            ResolveReactionIndex(emojiName));

        var blockchainClient = GetGrpcFactory().CreateClient<HushBlockchain.HushBlockchainClient>();
        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction
        });

        response.Successfull.Should().BeTrue($"Reaction submission should succeed for {userName}: {response.Message}");
        await GetBlockControl().ProduceBlockAsync();
    }

    private async Task SubmitSocialThreadReactionAsync(string userName, string threadEntryContent, string emojiName)
    {
        var threadEntries = GetOrCreateSocialThreadEntriesByContent();
        threadEntries.TryGetValue(threadEntryContent, out var threadEntryId)
            .Should()
            .BeTrue($"thread entry '{threadEntryContent}' should exist before reacting");

        var postTitle = FindPostTitleForThreadEntry(new FeedMessageId(threadEntryId!.Value));
        var identity = GetTestIdentity(userName);
        var postId = Guid.Parse(GetStoredPostId(postTitle));
        var reactionScopeId = new FeedId(postId);
        var nullifier = GetSocialThreadReactionNullifier(userName, threadEntryContent);
        var signedTransaction = TestTransactionFactory.CreateDevModeReaction(
            identity,
            reactionScopeId,
            new FeedMessageId(threadEntryId.Value),
            nullifier,
            ResolveReactionIndex(emojiName));

        var blockchainClient = GetGrpcFactory().CreateClient<HushBlockchain.HushBlockchainClient>();
        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction
        });

        response.Successfull.Should().BeTrue($"Thread reaction submission should succeed for {userName}: {response.Message}");
        await GetBlockControl().ProduceBlockAsync();
    }

    private async Task<IReadOnlyList<MessageReactionTally>> GetSocialReactionTalliesAsync(string postTitle)
    {
        var postId = Guid.Parse(GetStoredPostId(postTitle));
        var tallies = await GetReactionService().GetTalliesAsync(
            new FeedId(postId),
            [new FeedMessageId(postId)]);

        return tallies.ToList();
    }

    private async Task<IReadOnlyList<MessageReactionTally>> GetSocialReactionTalliesAsync(string postTitle, FeedMessageId messageId)
    {
        var postId = Guid.Parse(GetStoredPostId(postTitle));
        var tallies = await GetReactionService().GetTalliesAsync(
            new FeedId(postId),
            [messageId]);

        return tallies.ToList();
    }

    private static byte[] GetSocialReactionNullifier(string userName, string postTitle)
    {
        var material = Encoding.UTF8.GetBytes($"feat087:{userName}:{postTitle}");
        return System.Security.Cryptography.SHA256.HashData(material);
    }

    private static byte[] GetSocialThreadReactionNullifier(string userName, string threadEntryContent)
    {
        var material = Encoding.UTF8.GetBytes($"feat088-thread:{userName}:{threadEntryContent}");
        return System.Security.Cryptography.SHA256.HashData(material);
    }

    private static int ResolveReactionIndex(string emojiName) => emojiName switch
    {
        "thumbs_up" => 0,
        "heart" => 1,
        "fire" => 2,
        _ => throw new InvalidOperationException($"Unsupported test emoji label '{emojiName}'.")
    };

    private static int[] DecodeToyCounts(MessageReactionTally tally)
    {
        var curve = new BabyJubJubCurve();
        var counts = new int[tally.TallyC1X.Length];

        for (var index = 0; index < tally.TallyC1X.Length; index++)
        {
            var point = HushShared.Reactions.Model.ECPoint.FromCoordinates(
                tally.TallyC1X[index],
                tally.TallyC1Y[index]);

            counts[index] = DecodeGeneratorMultiple(curve, point);
        }

        return counts;
    }

    private static int DecodeGeneratorMultiple(IBabyJubJub curve, HushShared.Reactions.Model.ECPoint point)
    {
        for (var multiplier = 0; multiplier <= 8; multiplier++)
        {
            var expected = multiplier == 0
                ? curve.Identity
                : curve.ScalarMul(curve.Generator, multiplier);

            if (expected.X == point.X && expected.Y == point.Y)
            {
                return multiplier;
            }
        }

        throw new InvalidOperationException("Unable to decode reaction tally point in FEAT-087 test.");
    }

    private IReactionService GetReactionService() =>
        GetNode().Services.GetRequiredService<IReactionService>();

    private async Task AssertAuthorizedThreadReactionVisibleAsync(
        string postTitle,
        string threadEntryContent,
        string requesterUserName)
    {
        var threadEntries = GetOrCreateSocialThreadEntriesByContent();
        threadEntries.TryGetValue(threadEntryContent, out var threadEntryId)
            .Should()
            .BeTrue($"thread entry '{threadEntryContent}' should exist before asserting reactions");

        var tallies = await GetSocialReactionTalliesAsync(postTitle, new FeedMessageId(threadEntryId.Value));
        tallies.Should().ContainSingle($"thread entry '{threadEntryContent}' should have exactly one tally row");
        tallies[0].TotalCount.Should().BeGreaterThan(0, $"thread entry '{threadEntryContent}' should accumulate at least one reaction");

        var requester = GetTestIdentity(requesterUserName);
        var commentsResponse = await LoadCommentsPageAsync(postTitle, requester.PublicSigningAddress, true);
        var commentMatch = commentsResponse.Comments.FirstOrDefault(comment => comment.Content == threadEntryContent);
        if (commentMatch != null)
        {
            commentMatch.ReactionCount.Should().BeGreaterThan(0, $"authorized viewer '{requesterUserName}' should receive aggregate reaction count for comment '{threadEntryContent}'");
            return;
        }

        var postId = GetStoredPostId(postTitle);
        foreach (var comment in commentsResponse.Comments)
        {
            var repliesResponse = await LoadRepliesPageAsync(postTitle, Guid.Parse(comment.EntryId), requester.PublicSigningAddress, true);
            var replyMatch = repliesResponse.Replies.FirstOrDefault(reply => reply.Content == threadEntryContent);
            if (replyMatch != null)
            {
                replyMatch.ReactionCount.Should().BeGreaterThan(0, $"authorized viewer '{requesterUserName}' should receive aggregate reaction count for reply '{threadEntryContent}'");
                return;
            }
        }

        throw new InvalidOperationException($"Unable to locate thread entry '{threadEntryContent}' in authorized thread responses for post '{postId}'.");
    }

    private HushServerNode.HushServerNodeCore GetNode()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.NodeKey, out var nodeObj)
            && nodeObj is HushServerNode.HushServerNodeCore node)
        {
            return node;
        }

        throw new InvalidOperationException("HushServerNodeCore not found in ScenarioContext.");
    }

    private GetSocialPostPermalinkResponse GetLastPermalinkResponse()
    {
        _scenarioContext.TryGetValue(LastSocialPostPermalinkResponseKey, out var responseObj)
            .Should()
            .BeTrue("expected a previously fetched permalink response");
        responseObj.Should().BeOfType<GetSocialPostPermalinkResponse>();
        return (GetSocialPostPermalinkResponse)responseObj!;
    }

    private GetSocialCommentsPageResponse GetLastSocialCommentsPageResponse()
    {
        _scenarioContext.TryGetValue(LastSocialCommentsPageResponseKey, out var responseObj)
            .Should()
            .BeTrue("expected a previously fetched social comments page response");
        responseObj.Should().BeOfType<GetSocialCommentsPageResponse>();
        return (GetSocialCommentsPageResponse)responseObj!;
    }

    private GetSocialThreadRepliesResponse GetLastSocialThreadRepliesResponse()
    {
        _scenarioContext.TryGetValue(LastSocialThreadRepliesResponseKey, out var responseObj)
            .Should()
            .BeTrue("expected a previously fetched social thread replies response");
        responseObj.Should().BeOfType<GetSocialThreadRepliesResponse>();
        return (GetSocialThreadRepliesResponse)responseObj!;
    }

    private GetSocialComposerContractResponse GetLastSocialComposerContractResponse()
    {
        _scenarioContext.TryGetValue(LastSocialComposerContractResponseKey, out var responseObj)
            .Should()
            .BeTrue("expected a previously fetched social composer contract response");
        responseObj.Should().BeOfType<GetSocialComposerContractResponse>();
        return (GetSocialComposerContractResponse)responseObj!;
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

    private HushServerNode.Testing.BlockProductionControl GetBlockControl()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.BlockControlKey, out var controlObj)
            && controlObj is HushServerNode.Testing.BlockProductionControl blockControl)
        {
            return blockControl;
        }

        throw new InvalidOperationException("BlockProductionControl not found in ScenarioContext.");
    }

    private static AttachmentBlob CreateAttachmentBlob(SocialPostAttachment attachment)
    {
        var size = checked((int)attachment.Size);
        var buffer = new byte[size];
        for (var index = 0; index < buffer.Length; index++)
        {
            buffer[index] = (byte)(index % 251);
        }

        return new AttachmentBlob
        {
            AttachmentId = attachment.AttachmentId,
            EncryptedOriginal = ByteString.CopyFrom(buffer)
        };
    }
}
