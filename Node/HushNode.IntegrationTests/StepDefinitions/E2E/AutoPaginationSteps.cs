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

    [Then(@"the prefetch state should be initialized for the current feed")]
    public async Task ThenPrefetchStateShouldBeInitialized()
    {
        var page = await GetOrCreatePageAsync();

        // Wait for prefetch initialization to complete
        await Task.Delay(1000);

        // Check if prefetch state exists in the store
        var hasPrefetchState = await page.EvaluateAsync<bool>(@"() => {
            const state = window.__zustand_stores?.feedsStore?.getState?.();
            if (!state) return false;

            const selectedFeedId = window.__zustand_stores?.appStore?.getState?.()?.selectedFeedId;
            if (!selectedFeedId) return false;

            return state.prefetchState && state.prefetchState[selectedFeedId] !== undefined;
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

        var pageCount = await page.EvaluateAsync<int>(@"() => {
            const state = window.__zustand_stores?.feedsStore?.getState?.();
            if (!state) return 0;

            const selectedFeedId = window.__zustand_stores?.appStore?.getState?.()?.selectedFeedId;
            if (!selectedFeedId) return 0;

            return state.prefetchState?.[selectedFeedId]?.loadedPageCount || 0;
        }");

        pageCount.Should().BeGreaterOrEqualTo(minPages,
            $"Loaded page count should be at least {minPages}");

        Console.WriteLine($"[E2E FEAT-059] Loaded page count: {pageCount}");
    }

    [Then(@"the jump to bottom button should not be visible")]
    public async Task ThenJumpToBottomButtonShouldNotBeVisible()
    {
        var page = await GetOrCreatePageAsync();

        // Wait for UI to settle
        await Task.Delay(500);

        // Check if the jump to bottom button is visible
        var jumpButton = page.GetByTestId("jump-to-bottom-button");
        var isVisible = await jumpButton.IsVisibleAsync();

        isVisible.Should().BeFalse("Jump to bottom button should not be visible when at bottom");

        Console.WriteLine("[E2E FEAT-059] Jump to bottom button is not visible (as expected)");
    }

    [Then(@"the jump to bottom button should be visible")]
    public async Task ThenJumpToBottomButtonShouldBeVisible()
    {
        var page = await GetOrCreatePageAsync();

        // Wait for UI to settle
        await Task.Delay(500);

        // Check if the jump to bottom button is visible
        var jumpButton = page.GetByTestId("jump-to-bottom-button");
        var isVisible = await jumpButton.IsVisibleAsync();

        isVisible.Should().BeTrue("Jump to bottom button should be visible when scrolled up");

        Console.WriteLine("[E2E FEAT-059] Jump to bottom button is visible");
    }

    [When(@"Alice scrolls up in the message list")]
    public async Task WhenAliceScrollsUpInMessageList()
    {
        var page = await GetOrCreatePageAsync();

        // Find the message list container (Virtuoso)
        var messageList = page.GetByTestId("message-list");

        // Scroll up by a significant amount
        await messageList.EvaluateAsync("el => el.scrollTo({ top: 0, behavior: 'instant' })");

        // Wait for scroll to complete
        await Task.Delay(500);

        Console.WriteLine("[E2E FEAT-059] Scrolled up in message list");
    }

    [When(@"Alice clicks the jump to bottom button")]
    public async Task WhenAliceClicksJumpToBottomButton()
    {
        var page = await GetOrCreatePageAsync();

        var jumpButton = page.GetByTestId("jump-to-bottom-button");
        await jumpButton.ClickAsync();

        // Wait for scroll animation
        await Task.Delay(500);

        Console.WriteLine("[E2E FEAT-059] Clicked jump to bottom button");
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
