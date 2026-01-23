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
    /// Gets or sets whether trace capture is enabled for new contexts.
    /// Traces include screenshots, snapshots, and source files for debugging.
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Gets or sets the directory for trace files.
    /// </summary>
    public string? TraceDirectory { get; set; }

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
    /// Creates a new isolated browser context with tracing enabled.
    /// Traces capture screenshots, DOM snapshots, and source files for debugging.
    /// </summary>
    /// <param name="scenarioName">The name of the scenario for trace file naming.</param>
    public async Task<IBrowserContext> CreateContextWithTracingAsync(string scenarioName)
    {
        var context = await CreateContextAsync();

        if (EnableTracing)
        {
            await context.Tracing.StartAsync(new TracingStartOptions
            {
                Screenshots = true,
                Snapshots = true,
                Sources = true
            });
            Console.WriteLine($"[E2E Tracing] Started for scenario: {scenarioName}");
        }

        return context;
    }

    /// <summary>
    /// Stops tracing and saves the trace file on failure.
    /// On success, the trace is discarded to save storage.
    /// </summary>
    /// <param name="context">The browser context with tracing enabled.</param>
    /// <param name="failed">Whether the scenario failed.</param>
    /// <param name="scenarioName">The name of the scenario for trace file naming.</param>
    public async Task StopTracingAsync(IBrowserContext context, bool failed, string scenarioName)
    {
        if (!EnableTracing)
        {
            return;
        }

        if (failed && !string.IsNullOrEmpty(TraceDirectory))
        {
            var safeName = scenarioName.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
            var tracePath = Path.Combine(TraceDirectory, $"{safeName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
            Directory.CreateDirectory(TraceDirectory);

            await context.Tracing.StopAsync(new TracingStopOptions
            {
                Path = tracePath
            });
            Console.WriteLine($"[E2E Tracing] Saved trace for failed scenario: {tracePath}");
        }
        else
        {
            // Discard trace on success to save storage
            await context.Tracing.StopAsync();
            Console.WriteLine($"[E2E Tracing] Discarded trace for passing scenario: {scenarioName}");
        }
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
