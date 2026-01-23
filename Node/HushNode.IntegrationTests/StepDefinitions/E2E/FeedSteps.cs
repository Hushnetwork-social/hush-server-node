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
        // Personal feeds have data-testid="feed-item:personal"
        _ = userName; // Parameter kept for Gherkin readability but not used

        var page = await GetOrCreatePageAsync();

        // Wait for personal feed to appear using its specific test ID
        var personalFeed = page.GetByTestId("feed-item:personal");

        Console.WriteLine("-> Waiting for personal feed (feed-item:personal)...");
        try
        {
            await Expect(personalFeed.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
            {
                Timeout = 30000
            });
            Console.WriteLine("-> Found personal feed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"-> Personal feed not found: {ex.Message}");

            // Diagnostic: list all feed items in the DOM
            var allFeeds = page.Locator("[data-testid^='feed-item']");
            var feedCount = await allFeeds.CountAsync();
            Console.WriteLine($"-> Total feed items found: {feedCount}");

            for (int i = 0; i < feedCount && i < 5; i++)
            {
                var testIdAttr = await allFeeds.Nth(i).GetAttributeAsync("data-testid");
                var itemText = await allFeeds.Nth(i).TextContentAsync();
                Console.WriteLine($"-> Feed {i}: testid={testIdAttr}, text={itemText?.Substring(0, Math.Min(50, itemText?.Length ?? 0))}...");
            }

            // Check for empty state
            var emptyState = page.GetByText("No Feeds Yet");
            if (await emptyState.CountAsync() > 0)
            {
                Console.WriteLine("-> Found 'No Feeds Yet' message - feeds array is empty");
            }

            throw;
        }
    }

    [When(@"the user clicks on their personal feed")]
    public async Task WhenTheUserClicksOnTheirPersonalFeed()
    {
        var page = await GetOrCreatePageAsync();

        // Personal feeds have data-testid="feed-item:personal"
        // Use the visible element helper to handle responsive layouts
        var personalFeed = await WaitForVisibleFeedAsync(page, "feed-item:personal", 30000);

        await personalFeed.ClickAsync();

        // Wait for chat view to load (message input appears)
        await WaitForTestIdAsync(page, "message-input", 15000);
    }

    [When(@"the user clicks on the group feed ""(.*)""")]
    public async Task WhenTheUserClicksOnGroupFeed(string groupName)
    {
        var page = await GetOrCreatePageAsync();

        // Group feeds have data-testid="feed-item:group:{sanitized-name}"
        var testId = $"feed-item:group:{SanitizeName(groupName)}";
        var groupFeed = await WaitForVisibleFeedAsync(page, testId, 30000);

        await groupFeed.ClickAsync();

        // Wait for chat view to load
        await WaitForTestIdAsync(page, "message-input", 15000);
    }

    [When(@"the user clicks on the chat feed with ""(.*)""")]
    public async Task WhenTheUserClicksOnChatFeed(string participantName)
    {
        var page = await GetOrCreatePageAsync();

        // Chat feeds have data-testid="feed-item:chat:{sanitized-name}"
        var testId = $"feed-item:chat:{SanitizeName(participantName)}";
        var chatFeed = await WaitForVisibleFeedAsync(page, testId, 30000);

        await chatFeed.ClickAsync();

        // Wait for chat view to load
        await WaitForTestIdAsync(page, "message-input", 15000);
    }

    [Then(@"the feed list should contain a group feed ""(.*)""")]
    public async Task ThenFeedListShouldContainGroupFeed(string groupName)
    {
        var page = await GetOrCreatePageAsync();

        var testId = $"feed-item:group:{SanitizeName(groupName)}";
        var groupFeed = page.GetByTestId(testId);

        Console.WriteLine($"-> Waiting for group feed ({testId})...");
        await Expect(groupFeed.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 30000
        });
        Console.WriteLine("-> Found group feed");
    }

    [Then(@"the feed list should contain a chat feed with ""(.*)""")]
    public async Task ThenFeedListShouldContainChatFeed(string participantName)
    {
        var page = await GetOrCreatePageAsync();

        var testId = $"feed-item:chat:{SanitizeName(participantName)}";
        var chatFeed = page.GetByTestId(testId);

        Console.WriteLine($"-> Waiting for chat feed ({testId})...");
        await Expect(chatFeed.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 30000
        });
        Console.WriteLine("-> Found chat feed");
    }

    /// <summary>
    /// Sanitizes a name for use in test IDs.
    /// Matches the JavaScript implementation in ChatListItem.tsx.
    /// </summary>
    private static string SanitizeName(string name)
    {
        // Lowercase, replace non-alphanumeric with hyphens, collapse multiple hyphens, trim hyphens
        var sanitized = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "-");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "-+", "-");
        return sanitized.Trim('-');
    }

    /// <summary>
    /// Waits for a visible feed item with the specified test ID that is also ready for messaging.
    /// Handles responsive layouts where feeds appear in both sidebar and main content.
    /// Ready means the feed's encryption key has been decrypted (data-feed-ready="true").
    /// </summary>
    private async Task<ILocator> WaitForVisibleFeedAsync(IPage page, string testId, int timeoutMs = 10000)
    {
        var allFeeds = page.GetByTestId(testId);
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var count = await allFeeds.CountAsync();
            for (int i = 0; i < count; i++)
            {
                var feed = allFeeds.Nth(i);
                if (await feed.IsVisibleAsync())
                {
                    // Also check if feed is ready (has encryption key decrypted)
                    var readyAttr = await feed.GetAttributeAsync("data-feed-ready");
                    if (readyAttr == "true")
                    {
                        return feed;
                    }
                    Console.WriteLine($"[E2E] Found visible feed {testId} but not ready yet (data-feed-ready={readyAttr})");
                }
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"No visible ready feed found with data-testid='{testId}' within {timeoutMs}ms");
    }

    /// <summary>
    /// Helper method for Playwright assertions.
    /// </summary>
    private static ILocatorAssertions Expect(ILocator locator)
    {
        return Assertions.Expect(locator);
    }
}
