using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for EC-003/EC-004: Logout with pending/failed messages.
/// Tests the confirmation dialog that warns users about unsent messages.
/// </summary>
[Binding]
internal sealed class LogoutSteps : BrowserStepsBase
{
    public LogoutSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    /// <summary>
    /// Opens the user menu and clicks the logout button.
    /// The sidebar has a user avatar button that toggles a dropdown with the Logout option.
    /// Uses testid selectors with fallbacks for DOM-structure selectors, so the test works
    /// regardless of whether the Docker image includes the data-testid attributes.
    /// </summary>
    [When(@"the user clicks the logout button")]
    public async Task WhenTheUserClicksTheLogoutButton()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E Logout] Opening user menu and clicking logout...");

        // First, open the user menu (logout is inside a dropdown)
        // Try data-testid first; fall back to the sidebar's user profile button (last div > button in aside)
        var userMenuTrigger = await FindVisibleLocatorAsync(page,
            "[data-testid='user-menu-trigger']",
            "aside > div:last-child button");

        await userMenuTrigger.ClickAsync();
        Console.WriteLine("[E2E Logout] User menu opened");

        // Wait for the dropdown menu to appear
        await Task.Delay(500);

        // Click the logout button
        // Try data-testid first; fall back to a button containing "Logout" text inside the sidebar
        var logoutButton = await FindVisibleLocatorAsync(page,
            "[data-testid='logout-button']",
            "aside button:has-text('Logout')");

        await logoutButton.ClickAsync();

        // Brief delay for the logout handler to process
        await Task.Delay(500);

        Console.WriteLine("[E2E Logout] Logout button clicked");
    }

    /// <summary>
    /// Finds a visible locator, trying the primary selector first then the fallback.
    /// </summary>
    private static async Task<ILocator> FindVisibleLocatorAsync(IPage page, string primarySelector, string fallbackSelector)
    {
        // Try primary selector (data-testid)
        var primary = page.Locator(primarySelector);
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(5);

        while (DateTime.UtcNow - startTime < timeout)
        {
            if (await primary.CountAsync() > 0 && await primary.First.IsVisibleAsync())
            {
                Console.WriteLine($"[E2E Logout] Found element via primary selector: {primarySelector}");
                return primary.First;
            }
            await Task.Delay(250);
        }

        // Fall back to structural/text selector
        Console.WriteLine($"[E2E Logout] Primary selector not found, trying fallback: {fallbackSelector}");
        var fallback = page.Locator(fallbackSelector);

        startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < timeout)
        {
            if (await fallback.CountAsync() > 0 && await fallback.First.IsVisibleAsync())
            {
                Console.WriteLine($"[E2E Logout] Found element via fallback selector: {fallbackSelector}");
                return fallback.First;
            }
            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Neither '{primarySelector}' nor '{fallbackSelector}' found visible within timeout");
    }

    /// <summary>
    /// Verifies that a confirmation dialog is visible with the specified title.
    /// </summary>
    [Then(@"a confirmation dialog should be visible with title ""(.*)""")]
    public async Task ThenConfirmationDialogShouldBeVisibleWithTitle(string expectedTitle)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E Logout] Verifying confirmation dialog with title: '{expectedTitle}'");

        // Find the dialog by role
        var dialog = page.Locator("[role='dialog']");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });

        // Verify the title text
        var titleElement = dialog.Locator("#confirm-dialog-title");
        await Expect(titleElement).ToHaveTextAsync(expectedTitle, new LocatorAssertionsToHaveTextOptions
        {
            Timeout = 3000
        });

        Console.WriteLine($"[E2E Logout] Confirmation dialog visible with title: '{expectedTitle}'");
    }

    /// <summary>
    /// Clicks a button with the specified text inside the dialog.
    /// </summary>
    [When(@"the user clicks ""(.*)"" in the dialog")]
    public async Task WhenTheUserClicksButtonInDialog(string buttonText)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E Logout] Clicking '{buttonText}' button in dialog...");

        // Find the dialog and locate the button by its text
        var dialog = page.Locator("[role='dialog']");
        var button = dialog.Locator($"button:has-text('{buttonText}')");

        await Expect(button).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 3000
        });
        await button.ClickAsync();

        Console.WriteLine($"[E2E Logout] Clicked '{buttonText}' button");
    }

    /// <summary>
    /// Verifies that the page navigated to the auth page after logout.
    /// </summary>
    [Then(@"the page should navigate to the auth page")]
    public async Task ThenPageShouldNavigateToAuthPage()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E Logout] Verifying navigation to auth page...");

        // Use polling-based assertion for SPA navigation (router.push uses history.pushState)
        await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/auth"), new PageAssertionsToHaveURLOptions
        {
            Timeout = 10000
        });

        Console.WriteLine($"[E2E Logout] Successfully navigated to auth page: {page.Url}");
    }

    /// <summary>
    /// EC-004: Injects a failed message directly into the live Zustand feeds store.
    /// Uses window.__zustand_stores.feedsStore (exposed for E2E) to call addMessages()
    /// on the live store, avoiding page reload and rehydration timing issues.
    /// </summary>
    [When(@"a failed message is injected into the feeds store")]
    public async Task WhenFailedMessageIsInjectedIntoFeedsStore()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E Logout] Injecting failed message into live Zustand feeds store...");

        // Inject directly into the live Zustand store via window.__zustand_stores.feedsStore.
        // This avoids the fragile localStorage → reload → rehydrate → sync race condition.
        var injected = await page.EvaluateAsync<bool>(@"() => {
            const store = window.__zustand_stores?.feedsStore;
            if (!store) {
                console.error('[E2E] feedsStore not found on window.__zustand_stores');
                return false;
            }

            const state = store.getState();
            const feeds = state.feeds || [];
            if (feeds.length === 0) {
                console.error('[E2E] No feeds found in store');
                return false;
            }

            const feedId = feeds[0].id;
            const failedMessage = {
                id: 'injected-failed-msg-' + Date.now(),
                feedId: feedId,
                senderPublicKey: 'test-key',
                content: 'Injected failed message',
                timestamp: Date.now(),
                isConfirmed: false,
                status: 'failed',
                retryCount: 3,
                lastAttemptTime: Date.now()
            };

            // Use the store's addMessages action to inject into live state
            state.addMessages(feedId, [failedMessage]);

            // Verify it was injected
            const hasPending = store.getState().hasPendingOrFailedMessages();
            console.log('[E2E] Injected failed message, hasPendingOrFailed:', hasPending);
            return hasPending;
        }");

        if (!injected)
        {
            throw new InvalidOperationException(
                "Failed to inject failed message into feeds store. " +
                "Ensure window.__zustand_stores.feedsStore is available (NEXT_PUBLIC_DEBUG_LOGGING=true).");
        }

        Console.WriteLine("[E2E Logout] Failed message injected successfully into live store");
    }

    /// <summary>
    /// Helper method for Playwright assertions.
    /// </summary>
    private static ILocatorAssertions Expect(ILocator locator)
    {
        return Assertions.Expect(locator);
    }
}
