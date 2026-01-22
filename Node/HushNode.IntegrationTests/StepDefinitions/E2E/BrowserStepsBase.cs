using Microsoft.Playwright;
using TechTalk.SpecFlow;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Base class for browser step definitions.
/// Provides common helper methods and property access for E2E tests using Playwright.
/// </summary>
internal abstract class BrowserStepsBase
{
    protected readonly ScenarioContext ScenarioContext;

    private const string PageKey = "E2E_MainPage";
    private const string ContextKey = "E2E_MainContext";

    protected BrowserStepsBase(ScenarioContext scenarioContext)
    {
        ScenarioContext = scenarioContext;
    }

    /// <summary>
    /// Gets the PlaywrightFixture from ScenarioContext.
    /// </summary>
    protected PlaywrightFixture GetPlaywright()
    {
        if (ScenarioContext.TryGetValue(ScenarioHooks.PlaywrightKey, out var obj)
            && obj is PlaywrightFixture fixture)
        {
            return fixture;
        }
        throw new InvalidOperationException("PlaywrightFixture not found. Is this an @E2E scenario?");
    }

    /// <summary>
    /// Gets the WebClientFixture from ScenarioContext.
    /// </summary>
    protected WebClientFixture GetWebClient()
    {
        if (ScenarioContext.TryGetValue(ScenarioHooks.WebClientKey, out var obj)
            && obj is WebClientFixture fixture)
        {
            return fixture;
        }
        throw new InvalidOperationException("WebClientFixture not found. Is this an @E2E scenario?");
    }

    /// <summary>
    /// Gets or creates the main page for the current scenario.
    /// </summary>
    protected async Task<IPage> GetOrCreatePageAsync()
    {
        if (ScenarioContext.TryGetValue(PageKey, out var pageObj)
            && pageObj is IPage existingPage)
        {
            return existingPage;
        }

        var playwright = GetPlaywright();
        var (context, page) = await playwright.CreatePageAsync();

        ScenarioContext[ContextKey] = context;
        ScenarioContext[PageKey] = page;

        return page;
    }

    /// <summary>
    /// Gets the base URL for the web client.
    /// </summary>
    protected string GetBaseUrl()
    {
        return GetWebClient().BaseUrl;
    }

    /// <summary>
    /// Waits for an element with the specified data-testid to be visible.
    /// </summary>
    /// <param name="page">The page to search.</param>
    /// <param name="testId">The data-testid value.</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default: 10000).</param>
    /// <returns>The locator for the element.</returns>
    protected async Task<ILocator> WaitForTestIdAsync(IPage page, string testId, int timeoutMs = 10000)
    {
        var locator = page.GetByTestId(testId);
        await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
        return locator;
    }

    /// <summary>
    /// Clicks an element with the specified data-testid.
    /// </summary>
    /// <param name="page">The page containing the element.</param>
    /// <param name="testId">The data-testid value.</param>
    protected async Task ClickTestIdAsync(IPage page, string testId)
    {
        var locator = page.GetByTestId(testId);
        await locator.ClickAsync();
    }

    /// <summary>
    /// Fills an input element with the specified data-testid.
    /// </summary>
    /// <param name="page">The page containing the element.</param>
    /// <param name="testId">The data-testid value.</param>
    /// <param name="value">The value to fill.</param>
    protected async Task FillTestIdAsync(IPage page, string testId, string value)
    {
        var locator = page.GetByTestId(testId);
        await locator.FillAsync(value);
    }

    /// <summary>
    /// Gets the text content of an element with the specified data-testid.
    /// </summary>
    /// <param name="page">The page containing the element.</param>
    /// <param name="testId">The data-testid value.</param>
    /// <returns>The text content of the element.</returns>
    protected async Task<string?> GetTestIdTextAsync(IPage page, string testId)
    {
        var locator = page.GetByTestId(testId);
        return await locator.TextContentAsync();
    }

    /// <summary>
    /// Checks if an element with the specified data-testid is visible.
    /// </summary>
    /// <param name="page">The page to search.</param>
    /// <param name="testId">The data-testid value.</param>
    /// <returns>True if the element is visible, false otherwise.</returns>
    protected async Task<bool> IsTestIdVisibleAsync(IPage page, string testId)
    {
        var locator = page.GetByTestId(testId);
        return await locator.IsVisibleAsync();
    }

    /// <summary>
    /// Navigates to a path relative to the web client base URL.
    /// </summary>
    /// <param name="page">The page to navigate.</param>
    /// <param name="path">The path to navigate to (e.g., "/auth" or "/dashboard").</param>
    protected async Task NavigateToAsync(IPage page, string path)
    {
        var url = $"{GetBaseUrl()}{path}";
        await page.GotoAsync(url);
    }

    /// <summary>
    /// Waits for the page to be in a network idle state.
    /// Useful after navigation or actions that trigger network requests.
    /// </summary>
    /// <param name="page">The page to wait on.</param>
    protected async Task WaitForNetworkIdleAsync(IPage page)
    {
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Gets the HushServerNodeCore from ScenarioContext.
    /// </summary>
    protected HushServerNodeCore GetNode()
    {
        if (ScenarioContext.TryGetValue(ScenarioHooks.NodeKey, out var obj)
            && obj is HushServerNodeCore node)
        {
            return node;
        }
        throw new InvalidOperationException("HushServerNodeCore not found in ScenarioContext.");
    }

    /// <summary>
    /// Gets the BlockProductionControl from ScenarioContext.
    /// </summary>
    protected BlockProductionControl GetBlockControl()
    {
        if (ScenarioContext.TryGetValue(ScenarioHooks.BlockControlKey, out var obj)
            && obj is BlockProductionControl control)
        {
            return control;
        }
        throw new InvalidOperationException("BlockProductionControl not found in ScenarioContext.");
    }

    /// <summary>
    /// Waits for transaction(s) to reach the mempool, produces a block, and waits for indexing.
    /// Use this after UI actions that submit transactions to ensure data is persisted.
    /// </summary>
    /// <param name="minTransactions">Minimum number of transactions to wait for (default: 1).</param>
    protected async Task WaitForTransactionAndProduceBlockAsync(int minTransactions = 1)
    {
        var node = GetNode();
        var blockControl = GetBlockControl();

        // Wait for transaction(s) to reach the mempool
        await node.WaitForPendingTransactionsAsync(minTransactions, timeout: TimeSpan.FromSeconds(10));

        // Produce block and wait for indexing to complete (BlockIndexCompletedEvent)
        await blockControl.ProduceBlockAsync();
    }
}
