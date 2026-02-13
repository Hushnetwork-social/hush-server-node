using FluentAssertions;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// FEAT-063: Step definitions for Cross-Device Read Sync E2E tests.
/// Tests that reading messages on one device updates unread badges on another.
/// Uses a hybrid approach: identity via browser on primary context,
/// second browser context with same credentials for cross-device simulation.
/// </summary>
[Binding]
internal sealed class CrossDeviceReadSyncSteps : BrowserStepsBase
{
    private const string DeviceBPageKey = "E2E_Page_DeviceB";
    private const string DeviceBContextKey = "E2E_Context_DeviceB";

    public CrossDeviceReadSyncSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    // =========================================================================
    // Given Steps: Second browser context (same user, different device)
    // =========================================================================

    /// <summary>
    /// Creates a second browser context for the same user, simulating a second device.
    /// Copies the user's credentials from the primary context's localStorage to the new context.
    /// </summary>
    [Given(@"a second browser context for ""(.*)"" as ""(.*)""")]
    public async Task GivenASecondBrowserContextForUserAs(string userName, string deviceName)
    {
        Console.WriteLine($"[E2E ReadSync] Creating second browser context for '{userName}' as '{deviceName}'...");

        // Get credentials from the primary context
        var primaryPage = GetUserPage(userName);
        var localStorageData = await primaryPage.EvaluateAsync<string>(
            "() => localStorage.getItem('hush-app-storage')");
        localStorageData.Should().NotBeNullOrEmpty(
            $"Primary context for '{userName}' should have credentials in localStorage");

        // Create a new browser context
        var playwright = GetPlaywright();
        var (context, page) = await playwright.CreatePageAsync();

        // Set up logging
        SetupNetworkLogging(page, deviceName);

        // Navigate to the base URL first (localStorage needs a page loaded)
        var baseUrl = GetBaseUrl();
        await page.GotoAsync($"{baseUrl}/auth");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Inject the same credentials into the new context's localStorage
        await page.EvaluateAsync($@"(data) => {{
            localStorage.setItem('hush-app-storage', data);
        }}", localStorageData);

        Console.WriteLine($"[E2E ReadSync] Injected credentials into '{deviceName}' localStorage");

        // Navigate to dashboard (credentials are now in localStorage)
        await page.GotoAsync($"{baseUrl}/dashboard");
        await Assertions.Expect(page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex(@"/dashboard"),
            new PageAssertionsToHaveURLOptions { Timeout = 15000 });

        // Wait for SyncProvider to mount
        await Task.Delay(1000);

        // Store in context
        ScenarioContext[$"E2E_Page_{deviceName}"] = page;
        ScenarioContext[$"E2E_Context_{deviceName}"] = context;

        Console.WriteLine($"[E2E ReadSync] Second browser context '{deviceName}' created and on dashboard");
    }

    // =========================================================================
    // When Steps: Device-specific sync triggers
    // =========================================================================

    /// <summary>
    /// Triggers sync on a named device context.
    /// </summary>
    [Given(@"""(.*)"" triggers sync")]
    [When(@"""(.*)"" triggers sync")]
    public async Task WhenDeviceTriggersSync(string deviceName)
    {
        var page = GetDevicePage(deviceName);

        Console.WriteLine($"[E2E ReadSync] '{deviceName}' triggering sync...");
        await TriggerSyncAsync(page);

        // Wait for feeds to render after sync
        await Task.Delay(2000);

        Console.WriteLine($"[E2E ReadSync] '{deviceName}' sync complete");
    }

    // =========================================================================
    // Then Steps: Unread badge assertions
    // =========================================================================

    /// <summary>
    /// Asserts that the primary context's feed list shows an unread badge on a chat feed.
    /// </summary>
    [Then(@"the feed list should show unread badge on ChatFeed with ""(.*)""")]
    public async Task ThenFeedListShouldShowUnreadBadgeOnChatFeedWith(string otherUserName)
    {
        var page = await GetOrCreatePageAsync();
        await AssertUnreadBadgeVisibleAsync(page, otherUserName, shouldBeVisible: true, "Alice (primary)");
    }

    /// <summary>
    /// Asserts that the primary context's feed list does NOT show an unread badge.
    /// </summary>
    [Then(@"the feed list should NOT show unread badge on ChatFeed with ""(.*)""")]
    public async Task ThenFeedListShouldNotShowUnreadBadgeOnChatFeedWith(string otherUserName)
    {
        var page = await GetOrCreatePageAsync();
        await AssertUnreadBadgeVisibleAsync(page, otherUserName, shouldBeVisible: false, "Alice (primary)");
    }

    /// <summary>
    /// Asserts that a named device's feed list shows an unread badge on a chat feed.
    /// </summary>
    [Then(@"""(.*)"" feed list should show unread badge on ChatFeed with ""(.*)""")]
    public async Task ThenDeviceFeedListShouldShowUnreadBadgeOnChatFeedWith(string deviceName, string otherUserName)
    {
        var page = GetDevicePage(deviceName);
        await AssertUnreadBadgeVisibleAsync(page, otherUserName, shouldBeVisible: true, deviceName);
    }

    /// <summary>
    /// Asserts that a named device's feed list does NOT show an unread badge.
    /// </summary>
    [Then(@"""(.*)"" feed list should NOT show unread badge on ChatFeed with ""(.*)""")]
    public async Task ThenDeviceFeedListShouldNotShowUnreadBadgeOnChatFeedWith(string deviceName, string otherUserName)
    {
        var page = GetDevicePage(deviceName);
        await AssertUnreadBadgeVisibleAsync(page, otherUserName, shouldBeVisible: false, deviceName);
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    /// <summary>
    /// Asserts that the unread badge on a chat feed item is visible or hidden.
    /// Uses retry-based polling to handle async state updates.
    /// </summary>
    private async Task AssertUnreadBadgeVisibleAsync(
        IPage page, string otherUserName, bool shouldBeVisible, string contextName)
    {
        var sanitizedName = SanitizeName(otherUserName);
        var feedTestId = $"feed-item:chat:{sanitizedName}";

        Console.WriteLine($"[E2E ReadSync] Asserting unread badge {(shouldBeVisible ? "VISIBLE" : "HIDDEN")} on '{feedTestId}' for {contextName}...");

        // Find the visible feed item (handles responsive layout duplicates)
        var feedList = page.GetByTestId("feed-list").First;
        await Assertions.Expect(feedList).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        var feedItem = feedList.GetByTestId(feedTestId);

        // Wait for feed item to be visible
        await Assertions.Expect(feedItem.First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // Look for unread badge within the feed item
        var unreadBadge = feedItem.First.GetByTestId("unread-badge");

        if (shouldBeVisible)
        {
            await Assertions.Expect(unreadBadge).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
            var badgeText = await unreadBadge.Locator("span").TextContentAsync();
            Console.WriteLine($"[E2E ReadSync] Verified: Unread badge visible with count '{badgeText}' on {contextName}");
        }
        else
        {
            await Assertions.Expect(unreadBadge).ToBeHiddenAsync(
                new LocatorAssertionsToBeHiddenOptions { Timeout = 10000 });
            Console.WriteLine($"[E2E ReadSync] Verified: Unread badge hidden on {contextName}");
        }
    }

    /// <summary>
    /// Gets a page for a named device from ScenarioContext.
    /// Falls back to the standard E2E_Page_{name} key pattern.
    /// </summary>
    private IPage GetDevicePage(string deviceName)
    {
        var key = $"E2E_Page_{deviceName}";
        if (ScenarioContext.TryGetValue(key, out var pageObj) && pageObj is IPage page)
        {
            return page;
        }

        throw new InvalidOperationException(
            $"No browser page found for device '{deviceName}'. " +
            "Ensure the device context has been created first.");
    }

    /// <summary>
    /// Gets a user's page from ScenarioContext (standard multi-user pattern).
    /// </summary>
    private IPage GetUserPage(string userName)
    {
        var key = $"E2E_Page_{userName}";
        if (ScenarioContext.TryGetValue(key, out var pageObj) && pageObj is IPage page)
        {
            return page;
        }

        // Fall back to main page
        if (ScenarioContext.TryGetValue("E2E_MainPage", out var mainPageObj) && mainPageObj is IPage mainPage)
        {
            return mainPage;
        }

        throw new InvalidOperationException($"No browser page found for user '{userName}'");
    }

    /// <summary>
    /// Sanitizes a name for use in test IDs.
    /// Matches the JavaScript implementation in ChatListItem.tsx.
    /// </summary>
    private static string SanitizeName(string name)
    {
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            name.ToLowerInvariant(), "[^a-z0-9]", "-");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "-+", "-");
        return sanitized.Trim('-');
    }
}
