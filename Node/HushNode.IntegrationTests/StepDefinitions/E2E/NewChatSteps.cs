using Microsoft.Playwright;
using TechTalk.SpecFlow;
using HushNode.IntegrationTests.Hooks;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for creating new chats via the New Chat UI flow.
/// </summary>
[Binding]
internal sealed class NewChatSteps : BrowserStepsBase
{
    public NewChatSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    /// <summary>
    /// Creates a new chat with another user via the New Chat UI.
    /// Clicks nav-new-chat, searches for the target user, clicks result, waits for feed creation.
    /// </summary>
    [When(@"(\w+) creates a new chat with ""(.*)"" via browser")]
    public async Task WhenUserCreatesNewChatViaBrowser(string userName, string targetDisplayName)
    {
        var page = GetPageForUser(userName);
        var sanitizedTarget = SanitizeName(targetDisplayName);

        Console.WriteLine($"[E2E] {userName} creating new chat with '{targetDisplayName}'...");

        // 1. Click "New Chat" nav button in sidebar
        await ClickTestIdAsync(page, "nav-new-chat");
        Console.WriteLine($"[E2E] Clicked nav-new-chat");

        // 2. Wait for search input to appear
        await WaitForTestIdAsync(page, "new-chat-search-input", 10000);

        // 3. Fill search with target display name
        await FillTestIdAsync(page, "new-chat-search-input", targetDisplayName);

        // 4. Click search button
        await ClickTestIdAsync(page, "new-chat-search-button");
        Console.WriteLine($"[E2E] Searching for '{targetDisplayName}'...");

        // 5. Wait for search results to appear
        var resultTestId = $"new-chat-result:{sanitizedTarget}";
        var result = await WaitForTestIdAsync(page, resultTestId, 15000);
        Console.WriteLine($"[E2E] Found search result for '{targetDisplayName}'");

        // 6. Start listening for transaction BEFORE clicking the result
        var waiter = StartListeningForTransactions(minTransactions: 1);

        // 7. Click the result to create the chat feed
        await result.ClickAsync();
        Console.WriteLine($"[E2E] Clicked result, waiting for chat creation TX...");

        // 8. Wait for transaction and produce block
        try
        {
            await waiter.WaitAsync();
        }
        finally
        {
            waiter.Dispose();
        }

        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();
        Console.WriteLine($"[E2E] Block produced after chat creation");

        // 9. Trigger sync to pick up the new feed
        await page.EvaluateAsync("() => window.__e2e_triggerSync()");

        // 10. Wait for the chat feed to appear with data-feed-ready="true"
        var feedTestId = $"feed-item:chat:{sanitizedTarget}";
        await WaitForReadyFeedAsync(page, feedTestId, 15000);

        Console.WriteLine($"[E2E] {userName} created chat with '{targetDisplayName}' - feed ready");
    }

    /// <summary>
    /// Waits for a feed item to appear with data-feed-ready="true".
    /// </summary>
    private async Task WaitForReadyFeedAsync(IPage page, string testId, int timeoutMs)
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
                    var readyAttr = await feed.GetAttributeAsync("data-feed-ready");
                    if (readyAttr == "true")
                    {
                        return;
                    }
                }
            }
            await Task.Delay(200);
        }

        throw new TimeoutException($"Feed '{testId}' did not become ready within {timeoutMs}ms");
    }

    /// <summary>
    /// Gets the page for a specific user from ScenarioContext.
    /// </summary>
    private IPage GetPageForUser(string userName)
    {
        var key = $"E2E_Page_{userName}";
        if (ScenarioContext.TryGetValue(key, out var pageObj) && pageObj is IPage page)
        {
            // Update current user context
            ScenarioContext["E2E_MainPage"] = page;
            ScenarioContext["CurrentUser"] = userName;
            return page;
        }

        throw new InvalidOperationException($"No browser page found for user '{userName}'");
    }

    /// <summary>
    /// Sanitizes a name for use in test IDs.
    /// Matches the JavaScript implementation: lowercase, replace non-alphanumeric with hyphens.
    /// </summary>
    private static string SanitizeName(string name)
    {
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            name.ToLowerInvariant(), "[^a-z0-9]", "-");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "-+", "-");
        return sanitized.Trim('-');
    }
}
