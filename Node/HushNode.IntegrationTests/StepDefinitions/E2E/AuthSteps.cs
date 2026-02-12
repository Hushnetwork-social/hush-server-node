using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for authentication and identity creation via browser.
/// </summary>
[Binding]
internal sealed class AuthSteps : BrowserStepsBase
{
    public AuthSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [Given(@"a browser is launched")]
    public async Task GivenABrowserIsLaunched()
    {
        var page = await GetOrCreatePageAsync();
        page.Should().NotBeNull("Browser page should be created");
    }

    [When(@"the user navigates to ""(.*)""")]
    public async Task WhenTheUserNavigatesTo(string path)
    {
        var page = await GetOrCreatePageAsync();
        await NavigateToAsync(page, path);

        // Wait for page to be stable
        await WaitForNetworkIdleAsync(page);
    }

    [When(@"the user creates a new identity with display name ""(.*)""")]
    public async Task WhenTheUserCreatesIdentity(string displayName)
    {
        var page = await GetOrCreatePageAsync();

        // Wait for client-side hydration (Next.js needs time to initialize)
        await Task.Delay(3000);

        // Find the input by placeholder (more reliable for E2E tests)
        var inputLocator = page.GetByPlaceholder("Satoshi Nakamoto", new PageGetByPlaceholderOptions { Exact = false });
        await inputLocator.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await inputLocator.FillAsync(displayName);

        // Click "Generate Recovery Words" button
        var generateButton = page.GetByText("Generate Recovery Words");
        await generateButton.ClickAsync();

        // Wait for mnemonic words to be generated (checkbox becomes visible)
        var checkbox = page.Locator("input[type='checkbox']");
        await checkbox.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // Check the checkbox to confirm mnemonic has been saved
        await checkbox.CheckAsync();

        // Click "Create Account" button (use Last to get the submit button, not the tab)
        // There are 2 buttons with "Create Account" - the tab and the submit button
        var createButton = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create Account" }).Last;
        await createButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // IMPORTANT: Start listening for identity transaction BEFORE clicking the button.
        // This prevents race condition where the event fires before we start listening.
        Console.WriteLine("[E2E Auth] Creating TransactionWaiter BEFORE clicking Create Account...");
        var waiter = StartListeningForTransactions(minTransactions: 1);
        ScenarioContext["PendingIdentityTransactionWaiter"] = waiter;

        await createButton.ClickAsync();
        Console.WriteLine("[E2E Auth] Clicked Create Account, waiter is listening for identity transaction");

        // NOTE: Do NOT wait for redirect here - the test must produce a block first
        // The web client submits the transaction and waits for confirmation,
        // but in test mode, blocks are only produced when the test triggers them.
        // The "identity transaction is processed" step will await the waiter and produce block.

        // Store display name for later steps
        ScenarioContext["UserDisplayName"] = displayName;
    }

    /// <summary>
    /// Compound step that creates a full identity via browser.
    /// This wraps the explicit steps for convenience in tests that don't need to see the details.
    ///
    /// Equivalent to:
    ///   When the user navigates to "/auth"
    ///   And the user creates a new identity with display name "{displayName}"
    ///   And the identity transaction is processed
    ///   And the personal feed transaction is processed
    ///   Then the user should be redirected to "/dashboard"
    ///   And the feed list should show the personal feed for "{displayName}"
    /// </summary>
    [Given(@"the user has created identity ""(.*)"" via browser")]
    public async Task GivenTheUserHasCreatedIdentity(string displayName)
    {
        Console.WriteLine($"[E2E Auth] === COMPOUND STEP: Creating identity '{displayName}' via browser ===");

        // Step 1: Navigate to auth page
        await WhenTheUserNavigatesTo("/auth");

        // Step 2: Create identity (this creates the waiter BEFORE clicking)
        await WhenTheUserCreatesIdentity(displayName);

        // Step 3: Wait for identity transaction to be mined
        await WhenTheIdentityTransactionIsProcessed();

        // Step 4: Wait for personal feed transaction to be mined
        await WhenThePersonalFeedTransactionIsProcessed();

        // Step 5: Wait for redirect to dashboard
        // Use polling-based assertion instead of WaitForURLAsync because Next.js router.push()
        // is an SPA navigation (history.pushState) that doesn't fire a traditional page "load" event.
        // WaitForURLAsync with WaitUntil=Load is unreliable for SPA navigations.
        var page = await GetOrCreatePageAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/dashboard"), new PageAssertionsToHaveURLOptions { Timeout = 15000 });
        Console.WriteLine("[E2E Auth] Redirected to dashboard");

        // Step 6: Wait for feeds to be synced, rendered, and encryption keys decrypted
        Console.WriteLine("[E2E Auth] Waiting for feeds to be synced, rendered, and encryption keys decrypted...");
        await WaitForReadyFeedAsync(page, timeoutMs: 30000);

        Console.WriteLine($"[E2E Auth] === COMPOUND STEP COMPLETE: Identity '{displayName}' created ===");
    }

    /// <summary>
    /// Waits for at least one feed to be ready for messaging (has its encryption key decrypted).
    /// A feed is ready when it has data-feed-ready="true" attribute.
    /// </summary>
    private async Task WaitForReadyFeedAsync(IPage page, int timeoutMs = 30000)
    {
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        while (DateTime.UtcNow - startTime < timeout)
        {
            // Look for any feed item with data-feed-ready="true"
            // Using attribute selector to find elements with data-feed-ready attribute set to "true"
            var readyFeeds = page.Locator("[data-testid^='feed-item'][data-feed-ready='true']");
            var readyCount = await readyFeeds.CountAsync();

            if (readyCount > 0)
            {
                Console.WriteLine($"[E2E] Found {readyCount} ready feed(s)");
                return;
            }

            // Check for any feeds at all (for debugging)
            var allFeeds = page.Locator("[data-testid^='feed-item']");
            var allCount = await allFeeds.CountAsync();

            if (allCount > 0)
            {
                // Feeds exist but aren't ready yet - key decryption in progress
                Console.WriteLine($"[E2E] Found {allCount} feed(s), waiting for encryption keys...");
            }
            else
            {
                // Check for loading indicator
                var loader = page.Locator(".animate-spin");
                if (await loader.CountAsync() > 0)
                {
                    Console.WriteLine("[E2E] Loading indicator visible, waiting...");
                }
            }

            // Wait a bit before next check
            await Task.Delay(500);
        }

        // Timeout reached - take screenshot and throw
        var screenshotPath = "e2e-feed-ready-timeout.png";
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });
        Console.WriteLine($"[E2E] Timeout screenshot saved to: {screenshotPath}");

        throw new TimeoutException($"No ready feed (data-feed-ready='true') found within {timeoutMs}ms");
    }

    /// <summary>
    /// Waits for the feed list to have at least the specified number of feeds.
    /// Uses the data-feed-count attribute on the feed-list element.
    /// </summary>
    private async Task WaitForFeedCountAsync(IPage page, int minCount, int timeoutMs = 30000)
    {
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        while (DateTime.UtcNow - startTime < timeout)
        {
            // Use .First to handle responsive layouts that have multiple feed-list elements
            // (sidebar and main content for mobile/desktop views)
            var feedList = page.GetByTestId("feed-list").First;

            // Check if feed-list exists and has the data-feed-count attribute
            if (await feedList.CountAsync() > 0)
            {
                var feedCountAttr = await feedList.GetAttributeAsync("data-feed-count");
                if (feedCountAttr != null && int.TryParse(feedCountAttr, out var feedCount) && feedCount >= minCount)
                {
                    Console.WriteLine($"[E2E] Feed count: {feedCount} (>= {minCount})");
                    return;
                }
                Console.WriteLine($"[E2E] Feed count: {feedCountAttr ?? "N/A"}, waiting for >= {minCount}...");
            }
            else
            {
                // Feed list not rendered yet - might be loading or empty state
                // Check for loading indicator
                var loader = page.Locator(".animate-spin");
                if (await loader.CountAsync() > 0)
                {
                    Console.WriteLine("[E2E] Loading indicator visible, waiting...");
                }
            }

            // Wait a bit before next check (sync runs every 3s, so check more frequently)
            await Task.Delay(500);
        }

        // Timeout reached - take screenshot and throw
        var screenshotPath = "e2e-feed-sync-timeout.png";
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });
        Console.WriteLine($"[E2E] Timeout screenshot saved to: {screenshotPath}");

        throw new TimeoutException($"Feed count did not reach {minCount} within {timeoutMs}ms");
    }

    [Then(@"the user should be redirected to ""(.*)""")]
    public async Task ThenTheUserShouldBeRedirectedTo(string expectedPath)
    {
        var page = await GetOrCreatePageAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex(Regex.Escape(expectedPath)), new PageAssertionsToHaveURLOptions { Timeout = 10000 });
    }

    /// <summary>
    /// Waits for the identity transaction to be received in mempool, then produces a block.
    /// This step must follow "the user creates a new identity" which creates the waiter.
    ///
    /// IMPORTANT: This step also creates a waiter for the personal feed transaction BEFORE
    /// producing the identity block. This is critical because the web client auto-submits
    /// the personal feed transaction as soon as it detects the identity is confirmed.
    /// If we created the waiter AFTER producing the block, we might miss the event.
    /// </summary>
    [When(@"the identity transaction is processed")]
    public async Task WhenTheIdentityTransactionIsProcessed()
    {
        Console.WriteLine("[E2E Auth] Waiting for identity transaction to be processed...");

        // Retrieve the waiter that was created in WhenTheUserCreatesIdentity
        if (!ScenarioContext.TryGetValue("PendingIdentityTransactionWaiter", out var waiterObj)
            || waiterObj is not HushServerNode.HushServerNodeCore.TransactionWaiter identityWaiter)
        {
            throw new InvalidOperationException(
                "No PendingIdentityTransactionWaiter found. " +
                "This step must follow 'the user creates a new identity with display name'.");
        }

        ScenarioContext.Remove("PendingIdentityTransactionWaiter");

        try
        {
            // Wait for the identity transaction to arrive at the mempool
            await identityWaiter.WaitAsync();
            Console.WriteLine("[E2E Auth] Identity transaction received in mempool");

            // CRITICAL: Create the personal feed waiter BEFORE producing the identity block.
            // The web client will auto-submit the personal feed transaction as soon as it
            // detects the identity is confirmed. If we wait until after the block is produced,
            // we might miss the TransactionReceivedEvent (classic race condition).
            Console.WriteLine("[E2E Auth] Creating personal feed waiter BEFORE producing identity block...");
            var personalFeedWaiter = StartListeningForTransactions(minTransactions: 1);
            ScenarioContext["PendingPersonalFeedTransactionWaiter"] = personalFeedWaiter;

            // Now produce the block - this confirms the identity and triggers the client
            // to sync and submit the personal feed transaction
            var blockControl = GetBlockControl();
            await blockControl.ProduceBlockAsync();
            Console.WriteLine("[E2E Auth] Identity block produced, personal feed waiter is listening");
        }
        finally
        {
            identityWaiter.Dispose();
        }
    }

    /// <summary>
    /// Waits for the personal feed transaction to be received in mempool, then produces a block.
    /// The waiter was created in WhenTheIdentityTransactionIsProcessed BEFORE the identity
    /// block was produced, ensuring we don't miss the event due to race conditions.
    /// </summary>
    [When(@"the personal feed transaction is processed")]
    public async Task WhenThePersonalFeedTransactionIsProcessed()
    {
        Console.WriteLine("[E2E Auth] Waiting for personal feed transaction...");
        Console.WriteLine("[E2E Auth] (Waiter was created before identity block was produced)");

        // Retrieve the waiter that was created BEFORE the identity block was produced
        if (!ScenarioContext.TryGetValue("PendingPersonalFeedTransactionWaiter", out var waiterObj)
            || waiterObj is not HushServerNode.HushServerNodeCore.TransactionWaiter feedWaiter)
        {
            throw new InvalidOperationException(
                "No PendingPersonalFeedTransactionWaiter found. " +
                "This step must follow 'the identity transaction is processed'.");
        }

        ScenarioContext.Remove("PendingPersonalFeedTransactionWaiter");

        try
        {
            await AwaitTransactionsAndProduceBlockAsync(feedWaiter);
            Console.WriteLine("[E2E Auth] Personal feed transaction processed and block produced");
        }
        finally
        {
            feedWaiter.Dispose();
        }
    }

    /// <summary>
    /// Verifies the feed list shows the personal feed for the specified user.
    /// Waits for the feed to be synced, rendered, and encryption keys decrypted.
    /// </summary>
    [Then(@"the feed list should show the personal feed for ""(.*)""")]
    public async Task ThenTheFeedListShouldShowPersonalFeed(string displayName)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E Auth] Waiting for personal feed to appear for '{displayName}'...");

        // Wait for at least one feed to be ready (synced, rendered, encryption key decrypted)
        await WaitForReadyFeedAsync(page, timeoutMs: 30000);

        // Verify it's the personal feed with the correct display name
        // Personal feeds have data-testid="feed-item:personal"
        var personalFeed = page.GetByTestId("feed-item:personal");
        await personalFeed.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        // Verify the feed shows the display name
        var feedText = await personalFeed.First.TextContentAsync();
        feedText.Should().Contain(displayName, $"Personal feed should show display name '{displayName}'");

        Console.WriteLine($"[E2E Auth] Personal feed for '{displayName}' is visible and ready");
    }
}
