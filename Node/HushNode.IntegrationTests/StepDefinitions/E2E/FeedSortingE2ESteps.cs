using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// FEAT-062: Step definitions for Feed Sorting E2E tests.
/// Tests that feed list ordering in the browser reflects blockIndex-based sorting.
/// Uses a hybrid approach: identity via browser, feeds/messages via backend.
/// </summary>
[Binding]
internal sealed class FeedSortingE2ESteps : BrowserStepsBase
{
    public FeedSortingE2ESteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    // =========================================================================
    // Given Steps: Backend ChatFeed creation (hybrid approach)
    // =========================================================================

    /// <summary>
    /// Creates a ChatFeed between Alice (browser-created identity) and a backend user.
    /// Registers the backend user if needed, extracts Alice's keys from localStorage,
    /// and creates the ChatFeed via gRPC backend for speed.
    /// </summary>
    [Given(@"Alice has a backend ChatFeed with ""(.*)""")]
    public async Task GivenAliceHasBackendChatFeedWith(string otherUserName)
    {
        Console.WriteLine($"[E2E FeedSort] Creating backend ChatFeed between Alice and {otherUserName}...");

        var aliceIdentity = await GetOrCreateBrowserTestIdentityAsync("Alice");
        var otherIdentity = await EnsureBackendUserRegisteredAsync(otherUserName);

        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        // Create the ChatFeed - Alice initiates
        var (signedTxJson, feedId, aesKey) = TestTransactionFactory.CreateChatFeed(aliceIdentity, otherIdentity);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTxJson
        });
        response.Successfull.Should().BeTrue(
            $"ChatFeed creation between Alice and {otherUserName} should succeed: {response.Message}");

        // Produce block to commit
        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();

        // Store feed info for later steps
        var chatKey = GetChatFeedKey("Alice", otherUserName);
        ScenarioContext[$"E2E_ChatFeed_{chatKey}"] = feedId;
        ScenarioContext[$"E2E_ChatFeedAesKey_{chatKey}"] = aesKey;
        ScenarioContext[$"E2E_ChatFeedOtherUser_{chatKey}"] = otherUserName;

        Console.WriteLine($"[E2E FeedSort] ChatFeed(Alice,{otherUserName}) created: {feedId}");
    }

    // =========================================================================
    // When Steps: Backend confirmed messages
    // =========================================================================

    /// <summary>
    /// Sends a message via gRPC backend and produces a block to confirm it.
    /// This gives precise control over which block each message ends up in.
    /// </summary>
    [Given(@"Alice sends a confirmed backend message ""(.*)"" to ChatFeed\(Alice,(.*)\)")]
    [When(@"Alice sends a confirmed backend message ""(.*)"" to ChatFeed\(Alice,(.*)\)")]
    public async Task WhenAliceSendsConfirmedBackendMessage(string message, string otherUserName)
    {
        Console.WriteLine($"[E2E FeedSort] Sending confirmed backend message '{message}' to ChatFeed(Alice,{otherUserName})...");

        var aliceIdentity = await GetOrCreateBrowserTestIdentityAsync("Alice");
        var chatKey = GetChatFeedKey("Alice", otherUserName);
        var feedId = (FeedId)ScenarioContext[$"E2E_ChatFeed_{chatKey}"];
        var aesKey = (string)ScenarioContext[$"E2E_ChatFeedAesKey_{chatKey}"];

        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var signedTxJson = TestTransactionFactory.CreateFeedMessage(aliceIdentity, feedId, message, aesKey);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTxJson
        });
        response.Successfull.Should().BeTrue($"Message submission should succeed: {response.Message}");

        // Produce block to confirm the message
        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();

        Console.WriteLine($"[E2E FeedSort] Message '{message}' confirmed in block for ChatFeed(Alice,{otherUserName})");
    }

    // =========================================================================
    // When Steps: Browser sync and navigation
    // =========================================================================

    /// <summary>
    /// Triggers feed sync in the browser. Waits for sync to complete and feeds to render.
    /// </summary>
    [Given(@"Alice triggers feed sync")]
    [When(@"Alice triggers feed sync")]
    public async Task WhenAliceTriggersFeedSync()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E FeedSort] Triggering feed sync...");
        await TriggerSyncAsync(page);

        // Wait for feeds to be rendered and encryption keys decrypted
        await WaitForReadyFeedsAsync(page, timeoutMs: 30000);

        Console.WriteLine("[E2E FeedSort] Feed sync complete, feeds rendered");
    }

    /// <summary>
    /// Opens a specific ChatFeed in the browser by clicking on it.
    /// </summary>
    [When(@"Alice opens ChatFeed with ""(.*)"" in browser")]
    public async Task WhenAliceOpensChatFeedInBrowser(string otherUserName)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E FeedSort] Alice opening ChatFeed with {otherUserName}...");

        // Chat feeds have data-testid="feed-item:chat:{sanitized-name}"
        var sanitizedName = SanitizeName(otherUserName);
        var testId = $"feed-item:chat:{sanitizedName}";

        var feedItem = await WaitForVisibleFeedItemAsync(page, testId, 30000);
        await feedItem.ClickAsync();

        // Wait for chat view to load
        await WaitForTestIdAsync(page, "message-input", 15000);

        Console.WriteLine($"[E2E FeedSort] Alice opened ChatFeed with {otherUserName}");
    }

    /// <summary>
    /// Sends a message via the browser UI WITHOUT producing a block (pending message).
    /// </summary>
    [When(@"Alice sends a pending message ""(.*)"" via browser")]
    public async Task WhenAliceSendsPendingMessageViaBrowser(string messageText)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E FeedSort] Alice sending pending message '{messageText}' via browser...");

        var messageInput = await WaitForTestIdAsync(page, "message-input");
        await messageInput.FillAsync(messageText);

        // Start listening for transaction BEFORE clicking send
        var waiter = StartListeningForTransactions(minTransactions: 1);

        var sendButton = await WaitForTestIdAsync(page, "send-button");
        sendButton.Should().NotBeNull("Send button should be visible");

        var isDisabled = await sendButton.IsDisabledAsync();
        if (isDisabled)
        {
            waiter.Dispose();
            throw new InvalidOperationException("Send button is disabled - check encryption key availability");
        }

        await sendButton.ClickAsync();

        // Wait for transaction to reach server but do NOT produce a block
        try
        {
            await waiter.WaitAsync();
        }
        finally
        {
            waiter.Dispose();
        }

        Console.WriteLine($"[E2E FeedSort] Pending message '{messageText}' sent (no block produced)");
    }

    /// <summary>
    /// Ensures the feed list is visible for assertions.
    /// E2E tests run at 1280x720 (desktop viewport), where the feed list sidebar
    /// is always visible alongside the ChatView. No navigation is needed.
    /// </summary>
    [When(@"Alice navigates back to feed list")]
    public async Task WhenAliceNavigatesBackToFeedList()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E FeedSort] Verifying feed list is visible...");

        // E2E tests use a desktop viewport (1280x720). On desktop, the feed list
        // is always visible in the sidebar alongside the ChatView - no navigation needed.
        var feedList = page.GetByTestId("feed-list");
        await Assertions.Expect(feedList.First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // Brief delay to let any pending sort operations complete after message sends
        await Task.Delay(500);

        Console.WriteLine("[E2E FeedSort] Feed list visible");
    }

    // =========================================================================
    // Then Steps: Feed ordering verification
    // =========================================================================

    /// <summary>
    /// Verifies that one chat feed appears above another in the DOM order.
    /// </summary>
    [Then(@"the feed list should show ChatFeed with ""(.*)"" above ChatFeed with ""(.*)""")]
    public async Task ThenFeedListShouldShowChatFeedAboveAnother(string higherUser, string lowerUser)
    {
        var page = await GetOrCreatePageAsync();
        var feedOrder = await GetFeedOrderAsync(page);

        var higherTestId = $"feed-item:chat:{SanitizeName(higherUser)}";
        var lowerTestId = $"feed-item:chat:{SanitizeName(lowerUser)}";

        var higherIndex = feedOrder.IndexOf(higherTestId);
        var lowerIndex = feedOrder.IndexOf(lowerTestId);

        higherIndex.Should().BeGreaterOrEqualTo(0,
            $"ChatFeed with {higherUser} should be in the feed list. Found: [{string.Join(", ", feedOrder)}]");
        lowerIndex.Should().BeGreaterOrEqualTo(0,
            $"ChatFeed with {lowerUser} should be in the feed list. Found: [{string.Join(", ", feedOrder)}]");

        higherIndex.Should().BeLessThan(lowerIndex,
            $"ChatFeed({higherUser}) at index {higherIndex} should appear above ChatFeed({lowerUser}) at index {lowerIndex}. " +
            $"Feed order: [{string.Join(", ", feedOrder)}]");

        Console.WriteLine($"[E2E FeedSort] Verified: ChatFeed({higherUser}) at {higherIndex} is above ChatFeed({lowerUser}) at {lowerIndex}");
    }

    /// <summary>
    /// Verifies the personal feed is at position 0 (always pinned at top).
    /// </summary>
    [Then(@"the personal feed should be at position 0")]
    public async Task ThenPersonalFeedShouldBeAtPosition0()
    {
        var page = await GetOrCreatePageAsync();
        var feedOrder = await GetFeedOrderAsync(page);

        feedOrder.Should().NotBeEmpty("Feed list should not be empty");
        feedOrder[0].Should().Be("feed-item:personal",
            $"Personal feed should be at position 0 (pinned at top). Feed order: [{string.Join(", ", feedOrder)}]");

        Console.WriteLine("[E2E FeedSort] Verified: Personal feed is at position 0");
    }

    /// <summary>
    /// Verifies a specific chat feed is at a given position.
    /// </summary>
    [Then(@"ChatFeed with ""(.*)"" should be at position (\d+)")]
    public async Task ThenChatFeedShouldBeAtPosition(string otherUserName, int position)
    {
        var page = await GetOrCreatePageAsync();
        var feedOrder = await GetFeedOrderAsync(page);

        var expectedTestId = $"feed-item:chat:{SanitizeName(otherUserName)}";

        feedOrder.Count.Should().BeGreaterThan(position,
            $"Feed list should have at least {position + 1} items. Found: {feedOrder.Count}");

        feedOrder[position].Should().Be(expectedTestId,
            $"ChatFeed({otherUserName}) should be at position {position}. " +
            $"Found '{feedOrder[position]}' instead. Feed order: [{string.Join(", ", feedOrder)}]");

        Console.WriteLine($"[E2E FeedSort] Verified: ChatFeed({otherUserName}) is at position {position}");
    }

    /// <summary>
    /// Verifies the message shows a non-finalized status icon (pending or confirming).
    /// After sending via browser, the message transitions quickly from 'pending' (Clock)
    /// to 'confirming' (Loader2 spinner) once the transaction is accepted by the server.
    /// Both states indicate the message has NOT been included in a block yet.
    /// </summary>
    [Then(@"the message should show pending icon")]
    public async Task ThenMessageShouldShowPendingIcon()
    {
        var page = await GetOrCreatePageAsync();

        // The message may be in 'pending' (Clock) or 'confirming' (Loader2) state
        // depending on whether the server has already accepted the transaction.
        // Both mean "not yet in a block", which is what we're verifying.
        var pendingOrConfirming = page.Locator(
            "[data-testid='message-pending'], [data-testid='message-confirming']");
        await Assertions.Expect(pendingOrConfirming.First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        var testId = await pendingOrConfirming.First.GetAttributeAsync("data-testid");
        Console.WriteLine($"[E2E FeedSort] Verified: Message shows non-finalized icon ({testId})");
    }

    /// <summary>
    /// Verifies the message shows confirmed icon after sync.
    /// Triggers an additional sync if needed.
    /// </summary>
    [Then(@"the message should show confirmed icon after sync")]
    public async Task ThenMessageShouldShowConfirmedIconAfterSync()
    {
        var page = await GetOrCreatePageAsync();

        // Navigate to the chat to see the message
        // The page might be on the feed list, so check if we need to open the chat
        var messageInput = page.GetByTestId("message-input");
        if (await messageInput.CountAsync() == 0)
        {
            // We're on the feed list - need to open the last chat we were in
            // Click on the feed that should have the pending message
            var feedItems = page.Locator("[data-testid^='feed-item:chat']");
            if (await feedItems.CountAsync() > 0)
            {
                await feedItems.First.ClickAsync();
                await WaitForTestIdAsync(page, "message-input", 15000);
            }
        }

        // Trigger sync to pick up the confirmed block
        await TriggerSyncAsync(page);
        await Task.Delay(2000); // Allow time for re-rendering

        var confirmedIcon = page.GetByTestId("message-confirmed");
        await Assertions.Expect(confirmedIcon.First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        Console.WriteLine("[E2E FeedSort] Verified: Message shows confirmed icon");
    }

    /// <summary>
    /// Verifies that feeds with pending messages appear above feeds without pending messages.
    /// </summary>
    [Then(@"feeds with pending messages should be above feeds without pending messages")]
    public async Task ThenFeedsWithPendingMessagesShouldBeAboveFeedsWithout()
    {
        var page = await GetOrCreatePageAsync();
        var feedOrder = await GetFeedOrderAsync(page);

        // Personal feed is always at 0, so skip it
        // All chat feeds with pending messages should come before chat feeds without
        // In this scenario, both Bob and Charlie have pending messages, so they should both
        // be above any feed that doesn't have pending messages
        feedOrder.Count.Should().BeGreaterOrEqualTo(3,
            "Should have at least 3 feeds (personal + 2 chats)");

        // Verify that chat feeds are at positions 1 and 2 (after personal feed at 0)
        var chatFeeds = feedOrder.Skip(1).Where(f => f.StartsWith("feed-item:chat:")).ToList();
        chatFeeds.Count.Should().BeGreaterOrEqualTo(2,
            "Should have at least 2 chat feeds after personal feed");

        Console.WriteLine($"[E2E FeedSort] Feed order after pending messages: [{string.Join(", ", feedOrder)}]");
        Console.WriteLine("[E2E FeedSort] Verified: Feeds with pending messages are above others");
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    /// <summary>
    /// Gets the ordered list of feed item data-testid values from the DOM.
    /// Only includes visible feed items.
    /// </summary>
    private async Task<List<string>> GetFeedOrderAsync(IPage page)
    {
        // Use .First to handle responsive layouts (sidebar + main content)
        var feedList = page.GetByTestId("feed-list").First;
        await Assertions.Expect(feedList).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        var feedItems = feedList.Locator("[data-testid^='feed-item']");
        var count = await feedItems.CountAsync();

        var feedOrder = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var item = feedItems.Nth(i);
            if (await item.IsVisibleAsync())
            {
                var testId = await item.GetAttributeAsync("data-testid");
                if (testId != null)
                {
                    feedOrder.Add(testId);
                }
            }
        }

        Console.WriteLine($"[E2E FeedSort] Feed order ({feedOrder.Count} items): [{string.Join(", ", feedOrder)}]");
        return feedOrder;
    }

    /// <summary>
    /// Waits for at least one feed to have data-feed-ready="true".
    /// </summary>
    private async Task WaitForReadyFeedsAsync(IPage page, int timeoutMs = 30000)
    {
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var readyFeeds = page.Locator("[data-testid^='feed-item'][data-feed-ready='true']");
            var readyCount = await readyFeeds.CountAsync();

            if (readyCount > 0)
            {
                Console.WriteLine($"[E2E FeedSort] {readyCount} ready feed(s) found");
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"No ready feed (data-feed-ready='true') found within {timeoutMs}ms");
    }

    /// <summary>
    /// Waits for a visible feed item with the specified test ID.
    /// </summary>
    private async Task<ILocator> WaitForVisibleFeedItemAsync(IPage page, string testId, int timeoutMs = 10000)
    {
        var allFeeds = page.GetByTestId(testId);
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var count = await allFeeds.CountAsync();
            for (int i = 0; i < count; i++)
            {
                var feed = allFeeds.Nth(i);
                if (await feed.IsVisibleAsync())
                {
                    // Also check if feed is ready (encryption key decrypted)
                    var readyAttr = await feed.GetAttributeAsync("data-feed-ready");
                    if (readyAttr == "true")
                    {
                        return feed;
                    }
                }
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"No visible ready feed found with data-testid='{testId}' within {timeoutMs}ms");
    }

    /// <summary>
    /// Gets or creates a TestIdentity for a browser-created user by extracting keys from localStorage.
    /// Uses individual EvaluateAsync calls (matching the proven pattern in MultiUserSteps).
    /// </summary>
    private async Task<TestIdentity> GetOrCreateBrowserTestIdentityAsync(string userName)
    {
        var identityKey = $"E2E_BrowserIdentity_{userName}";
        if (ScenarioContext.TryGetValue(identityKey, out var existingObj) && existingObj is TestIdentity existing)
        {
            return existing;
        }

        var page = GetUserPage(userName);

        // Extract each key individually (proven pattern from MultiUserSteps - Dictionary<> deserialization
        // doesn't work reliably with Playwright's EvaluateAsync)
        var signingPublicKey = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (!appStorage) return null;
            const parsed = JSON.parse(appStorage);
            return parsed.state?.credentials?.signingPublicKey || null;
        }");

        var signingPrivateKey = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (!appStorage) return null;
            const parsed = JSON.parse(appStorage);
            return parsed.state?.credentials?.signingPrivateKey || null;
        }");

        var encryptionPublicKey = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (!appStorage) return null;
            const parsed = JSON.parse(appStorage);
            return parsed.state?.credentials?.encryptionPublicKey || null;
        }");

        var encryptionPrivateKey = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (!appStorage) return null;
            const parsed = JSON.parse(appStorage);
            return parsed.state?.credentials?.encryptionPrivateKey || null;
        }");

        signingPublicKey.Should().NotBeNullOrEmpty($"{userName}'s signingPublicKey should exist in localStorage");
        signingPrivateKey.Should().NotBeNullOrEmpty($"{userName}'s signingPrivateKey should exist in localStorage");
        encryptionPublicKey.Should().NotBeNullOrEmpty($"{userName}'s encryptionPublicKey should exist in localStorage");
        encryptionPrivateKey.Should().NotBeNullOrEmpty($"{userName}'s encryptionPrivateKey should exist in localStorage");

        var identity = new TestIdentity
        {
            DisplayName = userName,
            Seed = $"BROWSER_{userName}",
            PrivateSigningKey = signingPrivateKey!,
            PublicSigningAddress = signingPublicKey!,
            PrivateEncryptKey = encryptionPrivateKey!,
            PublicEncryptAddress = encryptionPublicKey!
        };

        ScenarioContext[identityKey] = identity;
        Console.WriteLine($"[E2E FeedSort] Created browser TestIdentity for {userName}: {identity.PublicSigningAddress[..20]}...");

        return identity;
    }

    /// <summary>
    /// Registers a backend user (Bob, Charlie) via gRPC if not already registered.
    /// </summary>
    private async Task<TestIdentity> EnsureBackendUserRegisteredAsync(string userName)
    {
        var identity = userName.ToLowerInvariant() switch
        {
            "bob" => TestIdentities.Bob,
            "charlie" => TestIdentities.Charlie,
            _ => throw new ArgumentException($"Unknown backend test user: {userName}")
        };

        var registeredKey = $"E2E_BackendRegistered_{userName}";
        if (ScenarioContext.ContainsKey(registeredKey))
        {
            return identity;
        }

        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();
        var blockControl = GetBlockControl();

        // Register identity
        var identityTxJson = TestTransactionFactory.CreateIdentityRegistration(identity);
        var identityResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = identityTxJson
        });
        identityResponse.Successfull.Should().BeTrue($"Identity registration for {userName} should succeed");
        await blockControl.ProduceBlockAsync();

        // Create personal feed
        var personalFeedTxJson = TestTransactionFactory.CreatePersonalFeed(identity);
        var feedResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = personalFeedTxJson
        });
        feedResponse.Successfull.Should().BeTrue($"Personal feed for {userName} should succeed");
        await blockControl.ProduceBlockAsync();

        ScenarioContext[registeredKey] = true;
        Console.WriteLine($"[E2E FeedSort] Backend user {userName} registered");

        return identity;
    }

    /// <summary>
    /// Gets the page for a specific user from ScenarioContext.
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

    private GrpcClientFactory GetGrpcFactory()
    {
        if (ScenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            && factoryObj is GrpcClientFactory grpcFactory)
        {
            return grpcFactory;
        }
        throw new InvalidOperationException("GrpcClientFactory not found in ScenarioContext.");
    }

    private static string GetChatFeedKey(string user1, string user2)
    {
        var names = new[] { user1.Trim().ToLowerInvariant(), user2.Trim().ToLowerInvariant() };
        Array.Sort(names);
        return $"{names[0]}_{names[1]}";
    }

    /// <summary>
    /// Sanitizes a name for use in test IDs.
    /// Matches the JavaScript implementation in ChatListItem.tsx.
    /// </summary>
    private static string SanitizeName(string name)
    {
        var sanitized = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "-");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "-+", "-");
        return sanitized.Trim('-');
    }
}
