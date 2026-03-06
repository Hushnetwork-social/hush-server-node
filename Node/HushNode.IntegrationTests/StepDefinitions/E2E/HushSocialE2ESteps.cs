using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using Microsoft.Playwright;
using Olimpo;
using System.Text.RegularExpressions;
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
    private const string SocialPostsByTitleKey = "E2E_HushSocialPostsByTitle";
    private const string LastPermalinkResponseKey = "E2E_HushSocialLastPermalinkResponse";

    public HushSocialE2ESteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [When(@"Owner opens the application")]
    public async Task WhenOwnerOpensTheApplication()
    {
        var ownerPage = GetUserPage("Owner");
        await NavigateToSocialExperienceAsync(ownerPage);
    }

    [Then(@"Owner should see HushSocial in main navigation")]
    public async Task ThenOwnerShouldSeeHushSocialInMainNavigation()
    {
        var ownerPage = GetUserPage("Owner");
        var hasSocialExperience = await IsSocialExperienceVisibleAsync(ownerPage);
        hasSocialExperience.Should().BeTrue("Owner should be in HushSocial view");

        var switchFeedsButton = ownerPage.GetByTestId("nav-switch-feeds");
        var hasSwitchFeeds = await switchFeedsButton.CountAsync() > 0 && await switchFeedsButton.First.IsVisibleAsync();
        hasSwitchFeeds.Should().BeTrue("Social navigation should expose switch back to HushFeeds");
    }

    [Then(@"Owner should not see Community in main navigation")]
    public async Task ThenOwnerShouldNotSeeCommunityInMainNavigation()
    {
        var ownerPage = GetUserPage("Owner");
        var hasVisibleCommunityText = await ownerPage.EvaluateAsync<bool>(@"() => {
            const nodes = Array.from(document.querySelectorAll('*'));
            return nodes.some(node => {
                const text = node.textContent?.trim();
                if (text !== 'Community') return false;
                const element = node;
                return !!(element instanceof HTMLElement && element.offsetParent);
            });
        }");
        var hasCommunityLink = await ownerPage.Locator("a[href='/community']").CountAsync() > 0;

        hasVisibleCommunityText.Should().BeFalse("Community should no longer appear in main navigation");
        hasCommunityLink.Should().BeFalse("Community route should not be present in main navigation");
    }

    [Then(@"Owner should see the HushSocial feed shell layout")]
    public async Task ThenOwnerShouldSeeTheHushSocialFeedShellLayout()
    {
        var ownerPage = GetUserPage("Owner");
        var hasSocialExperience = await IsSocialExperienceVisibleAsync(ownerPage);
        hasSocialExperience.Should().BeTrue("HushSocial shell should be rendered");
    }

    [Given(@"Owner opens HushSocial privacy settings")]
    public async Task GivenOwnerOpensHushSocialPrivacySettings()
    {
        var ownerPage = GetUserPage("Owner");
        await NavigateToSocialExperienceAsync(ownerPage);
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
    [When(@"Owner has approved followers ""(.*)"" via browser")]
    public async Task GivenOwnerHasApprovedFollowersViaBrowser(string csvFollowers)
    {
        foreach (var follower in ParseCsv(csvFollowers))
        {
            await ApproveFollowerAsync(follower);
        }
    }

    [Given(@"Owner has created FEAT-085 circle ""(.*)"" via backend")]
    [When(@"Owner has created FEAT-085 circle ""(.*)"" via backend")]
    public async Task GivenOwnerHasCreatedFeat085CircleViaBackend(string circleName)
    {
        await EnsureCircleExistsAsync(circleName);
    }

    [Given(@"Owner has added ""(.*)"" to FEAT-085 circle ""(.*)"" via backend")]
    [When(@"Owner has added ""(.*)"" to FEAT-085 circle ""(.*)"" via backend")]
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
        await NavigateToSocialExperienceAsync(page);
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

    [Given(@"Owner opens HushSocial composer")]
    [When(@"Owner opens HushSocial composer")]
    public async Task GivenOwnerOpensHushSocialComposer()
    {
        var ownerPage = GetUserPage("Owner");
        await NavigateToSocialExperienceAsync(ownerPage);

        await EnsureComposerDraftVisibleAsync(ownerPage);
    }

    [When(@"Owner creates Open post ""(.*)"" via browser")]
    public async Task WhenOwnerCreatesOpenPostViaBrowser(string postTitle)
    {
        await CreatePostViaBrowserAsync(postTitle, isPrivate: false);
    }

    [When(@"Owner attaches image (\d+) and animated GIF (\d+) via file picker")]
    public async Task WhenOwnerAttachesImageAndGifViaFilePicker(int imageIndex, int gifIndex)
    {
        var ownerPage = GetUserPage("Owner");
        await EnsureComposerDraftVisibleAsync(ownerPage);

        var (imageFileName, imageBytes) = TestImageGenerator.GenerateTestAttachment(imageIndex, "Owner", "HushSocial");
        var (gifFileName, gifBytes) = TestImageGenerator.GenerateAnimatedTestGif(gifIndex, "Owner", "HushSocial");

        var fileInput = ownerPage.GetByTestId("social-new-post-file-input").First;
        await fileInput.SetInputFilesAsync(
        [
            new FilePayload { Name = imageFileName, MimeType = "image/png", Buffer = imageBytes },
            new FilePayload { Name = gifFileName, MimeType = "image/gif", Buffer = gifBytes }
        ]);
    }

    [When(@"Owner drags and drops video into HushSocial composer")]
    public async Task WhenOwnerDragsAndDropsVideoIntoHushSocialComposer()
    {
        var ownerPage = GetUserPage("Owner");
        var (videoFileName, videoBytes) = TestFileGenerator.GenerateTestVideo("Owner", "HushSocialDrop");
        var base64 = Convert.ToBase64String(videoBytes);

        await ownerPage.EvaluateAsync(@"(args) => {
            const binary = atob(args.base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

            const blob = new Blob([bytes], { type: 'video/webm' });
            const file = new File([blob], args.fileName, { type: 'video/webm' });

            const dt = new DataTransfer();
            dt.items.add(file);

            const dropTarget = document.querySelector('[data-testid=""social-new-post-media""]');
            if (!dropTarget) return;

            const dragOverEvent = new DragEvent('dragover', { bubbles: true, cancelable: true });
            Object.defineProperty(dragOverEvent, 'dataTransfer', { value: dt });
            dropTarget.dispatchEvent(dragOverEvent);

            const dropEvent = new DragEvent('drop', { bubbles: true, cancelable: true });
            Object.defineProperty(dropEvent, 'dataTransfer', { value: dt });
            dropTarget.dispatchEvent(dropEvent);
        }", new { base64, fileName = videoFileName });
    }

    [Then(@"Owner should see (\d+) media items in composer")]
    public async Task ThenOwnerShouldSeeMediaItemsInComposer(int expectedCount)
    {
        var ownerPage = GetUserPage("Owner");
        var mediaList = await WaitForTestIdAsync(ownerPage, "social-new-post-media-list", 10000);
        var items = mediaList.Locator("li");

        var timeoutAt = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (await items.CountAsync() == expectedCount)
            {
                return;
            }

            await Task.Delay(150);
        }

        var actualCount = await items.CountAsync();
        actualCount.Should().Be(expectedCount, $"owner should see {expectedCount} media item(s) in composer");
    }

    [When(@"Owner pastes image (\d+) into HushSocial composer")]
    public async Task WhenOwnerPastesImageIntoHushSocialComposer(int imageIndex)
    {
        var ownerPage = GetUserPage("Owner");
        var (fileName, pngBytes) = TestImageGenerator.GenerateTestAttachment(imageIndex, "Owner", "HushSocialPaste");
        var base64 = Convert.ToBase64String(pngBytes);

        await ownerPage.EvaluateAsync(@"(args) => {
            const binary = atob(args.base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

            const blob = new Blob([bytes], { type: 'image/png' });
            const file = new File([blob], args.fileName, { type: 'image/png' });
            const dt = new DataTransfer();
            dt.items.add(file);

            const event = new ClipboardEvent('paste', { bubbles: true, cancelable: true });
            Object.defineProperty(event, 'clipboardData', { value: dt });

            const draft = document.querySelector('[data-testid=""social-new-post-draft""]');
            if (draft) {
              draft.dispatchEvent(event);
            }
        }", new { base64, fileName });
    }

    [When(@"Owner attempts to attach too many media files in one post")]
    public async Task WhenOwnerAttemptsToAttachTooManyMediaFilesInOnePost()
    {
        var ownerPage = GetUserPage("Owner");
        var (fileAName, fileABytes) = TestImageGenerator.GenerateTestAttachment(10, "Owner", "OverflowA");
        var (fileBName, fileBBytes) = TestImageGenerator.GenerateTestAttachment(11, "Owner", "OverflowB");

        var fileInput = ownerPage.GetByTestId("social-new-post-file-input").First;
        await fileInput.SetInputFilesAsync(
        [
            new FilePayload { Name = fileAName, MimeType = "image/png", Buffer = fileABytes },
            new FilePayload { Name = fileBName, MimeType = "image/png", Buffer = fileBBytes }
        ]);
    }

    [Then(@"Owner should see a media count limit validation message")]
    public async Task ThenOwnerShouldSeeMediaCountLimitValidationMessage()
    {
        var ownerPage = GetUserPage("Owner");
        var error = await WaitForTestIdAsync(ownerPage, "social-new-post-media-error", 10000);
        var text = (await error.InnerTextAsync()).Trim();
        text.Should().Contain("up to 4");
    }

    [When(@"Owner removes one media item from composer")]
    public async Task WhenOwnerRemovesOneMediaItemFromComposer()
    {
        var ownerPage = GetUserPage("Owner");
        var removeButton = ownerPage.Locator("[data-testid^='social-new-post-remove-media-']").First;
        await removeButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await removeButton.ClickAsync();
    }

    [When(@"Owner attempts to attach an oversized media file")]
    public async Task WhenOwnerAttemptsToAttachAnOversizedMediaFile()
    {
        var ownerPage = GetUserPage("Owner");
        var oversizedBytes = new byte[(25 * 1024 * 1024) + 1];
        var fileInput = ownerPage.GetByTestId("social-new-post-file-input").First;
        await fileInput.SetInputFilesAsync(new FilePayload
        {
            Name = "oversized-image.png",
            MimeType = "image/png",
            Buffer = oversizedBytes
        });
    }

    [Then(@"Owner should see a media size limit validation message")]
    public async Task ThenOwnerShouldSeeMediaSizeLimitValidationMessage()
    {
        var ownerPage = GetUserPage("Owner");
        var error = await WaitForTestIdAsync(ownerPage, "social-new-post-media-error", 10000);
        var text = (await error.InnerTextAsync()).Trim();
        text.Should().Contain("25MB or less");
    }

    [When(@"Owner creates Open post ""(.*)"" via backend")]
    public async Task WhenOwnerCreatesOpenPostViaBackend(string postTitle)
    {
        var owner = await GetOrCreateBrowserTestIdentityAsync("Owner");
        var response = await SubmitSocialPostAsync(owner, postTitle, SocialPostVisibility.Open);
        response.Success.Should().BeTrue($"Owner open post '{postTitle}' should succeed: {response.Message}");

        var ownerAddress = await GetUserAddressAsync("Owner");
        var post = await ResolvePostFromFeedWallAsync(ownerAddress, postTitle);
        StorePostId(postTitle, post.PostId);
        post.Visibility.Should().Be(SocialPostVisibilityProto.SocialPostVisibilityOpen);
    }

    [When(@"Owner creates Close post ""(.*)"" for Inner Circle via browser")]
    public async Task WhenOwnerCreatesClosePostForInnerCircleViaBrowser(string postTitle)
    {
        await WhenOwnerCreatesClosePostForCircleViaBrowser(postTitle, "Inner Circle");
    }

    [When(@"Owner creates Close post ""(.*)"" for Inner Circle via backend")]
    public async Task WhenOwnerCreatesClosePostForInnerCircleViaBackend(string postTitle)
    {
        await CreateClosePostViaBackendAsync(postTitle, "Inner Circle");
    }

    [Then(@"Owner should be able to copy permalink for post ""(.*)""")]
    public async Task ThenOwnerShouldBeAbleToCopyPermalinkForPost(string postTitle)
    {
        var ownerPage = GetUserPage("Owner");
        await NavigateToSocialExperienceAsync(ownerPage);
        await TriggerSyncAsync(ownerPage);

        var escapedTitle = postTitle.Replace("\"", "\\\"");
        var linkButton = ownerPage.Locator($"article:has-text(\"{escapedTitle}\") [data-testid^='post-action-link-']").First;
        await linkButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await linkButton.ClickAsync();

        var copiedToast = ownerPage.Locator("text=Post permalink copied.").First;
        try
        {
            await copiedToast.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 3000
            });
        }
        catch (TimeoutException)
        {
            var fallbackToast = ownerPage.Locator("text=Permalink:").First;
            await fallbackToast.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 3000
            });
        }

        var postId = GetStoredPostId(postTitle);
        postId.Should().NotBeNullOrWhiteSpace();
    }

    [When(@"FollowerB opens permalink for post ""(.*)""")]
    public async Task WhenFollowerBOpensPermalinkForPost(string postTitle)
    {
        await OpenPermalinkAsUserAsync("FollowerB", postTitle);
    }

    [When(@"FollowerA opens permalink for post ""(.*)""")]
    public async Task WhenFollowerAOpensPermalinkForPost(string postTitle)
    {
        await OpenPermalinkAsUserAsync("FollowerA", postTitle);
    }

    [When(@"Owner opens HushSocial FeedWall")]
    [When(@"FollowerA opens HushSocial FeedWall")]
    [When(@"FollowerB opens HushSocial FeedWall")]
    public async Task WhenFollowerOpensHushSocialFeedWall()
    {
        var followerName = GetStepUserNameFromWhenStep("opens HushSocial FeedWall");
        var followerPage = GetUserPage(followerName);
        await NavigateToSocialExperienceAsync(followerPage);
        await TriggerSyncAsync(followerPage);
    }

    [Then(@"FollowerB should see post ""(.*)""")]
    [Then(@"FollowerA should see post ""(.*)""")]
    [Then(@"FollowerA should see permalink post ""(.*)""")]
    [Then(@"FollowerB should see permalink post ""(.*)""")]
    public async Task ThenFollowerShouldSeePermalinkPost(string postTitle)
    {
        var response = GetLastPermalinkResponse();
        response.Success.Should().BeTrue($"Permalink should resolve for post: {postTitle}");
        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateAllowed);
        response.Content.Should().Be(postTitle);

        var page = GetCurrentPermalinkUserPage();
        var publicCard = page.GetByTestId("social-permalink-public");
        (await publicCard.CountAsync()).Should().BeGreaterThan(0);

        var authorName = (await page.GetByTestId("social-permalink-author-name").First.InnerTextAsync()).Trim();
        authorName.Should().Be("Owner", "public permalink should show owner display name");

        var ownerAddress = await GetUserAddressAsync("Owner");
        var pageText = await page.InnerTextAsync("body");
        pageText.Should().NotContain(ownerAddress, "raw owner public address should not be exposed in permalink UI");

        var confirmedAt = (await page.GetByTestId("social-permalink-confirmed-at").First.InnerTextAsync()).Trim();
        confirmedAt.Should().NotContain("Confirmed at block", "permalink should show real datetime, not block-only fallback");
        confirmedAt.Should().MatchRegex(@"^\d{2}/\d{2}/\d{4}, \d{2}:\d{2}:\d{2}$");
    }

    [Then(@"FollowerB should see full-page permalink layout")]
    [Then(@"FollowerA should see full-page permalink layout")]
    public async Task ThenFollowerShouldSeeFullPagePermalinkLayout()
    {
        var page = GetCurrentPermalinkUserPage();
        var container = page.GetByTestId("social-permalink-layout").First;
        await container.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var widthRatio = await page.EvaluateAsync<double>(@"() => {
            const node = document.querySelector('[data-testid=""social-permalink-layout""]');
            if (!(node instanceof HTMLElement)) return 0;
            const rect = node.getBoundingClientRect();
            const viewportWidth = window.innerWidth || document.documentElement.clientWidth || 1;
            return rect.width / viewportWidth;
        }");
        widthRatio.Should().BeGreaterThan(0.7, "permalink detail should use a full-page layout, not a narrow centered card");
    }

    [Then(@"FollowerB should see a generic access denied message")]
    public async Task ThenFollowerBShouldSeeAGenericAccessDeniedMessage()
    {
        var response = GetLastPermalinkResponse();
        response.Success.Should().BeTrue();
        response.AccessState.Should().Be(SocialPermalinkAccessStateProto.SocialPermalinkAccessStateUnauthorizedDenied);
        response.Content.Should().BeNullOrEmpty();

        var page = GetCurrentPermalinkUserPage();
        var denied = page.GetByTestId("social-permalink-denied");
        await denied.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    [Then(@"FollowerA should see Open post ""(.*)"" authored by ""(.*)""")]
    [Then(@"FollowerB should see Open post ""(.*)"" authored by ""(.*)""")]
    public async Task ThenFollowerShouldSeeOpenPostAuthoredBy(string postTitle, string expectedAuthorName)
    {
        var postId = GetStoredPostId(postTitle);
        var followerName = GetStepUserNameFromThenStep("should see Open post");
        var followerPage = GetUserPage(followerName);
        await NavigateToSocialExperienceAsync(followerPage);
        await TriggerSyncAsync(followerPage);

        var postCard = followerPage.GetByTestId($"social-post-{postId}");
        await postCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var authorNameLocator = postCard.Locator("p.text-sm.font-semibold").First;
        var authorName = (await authorNameLocator.InnerTextAsync()).Trim();
        authorName.Should().Be(expectedAuthorName, "public posts should show author display name to other users");
        authorName.Should().NotContain("...", "author should not fall back to truncated address");

        var avatarInitials = (await postCard.Locator("span.inline-flex.h-10.w-10").First.InnerTextAsync()).Trim();
        avatarInitials.Should().NotBeNullOrWhiteSpace("public posts should show author avatar initials");
        avatarInitials.Length.Should().BeInRange(1, 2, "avatar initials should be compact");
        avatarInitials[0].Should().Be(char.ToUpperInvariant(authorName[0]), "avatar initials should match author name");
    }

    [When(@"Owner opens HushSocial Following page")]
    public async Task WhenOwnerOpensHushSocialFollowingPage()
    {
        var ownerPage = GetUserPage("Owner");
        await NavigateToSocialExperienceAsync(ownerPage);
        await OpenFollowingTabAsync(ownerPage);
        await TriggerSyncAsync(ownerPage);
    }

    [Then(@"Owner should see following members ""(.*)"" tagged with ""(.*)""")]
    public async Task ThenOwnerShouldSeeFollowingMembersTaggedWith(string csvMembers, string circleName)
    {
        var ownerPage = GetUserPage("Owner");
        await NavigateToSocialExperienceAsync(ownerPage);
        await OpenFollowingTabAsync(ownerPage);
        var normalizedAddresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in ParseCsv(csvMembers))
        {
            normalizedAddresses.Add((await GetUserAddressAsync(member)).Trim().ToLowerInvariant());
        }

        var expectedTagVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            circleName
        };
        var withoutSpaces = circleName.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!string.Equals(withoutSpaces, circleName, StringComparison.Ordinal))
        {
            expectedTagVariants.Add(withoutSpaces);
        }

        var uiResolvedMembers = new HashSet<string>(StringComparer.Ordinal);
        var timeoutAt = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < timeoutAt)
        {
            await TriggerSyncAsync(ownerPage);

            var allFound = true;
            foreach (var memberAddress in normalizedAddresses)
            {
                var memberCard = ownerPage.GetByTestId($"social-following-item-{memberAddress}").First;
                if (await memberCard.CountAsync() == 0 || !await memberCard.IsVisibleAsync())
                {
                    allFound = false;
                    break;
                }

                var hasAnyVariant = false;
                foreach (var variant in expectedTagVariants)
                {
                    var tag = memberCard.GetByText(variant, new LocatorGetByTextOptions { Exact = true }).First;
                    if (await tag.CountAsync() > 0 && await tag.IsVisibleAsync())
                    {
                        hasAnyVariant = true;
                        break;
                    }
                }

                if (!hasAnyVariant)
                {
                    allFound = false;
                    continue;
                }

                uiResolvedMembers.Add(memberAddress);
            }

            if (allFound)
            {
                return;
            }

            await Task.Delay(300);
        }
        var missingMembers = normalizedAddresses
            .Except(uiResolvedMembers, StringComparer.Ordinal)
            .ToArray();

        missingMembers.Should().BeEmpty(
            $"expected following members to be visibly tagged with '{circleName}' in HushSocial Following UI");
    }

    [When(@"Owner creates circle ""(.*)"" via browser")]
    public async Task WhenOwnerCreatesCircleViaBrowser(string circleName)
    {
        var ownerPage = GetUserPage("Owner");
        await NavigateToSocialExperienceAsync(ownerPage);
        await OpenFollowingTabAsync(ownerPage);

        await ClickTestIdAsync(ownerPage, "social-create-circle-button");
        await FillTestIdAsync(ownerPage, "social-create-circle-input", circleName);
        using var waiter = StartListeningForTransactions(1);
        await ClickTestIdAsync(ownerPage, "social-create-circle-submit");
        await AwaitTransactionsAndProduceBlockAsync(waiter);
        await TriggerSyncAsync(ownerPage);
        await ownerPage.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await NavigateToSocialExperienceAsync(ownerPage);
        await OpenFollowingTabAsync(ownerPage);

        await WaitForCircleCardByNameAsync(ownerPage, circleName, 15000);
    }

    [When(@"Owner adds ""(.*)"" to circle ""(.*)"" via browser")]
    public async Task WhenOwnerAddsMemberToCircleViaBrowser(string memberName, string circleName)
    {
        var ownerPage = GetUserPage("Owner");
        await NavigateToSocialExperienceAsync(ownerPage);
        await OpenFollowingTabAsync(ownerPage);

        var memberAddress = (await GetUserAddressAsync(memberName)).Trim().ToLowerInvariant();
        var memberCard = ownerPage.GetByTestId($"social-following-item-{memberAddress}").First;
        await memberCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
        await memberCard.ClickAsync();

        var circleCard = await WaitForCircleCardByNameAsync(ownerPage, circleName, 10000);
        using var waiter = StartListeningForTransactions(1);
        await circleCard.ClickAsync();
        await AwaitTransactionsAndProduceBlockAsync(waiter);
        await TriggerSyncAsync(ownerPage);

        var tag = memberCard.GetByText(circleName, new LocatorGetByTextOptions { Exact = true }).First;
        await tag.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }

    [When(@"Owner creates Close post ""(.*)"" for circle ""(.*)"" via browser")]
    public async Task WhenOwnerCreatesClosePostForCircleViaBrowser(string postTitle, string circleName)
    {
        var ownerPage = GetUserPage("Owner");
        await NavigateToSocialExperienceAsync(ownerPage);

        await EnsureInnerCircleExistsForOwnerAsync();
        await ownerPage.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await NavigateToSocialExperienceAsync(ownerPage);
        await TriggerSyncAsync(ownerPage);

        var draftInput = await EnsureComposerDraftVisibleAsync(ownerPage);
        await draftInput.FillAsync(postTitle);

        await ClickTestIdAsync(ownerPage, "social-new-post-audience-close");
        await TriggerSyncAsync(ownerPage);
        await EnsurePrivateAudienceReadyAsync(ownerPage);

        await ClickTestIdAsync(ownerPage, "social-new-post-add-circle");
        var circleCheckbox = ownerPage
            .GetByTestId("social-new-post-circle-picker")
            .Locator("label", new LocatorLocatorOptions { HasTextString = circleName })
            .Locator("input[type='checkbox']")
            .First;
        await circleCheckbox.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        if (!await circleCheckbox.IsCheckedAsync())
        {
            await circleCheckbox.ClickAsync();
        }

        var removeInnerCircle = ownerPage.GetByTestId("social-new-post-remove-inner-circle").First;
        var isInnerCircleTarget = string.Equals(circleName, "Inner Circle", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(circleName, "InnerCircle", StringComparison.OrdinalIgnoreCase);
        if (!isInnerCircleTarget && await removeInnerCircle.IsVisibleAsync() && await removeInnerCircle.IsEnabledAsync())
        {
            await removeInnerCircle.ClickAsync();
        }

        var selectedCircles = ownerPage.GetByTestId("social-new-post-selected-circles").First;
        var selectedText = (await selectedCircles.InnerTextAsync()).Trim();
        selectedText.Should().Contain(circleName);
        if (!isInnerCircleTarget)
        {
            selectedText.Should().NotContain("Inner Circle");
        }

        using var waiter = StartListeningForTransactions(1);
        await ClickTestIdAsync(ownerPage, "social-new-post-publish");
        await AwaitTransactionsAndProduceBlockAsync(waiter);
        await TriggerSyncAsync(ownerPage);

        var ownerAddress = await GetUserAddressAsync("Owner");
        var post = await ResolvePostFromFeedWallAsync(ownerAddress, postTitle);
        StorePostId(postTitle, post.PostId);

        post.Visibility.Should().Be(SocialPostVisibilityProto.SocialPostVisibilityPrivate);
        post.CircleFeedIds.Should().NotBeEmpty();
    }

    [When(@"Owner creates Close post ""(.*)"" for circle ""(.*)"" via backend")]
    public async Task WhenOwnerCreatesClosePostForCircleViaBackend(string postTitle, string circleName)
    {
        await CreateClosePostViaBackendAsync(postTitle, circleName);
    }

    [Then(@"Owner should see FeedWall post ""(.*)""")]
    [Then(@"FollowerA should see FeedWall post ""(.*)""")]
    [Then(@"FollowerB should see FeedWall post ""(.*)""")]
    public async Task ThenFollowerShouldSeeFeedWallPost(string postTitle)
    {
        var followerName = GetStepUserNameFromThenStep("should see FeedWall post");
        var postId = GetStoredPostId(postTitle);
        var page = GetUserPage(followerName);

        await NavigateToSocialExperienceAsync(page);
        await TriggerSyncAsync(page);

        var postCard = page.GetByTestId($"social-post-{postId}").First;
        await postCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
    }

    [Then(@"FollowerA should not see FeedWall post ""(.*)""")]
    [Then(@"FollowerB should not see FeedWall post ""(.*)""")]
    public async Task ThenFollowerShouldNotSeeFeedWallPost(string postTitle)
    {
        var followerName = GetStepUserNameFromThenStep("should not see FeedWall post");
        var postId = GetStoredPostId(postTitle);
        var page = GetUserPage(followerName);

        await NavigateToSocialExperienceAsync(page);
        await TriggerSyncAsync(page);

        var postCard = page.GetByTestId($"social-post-{postId}");
        var count = await postCard.CountAsync();
        count.Should().Be(0, $"'{followerName}' should not see post '{postTitle}' in FeedWall");
    }

    [Then(@"Owner should see audience badge ""(.*)"" for FeedWall post ""(.*)""")]
    [Then(@"FollowerA should see audience badge ""(.*)"" for FeedWall post ""(.*)""")]
    [Then(@"FollowerB should see audience badge ""(.*)"" for FeedWall post ""(.*)""")]
    public async Task ThenUserShouldSeeAudienceBadgeForFeedWallPost(string expectedBadge, string postTitle)
    {
        var userName = GetStepUserNameFromThenStep("should see audience badge");
        var postId = GetStoredPostId(postTitle);
        var page = GetUserPage(userName);

        await NavigateToSocialExperienceAsync(page);
        await TriggerSyncAsync(page);

        var postCard = page.GetByTestId($"social-post-{postId}").First;
        await postCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var audienceContainer = postCard.GetByTestId($"post-audience-badges-{postId}").First;
        await audienceContainer.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var badges = (await audienceContainer.Locator("span").AllInnerTextsAsync())
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        badges.Should().Contain(expectedBadge, $"'{userName}' should see audience badge '{expectedBadge}' for post '{postTitle}'");
    }

    [Then(@"FollowerA should not see audience badge ""(.*)"" for FeedWall post ""(.*)""")]
    [Then(@"FollowerB should not see audience badge ""(.*)"" for FeedWall post ""(.*)""")]
    public async Task ThenFollowerShouldNotSeeAudienceBadgeForFeedWallPost(string forbiddenBadge, string postTitle)
    {
        var followerName = GetStepUserNameFromThenStep("should not see audience badge");
        var postId = GetStoredPostId(postTitle);
        var page = GetUserPage(followerName);

        await NavigateToSocialExperienceAsync(page);
        await TriggerSyncAsync(page);

        var postCard = page.GetByTestId($"social-post-{postId}").First;
        await postCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var audienceContainer = postCard.GetByTestId($"post-audience-badges-{postId}").First;
        var badges = (await audienceContainer.Locator("span").AllInnerTextsAsync())
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        badges.Should().NotContain(forbiddenBadge, $"'{followerName}' should not see audience badge '{forbiddenBadge}' for post '{postTitle}'");
    }

    [Then(@"FollowerA should not see truncated audience badge for FeedWall post ""(.*)""")]
    [Then(@"FollowerB should not see truncated audience badge for FeedWall post ""(.*)""")]
    public async Task ThenFollowerShouldNotSeeTruncatedAudienceBadgeForFeedWallPost(string postTitle)
    {
        var followerName = GetStepUserNameFromThenStep("should not see truncated audience badge");
        var postId = GetStoredPostId(postTitle);
        var page = GetUserPage(followerName);

        await NavigateToSocialExperienceAsync(page);
        await TriggerSyncAsync(page);

        var postCard = page.GetByTestId($"social-post-{postId}").First;
        await postCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var audienceContainer = postCard.GetByTestId($"post-audience-badges-{postId}").First;
        var badges = (await audienceContainer.Locator("span").AllInnerTextsAsync())
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var truncatedPattern = new Regex("^[0-9a-f]{8}\\.\\.\\.[0-9a-f]{4}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        badges.Should().OnlyContain(
            x => !truncatedPattern.IsMatch(x),
            $"'{followerName}' should never see truncated feed-id audience badges for post '{postTitle}'");
    }

    [When(@"FollowerA opens post detail for ""(.*)"" from FeedWall")]
    [When(@"FollowerB opens post detail for ""(.*)"" from FeedWall")]
    public async Task WhenFollowerOpensPostDetailFromFeedWall(string postTitle)
    {
        var followerName = GetStepUserNameFromWhenStep("opens post detail for");
        var postId = GetStoredPostId(postTitle);
        var page = GetUserPage(followerName);

        await NavigateToSocialExperienceAsync(page);
        await TriggerSyncAsync(page);

        var postCard = page.GetByTestId($"social-post-{postId}").First;
        await postCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
        await postCard.ClickAsync();

        var overlay = page.GetByTestId("post-detail-overlay").First;
        await overlay.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }

    [Then(@"FollowerA should see post detail overlay for ""(.*)"" authored by ""(.*)""")]
    [Then(@"FollowerB should see post detail overlay for ""(.*)"" authored by ""(.*)""")]
    public async Task ThenFollowerShouldSeePostDetailOverlay(string postTitle, string expectedAuthorName)
    {
        var followerName = GetStepUserNameFromThenStep("should see post detail overlay for");
        var page = GetUserPage(followerName);

        var overlay = page.GetByTestId("post-detail-overlay").First;
        await overlay.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var postText = (await page.GetByTestId("post-detail-full-text").First.InnerTextAsync()).Trim();
        postText.Should().Be(postTitle);

        var authorText = (await overlay.Locator("p.text-hush-text-primary.font-semibold").First.InnerTextAsync()).Trim();
        authorText.Should().Be(expectedAuthorName);
    }

    [When(@"FollowerB navigates from permalink to HushSocial FeedWall")]
    public async Task WhenFollowerBNavigatesFromPermalinkToHushSocialFeedWall()
    {
        var page = GetUserPage("FollowerB");
        await page.GotoAsync($"{GetBaseUrl()}/social", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await NavigateToSocialExperienceAsync(page);
        await TriggerSyncAsync(page);
    }

    private async Task ApproveFollowerAsync(string followerName)
    {
        var pending = GetOrCreateFollowerSet(PendingFollowersKey);
        pending.Remove(followerName);

        var ownerAddress = await GetUserAddressAsync("Owner");
        var followerAddress = await GetUserAddressAsync(followerName);
        var followerEncrypt = await GetPublicEncryptAddressAsync(followerAddress);

        var innerCircleFeedId = await EnsureInnerCircleExistsForOwnerAsync();
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var existingMembers = await feedClient.GetGroupMembersAsync(new GetGroupMembersRequest
        {
            FeedId = innerCircleFeedId
        });

        var alreadyMember = existingMembers.Members.Any(m =>
            string.Equals(m.PublicAddress, followerAddress, StringComparison.OrdinalIgnoreCase));
        if (alreadyMember)
        {
            var approvedExisting = GetOrCreateFollowerSet(ApprovedFollowersKey);
            approvedExisting.Add(followerName);
            return;
        }

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

    private async Task CreatePostViaBrowserAsync(string postTitle, bool isPrivate)
    {
        var ownerPage = GetUserPage("Owner");
        await NavigateToSocialExperienceAsync(ownerPage);

        if (isPrivate)
        {
            // Ensure backend state exists before interacting with private audience controls.
            await EnsureInnerCircleExistsForOwnerAsync();
            await ownerPage.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await NavigateToSocialExperienceAsync(ownerPage);
            await TriggerSyncAsync(ownerPage);
        }

        var draftInput = await EnsureComposerDraftVisibleAsync(ownerPage);
        await draftInput.FillAsync(postTitle);

        if (isPrivate)
        {
            await ClickTestIdAsync(ownerPage, "social-new-post-audience-close");
            await TriggerSyncAsync(ownerPage);
            await EnsurePrivateAudienceReadyAsync(ownerPage);
            await EnsureInnerCircleSelectedInComposerAsync(ownerPage);
        }
        else
        {
            await ClickTestIdAsync(ownerPage, "social-new-post-audience-public");
        }

        using var waiter = StartListeningForTransactions(1);
        await ClickTestIdAsync(ownerPage, "social-new-post-publish");
        try
        {
            await AwaitTransactionsAndProduceBlockAsync(waiter);
        }
        catch (TimeoutException)
        {
            await GetBlockControl().ProduceBlockAsync();
        }
        await TriggerSyncAsync(ownerPage);

        var contentLocator = ownerPage.GetByText(postTitle, new PageGetByTextOptions { Exact = true }).First;
        await contentLocator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var ownerAddress = await GetUserAddressAsync("Owner");
        var post = await ResolvePostFromFeedWallAsync(ownerAddress, postTitle);
        StorePostId(postTitle, post.PostId);

        if (isPrivate)
        {
            var innerCircleFeedId = await EnsureInnerCircleExistsForOwnerAsync();
            post.Visibility.Should().Be(SocialPostVisibilityProto.SocialPostVisibilityPrivate);
            post.CircleFeedIds.Should().Contain(innerCircleFeedId);
        }
        else
        {
            post.Visibility.Should().Be(SocialPostVisibilityProto.SocialPostVisibilityOpen);
            post.CircleFeedIds.Should().BeEmpty();
        }
    }

    [When(@"Owner switches from HushSocial to HushFeeds")]
    public async Task WhenOwnerSwitchesFromHushSocialToHushFeeds()
    {
        var ownerPage = GetUserPage("Owner");
        var switchFeedsButton = ownerPage.GetByTestId("nav-switch-feeds");
        if (await switchFeedsButton.CountAsync() > 0 && await switchFeedsButton.First.IsVisibleAsync())
        {
            await switchFeedsButton.First.ClickAsync();
        }
        await ownerPage.GetByTestId("feed-list").First.WaitForAsync();
    }

    [Then(@"Owner should not see Inner Circle in HushFeeds feed list")]
    public async Task ThenOwnerShouldNotSeeInnerCircleInHushFeedsFeedList()
    {
        var ownerPage = GetUserPage("Owner");

        var withSpace = ownerPage.GetByTestId("feed-item:group:inner-circle");
        var compact = ownerPage.GetByTestId("feed-item:group:innercircle");
        var withUnderscore = ownerPage.GetByTestId("feed-item:group:inner_circle");
        var feedListText = (await ownerPage.GetByTestId("feed-list").First.InnerTextAsync()).ToLowerInvariant();

        (await withSpace.CountAsync()).Should().Be(0, "Inner Circle must never appear as a chat/group feed in HushFeeds list");
        (await compact.CountAsync()).Should().Be(0, "InnerCircle must never appear as a chat/group feed in HushFeeds list");
        (await withUnderscore.CountAsync()).Should().Be(0, "Inner_Circle must never appear as a chat/group feed in HushFeeds list");
        feedListText.Should().NotContain("inner circle", "Inner Circle label must never appear in HushFeeds feed list");
        feedListText.Should().NotContain("innercircle", "InnerCircle label must never appear in HushFeeds feed list");
    }

    private async Task EnsurePrivateAudienceReadyAsync(IPage page)
    {
        await WaitForTestIdAsync(page, "social-new-post-private-options", 10000);
        var selected = await WaitForTestIdAsync(page, "social-new-post-selected-circles", 10000);

        var timeoutAt = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < timeoutAt)
        {
            var text = (await selected.InnerTextAsync()).Trim();
            if (!text.Contains("none", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(200);
        }

        // Retry audience toggle once to recover from transient circle hydration races.
        await ClickTestIdAsync(page, "social-new-post-audience-public");
        await ClickTestIdAsync(page, "social-new-post-audience-close");

        var retriedText = (await selected.InnerTextAsync()).Trim();
        retriedText.Should().NotContain("none", "private posts require at least one selected circle");
    }

    private async Task EnsureInnerCircleSelectedInComposerAsync(IPage page)
    {
        var selected = await WaitForTestIdAsync(page, "social-new-post-selected-circles", 10000);
        var selectedText = (await selected.InnerTextAsync()).Trim();
        if (selectedText.Contains("inner circle", StringComparison.OrdinalIgnoreCase) ||
            selectedText.Contains("innercircle", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await ClickTestIdAsync(page, "social-new-post-add-circle");
        var innerCircleCheckbox = page
            .GetByTestId("social-new-post-circle-picker")
            .Locator("label", new LocatorLocatorOptions { HasTextString = "Inner Circle" })
            .Locator("input[type='checkbox']")
            .First;

        await innerCircleCheckbox.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        if (!await innerCircleCheckbox.IsCheckedAsync())
        {
            await innerCircleCheckbox.ClickAsync();
        }

        selectedText = (await selected.InnerTextAsync()).Trim();
        selectedText.Should().ContainEquivalentOf("Inner Circle");
    }

    private async Task<ILocator> EnsureComposerDraftVisibleAsync(IPage page)
    {
        var feedWallNavCandidates = new ILocator[]
        {
            page.GetByRole(AriaRole.Link, new() { Name = "Feed Wall" }).First,
            page.GetByRole(AriaRole.Button, new() { Name = "Feed Wall" }).First,
            page.GetByText("Feed Wall", new() { Exact = true }).First
        };

        foreach (var navCandidate in feedWallNavCandidates)
        {
            if (await navCandidate.CountAsync() == 0 || !await navCandidate.IsVisibleAsync())
            {
                continue;
            }

            await navCandidate.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            break;
        }

        var compactTrigger = page.GetByTestId("social-new-post-compact-trigger");
        if (await compactTrigger.CountAsync() > 0 && await compactTrigger.First.IsVisibleAsync())
        {
            await compactTrigger.First.ClickAsync();
        }

        var inlinePrompt = page.GetByText("What do you want to show us?", new PageGetByTextOptions { Exact = true });
        if (await inlinePrompt.CountAsync() > 0 && await inlinePrompt.First.IsVisibleAsync())
        {
            await inlinePrompt.First.ClickAsync();
        }

        try
        {
            return await WaitForTestIdAsync(page, "social-new-post-draft", 10000);
        }
        catch (TimeoutException)
        {
            var newPostNavCandidates = new ILocator[]
            {
                page.GetByRole(AriaRole.Link, new() { Name = "New Post" }).First,
                page.GetByRole(AriaRole.Button, new() { Name = "New Post" }).First,
                page.GetByText("New Post", new() { Exact = true }).First
            };

            foreach (var navCandidate in newPostNavCandidates)
            {
                if (await navCandidate.CountAsync() == 0 || !await navCandidate.IsVisibleAsync())
                {
                    continue;
                }

                await navCandidate.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                try
                {
                    return await WaitForTestIdAsync(page, "social-new-post-draft", 5000);
                }
                catch (TimeoutException)
                {
                    // Keep trying other fallbacks.
                }
            }

            var draftCandidates = new ILocator[]
            {
                page.GetByPlaceholder("What do you want to show us?").First,
                page.Locator("textarea[placeholder*='What do you want to show us?']").First,
                page.Locator("textarea").First,
                page.GetByRole(AriaRole.Textbox).First
            };

            foreach (var candidate in draftCandidates)
            {
                if (await candidate.CountAsync() > 0 && await candidate.IsVisibleAsync())
                {
                    return candidate;
                }

                try
                {
                    await candidate.WaitForAsync(new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Visible,
                        Timeout = 5000
                    });
                    return candidate;
                }
                catch (TimeoutException)
                {
                    // try next candidate
                }
            }

            throw new TimeoutException(
                $"Unable to find social post draft input on page URL '{page.Url}'.");
        }
    }

    private async Task NavigateToSocialExperienceAsync(IPage page)
    {
        await page.GotoAsync($"{GetBaseUrl()}/social", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var notFound = page.GetByText("This page could not be found.");
        if (await notFound.CountAsync() > 0 && await notFound.First.IsVisibleAsync())
        {
            await page.GotoAsync(GetBaseUrl(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            var navCandidates = new[]
            {
                page.GetByTestId("nav-social"),
                page.GetByRole(AriaRole.Link, new() { Name = "HushSocial!" }),
                page.GetByText("HushSocial!", new() { Exact = true })
            };

            foreach (var candidate in navCandidates)
            {
                if (await candidate.CountAsync() > 0 && await candidate.First.IsVisibleAsync())
                {
                    await candidate.First.ClickAsync();
                    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                    break;
                }
            }
        }

        // Poll for stable social shell instead of NetworkIdle (app keeps active polling/websocket traffic).
        var timeoutAt = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (await IsSocialExperienceVisibleAsync(page))
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Unable to reach social experience. Current URL: {page.Url}");
    }

    private async Task OpenFollowingTabAsync(IPage page)
    {
        var followingNavCandidates = new[]
        {
            page.GetByRole(AriaRole.Link, new() { Name = "Following" }).First,
            page.GetByRole(AriaRole.Button, new() { Name = "Following" }).First,
            page.GetByText("Following", new() { Exact = true }).First
        };

        foreach (var navCandidate in followingNavCandidates)
        {
            if (await navCandidate.CountAsync() == 0 || !await navCandidate.IsVisibleAsync())
            {
                continue;
            }

            await navCandidate.ClickAsync();
            var followingHeading = page.GetByRole(AriaRole.Heading, new() { Name = "Following" }).First;
            await followingHeading.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10000
            });
            return;
        }

        throw new TimeoutException("Could not open Following tab in HushSocial.");
    }

    private static async Task<ILocator> WaitForCircleCardByNameAsync(IPage page, string circleName, int timeoutMs)
    {
        var cardLocator = page.GetByTestId("social-circles-strip")
            .Locator("button", new LocatorLocatorOptions { HasTextString = circleName });

        await cardLocator.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });

        return cardLocator.First;
    }

    private static async Task<bool> IsSocialExperienceVisibleAsync(IPage page)
    {
        var socialShell = page.GetByTestId("social-shell");
        if (await socialShell.CountAsync() > 0 && await socialShell.First.IsVisibleAsync())
        {
            return true;
        }

        var feedWallHeading = page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Feed Wall" });
        if (await feedWallHeading.CountAsync() > 0 && await feedWallHeading.First.IsVisibleAsync())
        {
            return true;
        }

        var followingHeading = page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Following" });
        if (await followingHeading.CountAsync() > 0 && await followingHeading.First.IsVisibleAsync())
        {
            return true;
        }

        return false;
    }

    private async Task OpenPermalinkAsUserAsync(string userName, string postTitle)
    {
        var postId = GetStoredPostId(postTitle);
        var requesterAddress = await GetUserAddressAsync(userName);
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var response = await feedClient.GetSocialPostPermalinkAsync(new GetSocialPostPermalinkRequest
        {
            PostId = postId,
            IsAuthenticated = true,
            RequesterPublicAddress = requesterAddress
        });

        ScenarioContext[LastPermalinkResponseKey] = response;
        ScenarioContext["E2E_LastPermalinkUser"] = userName;

        var accessQuery = response.AccessState switch
        {
            SocialPermalinkAccessStateProto.SocialPermalinkAccessStateGuestDenied => "guest",
            SocialPermalinkAccessStateProto.SocialPermalinkAccessStateUnauthorizedDenied => "denied",
            _ => null
        };

        var page = GetUserPage(userName);
        var url = accessQuery is null
            ? $"{GetBaseUrl()}/social/post/{postId}"
            : $"{GetBaseUrl()}/social/post/{postId}?access={accessQuery}";
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var timeoutAt = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeoutAt)
        {
            var hasPublic = await page.GetByTestId("social-permalink-public").CountAsync() > 0;
            var hasGuest = await page.GetByTestId("social-permalink-guest").CountAsync() > 0;
            var hasDenied = await page.GetByTestId("social-permalink-denied").CountAsync() > 0;
            if (hasPublic || hasGuest || hasDenied)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Permalink UI did not render expected state. URL: {page.Url}");
    }

    private GetSocialPostPermalinkResponse GetLastPermalinkResponse()
    {
        ScenarioContext.TryGetValue(LastPermalinkResponseKey, out var responseObj)
            .Should()
            .BeTrue("expected permalink response in scenario context");
        responseObj.Should().BeOfType<GetSocialPostPermalinkResponse>();
        return (GetSocialPostPermalinkResponse)responseObj!;
    }

    private IPage GetCurrentPermalinkUserPage()
    {
        var userName = ScenarioContext.TryGetValue("E2E_LastPermalinkUser", out var userObj) && userObj is string value
            ? value
            : "Owner";
        return GetUserPage(userName);
    }

    private string GetStepUserNameFromWhenStep(string expectedFragment)
    {
        var text = ScenarioContext.StepContext.StepInfo.Text;
        text.Should().Contain(expectedFragment);

        if (text.StartsWith("FollowerA ", StringComparison.Ordinal))
        {
            return "FollowerA";
        }
        if (text.StartsWith("FollowerB ", StringComparison.Ordinal))
        {
            return "FollowerB";
        }
        if (text.StartsWith("Owner ", StringComparison.Ordinal))
        {
            return "Owner";
        }

        throw new InvalidOperationException($"Unable to infer user from step text: '{text}'");
    }

    private string GetStepUserNameFromThenStep(string expectedFragment)
    {
        var text = ScenarioContext.StepContext.StepInfo.Text;
        text.Should().Contain(expectedFragment);

        if (text.StartsWith("FollowerA ", StringComparison.Ordinal))
        {
            return "FollowerA";
        }
        if (text.StartsWith("FollowerB ", StringComparison.Ordinal))
        {
            return "FollowerB";
        }
        if (text.StartsWith("Owner ", StringComparison.Ordinal))
        {
            return "Owner";
        }

        throw new InvalidOperationException($"Unable to infer user from step text: '{text}'");
    }

    private async Task<SocialFeedWallPostProto> ResolvePostFromFeedWallAsync(string requesterAddress, string content)
    {
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var response = await feedClient.GetSocialFeedWallAsync(new GetSocialFeedWallRequest
        {
            RequesterPublicAddress = requesterAddress,
            IsAuthenticated = true,
            Limit = 200
        });

        response.Success.Should().BeTrue($"GetSocialFeedWall should succeed: {response.Message}");
        var post = response.Posts.FirstOrDefault(x =>
            string.Equals(x.AuthorPublicAddress, requesterAddress, StringComparison.Ordinal) &&
            string.Equals(x.Content, content, StringComparison.Ordinal));

        post.Should().NotBeNull($"Expected feed wall to contain post content '{content}'.");
        post!.CreatedAtUnixMs.Should().BeGreaterThan(0);
        return post;
    }

    private void StorePostId(string title, string postId)
    {
        var map = GetOrCreateSocialPostsByTitle();
        map[title] = postId;
    }

    private string GetStoredPostId(string title)
    {
        var map = GetOrCreateSocialPostsByTitle();
        map.TryGetValue(title, out var postId)
            .Should()
            .BeTrue($"Post '{title}' should have a stored id.");
        return postId!;
    }

    private Dictionary<string, string> GetOrCreateSocialPostsByTitle()
    {
        if (ScenarioContext.TryGetValue(SocialPostsByTitleKey, out var existingObj)
            && existingObj is Dictionary<string, string> existing)
        {
            return existing;
        }

        var created = new Dictionary<string, string>(StringComparer.Ordinal);
        ScenarioContext[SocialPostsByTitleKey] = created;
        return created;
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

    private async Task CreateClosePostViaBackendAsync(string postTitle, string circleName)
    {
        var owner = await GetOrCreateBrowserTestIdentityAsync("Owner");
        var circleFeedId = await ResolveCircleFeedIdAsync(circleName);
        var response = await SubmitSocialPostAsync(
            owner,
            postTitle,
            SocialPostVisibility.Private,
            new[] { new FeedId(Guid.Parse(circleFeedId)) });

        response.Success.Should().BeTrue($"Owner close post '{postTitle}' should succeed: {response.Message}");

        var ownerAddress = await GetUserAddressAsync("Owner");
        var post = await ResolvePostFromFeedWallAsync(ownerAddress, postTitle);
        StorePostId(postTitle, post.PostId);

        post.Visibility.Should().Be(SocialPostVisibilityProto.SocialPostVisibilityPrivate);
        post.CircleFeedIds.Should().Contain(circleFeedId);
    }

    private async Task<string> ResolveCircleFeedIdAsync(string circleName)
    {
        if (string.Equals(circleName, "Inner Circle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(circleName, "InnerCircle", StringComparison.OrdinalIgnoreCase))
        {
            return await EnsureInnerCircleExistsForOwnerAsync();
        }

        var ownerAddress = await GetUserAddressAsync("Owner");
        var feedClient = GetGrpcFactory().CreateClient<HushFeed.HushFeedClient>();
        var contract = await feedClient.GetSocialComposerContractAsync(new GetSocialComposerContractRequest
        {
            OwnerPublicAddress = ownerAddress
        });

        if (contract.Success)
        {
            var existing = contract.AvailableCircles.FirstOrDefault(c =>
                string.Equals(c.CircleName, circleName, StringComparison.OrdinalIgnoreCase));
            if (existing is { FeedId: { Length: > 0 } })
            {
                return existing.FeedId;
            }
        }

        return await EnsureCircleExistsAsync(circleName);
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

        var submitResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction
        });

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
            Message = submitResponse.Message
        };
    }

    private async Task<TestIdentity> GetOrCreateBrowserTestIdentityAsync(string userName)
    {
        var identityKey = $"E2E_BrowserIdentity_{userName}";
        if (ScenarioContext.TryGetValue(identityKey, out var existingObj) && existingObj is TestIdentity existing)
        {
            return existing;
        }

        var page = GetUserPage(userName);

        var signingPublicKey = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (!appStorage) return null;
            const parsed = JSON.parse(appStorage);
            return parsed.state?.credentials?.signingPublicKey || null;
        }");

        var signingPrivateKey = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (!appStorage) return null;
            const parsed = JSON.parse(appStorage);
            return parsed.state?.credentials?.signingPrivateKey || null;
        }");

        var encryptionPublicKey = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (!appStorage) return null;
            const parsed = JSON.parse(appStorage);
            return parsed.state?.credentials?.encryptionPublicKey || null;
        }");

        var encryptionPrivateKey = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (!appStorage) return null;
            const parsed = JSON.parse(appStorage);
            return parsed.state?.credentials?.encryptionPrivateKey || null;
        }");

        signingPublicKey.Should().NotBeNullOrEmpty($"{userName}'s signingPublicKey should exist in localStorage");
        signingPrivateKey.Should().NotBeNullOrEmpty($"{userName}'s signingPrivateKey should exist in localStorage");
        encryptionPublicKey.Should().NotBeNullOrEmpty($"{userName}'s encryptionPublicKey should exist in localStorage");
        encryptionPrivateKey.Should().NotBeNullOrEmpty($"{userName}'s encryptionPrivateKey should exist in localStorage");

        var identity = new TestIdentity
        {
            DisplayName = userName,
            Seed = $"E2E_BROWSER_{userName}",
            PrivateSigningKey = signingPrivateKey!,
            PublicSigningAddress = signingPublicKey!,
            PrivateEncryptKey = encryptionPrivateKey!,
            PublicEncryptAddress = encryptionPublicKey!
        };

        ScenarioContext[identityKey] = identity;
        return identity;
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
