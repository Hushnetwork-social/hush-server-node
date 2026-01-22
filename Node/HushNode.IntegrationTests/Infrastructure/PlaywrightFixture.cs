using Microsoft.Playwright;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Manages Playwright browser lifecycle for E2E tests.
/// Follows same pattern as HushTestFixture for containers.
/// </summary>
internal sealed class PlaywrightFixture : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>
    /// Gets the current browser instance.
    /// </summary>
    public IBrowser Browser => _browser
        ?? throw new InvalidOperationException("Browser not launched. Call InitializeAsync() first.");

    /// <summary>
    /// Gets whether the browser has been initialized.
    /// </summary>
    public bool IsInitialized => _browser != null;

    /// <summary>
    /// Initializes Playwright and launches browser.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_browser != null)
        {
            return; // Already initialized
        }

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    /// <summary>
    /// Creates a new isolated browser context for a test user.
    /// Each context has its own cookies, localStorage, etc.
    /// </summary>
    public async Task<IBrowserContext> CreateContextAsync()
    {
        return await Browser.NewContextAsync();
    }

    /// <summary>
    /// Creates a new page in a new context.
    /// Convenience method for single-user scenarios.
    /// </summary>
    public async Task<(IBrowserContext Context, IPage Page)> CreatePageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        return (context, page);
    }

    /// <summary>
    /// Closes browser and disposes Playwright.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }
}
