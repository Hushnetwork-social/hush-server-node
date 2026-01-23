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
    /// Gets or sets whether video recording is enabled for new contexts.
    /// </summary>
    public bool EnableVideoRecording { get; set; } = true;

    /// <summary>
    /// Gets or sets the directory for video recordings.
    /// </summary>
    public string? VideoDirectory { get; set; }

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
    /// Uses a desktop viewport (1280x720) to ensure consistent layout for E2E tests.
    /// Optionally records video if EnableVideoRecording is true and VideoDirectory is set.
    /// </summary>
    public async Task<IBrowserContext> CreateContextAsync()
    {
        var options = new BrowserNewContextOptions
        {
            // Use a desktop viewport to avoid mobile layout issues
            // The web client switches to mobile layout at width < 768px
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        };

        // Enable video recording if configured
        if (EnableVideoRecording && !string.IsNullOrEmpty(VideoDirectory))
        {
            options.RecordVideoDir = VideoDirectory;
            options.RecordVideoSize = new RecordVideoSize { Width = 1280, Height = 720 };
            Console.WriteLine($"[E2E Video] Recording enabled, saving to: {VideoDirectory}");
        }

        return await Browser.NewContextAsync(options);
    }

    /// <summary>
    /// Creates a new page in a new context.
    /// Convenience method for single-user scenarios.
    /// </summary>
    /// <param name="captureConsoleLogs">If true, browser console logs will be printed to stdout.</param>
    public async Task<(IBrowserContext Context, IPage Page)> CreatePageAsync(bool captureConsoleLogs = true)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        if (captureConsoleLogs)
        {
            // Capture browser console logs for debugging
            // Filter to only show [E2E logs for clarity
            page.Console += (_, msg) =>
            {
                var text = msg.Text;
                if (text.Contains("[E2E") || text.Contains("initializeReactions") || text.Contains("ReactionsS"))
                {
                    Console.WriteLine($"[Browser Console] {msg.Type}: {text}");
                }
            };

            page.PageError += (_, error) =>
            {
                Console.WriteLine($"[Browser Error] {error}");
            };
        }

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
