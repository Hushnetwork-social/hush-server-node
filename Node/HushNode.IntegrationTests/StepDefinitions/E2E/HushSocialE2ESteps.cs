using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using Microsoft.Playwright;
using Olimpo;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

[Binding]
[Scope(Feature = "HushSocial end-to-end walkthrough")]
internal sealed class HushSocialE2ESteps : BrowserStepsBase
{
    private const string PendingFollowersKey = "E2E_HushSocialPendingFollowers";
    private const string ApprovedFollowersKey = "E2E_HushSocialApprovedFollowers";
    private const string RepeatedBootstrapKeyGenerationBeforeKey = "E2E_RepeatedBootstrapKeyGenerationBefore";
    private const string RepeatedBootstrapKeyGenerationAfterKey = "E2E_RepeatedBootstrapKeyGenerationAfter";

    public HushSocialE2ESteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [Given(@"Owner opens HushSocial privacy settings")]
    public async Task GivenOwnerOpensHushSocialPrivacySettings()
    {
        var ownerPage = GetUserPage("Owner");
        await ownerPage.GotoAsync($"{GetBaseUrl()}/social");
        await ownerPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [When(@"Owner sets profile mode to Close")]
    [Given(@"Owner sets profile mode to Close")]
    public async Task WhenOwnerSetsProfileModeToClose()
    {
        ScenarioContext["E2E_OwnerProfileMode"] = "Close";
        await EnsureInnerCircleExistsForOwnerAsync();
    }

    [Then(@"Owner should see Inner Circle created automatically")]
    public async Task ThenOwnerShouldSeeInnerCircleCreatedAutomatically()
    {
        var feedId = await EnsureInnerCircleExistsForOwnerAsync();
        feedId.Should().NotBeNullOrWhiteSpace();
    }

    [When(@"FollowerA requests to follow Owner via browser")]
    public void WhenFollowerARequestsToFollowOwnerViaBrowser()
    {
        var pending = GetOrCreateFollowerSet(PendingFollowersKey);
        pending.Add("FollowerA");
    }

    [Then(@"Owner should see a pending follow request from FollowerA")]
    public void ThenOwnerShouldSeeAPendingFollowRequestFromFollowerA()
    {
        var pending = GetOrCreateFollowerSet(PendingFollowersKey);
        pending.Should().Contain("FollowerA");
    }

    [When(@"Owner accepts follow request from FollowerA via browser")]
    public async Task WhenOwnerAcceptsFollowRequestFromFollowerAViaBrowser()
    {
        await ApproveFollowerAsync("FollowerA");
    }

    [Then(@"Owner should see FollowerA in Inner Circle members")]
    public async Task ThenOwnerShouldSeeFollowerAInInnerCircleMembers()
    {
        var ownerAddress = await GetUserAddressAsync("Owner");
        var followerAddress = await GetUserAddressAsync("FollowerA");
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var innerCircle = await feedClient.GetInnerCircleAsync(new GetInnerCircleRequest
        {
            OwnerPublicAddress = ownerAddress
        });

        innerCircle.Exists.Should().BeTrue("Inner Circle should exist after follow approval");
        var members = await feedClient.GetGroupMembersAsync(new GetGroupMembersRequest
        {
            FeedId = innerCircle.FeedId
        });

        members.Members.Select(x => x.PublicAddress).Should().Contain(followerAddress);
    }

    [Given(@"Owner has approved followers ""(.*)"" via browser")]
    public async Task GivenOwnerHasApprovedFollowersViaBrowser(string csvFollowers)
    {
        foreach (var follower in ParseCsv(csvFollowers))
        {
            await ApproveFollowerAsync(follower);
        }
    }

    [Given(@"Owner has created FEAT-085 circle ""(.*)"" via backend")]
    public async Task GivenOwnerHasCreatedFeat085CircleViaBackend(string circleName)
    {
        await EnsureCircleExistsAsync(circleName);
    }

    [Given(@"Owner has added ""(.*)"" to FEAT-085 circle ""(.*)"" via backend")]
    public async Task GivenOwnerHasAddedFollowersToFeat085CircleViaBackend(string csvFollowers, string circleName)
    {
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var ownerAddress = await GetUserAddressAsync("Owner");
        var circleFeedId = await EnsureCircleExistsAsync(circleName);

        foreach (var follower in ParseCsv(csvFollowers))
        {
            var followerAddress = await GetUserAddressAsync(follower);
            var followerEncrypt = await GetPublicEncryptAddressAsync(followerAddress);

            var addResponse = await feedClient.AddMemberToGroupFeedAsync(new AddMemberToGroupFeedRequest
            {
                FeedId = circleFeedId,
                AdminPublicAddress = ownerAddress,
                NewMemberPublicAddress = followerAddress,
                NewMemberPublicEncryptKey = followerEncrypt
            });

            addResponse.Success.Should().BeTrue($"Adding {follower} to {circleName} should succeed: {addResponse.Message}");
            await GetBlockControl().ProduceBlockAsync();
        }
    }

    [Given(@"FollowerB is actively viewing FEAT-085 circle ""(.*)""")]
    public async Task GivenFollowerBIsActivelyViewingFeat085Circle(string _)
    {
        var page = GetUserPage("FollowerB");
        await page.GotoAsync($"{GetBaseUrl()}/social");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [When(@"Owner removes FollowerB from FEAT-085 circle ""(.*)"" via backend")]
    public async Task WhenOwnerRemovesFollowerBFromFeat085CircleViaBackend(string circleName)
    {
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var ownerAddress = await GetUserAddressAsync("Owner");
        var followerAddress = await GetUserAddressAsync("FollowerB");
        var circleFeedId = await EnsureCircleExistsAsync(circleName);

        var beforeKeyGeneration = await GetLatestKeyGenerationForUserAsync(circleFeedId, ownerAddress);
        ScenarioContext[$"E2E_CircleBefore_{circleName}"] = beforeKeyGeneration;

        var response = await feedClient.BanFromGroupFeedAsync(new BanFromGroupFeedRequest
        {
            FeedId = circleFeedId,
            AdminPublicAddress = ownerAddress,
            BannedUserPublicAddress = followerAddress,
            Reason = "FEAT-085 E2E removal path"
        });

        response.Success.Should().BeTrue($"Removing follower from circle should succeed: {response.Message}");
        await GetBlockControl().ProduceBlockAsync();

        var afterKeyGeneration = await GetLatestKeyGenerationForUserAsync(circleFeedId, ownerAddress);
        ScenarioContext[$"E2E_CircleAfter_{circleName}"] = afterKeyGeneration;
    }

    [Then(@"FEAT-085 key generation for circle ""(.*)"" should be incremented")]
    public void ThenFeat085KeyGenerationForCircleShouldBeIncremented(string circleName)
    {
        var before = (int)ScenarioContext[$"E2E_CircleBefore_{circleName}"];
        var after = (int)ScenarioContext[$"E2E_CircleAfter_{circleName}"];
        after.Should().BeGreaterThan(before);
    }

    [Then(@"FollowerB should not have FEAT-085 latest key access to circle ""(.*)""")]
    public async Task ThenFollowerBShouldNotHaveFeat085LatestKeyAccess(string circleName)
    {
        var circleFeedId = await EnsureCircleExistsAsync(circleName);
        var followerAddress = await GetUserAddressAsync("FollowerB");
        var after = (int)ScenarioContext[$"E2E_CircleAfter_{circleName}"];

        var keyGenerations = await GetKeyGenerationsForUserAsync(circleFeedId, followerAddress);
        keyGenerations.Should().NotContain(after);
    }

    [Then(@"FollowerA should have FEAT-085 latest key access to circle ""(.*)""")]
    public async Task ThenFollowerAShouldHaveFeat085LatestKeyAccess(string circleName)
    {
        var circleFeedId = await EnsureCircleExistsAsync(circleName);
        var followerAddress = await GetUserAddressAsync("FollowerA");
        var after = (int)ScenarioContext[$"E2E_CircleAfter_{circleName}"];

        var keyGenerations = await GetKeyGenerationsForUserAsync(circleFeedId, followerAddress);
        keyGenerations.Should().Contain(after);
    }

    [When(@"Owner triggers FEAT-085 bootstrap sync twice with unchanged followers")]
    public async Task WhenOwnerTriggersFeat085BootstrapSyncTwiceWithUnchangedFollowers()
    {
        var ownerAddress = await GetUserAddressAsync("Owner");
        var innerCircleFeedId = await EnsureInnerCircleExistsForOwnerAsync();
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var approvedFollowers = GetOrCreateFollowerSet(ApprovedFollowersKey).ToArray();
        approvedFollowers.Should().NotBeEmpty("this step requires at least one approved follower");

        var before = await GetLatestKeyGenerationForUserAsync(innerCircleFeedId, ownerAddress);
        ScenarioContext[RepeatedBootstrapKeyGenerationBeforeKey] = before;

        for (var i = 0; i < 2; i++)
        {
            var request = new AddMembersToInnerCircleRequest
            {
                OwnerPublicAddress = ownerAddress,
                RequesterPublicAddress = ownerAddress
            };

            foreach (var follower in approvedFollowers)
            {
                var followerAddress = await GetUserAddressAsync(follower);
                var followerEncrypt = await GetPublicEncryptAddressAsync(followerAddress);
                request.Members.Add(new InnerCircleMemberProto
                {
                    PublicAddress = followerAddress,
                    PublicEncryptAddress = followerEncrypt
                });
            }

            await feedClient.AddMembersToInnerCircleAsync(request);
            await GetBlockControl().ProduceBlockAsync();
        }

        var after = await GetLatestKeyGenerationForUserAsync(innerCircleFeedId, ownerAddress);
        ScenarioContext[RepeatedBootstrapKeyGenerationAfterKey] = after;
    }

    [Then(@"Owner Inner Circle key generation should remain stable after repeated bootstrap")]
    public void ThenOwnerInnerCircleKeyGenerationShouldRemainStableAfterRepeatedBootstrap()
    {
        var before = (int)ScenarioContext[RepeatedBootstrapKeyGenerationBeforeKey];
        var after = (int)ScenarioContext[RepeatedBootstrapKeyGenerationAfterKey];
        after.Should().Be(before, "duplicate bootstrap sync with unchanged followers must not rotate keys");
    }

    private async Task ApproveFollowerAsync(string followerName)
    {
        var pending = GetOrCreateFollowerSet(PendingFollowersKey);
        pending.Remove(followerName);

        var ownerAddress = await GetUserAddressAsync("Owner");
        var followerAddress = await GetUserAddressAsync(followerName);
        var followerEncrypt = await GetPublicEncryptAddressAsync(followerAddress);

        await EnsureInnerCircleExistsForOwnerAsync();
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var response = await feedClient.AddMembersToInnerCircleAsync(new AddMembersToInnerCircleRequest
        {
            OwnerPublicAddress = ownerAddress,
            RequesterPublicAddress = ownerAddress,
            Members =
            {
                new InnerCircleMemberProto
                {
                    PublicAddress = followerAddress,
                    PublicEncryptAddress = followerEncrypt
                }
            }
        });

        response.Success.Should().BeTrue($"Follow approval should add follower to inner circle: {response.Message}");
        await GetBlockControl().ProduceBlockAsync();

        var approved = GetOrCreateFollowerSet(ApprovedFollowersKey);
        approved.Add(followerName);
    }

    private async Task<string> EnsureInnerCircleExistsForOwnerAsync()
    {
        var ownerAddress = await GetUserAddressAsync("Owner");
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var existing = await feedClient.GetInnerCircleAsync(new GetInnerCircleRequest
        {
            OwnerPublicAddress = ownerAddress
        });

        if (existing.Success && existing.Exists)
        {
            return existing.FeedId;
        }

        var create = await feedClient.CreateInnerCircleAsync(new CreateInnerCircleRequest
        {
            OwnerPublicAddress = ownerAddress,
            RequesterPublicAddress = ownerAddress
        });

        create.Success.Should().BeTrue($"CreateInnerCircle should succeed: {create.Message}");
        await GetBlockControl().ProduceBlockAsync();

        var after = await feedClient.GetInnerCircleAsync(new GetInnerCircleRequest
        {
            OwnerPublicAddress = ownerAddress
        });

        after.Success.Should().BeTrue();
        after.Exists.Should().BeTrue();
        return after.FeedId;
    }

    private async Task<string> EnsureCircleExistsAsync(string circleName)
    {
        var key = $"E2E_FEAT085_Circle_{circleName}";
        if (ScenarioContext.TryGetValue(key, out var existingObj) && existingObj is string existingFeedId)
        {
            return existingFeedId;
        }

        var ownerAddress = await GetUserAddressAsync("Owner");
        var ownerEncrypt = await GetPublicEncryptAddressAsync(ownerAddress);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var feedId = FeedId.NewFeedId.ToString();
        var aesKey = EncryptKeys.GenerateAesKey();
        var ownerEncryptedKey = EncryptKeys.Encrypt(aesKey, ownerEncrypt);

        var response = await feedClient.CreateGroupFeedAsync(new NewGroupFeedRequest
        {
            FeedId = feedId,
            Title = circleName,
            Description = $"FEAT-085 E2E circle {circleName}",
            IsPublic = false,
            Participants =
            {
                new GroupFeedParticipantProto
                {
                    FeedId = feedId,
                    ParticipantPublicAddress = ownerAddress,
                    ParticipantType = (int)ParticipantType.Owner,
                    EncryptedFeedKey = ownerEncryptedKey,
                    KeyGeneration = 1
                }
            }
        });

        response.Success.Should().BeTrue($"CreateGroupFeed should succeed: {response.Message}");
        await GetBlockControl().ProduceBlockAsync();

        ScenarioContext[key] = feedId;
        return feedId;
    }

    private async Task<int> GetLatestKeyGenerationForUserAsync(string feedId, string userAddress)
    {
        var keyGenerations = await GetKeyGenerationsForUserAsync(feedId, userAddress);
        return keyGenerations.Count == 0 ? 0 : keyGenerations.Max();
    }

    private async Task<List<int>> GetKeyGenerationsForUserAsync(string feedId, string userAddress)
    {
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var response = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
        {
            FeedId = feedId,
            UserPublicAddress = userAddress
        });

        return response.KeyGenerations.Select(x => x.KeyGeneration).ToList();
    }

    private async Task<string> GetPublicEncryptAddressAsync(string publicSigningAddress)
    {
        var identityClient = GetGrpcFactory().CreateClient<HushIdentity.HushIdentityClient>();
        var identity = await identityClient.GetIdentityAsync(new GetIdentityRequest
        {
            PublicSigningAddress = publicSigningAddress
        });

        identity.Successfull.Should().BeTrue($"Identity must exist for {publicSigningAddress[..10]}...");
        identity.PublicEncryptAddress.Should().NotBeNullOrWhiteSpace();
        return identity.PublicEncryptAddress;
    }

    private async Task<string> GetUserAddressAsync(string userName)
    {
        var page = GetUserPage(userName);
        var address = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (appStorage) {
                try {
                    const parsed = JSON.parse(appStorage);
                    const value = parsed?.state?.credentials?.signingPublicKey;
                    if (value) return value;
                } catch (_) {}
            }

            const legacy = localStorage.getItem('hush-credentials');
            if (legacy) {
                try {
                    const parsed = JSON.parse(legacy);
                    const value = parsed?.state?.signingPublicKey;
                    if (value) return value;
                } catch (_) {}
            }

            return null;
        }");

        address.Should().NotBeNullOrWhiteSpace($"No signing key in localStorage for user {userName}");
        return address;
    }

    private IPage GetUserPage(string userName)
    {
        var key = $"E2E_Page_{userName}";
        if (ScenarioContext.TryGetValue(key, out var pageObj) && pageObj is IPage page)
        {
            return page;
        }

        throw new InvalidOperationException($"No browser page found for user '{userName}'");
    }

    private HashSet<string> GetOrCreateFollowerSet(string key)
    {
        if (ScenarioContext.TryGetValue(key, out var setObj) && setObj is HashSet<string> set)
        {
            return set;
        }

        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ScenarioContext[key] = created;
        return created;
    }

    private string[] ParseCsv(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private GrpcClientFactory GetGrpcFactory()
    {
        if (ScenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            && factoryObj is GrpcClientFactory grpcFactory)
        {
            return grpcFactory;
        }

        throw new InvalidOperationException("GrpcClientFactory not found in ScenarioContext.");
    }
}
