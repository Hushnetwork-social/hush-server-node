using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for FEAT-059: Auto-Pagination Scroll-Based Prefetch.
/// Tests verify that prefetch is initialized on feed open and that
/// the jump to bottom button works correctly.
/// </summary>
[Binding]
internal sealed class AutoPaginationSteps : BrowserStepsBase
{
    public AutoPaginationSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [Given(@"the transactions are processed")]
    public async Task GivenTheTransactionsAreProcessed()
    {
        // Produce a block to mine any remaining pending transactions
        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();

        // Trigger browser sync so the client picks up the new block
        var page = await GetOrCreatePageAsync();
        await TriggerSyncAsync(page);

        // Wait for sync to complete
        await Task.Delay(1000);

        Console.WriteLine("[E2E FEAT-059] Transactions processed and synced");
    }

    [Then(@"the prefetch state should be initialized for the current feed")]
    public async Task ThenPrefetchStateShouldBeInitialized()
    {
        var page = await GetOrCreatePageAsync();

        // Wait for prefetch initialization to complete
        await Task.Delay(1000);

        // Check if prefetch state exists in the store
        // Use Object.keys to check for any prefetch state entry rather than
        // depending on appStore (which may not be exposed to __zustand_stores)
        var hasPrefetchState = await page.EvaluateAsync<bool>(@"() => {
            const state = window.__zustand_stores?.feedsStore?.getState?.();
            if (!state) return false;

            return state.prefetchState && Object.keys(state.prefetchState).length > 0;
        }");

        hasPrefetchState.Should().BeTrue("Prefetch state should be initialized for the current feed");

        Console.WriteLine("[E2E FEAT-059] Prefetch state is initialized");
    }

    [Then(@"the loaded page count should be at least (\d+)")]
    public async Task ThenLoadedPageCountShouldBeAtLeast(int minPages)
    {
        var page = await GetOrCreatePageAsync();

        // Wait for prefetch initialization to complete
        await Task.Delay(500);

        // Get the first prefetch state entry's loadedPageCount
        // (there's only one feed open in E2E test scenarios)
        var pageCount = await page.EvaluateAsync<int>(@"() => {
            const state = window.__zustand_stores?.feedsStore?.getState?.();
            if (!state?.prefetchState) return 0;

            const entries = Object.values(state.prefetchState);
            if (entries.length === 0) return 0;

            return entries[0]?.loadedPageCount || 0;
        }");

        pageCount.Should().BeGreaterOrEqualTo(minPages,
            $"Loaded page count should be at least {minPages}");

        Console.WriteLine($"[E2E FEAT-059] Loaded page count: {pageCount}");
    }

    [Then(@"the jump to bottom button should not be visible")]
    public async Task ThenJumpToBottomButtonShouldNotBeVisible()
    {
        var page = await GetOrCreatePageAsync();

        // Poll for up to 5 seconds for the button to disappear.
        // Virtuoso's atBottomStateChange callback fires asynchronously
        // and may need time to report atBottom=true after rendering.
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(5);
        var anyVisible = true;

        while (DateTime.UtcNow - startTime < timeout)
        {
            anyVisible = await IsAnyJumpButtonVisibleAsync(page);
            if (!anyVisible) break;
            await Task.Delay(250);
        }

        anyVisible.Should().BeFalse("Jump to bottom button should not be visible when at bottom");

        Console.WriteLine("[E2E FEAT-059] Jump to bottom button is not visible (as expected)");
    }

    [Then(@"the jump to bottom button should be visible")]
    public async Task ThenJumpToBottomButtonShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();

        // Poll for up to 5 seconds for the button to appear.
        // Virtuoso's atBottomStateChange callback fires asynchronously
        // after a scroll event, so the button may not render immediately.
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(5);
        var anyVisible = false;

        while (DateTime.UtcNow - startTime < timeout)
        {
            anyVisible = await IsAnyJumpButtonVisibleAsync(page);
            if (anyVisible) break;
            await Task.Delay(250);
        }

        anyVisible.Should().BeTrue("Jump to bottom button should be visible when scrolled up");

        Console.WriteLine("[E2E FEAT-059] Jump to bottom button is visible");
    }

    [When(@"Alice scrolls up in the message list")]
    public async Task WhenAliceScrollsUpInMessageList()
    {
        var page = await GetOrCreatePageAsync();

        // Wait for at least one message to be rendered
        await page.GetByTestId("message").First.WaitForAsync(new() { Timeout = 10000 });

        // Find the Virtuoso scroll container by walking up from a VISIBLE message element
        // to the nearest scrollable parent, then scroll to top.
        // Must target visible messages because the responsive layout renders two ChatViews
        // (mobile + desktop) and only one is CSS-visible.
        await page.EvaluateAsync(@"() => {
            const messages = document.querySelectorAll('[data-testid=""message""]');
            let visibleMsg = null;
            for (const msg of messages) {
                if (msg.offsetHeight > 0 && msg.offsetWidth > 0) {
                    visibleMsg = msg;
                    break;
                }
            }
            if (!visibleMsg) return;
            let el = visibleMsg.parentElement;
            while (el) {
                const style = getComputedStyle(el);
                const isScrollable = el.scrollHeight > el.clientHeight
                    && (style.overflowY === 'auto' || style.overflowY === 'scroll');
                if (isScrollable) {
                    el.scrollTop = 0;
                    return;
                }
                el = el.parentElement;
            }
        }");

        // Wait for scroll and UI to settle
        await Task.Delay(1000);

        Console.WriteLine("[E2E FEAT-059] Scrolled up in message list");
    }

    [When(@"Alice clicks the jump to bottom button")]
    public async Task WhenAliceClicksJumpToBottomButton()
    {
        var page = await GetOrCreatePageAsync();

        // Find the visible jump button (responsive layout has two ChatViews)
        var jumpButtons = page.GetByTestId("jump-to-bottom-button");
        var count = await jumpButtons.CountAsync();
        ILocator? visibleButton = null;
        for (int i = 0; i < count; i++)
        {
            if (await jumpButtons.Nth(i).IsVisibleAsync())
            {
                visibleButton = jumpButtons.Nth(i);
                break;
            }
        }

        visibleButton.Should().NotBeNull("A visible jump-to-bottom button should exist");
        await visibleButton!.ClickAsync();

        // Wait for scroll animation
        await Task.Delay(500);

        Console.WriteLine("[E2E FEAT-059] Clicked jump to bottom button");
    }

    /// <summary>
    /// Checks if any jump-to-bottom button is CSS-visible on the page.
    /// Handles the responsive layout which renders two ChatView instances (mobile + desktop).
    /// </summary>
    private async Task<bool> IsAnyJumpButtonVisibleAsync(IPage page)
    {
        var jumpButtons = page.GetByTestId("jump-to-bottom-button");
        var count = await jumpButtons.CountAsync();
        for (int i = 0; i < count; i++)
        {
            if (await jumpButtons.Nth(i).IsVisibleAsync())
                return true;
        }
        return false;
    }

    #region Manual Test Scenario Steps

    [Given(@"this scenario requires manual testing")]
    public void GivenScenarioRequiresManualTesting()
    {
        Console.WriteLine("[E2E FEAT-059] This scenario is marked for manual testing");
        Console.WriteLine("[E2E FEAT-059] See scenario description for manual test steps");
    }

    [Then(@"skip automated verification")]
    public void ThenSkipAutomatedVerification()
    {
        Console.WriteLine("[E2E FEAT-059] Skipping automated verification - manual testing required");
        // This step does nothing - it's a placeholder for manual test scenarios
    }

    #endregion
}
