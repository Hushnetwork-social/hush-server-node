using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for feed list and feed selection operations.
/// </summary>
[Binding]
internal sealed class FeedSteps : BrowserStepsBase
{
    public FeedSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [Then(@"the feed list should contain a personal feed for ""(.*)""")]
    public async Task ThenFeedListShouldContainPersonalFeed(string userName)
    {
        var page = await GetOrCreatePageAsync();

        // Wait for feed list to load
        await WaitForTestIdAsync(page, "feed-list", 15000);

        // Find feed item containing user name
        var feedItem = page.GetByTestId("feed-item").Filter(new LocatorFilterOptions
        {
            HasText = userName
        });

        // Use Playwright's auto-retry mechanism
        await Expect(feedItem.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 10000
        });
    }

    [When(@"the user clicks on their personal feed")]
    public async Task WhenTheUserClicksOnTheirPersonalFeed()
    {
        var page = await GetOrCreatePageAsync();
        var displayName = ScenarioContext["UserDisplayName"] as string;

        displayName.Should().NotBeNullOrEmpty("Display name must be set before clicking on personal feed");

        // Find and click on the personal feed
        var feedItem = page.GetByTestId("feed-item").Filter(new LocatorFilterOptions
        {
            HasText = displayName
        });

        await feedItem.First.ClickAsync();

        // Wait for chat view to load (message input appears)
        await WaitForTestIdAsync(page, "message-input", 15000);
    }

    /// <summary>
    /// Helper method for Playwright assertions.
    /// </summary>
    private static ILocatorAssertions Expect(ILocator locator)
    {
        return Assertions.Expect(locator);
    }
}
