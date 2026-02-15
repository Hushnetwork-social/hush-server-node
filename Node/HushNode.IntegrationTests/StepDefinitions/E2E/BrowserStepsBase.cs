using Microsoft.Playwright;
using StackExchange.Redis;
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
    /// Waits for a visible element with the specified data-testid.
    /// Handles responsive layouts where multiple elements exist but only one is visible.
    /// </summary>
    /// <param name="page">The page to search.</param>
    /// <param name="testId">The data-testid value.</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default: 10000).</param>
    /// <returns>The locator for the visible element.</returns>
    protected async Task<ILocator> WaitForTestIdAsync(IPage page, string testId, int timeoutMs = 10000)
    {
        // Responsive layouts may have duplicate elements (mobile/desktop views).
        // Use Locator.Filter with IsVisible to find the visible one.
        var allElements = page.GetByTestId(testId);

        // Wait for at least one element to be visible
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var count = await allElements.CountAsync();
            for (int i = 0; i < count; i++)
            {
                var element = allElements.Nth(i);
                if (await element.IsVisibleAsync())
                {
                    return element;
                }
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"No visible element found with data-testid='{testId}' within {timeoutMs}ms");
    }

    /// <summary>
    /// Clicks a visible element with the specified data-testid.
    /// Handles responsive layouts where multiple elements exist but only one is visible.
    /// </summary>
    /// <param name="page">The page containing the element.</param>
    /// <param name="testId">The data-testid value.</param>
    protected async Task ClickTestIdAsync(IPage page, string testId)
    {
        var locator = await GetVisibleElementByTestIdAsync(page, testId);
        await locator.ClickAsync();
    }

    /// <summary>
    /// Fills a visible input element with the specified data-testid.
    /// Handles responsive layouts where multiple elements exist but only one is visible.
    /// </summary>
    /// <param name="page">The page containing the element.</param>
    /// <param name="testId">The data-testid value.</param>
    /// <param name="value">The value to fill.</param>
    protected async Task FillTestIdAsync(IPage page, string testId, string value)
    {
        var locator = await GetVisibleElementByTestIdAsync(page, testId);
        await locator.FillAsync(value);
    }

    /// <summary>
    /// Finds a visible element by data-testid.
    /// Handles responsive layouts where multiple elements exist but only one is visible.
    /// </summary>
    private async Task<ILocator> GetVisibleElementByTestIdAsync(IPage page, string testId, int timeoutMs = 5000)
    {
        var allElements = page.GetByTestId(testId);
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var count = await allElements.CountAsync();
            for (int i = 0; i < count; i++)
            {
                var element = allElements.Nth(i);
                if (await element.IsVisibleAsync())
                {
                    return element;
                }
            }
            await Task.Delay(50);
        }

        throw new TimeoutException($"No visible element found with data-testid='{testId}' within {timeoutMs}ms");
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
    /// NOTE: This has a race condition - use StartListeningForTransactions + AwaitTransactionsAndProduceBlockAsync instead.
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

    /// <summary>
    /// Starts listening for transactions BEFORE the browser action is triggered.
    /// Use pattern:
    ///   using var waiter = StartListeningForTransactions(1);
    ///   await page.ClickAsync(...); // browser action that submits transaction
    ///   await AwaitTransactionsAndProduceBlockAsync(waiter);
    /// </summary>
    /// <param name="minTransactions">Minimum number of transactions to wait for (default: 1).</param>
    /// <returns>A waiter that should be passed to AwaitTransactionsAndProduceBlockAsync after the action.</returns>
    protected HushServerNodeCore.TransactionWaiter StartListeningForTransactions(int minTransactions = 1)
    {
        var node = GetNode();
        return node.StartListeningForTransactions(minTransactions, timeout: TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Awaits the transactions and produces a block.
    /// Call this AFTER triggering the browser action with a waiter created by StartListeningForTransactions.
    /// </summary>
    /// <param name="waiter">The waiter from StartListeningForTransactions.</param>
    protected async Task AwaitTransactionsAndProduceBlockAsync(HushServerNodeCore.TransactionWaiter waiter)
    {
        var blockControl = GetBlockControl();

        // Wait for transaction(s) to reach the mempool
        await waiter.WaitAsync();
        waiter.Dispose();

        // Produce block and wait for indexing to complete (BlockIndexCompletedEvent)
        await blockControl.ProduceBlockAsync();
    }

    /// <summary>
    /// Triggers the browser sync via window.__e2e_triggerSync().
    /// This replaces the automatic 3-second sync interval with manual control.
    /// The sync function runs all registered syncables (feeds, reactions, etc.).
    /// </summary>
    /// <param name="page">The page to trigger sync on.</param>
    protected async Task TriggerSyncAsync(IPage page)
    {
        Console.WriteLine("[E2E] Triggering manual sync...");
        try
        {
            await page.EvaluateAsync("() => window.__e2e_triggerSync()");
            Console.WriteLine("[E2E] Manual sync completed");
        }
        catch (PlaywrightException ex)
        {
            Console.WriteLine($"[E2E] Warning: Sync trigger failed - {ex.Message}");
            // This may happen if the page hasn't loaded the SyncProvider yet
            // or if __e2e_triggerSync isn't available
        }
    }

    /// <summary>
    /// Gets the HushTestFixture from ScenarioContext.
    /// </summary>
    protected HushTestFixture GetFixture()
    {
        if (ScenarioContext.TryGetValue(ScenarioHooks.FixtureKey, out var obj)
            && obj is HushTestFixture fixture)
        {
            return fixture;
        }
        throw new InvalidOperationException("HushTestFixture not found in ScenarioContext.");
    }

    /// <summary>
    /// Gets the Redis database for cache verification.
    /// </summary>
    protected IDatabase GetRedisDatabase()
    {
        var fixture = GetFixture();
        return fixture.RedisConnection.GetDatabase();
    }

    // Screenshot support is centralized in ScenarioHooks.AfterStep —
    // automatic screenshots are taken after every E2E step without
    // requiring individual step definitions to call anything.

    #region localStorage Timeline

    /// <summary>
    /// Captures localStorage state and records changes in a timeline.
    /// Call this after key actions to track state evolution.
    /// </summary>
    /// <param name="page">The page to capture localStorage from.</param>
    /// <param name="stepName">Description of what action just occurred.</param>
    /// <param name="userName">The user whose localStorage is being captured.</param>
    protected async Task CaptureLocalStorageAsync(IPage page, string stepName, string userName)
    {
        try
        {
            // Get current localStorage as JSON
            var currentState = await page.EvaluateAsync<Dictionary<string, string>>(
                "() => { const items = {}; for (let i = 0; i < localStorage.length; i++) { const key = localStorage.key(i); items[key] = localStorage.getItem(key); } return items; }");

            // Get the timeline from context
            var timelineKey = $"LocalStorageTimeline_{userName}";
            if (!ScenarioContext.TryGetValue(timelineKey, out var timelineObj))
            {
                timelineObj = new List<LocalStorageSnapshot>();
                ScenarioContext[timelineKey] = timelineObj;
            }
            var timeline = (List<LocalStorageSnapshot>)timelineObj;

            // Get previous state for diff
            var previousState = timeline.Count > 0 ? timeline[^1].State : new Dictionary<string, string>();

            // Calculate diff
            var changes = new List<string>();
            foreach (var (key, value) in currentState)
            {
                if (!previousState.TryGetValue(key, out var prevValue))
                {
                    changes.Add($"  + [{key}] = {TruncateValue(value)}");
                }
                else if (prevValue != value)
                {
                    changes.Add($"  ~ [{key}] changed: {TruncateValue(prevValue)} → {TruncateValue(value)}");
                }
            }
            foreach (var key in previousState.Keys.Where(k => !currentState.ContainsKey(k)))
            {
                changes.Add($"  - [{key}] removed");
            }

            // Create snapshot
            var snapshot = new LocalStorageSnapshot
            {
                Timestamp = DateTime.Now,
                StepName = stepName,
                UserName = userName,
                State = currentState,
                Changes = changes
            };
            timeline.Add(snapshot);

            // Log to console
            if (changes.Count > 0)
            {
                Console.WriteLine($"[E2E localStorage] {userName} @ {stepName}: {changes.Count} change(s)");
                foreach (var change in changes.Take(5))
                {
                    Console.WriteLine($"[E2E localStorage] {change}");
                }
                if (changes.Count > 5)
                {
                    Console.WriteLine($"[E2E localStorage]   ... and {changes.Count - 5} more");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[E2E localStorage] Failed to capture: {ex.Message}");
        }
    }

    private static string TruncateValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        if (value.Length <= 80) return value;
        return value.Substring(0, 77) + "...";
    }

    #endregion

    #region Network/gRPC Logging

    /// <summary>
    /// Sets up network request/response logging for a page.
    /// Call this when creating a new browser context.
    /// </summary>
    /// <param name="page">The page to monitor.</param>
    /// <param name="userName">The user name for logging context.</param>
    protected void SetupNetworkLogging(IPage page, string userName)
    {
        var networkLogKey = $"NetworkLog_{userName}";
        if (!ScenarioContext.ContainsKey(networkLogKey))
        {
            ScenarioContext[networkLogKey] = new List<NetworkLogEntry>();
        }
        var networkLog = (List<NetworkLogEntry>)ScenarioContext[networkLogKey];

        // Log requests
        page.Request += (_, request) =>
        {
            // Only log gRPC and API requests
            var url = request.Url;
            if (url.Contains("/rpcHush.") || url.Contains("/api/"))
            {
                var entry = new NetworkLogEntry
                {
                    Timestamp = DateTime.Now,
                    Direction = "REQUEST",
                    Method = request.Method,
                    Url = url,
                    PostData = request.PostData
                };
                networkLog.Add(entry);

                var shortUrl = url.Length > 80 ? "..." + url.Substring(url.Length - 77) : url;
                Console.WriteLine($"[E2E Network] {userName} → {request.Method} {shortUrl}");
            }
        };

        // Log responses
        page.Response += (_, response) =>
        {
            var url = response.Url;
            if (url.Contains("/rpcHush.") || url.Contains("/api/"))
            {
                var entry = new NetworkLogEntry
                {
                    Timestamp = DateTime.Now,
                    Direction = "RESPONSE",
                    Method = response.Request.Method,
                    Url = url,
                    Status = response.Status,
                    StatusText = response.StatusText
                };
                networkLog.Add(entry);

                var shortUrl = url.Length > 80 ? "..." + url.Substring(url.Length - 77) : url;
                var statusIcon = response.Status >= 200 && response.Status < 300 ? "✓" : "✗";
                Console.WriteLine($"[E2E Network] {userName} ← {statusIcon} {response.Status} {shortUrl}");
            }
        };

        // Log request failures
        page.RequestFailed += (_, request) =>
        {
            var url = request.Url;
            if (url.Contains("/rpcHush.") || url.Contains("/api/"))
            {
                var entry = new NetworkLogEntry
                {
                    Timestamp = DateTime.Now,
                    Direction = "FAILED",
                    Method = request.Method,
                    Url = url,
                    Error = request.Failure
                };
                networkLog.Add(entry);

                Console.WriteLine($"[E2E Network] {userName} ✗ FAILED {request.Method} {url}: {request.Failure}");
            }
        };
    }

    #endregion

    /// <summary>
    /// Dumps the Redis cache state for a specific feed.
    /// Useful for debugging cache synchronization issues.
    /// </summary>
    /// <param name="feedId">The feed ID to check.</param>
    /// <param name="feedName">A human-readable name for logging.</param>
    protected async Task DumpRedisCacheStateAsync(string feedId, string feedName)
    {
        var redisDb = GetRedisDatabase();
        Console.WriteLine($"[E2E Redis] === Cache state for '{feedName}' (feedId: {feedId.Substring(0, 8)}...) ===");

        // Check participants cache
        var participantsKey = $"HushTest:feed:{feedId}:participants";
        var participantsExists = await redisDb.KeyExistsAsync(participantsKey);
        if (participantsExists)
        {
            var members = await redisDb.SetMembersAsync(participantsKey);
            Console.WriteLine($"[E2E Redis] Participants ({members.Length}): {string.Join(", ", members.Select(m => m.ToString().Substring(0, 10) + "..."))}");
        }
        else
        {
            Console.WriteLine("[E2E Redis] Participants: NOT CACHED");
        }

        // Check key generations cache
        var keysKey = $"HushTest:feed:{feedId}:keys";
        var keysExists = await redisDb.KeyExistsAsync(keysKey);
        if (keysExists)
        {
            var keysValue = await redisDb.StringGetAsync(keysKey);
            // Parse to count key generations
            var keyGenCount = keysValue.ToString().Split("keyGeneration").Length - 1;
            Console.WriteLine($"[E2E Redis] KeyGenerations: CACHED ({keyGenCount} generations)");
            // Show first 200 chars for debugging
            var preview = keysValue.ToString().Length > 200
                ? keysValue.ToString().Substring(0, 200) + "..."
                : keysValue.ToString();
            Console.WriteLine($"[E2E Redis] KeyGenerations preview: {preview}");
        }
        else
        {
            Console.WriteLine("[E2E Redis] KeyGenerations: NOT CACHED");
        }

        // Check group members cache
        var membersKey = $"HushTest:feed:{feedId}:members";
        var membersExists = await redisDb.KeyExistsAsync(membersKey);
        if (membersExists)
        {
            var membersValue = await redisDb.StringGetAsync(membersKey);
            Console.WriteLine($"[E2E Redis] GroupMembers: CACHED");
            var preview = membersValue.ToString().Length > 200
                ? membersValue.ToString().Substring(0, 200) + "..."
                : membersValue.ToString();
            Console.WriteLine($"[E2E Redis] GroupMembers preview: {preview}");
        }
        else
        {
            Console.WriteLine("[E2E Redis] GroupMembers: NOT CACHED");
        }

        Console.WriteLine("[E2E Redis] === End cache state ===");
    }
}

/// <summary>
/// Snapshot of localStorage state at a point in time.
/// </summary>
internal sealed class LocalStorageSnapshot
{
    public required DateTime Timestamp { get; init; }
    public required string StepName { get; init; }
    public required string UserName { get; init; }
    public required Dictionary<string, string> State { get; init; }
    public required List<string> Changes { get; init; }
}

/// <summary>
/// Entry in the network request/response log.
/// </summary>
internal sealed class NetworkLogEntry
{
    public required DateTime Timestamp { get; init; }
    public required string Direction { get; init; } // REQUEST, RESPONSE, FAILED
    public required string Method { get; init; }
    public required string Url { get; init; }
    public string? PostData { get; init; }
    public int? Status { get; init; }
    public string? StatusText { get; init; }
    public string? Error { get; init; }
}
