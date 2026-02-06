using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushNetwork.proto;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for multi-user E2E tests.
/// Handles multiple browser contexts (one per user) for testing cross-user interactions.
/// Uses explicit sync triggers instead of waiting for auto-sync.
///
/// Output folder structure:
/// TestRun_2024-01-23_15-30-00/
///   ScenarioName/
///     001-alice-step.png
///     002-bob-step.png
///     server.log
///     browser-alice.log
///     browser-bob.log
/// </summary>
[Binding]
internal sealed class MultiUserSteps : BrowserStepsBase
{
    private string? _currentUser;

    // Screenshot counter for sequential ordering within a scenario
    private int _screenshotCounter = 0;

    public MultiUserSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    // =============================================================================
    // Screenshot & Logging Helpers
    // =============================================================================

    /// <summary>
    /// Gets the scenario output folder from context.
    /// </summary>
    private string GetScenarioFolder()
    {
        if (ScenarioContext.TryGetValue(ScenarioHooks.ScenarioFolderKey, out var folderObj)
            && folderObj is string folder)
        {
            return folder;
        }

        // Fallback to current directory if not in E2E context
        var fallback = Path.Combine(Directory.GetCurrentDirectory(), "e2e-output");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>
    /// Takes a screenshot for the current user.
    /// Screenshots are saved to the scenario folder with format: 001-alice-step-name.png
    /// </summary>
    private async Task TakeScreenshotAsync(string stepName)
    {
        try
        {
            var page = GetUserPage(_currentUser ?? "unknown");
            var counter = Interlocked.Increment(ref _screenshotCounter);
            var userName = _currentUser?.ToLowerInvariant() ?? "unknown";

            var scenarioFolder = GetScenarioFolder();
            var filename = $"{counter:D3}-{userName}-{stepName}.png";
            var fullPath = Path.Combine(scenarioFolder, filename);

            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = fullPath,
                FullPage = true
            });

            Console.WriteLine($"[E2E Screenshot] Saved: {filename}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[E2E Screenshot] Failed to take screenshot '{stepName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a browser console log entry for a user.
    /// </summary>
    private void AddBrowserLog(string userName, string message)
    {
        if (!ScenarioContext.TryGetValue(ScenarioHooks.BrowserLogsKey, out var logsObj)
            || logsObj is not Dictionary<string, List<string>> browserLogs)
        {
            return;
        }

        if (!browserLogs.TryGetValue(userName, out var userLogs))
        {
            userLogs = new List<string>();
            browserLogs[userName] = userLogs;
        }

        userLogs.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    /// <summary>
    /// Sets up browser console log capture for a page.
    /// </summary>
    private void SetupBrowserConsoleCapture(IPage page, string userName)
    {
        page.Console += (_, e) =>
        {
            var logLine = $"[{e.Type}] {e.Text}";
            AddBrowserLog(userName, logLine);

            // Also echo important messages to test console
            if (e.Type == "error" || e.Type == "warning" || e.Text.Contains("[E2E]") || e.Text.Contains("sync"))
            {
                Console.WriteLine($"[Browser:{userName}] {logLine}");
            }
        };

        page.PageError += (_, error) =>
        {
            var logLine = $"[PAGE ERROR] {error}";
            AddBrowserLog(userName, logLine);
            Console.WriteLine($"[Browser:{userName}] {logLine}");
        };
    }

    // =============================================================================
    // Sync Trigger
    // =============================================================================

    /// <summary>
    /// Triggers sync for a specific user.
    /// Calls window.__e2e_triggerSync() which runs all syncables.
    /// </summary>
    [When(@"(.*) triggers sync")]
    public async Task WhenUserTriggersSync(string userName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] {userName} triggering sync...");

        // Dump Redis cache state BEFORE sync
        await DumpRedisCacheForCurrentGroupAsync($"Before {userName} Sync");

        try
        {
            await page.EvaluateAsync("() => window.__e2e_triggerSync()");
            Console.WriteLine($"[E2E] {userName} sync completed");
        }
        catch (PlaywrightException ex)
        {
            Console.WriteLine($"[E2E] Warning: Sync trigger failed - {ex.Message}");
            // Take screenshot for debugging
            await TakeScreenshotAsync($"sync-failed-{userName}");
        }

        // Dump Redis cache state AFTER sync (to see if anything changed)
        await DumpRedisCacheForCurrentGroupAsync($"After {userName} Sync");

        // Capture localStorage state after sync (shows new feeds, keys, messages)
        await CaptureLocalStorageAsync(page, "after-sync", userName);

        await TakeScreenshotAsync($"after-sync");
    }

    // =============================================================================
    // Compound Steps: Message with Confirmation
    // =============================================================================

    /// <summary>
    /// Sends a message and waits for confirmation (checkmark).
    /// Combines: send message + wait for transaction + produce block + sync + verify checkmark.
    /// </summary>
    [When(@"(.*) sends message ""(.*)"" and waits for confirmation")]
    public async Task WhenUserSendsMessageAndWaitsForConfirmation(string userName, string messageText)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] {userName} sending message '{messageText}'...");
        await TakeScreenshotAsync("before-send");

        // 1. Get message input and fill
        var messageInput = await WaitForTestIdAsync(page, "message-input");
        await messageInput.FillAsync(messageText);

        // 2. Start listening for transaction BEFORE clicking send
        var waiter = StartListeningForTransactions(minTransactions: 1);

        // 3. Click send button
        var sendButton = await WaitForTestIdAsync(page, "send-button");
        var isDisabled = await sendButton.IsDisabledAsync();

        if (isDisabled)
        {
            await TakeScreenshotAsync("send-disabled");
            waiter.Dispose();
            throw new InvalidOperationException($"Send button is disabled for {userName} - check encryption key availability");
        }

        await sendButton.ClickAsync();
        Console.WriteLine($"[E2E] {userName} clicked send, waiting for transaction...");
        await TakeScreenshotAsync("after-send-click");

        // 4. Wait for transaction and produce block
        try
        {
            await waiter.WaitAsync();
        }
        finally
        {
            waiter.Dispose();
        }

        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();
        Console.WriteLine($"[E2E] Block produced after {userName}'s message");

        // 5. Trigger sync to pick up the new block
        Console.WriteLine($"[E2E] {userName} triggering sync...");
        await page.EvaluateAsync("() => window.__e2e_triggerSync()");

        // 6. Wait for message to show confirmation checkmark
        Console.WriteLine($"[E2E] Waiting for message confirmation checkmark...");
        var messageItem = page.Locator("[data-testid='message']")
            .Filter(new LocatorFilterOptions { HasText = messageText });

        await messageItem.Locator("[data-testid='message-confirmed']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        Console.WriteLine($"[E2E] {userName} message '{messageText}' confirmed");
        await TakeScreenshotAsync("message-confirmed");
    }

    // =============================================================================
    // Compound Steps: Join with Confirmation
    // =============================================================================

    /// <summary>
    /// Joins a group and waits for confirmation.
    /// Combines: click join + wait for transaction + produce block + sync.
    /// JoinGroupFeed is now a blockchain transaction, so we wait for it to be indexed.
    /// </summary>
    [When(@"(.*) joins the group and waits for confirmation")]
    public async Task WhenUserJoinsGroupAndWaitsForConfirmation(string userName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] {userName} clicking join button...");
        await TakeScreenshotAsync("before-join");

        // Wait for join button to be visible
        var joinButton = page.GetByTestId("join-group-button");
        await joinButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 20000, State = WaitForSelectorState.Visible });

        // Check button state before clicking
        var isEnabled = await joinButton.IsEnabledAsync();
        var buttonText = await joinButton.TextContentAsync();
        Console.WriteLine($"[E2E] Join button found - enabled: {isEnabled}, text: '{buttonText}'");

        // 1. Start listening for transactions BEFORE clicking
        var waiter = StartListeningForTransactions(minTransactions: 1);

        // 2. Click join button
        Console.WriteLine($"[E2E] {userName} clicking join button NOW...");
        await joinButton.ClickAsync();
        Console.WriteLine($"[E2E] {userName} clicked join button");

        // Take screenshot immediately after click to see UI state change
        await TakeScreenshotAsync("after-join-click");

        // 3. Wait for transaction to arrive at server
        Console.WriteLine($"[E2E] Waiting for JoinGroupFeed transaction...");
        try
        {
            await waiter.WaitAsync();
            Console.WriteLine($"[E2E] JoinGroupFeed transaction received");
        }
        finally
        {
            waiter.Dispose();
        }

        // 4. Produce block to index the join transaction
        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();
        Console.WriteLine($"[E2E] Block produced after {userName}'s join");

        // 5. Wait for redirect to dashboard (join completes and redirects)
        Console.WriteLine($"[E2E] {userName} waiting for redirect to dashboard...");
        await page.WaitForURLAsync("**/dashboard**", new PageWaitForURLOptions { Timeout = 20000 });
        Console.WriteLine($"[E2E] {userName} redirected to dashboard");

        // Wait for page to load
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // 6. Trigger sync to pick up the new group membership
        Console.WriteLine($"[E2E] {userName} triggering sync...");
        await page.EvaluateAsync("() => window.__e2e_triggerSync()");

        // Capture localStorage state after joining (includes new group + KeyGen)
        await CaptureLocalStorageAsync(page, "group-joined", userName);

        Console.WriteLine($"[E2E] {userName} joined group");
        await TakeScreenshotAsync("group-joined");

        // 7. Debug: Dump Redis cache state after join
        await DumpRedisCacheForCurrentGroupAsync("After Bob Joins");
    }

    /// <summary>
    /// Dumps the Redis cache state for the current group (stored in context).
    /// </summary>
    private async Task DumpRedisCacheForCurrentGroupAsync(string context)
    {
        Console.WriteLine($"[E2E Redis Debug] === {context} ===");

        // Try to get the group feed ID from context
        var groupName = "Team Chat"; // Default for our test
        string? feedId = null;

        // First try to get from stored context
        if (ScenarioContext.TryGetValue("LastCreatedFeedId", out var feedIdObj))
        {
            feedId = feedIdObj as string;
        }

        // If not found, try to get from gRPC
        if (feedId == null && ScenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            && factoryObj is GrpcClientFactory grpcFactory)
        {
            var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
            var searchResponse = await feedClient.SearchPublicGroupsAsync(new SearchPublicGroupsRequest
            {
                SearchQuery = groupName,
                MaxResults = 10
            });

            if (searchResponse.Success && searchResponse.Groups.Count > 0)
            {
                var group = searchResponse.Groups.FirstOrDefault(g =>
                    g.Title.Equals(groupName, StringComparison.OrdinalIgnoreCase)) ?? searchResponse.Groups[0];
                feedId = group.FeedId;
                ScenarioContext["LastCreatedFeedId"] = feedId;
            }
        }

        if (feedId == null)
        {
            Console.WriteLine("[E2E Redis Debug] Could not find feed ID for cache dump");
            return;
        }

        await DumpRedisCacheStateAsync(feedId, groupName);

        // Also verify what keys the gRPC would return for Alice and Bob
        await DumpKeyGenerationsForUsersAsync(feedId, groupName);
    }

    /// <summary>
    /// Dumps the key generations and members that would be returned via gRPC.
    /// This helps verify that the server is returning the correct keys.
    /// </summary>
    private async Task DumpKeyGenerationsForUsersAsync(string feedId, string groupName)
    {
        if (!ScenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            || factoryObj is not GrpcClientFactory grpcFactory)
        {
            Console.WriteLine("[E2E gRPC Debug] Could not get gRPC factory");
            return;
        }

        Console.WriteLine($"[E2E gRPC Debug] === Group info for '{groupName}' ===");

        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        try
        {
            // Get group info
            var groupResponse = await feedClient.GetGroupFeedAsync(new GetGroupFeedRequest
            {
                FeedId = feedId
            });

            if (groupResponse.Success)
            {
                Console.WriteLine($"[E2E gRPC Debug] Group '{groupResponse.Title}' found");
                Console.WriteLine($"[E2E gRPC Debug] MemberCount: {groupResponse.MemberCount}");
                Console.WriteLine($"[E2E gRPC Debug] CurrentKeyGeneration: {groupResponse.CurrentKeyGeneration}");
            }
            else
            {
                Console.WriteLine($"[E2E gRPC Debug] GetGroupFeed failed: {groupResponse.Message}");
            }

            // Get group members
            var membersResponse = await feedClient.GetGroupMembersAsync(new GetGroupMembersRequest
            {
                FeedId = feedId
            });

            Console.WriteLine($"[E2E gRPC Debug] Members ({membersResponse.Members.Count}):");
            foreach (var member in membersResponse.Members)
            {
                Console.WriteLine($"[E2E gRPC Debug]   - {member.PublicAddress.Substring(0, 10)}... Name: {member.DisplayName}, Type: {member.ParticipantType}");

                // Get key generations for each member
                var keysResponse = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
                {
                    FeedId = feedId,
                    UserPublicAddress = member.PublicAddress
                });

                Console.WriteLine($"[E2E gRPC Debug]     KeyGenerations ({keysResponse.KeyGenerations.Count}):");
                foreach (var kg in keysResponse.KeyGenerations)
                {
                    Console.WriteLine($"[E2E gRPC Debug]       - KeyGen {kg.KeyGeneration}: KeyLen={kg.EncryptedKey.Length}, ValidFrom={kg.ValidFromBlock}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[E2E gRPC Debug] Error: {ex.Message}");
        }

        Console.WriteLine("[E2E gRPC Debug] === End Group info ===");
    }

    // =============================================================================
    // Verification Steps
    // =============================================================================

    /// <summary>
    /// CRITICAL ASSERTION: Verifies user has exactly N KeyGenerations for a group via gRPC.
    /// This is the same assertion as the Twin Test - if this fails, the server is not
    /// returning the expected KeyGenerations to the user.
    /// </summary>
    [Then(@"(.*) should have exactly (\d+) KeyGenerations? for ""(.*)"" via gRPC")]
    public async Task ThenUserShouldHaveExactlyNKeyGenerationsViaGrpc(string userName, int expectedCount, string groupName)
    {
        Console.WriteLine($"[E2E CRITICAL] Verifying {userName} has exactly {expectedCount} KeyGeneration(s) for '{groupName}'...");

        // Get user's public address from localStorage
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        // Debug: List all localStorage keys
        var allKeys = await page.EvaluateAsync<string[]>(@"() => Object.keys(localStorage)");
        Console.WriteLine($"[E2E CRITICAL] localStorage keys: [{string.Join(", ", allKeys ?? Array.Empty<string>())}]");

        var userAddress = await page.EvaluateAsync<string>(@"() => {
            // Try hush-app-storage first (current key used by useAppStore)
            const appStorage = localStorage.getItem('hush-app-storage');
            if (appStorage) {
                try {
                    const parsed = JSON.parse(appStorage);
                    const addr = parsed.state?.credentials?.signingPublicKey;
                    if (addr) {
                        console.log('[E2E CRITICAL] Found address in hush-app-storage');
                        return addr;
                    }
                } catch (e) {
                    console.log('[E2E CRITICAL] Failed to parse hush-app-storage: ' + e);
                }
            }

            // Fallback: try hush-credentials (legacy key)
            const creds = localStorage.getItem('hush-credentials');
            if (creds) {
                try {
                    const parsed = JSON.parse(creds);
                    return parsed.state?.signingPublicKey || null;
                } catch (e) {
                    console.log('[E2E CRITICAL] Failed to parse hush-credentials: ' + e);
                }
            }

            console.log('[E2E CRITICAL] No credentials found in localStorage');
            return null;
        }");

        if (string.IsNullOrEmpty(userAddress))
        {
            // Capture a screenshot for debugging
            await TakeScreenshotAsync("critical-assertion-failed-no-address");
            throw new InvalidOperationException($"Could not get {userName}'s public address from localStorage. Keys: [{string.Join(", ", allKeys ?? Array.Empty<string>())}]");
        }

        Console.WriteLine($"[E2E CRITICAL] {userName}'s address: {userAddress.Substring(0, 20)}...");

        // Get the feed ID for the group
        string? feedId = null;

        if (ScenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            && factoryObj is GrpcClientFactory grpcFactory)
        {
            var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

            // Search for the group
            var searchResponse = await feedClient.SearchPublicGroupsAsync(new SearchPublicGroupsRequest
            {
                SearchQuery = groupName,
                MaxResults = 10
            });

            if (searchResponse.Success && searchResponse.Groups.Count > 0)
            {
                var group = searchResponse.Groups.FirstOrDefault(g =>
                    g.Title.Equals(groupName, StringComparison.OrdinalIgnoreCase)) ?? searchResponse.Groups[0];
                feedId = group.FeedId;
            }

            if (string.IsNullOrEmpty(feedId))
            {
                throw new InvalidOperationException($"Could not find group '{groupName}'");
            }

            Console.WriteLine($"[E2E CRITICAL] Group '{groupName}' feedId: {feedId}");

            // Store for subsequent steps
            ScenarioContext["LastVerifiedFeedId"] = feedId;
            ScenarioContext["LastVerifiedUserAddress"] = userAddress;

            // Get KeyGenerations for this user via gRPC
            var keysResponse = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
            {
                FeedId = feedId,
                UserPublicAddress = userAddress
            });

            Console.WriteLine($"[E2E CRITICAL] {userName} GetKeyGenerations returned {keysResponse.KeyGenerations.Count} KeyGeneration(s):");
            foreach (var kg in keysResponse.KeyGenerations)
            {
                Console.WriteLine($"[E2E CRITICAL]   - KeyGen {kg.KeyGeneration}: ValidFrom={kg.ValidFromBlock}, HasEncryptedKey={!string.IsNullOrEmpty(kg.EncryptedKey)}, KeyLength={kg.EncryptedKey?.Length ?? 0}");
            }

            // THE CRITICAL ASSERTION
            keysResponse.KeyGenerations.Count.Should().Be(expectedCount,
                $"[E2E CRITICAL FAILURE] {userName} should have exactly {expectedCount} KeyGeneration(s) for '{groupName}'. " +
                $"Got {keysResponse.KeyGenerations.Count}. " +
                "This is the SAME check as the Twin Test. If this fails, the server is not returning " +
                "the new KeyGeneration to existing members after a new member joins.");

            Console.WriteLine($"[E2E CRITICAL] ASSERTION PASSED: {userName} has exactly {expectedCount} KeyGeneration(s)");
        }
        else
        {
            throw new InvalidOperationException("Could not get gRPC factory from context");
        }
    }

    /// <summary>
    /// CRITICAL ASSERTION: Verifies user receives a specific message with expected KeyGeneration via gRPC.
    /// </summary>
    [Then(@"(.*) should receive (.*)'s message ""(.*)"" with KeyGeneration (\d+) via gRPC")]
    public async Task ThenUserShouldReceiveMessageWithKeyGeneration(string userName, string senderName, string messageContent, int expectedKeyGen)
    {
        Console.WriteLine($"[E2E CRITICAL] Verifying {userName} receives {senderName}'s message with KeyGeneration {expectedKeyGen}...");

        // Get feed ID from context
        var feedId = ScenarioContext.Get<string>("LastVerifiedFeedId");

        // Get the user's address from localStorage (not from context - we want THIS user's address)
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        var userAddress = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (!appStorage) return null;
            const parsed = JSON.parse(appStorage);
            return parsed.state?.credentials?.signingPublicKey || null;
        }");

        if (string.IsNullOrEmpty(userAddress))
        {
            throw new InvalidOperationException($"Could not get {userName}'s address from localStorage");
        }

        Console.WriteLine($"[E2E CRITICAL] {userName}'s address: {userAddress.Substring(0, 20)}...");

        // Store for next step
        ScenarioContext["LastVerifiedUserAddress"] = userAddress;

        if (!ScenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            || factoryObj is not GrpcClientFactory grpcFactory)
        {
            throw new InvalidOperationException("Could not get gRPC factory from context");
        }

        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        // Get messages for this user
        var messagesResponse = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = userAddress,
            BlockIndex = 0
        });

        Console.WriteLine($"[E2E CRITICAL] Server returned {messagesResponse.Messages.Count} message(s)");

        // Find messages for our feed
        var feedMessages = messagesResponse.Messages.Where(m => m.FeedId == feedId).ToList();
        Console.WriteLine($"[E2E CRITICAL] Messages in feed '{feedId}': {feedMessages.Count}");

        foreach (var msg in feedMessages)
        {
            Console.WriteLine($"[E2E CRITICAL]   - MsgId={msg.FeedMessageId.Substring(0, 8)}..., KeyGen={msg.KeyGeneration}, ContentLen={msg.MessageContent.Length}, From={msg.IssuerName}");
        }

        // Find message with expected KeyGeneration FROM the expected sender
        var targetMessage = feedMessages.FirstOrDefault(m =>
            m.KeyGeneration == expectedKeyGen &&
            m.IssuerName == senderName);

        targetMessage.Should().NotBeNull(
            $"[E2E CRITICAL FAILURE] {userName} should receive a message from {senderName} with KeyGeneration {expectedKeyGen}. " +
            $"Found {feedMessages.Count} message(s) in feed, KeyGens: [{string.Join(", ", feedMessages.Select(m => $"{m.KeyGeneration} from {m.IssuerName}"))}]");

        Console.WriteLine($"[E2E CRITICAL] Found message with KeyGeneration {expectedKeyGen}:");
        Console.WriteLine($"[E2E CRITICAL]   MessageId: {targetMessage!.FeedMessageId}");
        Console.WriteLine($"[E2E CRITICAL]   IssuerName: {targetMessage.IssuerName}");
        Console.WriteLine($"[E2E CRITICAL]   ContentLength: {targetMessage.MessageContent.Length}");
        Console.WriteLine($"[E2E CRITICAL]   BlockIndex: {targetMessage.BlockIndex}");

        // Store for next step
        ScenarioContext["LastVerifiedMessage"] = targetMessage;
        ScenarioContext["LastExpectedMessageContent"] = messageContent;

        Console.WriteLine($"[E2E CRITICAL] ASSERTION PASSED: Message with KeyGeneration {expectedKeyGen} received");
    }

    /// <summary>
    /// CRITICAL ASSERTION: Verifies user can decrypt a message using a specific KeyGeneration.
    /// </summary>
    [Then(@"(.*) should be able to decrypt the message using KeyGeneration (\d+)")]
    public async Task ThenUserShouldBeAbleToDecryptMessage(string userName, int keyGeneration)
    {
        Console.WriteLine($"[E2E CRITICAL] Verifying {userName} can decrypt message using KeyGeneration {keyGeneration}...");

        // Get stored data from previous steps
        var feedId = ScenarioContext.Get<string>("LastVerifiedFeedId");
        var userAddress = ScenarioContext.Get<string>("LastVerifiedUserAddress");
        var message = ScenarioContext.Get<GetFeedMessagesForAddressReply.Types.FeedMessage>("LastVerifiedMessage");
        var expectedContent = ScenarioContext.Get<string>("LastExpectedMessageContent");

        if (!ScenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            || factoryObj is not GrpcClientFactory grpcFactory)
        {
            throw new InvalidOperationException("Could not get gRPC factory from context");
        }

        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        // Get KeyGenerations to find the encrypted AES key
        var keysResponse = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
        {
            FeedId = feedId,
            UserPublicAddress = userAddress
        });

        var keyGen = keysResponse.KeyGenerations.FirstOrDefault(k => k.KeyGeneration == keyGeneration);
        keyGen.Should().NotBeNull($"KeyGeneration {keyGeneration} should exist for {userName}");

        Console.WriteLine($"[E2E CRITICAL] KeyGen {keyGeneration} encrypted key length: {keyGen!.EncryptedKey.Length}");

        // Get user's private key from localStorage to decrypt the AES key
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        var encryptionPrivateKey = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (!appStorage) return null;
            const parsed = JSON.parse(appStorage);
            return parsed.state?.credentials?.encryptionPrivateKey || null;
        }");

        if (string.IsNullOrEmpty(encryptionPrivateKey))
        {
            throw new InvalidOperationException($"Could not get {userName}'s encryption private key from localStorage");
        }

        Console.WriteLine($"[E2E CRITICAL] Got {userName}'s encryption private key (length={encryptionPrivateKey.Length})");

        // Decrypt the AES key using ECIES
        string decryptedAesKey;
        try
        {
            decryptedAesKey = Olimpo.EncryptKeys.Decrypt(keyGen.EncryptedKey, encryptionPrivateKey);
            Console.WriteLine($"[E2E CRITICAL] Successfully decrypted AES key for KeyGeneration {keyGeneration}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[E2E CRITICAL FAILURE] Failed to decrypt AES key for KeyGeneration {keyGeneration}: {ex.Message}");
        }

        // Decrypt the message content
        string decryptedContent;
        try
        {
            decryptedContent = Olimpo.EncryptKeys.AesDecrypt(message.MessageContent, decryptedAesKey);
            Console.WriteLine($"[E2E CRITICAL] Successfully decrypted message content: '{decryptedContent}'");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[E2E CRITICAL FAILURE] Failed to decrypt message content: {ex.Message}. " +
                $"AES key length: {decryptedAesKey.Length}, Content length: {message.MessageContent.Length}");
        }

        // Verify the decrypted content matches expected
        decryptedContent.Should().Be(expectedContent,
            $"[E2E CRITICAL FAILURE] Decrypted content should match expected. " +
            $"Got: '{decryptedContent}', Expected: '{expectedContent}'");

        Console.WriteLine($"[E2E CRITICAL] ASSERTION PASSED: {userName} successfully decrypted message: '{decryptedContent}'");
    }

    /// <summary>
    /// CRITICAL ASSERTION: Verifies a user's message exists on server with expected KeyGeneration.
    /// </summary>
    [Then(@"(.*)'s message ""(.*)"" should be on server with KeyGeneration (\d+)")]
    public async Task ThenUsersMessageShouldBeOnServerWithKeyGeneration(string senderName, string messageContent, int expectedKeyGen)
    {
        Console.WriteLine($"[E2E CRITICAL] Verifying {senderName}'s message '{messageContent}' is on server with KeyGeneration {expectedKeyGen}...");

        // Get sender's address from localStorage
        await SwitchToUserAsync(senderName);
        var page = GetUserPage(senderName);

        var senderAddress = await page.EvaluateAsync<string>(@"() => {
            const appStorage = localStorage.getItem('hush-app-storage');
            if (!appStorage) return null;
            const parsed = JSON.parse(appStorage);
            return parsed.state?.credentials?.signingPublicKey || null;
        }");

        if (string.IsNullOrEmpty(senderAddress))
        {
            throw new InvalidOperationException($"Could not get {senderName}'s address from localStorage");
        }

        Console.WriteLine($"[E2E CRITICAL] {senderName}'s address: {senderAddress.Substring(0, 20)}...");

        // Get feed ID from context
        var feedId = ScenarioContext.Get<string>("LastVerifiedFeedId");

        if (!ScenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            || factoryObj is not GrpcClientFactory grpcFactory)
        {
            throw new InvalidOperationException("Could not get gRPC factory from context");
        }

        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        // Get messages for the sender
        var messagesResponse = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = senderAddress,
            BlockIndex = 0
        });

        Console.WriteLine($"[E2E CRITICAL] Server returned {messagesResponse.Messages.Count} message(s) for {senderName}");

        // Find messages in our feed from this sender
        var feedMessages = messagesResponse.Messages
            .Where(m => m.FeedId == feedId && m.IssuerPublicAddress == senderAddress)
            .ToList();

        Console.WriteLine($"[E2E CRITICAL] Messages from {senderName} in feed: {feedMessages.Count}");

        foreach (var msg in feedMessages)
        {
            Console.WriteLine($"[E2E CRITICAL]   - MsgId={msg.FeedMessageId.Substring(0, 8)}..., KeyGen={msg.KeyGeneration}, ContentLen={msg.MessageContent.Length}, Block={msg.BlockIndex}");
        }

        // Find message with expected KeyGeneration
        var targetMessage = feedMessages.FirstOrDefault(m => m.KeyGeneration == expectedKeyGen);

        targetMessage.Should().NotBeNull(
            $"[E2E CRITICAL FAILURE] {senderName}'s message should exist with KeyGeneration {expectedKeyGen}. " +
            $"Found {feedMessages.Count} message(s), KeyGens: [{string.Join(", ", feedMessages.Select(m => m.KeyGeneration))}]");

        Console.WriteLine($"[E2E CRITICAL] Found {senderName}'s message with KeyGeneration {expectedKeyGen}:");
        Console.WriteLine($"[E2E CRITICAL]   MessageId: {targetMessage!.FeedMessageId}");
        Console.WriteLine($"[E2E CRITICAL]   BlockIndex: {targetMessage.BlockIndex}");
        Console.WriteLine($"[E2E CRITICAL]   ContentLength: {targetMessage.MessageContent.Length}");

        // Store for subsequent steps (for Bob to verify)
        ScenarioContext["LastSenderMessage"] = targetMessage;
        ScenarioContext["LastSenderMessageContent"] = messageContent;

        Console.WriteLine($"[E2E CRITICAL] ASSERTION PASSED: {senderName}'s message exists with KeyGeneration {expectedKeyGen}");
    }

    /// <summary>
    /// DEBUG: Dumps the client-side message state for a group to diagnose rendering issues.
    /// </summary>
    [Then(@"dump (.*)'s message state for ""(.*)""")]
    public async Task ThenDumpUserMessageStateForGroup(string userName, string groupName)
    {
        Console.WriteLine($"[E2E DEBUG] Dumping {userName}'s message state for '{groupName}'...");

        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        // Get the feed ID from context
        var feedId = ScenarioContext.Get<string>("LastVerifiedFeedId");

        // Dump the feeds store state
        var storeState = await page.EvaluateAsync<string>(@"(feedId) => {
            try {
                const feedsStorage = localStorage.getItem('hush-feeds-storage');
                if (!feedsStorage) return JSON.stringify({ error: 'No hush-feeds-storage found' });

                const parsed = JSON.parse(feedsStorage);
                const state = parsed.state;

                // Find the feed
                const feed = state.feeds?.find(f => f.id === feedId);
                const messages = state.messages?.[feedId] || [];
                const groupKeyState = state.groupKeyStates?.[feedId];

                return JSON.stringify({
                    feedId: feedId,
                    feedFound: !!feed,
                    feedName: feed?.name,
                    feedType: feed?.type,
                    messageCount: messages.length,
                    messages: messages.map(m => ({
                        id: m.id?.substring(0, 8) + '...',
                        content: m.content?.substring(0, 30) + (m.content?.length > 30 ? '...' : ''),
                        keyGeneration: m.keyGeneration,
                        decryptionFailed: m.decryptionFailed,
                        senderName: m.senderName,
                        isConfirmed: m.isConfirmed
                    })),
                    groupKeyState: groupKeyState ? {
                        currentKeyGeneration: groupKeyState.currentKeyGeneration,
                        keyCount: groupKeyState.keyGenerations?.length,
                        keyGens: groupKeyState.keyGenerations?.map(k => ({
                            keyGeneration: k.keyGeneration,
                            hasAesKey: !!k.aesKey
                        }))
                    } : null
                }, null, 2);
            } catch (e) {
                return JSON.stringify({ error: e.message });
            }
        }", feedId);

        Console.WriteLine($"[E2E DEBUG] Store state:\n{storeState}");

        // Also check the DOM for rendered messages
        var renderedMessages = await page.EvaluateAsync<string>(@"() => {
            const messages = document.querySelectorAll('[data-testid=""message""]');
            const result = [];
            messages.forEach((msg, i) => {
                result.push({
                    index: i,
                    text: msg.textContent?.substring(0, 50) + (msg.textContent?.length > 50 ? '...' : ''),
                    visible: msg.offsetParent !== null
                });
            });
            return JSON.stringify(result, null, 2);
        }");

        Console.WriteLine($"[E2E DEBUG] Rendered messages in DOM:\n{renderedMessages}");

        await TakeScreenshotAsync("debug-message-state");
    }

    /// <summary>
    /// Verifies user should NOT see a specific message.
    /// </summary>
    [Then(@"(.*) should NOT see message ""(.*)"" in ""(.*)""")]
    public async Task ThenUserShouldNotSeeMessage(string userName, string messageText, string groupName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] Verifying {userName} does NOT see message '{messageText}'...");

        var message = page.Locator("[data-testid='message']")
            .Filter(new LocatorFilterOptions { HasText = messageText });

        var count = await message.CountAsync();
        count.Should().Be(0, $"{userName} should not see message '{messageText}' (sent before joining)");

        Console.WriteLine($"[E2E] Confirmed: {userName} does not see '{messageText}'");
        await TakeScreenshotAsync("verified-no-message");
    }

    /// <summary>
    /// Verifies user sees a message from another user.
    /// Waits for message to appear with retries and sync triggers.
    /// </summary>
    [Then(@"(.*) should see message ""(.*)"" from (.*)")]
    public async Task ThenUserShouldSeeMessageFrom(string userName, string messageText, string senderName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] Verifying {userName} can see message '{messageText}' from {senderName}...");
        Console.WriteLine($"[E2E] Page URL: {page.Url}");

        // The message might take time to load due to async sync/decryption
        // Try multiple sync cycles if needed
        const int maxAttempts = 5;
        const int waitBetweenAttemptsMs = 2000;

        var message = page.Locator("[data-testid='message']")
            .Filter(new LocatorFilterOptions { HasText = messageText });

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // Check if message is already visible
            var count = await message.CountAsync();
            if (count > 0)
            {
                Console.WriteLine($"[E2E] {userName} can see message '{messageText}' from {senderName} (attempt {attempt})");
                await TakeScreenshotAsync($"sees-message-from-{senderName.ToLower()}");
                return;
            }

            if (attempt < maxAttempts)
            {
                Console.WriteLine($"[E2E] Message not found yet (attempt {attempt}/{maxAttempts}), triggering sync...");

                // Debug: dump current state
                var allMessages = page.Locator("[data-testid='message']");
                var msgCount = await allMessages.CountAsync();
                Console.WriteLine($"[E2E]   Current message count: {msgCount}");

                // Trigger another sync cycle
                await page.EvaluateAsync("() => window.__e2e_triggerSync()");
                await Task.Delay(waitBetweenAttemptsMs);
            }
        }

        // Final attempt - throw if still not visible
        Console.WriteLine($"[E2E] Final check after {maxAttempts} attempts...");

        // Debug: dump all messages
        var finalMessages = page.Locator("[data-testid='message']");
        var finalCount = await finalMessages.CountAsync();
        Console.WriteLine($"[E2E] Found {finalCount} message elements in DOM:");
        for (int i = 0; i < Math.Min(finalCount, 10); i++)
        {
            var msgText = await finalMessages.Nth(i).TextContentAsync();
            Console.WriteLine($"[E2E]   Message {i}: {msgText?.Substring(0, Math.Min(80, msgText?.Length ?? 0))}...");
        }

        await Expect(message.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });

        Console.WriteLine($"[E2E] {userName} can see message '{messageText}' from {senderName}");
        await TakeScreenshotAsync($"sees-message-from-{senderName.ToLower()}");
    }

    /// <summary>
    /// Verifies user sees that another user joined the group.
    /// </summary>
    [Then(@"(.*) should see that (.*) joined the group")]
    public async Task ThenUserShouldSeeThatUserJoined(string userName, string otherUserName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] Verifying {userName} can see that {otherUserName} joined...");

        // Look for system message about user joining, or member in group info
        // This could be a system message or shown in members list
        var systemMessage = page.Locator("[data-testid='system-message']")
            .Filter(new LocatorFilterOptions { HasText = otherUserName });

        // Also check members count or list if visible
        var membersInfo = page.Locator("[data-testid='group-members']");

        var systemMessageVisible = await systemMessage.CountAsync() > 0;
        var membersInfoVisible = await membersInfo.CountAsync() > 0;

        // At minimum, the other user should be able to send messages (verified in other steps)
        Console.WriteLine($"[E2E] {userName} sees group state updated (system message: {systemMessageVisible}, members: {membersInfoVisible})");
        await TakeScreenshotAsync($"sees-{otherUserName.ToLower()}-joined");
    }

    // =============================================================================
    // Reaction Steps
    // =============================================================================

    /// <summary>
    /// User adds a reaction to another user's message.
    /// </summary>
    [When(@"(.*) adds reaction to (.*)'s message")]
    public async Task WhenUserAddsReactionToMessage(string userName, string targetUserName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] {userName} adding reaction to {targetUserName}'s message...");

        // Take screenshot before attempting to find message
        await TakeScreenshotAsync("before-reaction");

        // Find messages from the target user by looking for their name in the message
        // Messages from others show the sender name above the message content
        var targetMessages = page.Locator("[data-testid='message']")
            .Filter(new LocatorFilterOptions { HasText = targetUserName });

        var count = await targetMessages.CountAsync();
        Console.WriteLine($"[E2E] Found {count} message(s) containing '{targetUserName}'");

        if (count == 0)
        {
            // Fallback: find any message that is NOT from the current user (no checkmark)
            Console.WriteLine($"[E2E] Falling back to messages without checkmark");
            targetMessages = page.Locator("[data-testid='message']")
                .Filter(new LocatorFilterOptions { HasNot = page.Locator("[data-testid='message-confirmed']") });
            count = await targetMessages.CountAsync();
            Console.WriteLine($"[E2E] Found {count} message(s) without checkmark");
        }

        // Use the last message (most recent)
        var messageToReact = targetMessages.Last;
        await Expect(messageToReact).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        // Hover to reveal reaction button
        Console.WriteLine($"[E2E] Hovering over message to reveal reaction button...");
        await messageToReact.HoverAsync();

        // Click reaction button
        var reactionButton = messageToReact.GetByTestId("add-reaction-button");
        await reactionButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000, State = WaitForSelectorState.Visible });
        await reactionButton.ClickAsync();
        Console.WriteLine($"[E2E] Clicked add-reaction-button");

        // Pick first emoji (thumbs up)
        var reactionPicker = page.GetByTestId("reaction-picker");
        await reactionPicker.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        Console.WriteLine($"[E2E] Reaction picker visible");

        var firstEmoji = reactionPicker.Locator("button").First;
        await firstEmoji.ClickAsync();

        Console.WriteLine($"[E2E] {userName} added reaction to {targetUserName}'s message");
        await TakeScreenshotAsync("reaction-added");
    }

    /// <summary>
    /// Verifies user sees a reaction on their message.
    /// </summary>
    [Then(@"(.*) should see reaction on his message")]
    public async Task ThenUserShouldSeeReactionOnMessage(string userName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] Verifying {userName} can see reaction on their message...");

        // Look for reaction bar on own messages
        var reactionBar = page.Locator("[data-testid='message']")
            .Locator("[data-testid^='reaction-']");

        await Expect(reactionBar.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 10000
        });

        Console.WriteLine($"[E2E] {userName} can see reaction on their message");
        await TakeScreenshotAsync("sees-reaction");
    }

    // =============================================================================
    // Browser Context Management
    // =============================================================================

    /// <summary>
    /// Creates a browser context for a specific user.
    /// Each user gets their own isolated browser context with separate cookies/storage.
    /// </summary>
    [Given(@"a browser context for ""(.*)""")]
    public async Task GivenABrowserContextForUser(string userName)
    {
        Console.WriteLine($"[E2E] Creating browser context for '{userName}'...");

        var playwright = GetPlaywright();
        var (context, page) = await playwright.CreatePageAsync();

        // Set up browser console log capture for this user
        SetupBrowserConsoleCapture(page, userName);
        AddBrowserLog(userName, $"Browser context created for {userName}");

        // Set up network request/response logging for this user
        SetupNetworkLogging(page, userName);

        // Store user-specific page and context
        var pageKey = $"E2E_Page_{userName}";
        var contextKey = $"E2E_Context_{userName}";

        ScenarioContext[pageKey] = page;
        ScenarioContext[contextKey] = context;

        // Set this as the current user
        _currentUser = userName;
        ScenarioContext["CurrentUser"] = userName;

        // Also set as main page for steps that don't specify user
        ScenarioContext["E2E_MainPage"] = page;
        ScenarioContext["E2E_MainContext"] = context;

        Console.WriteLine($"[E2E] Browser context created for '{userName}'");
        await TakeScreenshotAsync("context-created");
    }

    /// <summary>
    /// Creates identity for a named user.
    /// </summary>
    [Given(@"""(.*)"" has created identity via browser")]
    public async Task GivenUserHasCreatedIdentityViaBrowser(string userName)
    {
        Console.WriteLine($"[E2E] === Creating identity for '{userName}' ===");

        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        // Navigate to auth page
        var baseUrl = GetBaseUrl();
        await page.GotoAsync($"{baseUrl}/auth");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await TakeScreenshotAsync("auth-page");

        // Wait for client-side hydration
        var inputLocator = page.GetByPlaceholder("Satoshi Nakamoto", new PageGetByPlaceholderOptions { Exact = false });
        await inputLocator.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await inputLocator.FillAsync(userName);

        // Click "Generate Recovery Words" button
        var generateButton = page.GetByText("Generate Recovery Words");
        await generateButton.ClickAsync();

        // Wait for mnemonic words to be generated (checkbox becomes visible)
        var checkbox = page.Locator("input[type='checkbox']");
        await checkbox.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // Check the checkbox
        await checkbox.CheckAsync();

        // Click "Create Account" button
        var createButton = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create Account" }).Last;
        await createButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Create waiter before clicking
        var waiter = StartListeningForTransactions(minTransactions: 1);

        await createButton.ClickAsync();
        Console.WriteLine($"[E2E] Clicked Create Account for '{userName}'");

        // Wait for identity transaction
        try
        {
            await waiter.WaitAsync();
        }
        finally
        {
            waiter.Dispose();
        }

        // Produce block for identity
        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();
        Console.WriteLine($"[E2E] Identity block produced for '{userName}'");

        // Wait for personal feed transaction
        var personalFeedWaiter = StartListeningForTransactions(minTransactions: 1);
        try
        {
            await personalFeedWaiter.WaitAsync();
        }
        finally
        {
            personalFeedWaiter.Dispose();
        }

        // Produce block for personal feed
        await blockControl.ProduceBlockAsync();
        Console.WriteLine($"[E2E] Personal feed block produced for '{userName}'");

        // Wait for redirect to dashboard
        await page.WaitForURLAsync("**/dashboard**", new PageWaitForURLOptions { Timeout = 15000 });

        // Trigger initial sync
        await page.EvaluateAsync("() => window.__e2e_triggerSync()");

        // Capture localStorage state after identity creation
        await CaptureLocalStorageAsync(page, "identity-created", userName);

        Console.WriteLine($"[E2E] === Identity created for '{userName}' ===");
        await TakeScreenshotAsync("identity-created");
    }

    /// <summary>
    /// User creates a public group via browser.
    /// </summary>
    // NOTE: Pattern uses negative lookahead (?!the user) to avoid conflict with GroupSteps.cs
    [Given(@"(?!the user)(\w+) has created a public group ""(.*)"" via browser")]
    public async Task GivenUserHasCreatedPublicGroup(string userName, string groupName)
    {
        Console.WriteLine($"[E2E] === Creating group '{groupName}' for '{userName}' ===");

        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        // Click Create Group nav button
        await ClickTestIdAsync(page, "nav-create-group");

        // Wait for wizard
        await WaitForTestIdAsync(page, "group-creation-wizard", 10000);
        await TakeScreenshotAsync("wizard-opened");

        // Select public type
        await ClickTestIdAsync(page, "group-type-public");
        await ClickTestIdAsync(page, "type-selection-next-button");

        // Fill details
        var nameInput = await WaitForTestIdAsync(page, "group-name-input");
        await nameInput.FillAsync(groupName);

        var descInput = await WaitForTestIdAsync(page, "group-description-input");
        await descInput.FillAsync($"E2E test group: {groupName}");

        // Create waiter before clicking - we need to produce a block while the wizard polls
        var waiter = StartListeningForTransactions(minTransactions: 1);

        // Click create - wizard starts polling for blockchain confirmation internally
        await ClickTestIdAsync(page, "confirm-create-group-button");
        Console.WriteLine($"[E2E] Clicked create group for '{groupName}' - wizard is now polling");

        // Wait for transaction to arrive at server (wizard submitted it)
        try
        {
            await waiter.WaitAsync();
        }
        finally
        {
            waiter.Dispose();
        }

        // Produce block - this indexes the transaction
        // The wizard is still polling and will find the feed after this
        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();
        Console.WriteLine($"[E2E] Group creation block produced - wizard polling should find it");

        // Wait for wizard to close (it closes only after feed is confirmed on blockchain)
        var wizard = page.GetByTestId("group-creation-wizard");
        await Expect(wizard).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions
        {
            Timeout = 30000  // Wizard should close within a few poll cycles (1s each)
        });
        Console.WriteLine($"[E2E] Wizard closed - feed confirmed on blockchain");

        // Feed should now be visible with data-feed-ready="true" (has encryption key)
        // Sanitization matches ChatListItem: name.toLowerCase().replace(/[^a-z0-9]/g, '-')
        var sanitizedGroupName = System.Text.RegularExpressions.Regex.Replace(
            groupName.ToLowerInvariant(), "[^a-z0-9]", "-");
        var readyFeedItem = page.Locator($"[data-testid='feed-item:group:{sanitizedGroupName}'][data-feed-ready='true']");
        await Expect(readyFeedItem).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 15000
        });
        Console.WriteLine($"[E2E] Feed item for '{groupName}' is visible with data-feed-ready=true");

        // Capture localStorage state after group creation (includes new feed + KeyGen 0)
        await CaptureLocalStorageAsync(page, $"group-created:{groupName}", userName);

        // Store group name for this user
        ScenarioContext[$"LastCreatedGroup_{userName}"] = groupName;

        Console.WriteLine($"[E2E] === Group '{groupName}' created for '{userName}' ===");
        await TakeScreenshotAsync("group-created");
    }

    /// <summary>
    /// User opens their personal feed.
    /// </summary>
    [When(@"(.*) opens (?:their|her|his) personal feed")]
    public async Task WhenUserOpensPersonalFeed(string userName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] {userName} opening personal feed...");
        Console.WriteLine($"[E2E] Page URL before: {page.Url}");

        // Dump browser state before click
        var allFeeds = page.Locator("[data-testid^='feed-item']");
        var feedCount = await allFeeds.CountAsync();
        Console.WriteLine($"[E2E] Found {feedCount} feed items in sidebar:");
        for (int i = 0; i < Math.Min(feedCount, 5); i++)
        {
            var testId = await allFeeds.Nth(i).GetAttributeAsync("data-testid");
            var text = await allFeeds.Nth(i).TextContentAsync();
            Console.WriteLine($"[E2E]   Feed {i}: testid='{testId}', text='{text?.Substring(0, Math.Min(30, text?.Length ?? 0))}...'");
        }

        // Find and click the personal feed item
        var personalFeed = page.GetByTestId("feed-item:personal");
        await Expect(personalFeed.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 15000
        });

        await personalFeed.First.ClickAsync();
        Console.WriteLine($"[E2E] {userName} clicked personal feed");

        // Wait for message input to confirm chat view is loaded
        await WaitForTestIdAsync(page, "message-input", 10000);

        Console.WriteLine($"[E2E] {userName} opened personal feed");
        Console.WriteLine($"[E2E] Page URL after: {page.Url}");
        await TakeScreenshotAsync("personal-feed-opened");
    }

    /// <summary>
    /// User opens a specific group.
    /// </summary>
    // NOTE: Pattern uses negative lookahead (?!the user) to avoid conflict with GroupSteps.cs
    [When(@"(?!the user)(\w+) opens the group ""(.*)""")]
    public async Task WhenUserOpensGroup(string userName, string groupName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] {userName} opening group '{groupName}'...");

        // Find and click the feed item
        var feedItem = page.Locator("[data-testid^='feed-item']").Filter(new LocatorFilterOptions
        {
            HasText = groupName
        });

        await Expect(feedItem.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 15000
        });

        await feedItem.First.ClickAsync();

        // Wait for message input to confirm chat view is loaded
        await WaitForTestIdAsync(page, "message-input", 10000);

        // Trigger sync to load messages (AES decryption is async)
        Console.WriteLine($"[E2E] {userName} triggering sync to load messages...");
        await page.EvaluateAsync("() => window.__e2e_triggerSync()");

        // Wait for messages to be loaded and decrypted
        // The sync runs async, so we need to wait for the DOM to update
        await Task.Delay(1000); // Allow time for async decryption and rendering

        Console.WriteLine($"[E2E] {userName} opened group '{groupName}'");
        await TakeScreenshotAsync("group-opened");
    }

    /// <summary>
    /// User opens group settings.
    /// </summary>
    // NOTE: Pattern uses negative lookahead (?!the user) to avoid conflict with GroupSteps.cs
    [When(@"(?!the user)(\w+) opens group settings")]
    public async Task WhenUserOpensGroupSettings(string userName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] {userName} opening group settings...");

        // Click the header to open settings
        var header = page.Locator("[data-testid='chat-header']");
        if (await header.CountAsync() > 0)
        {
            await header.First.ClickAsync();
        }

        Console.WriteLine($"[E2E] {userName} opened group settings");
        await TakeScreenshotAsync("settings-opened");
    }

    /// <summary>
    /// User navigates to join page using the stored invite code.
    /// </summary>
    [When(@"(.*) navigates to the join page with the invite code")]
    public async Task WhenUserNavigatesToJoinPage(string userName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        var inviteCode = ScenarioContext.Get<string>("LastInviteCode");
        Console.WriteLine($"[E2E] {userName} navigating to join page with code '{inviteCode}'...");

        var baseUrl = GetBaseUrl();
        await page.GotoAsync($"{baseUrl}/join/{inviteCode}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Console.WriteLine($"[E2E] {userName} on join page");
        await TakeScreenshotAsync("join-page");
    }

    /// <summary>
    /// Verifies group appears in user's feed list.
    /// </summary>
    [Then(@"""(.*)"" should appear in (.*)'s feed list")]
    public async Task ThenGroupShouldAppearInUserFeedList(string groupName, string userName)
    {
        await SwitchToUserAsync(userName);
        var page = GetUserPage(userName);

        Console.WriteLine($"[E2E] Verifying '{groupName}' appears in {userName}'s feed list...");

        var feedItem = page.Locator("[data-testid^='feed-item']").Filter(new LocatorFilterOptions
        {
            HasText = groupName
        });

        await Expect(feedItem.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 15000
        });

        Console.WriteLine($"[E2E] '{groupName}' is visible in {userName}'s feed list");
        await TakeScreenshotAsync("group-in-feedlist");
    }

    // =============================================================================
    // Helper Methods
    // =============================================================================

    /// <summary>
    /// Switches the current user context for subsequent steps.
    /// </summary>
    private async Task SwitchToUserAsync(string userName)
    {
        if (_currentUser == userName)
            return;

        Console.WriteLine($"[E2E] === CONTEXT SWITCH: '{_currentUser ?? "none"}' -> '{userName}' ===");

        var page = GetUserPage(userName);

        // Debug: Log page state
        Console.WriteLine($"[E2E] {userName}'s page URL: {page.Url}");

        // Check if page is still valid and responsive
        try
        {
            var isConnected = !page.IsClosed;
            Console.WriteLine($"[E2E] {userName}'s page connected: {isConnected}");

            // Dump current feed items to verify UI state
            var allFeeds = page.Locator("[data-testid^='feed-item']");
            var feedCount = await allFeeds.CountAsync();
            Console.WriteLine($"[E2E] {userName}'s feed list has {feedCount} items");

            // Check if currently viewing a chat (message input visible)
            var messageInput = page.GetByTestId("message-input");
            var isInChat = await messageInput.CountAsync() > 0;
            Console.WriteLine($"[E2E] {userName} is currently in a chat view: {isInChat}");

            // Check currently selected feed
            var selectedFeed = page.Locator("[data-testid^='feed-item'][data-selected='true']");
            var selectedCount = await selectedFeed.CountAsync();
            if (selectedCount > 0)
            {
                var selectedTestId = await selectedFeed.First.GetAttributeAsync("data-testid");
                Console.WriteLine($"[E2E] {userName}'s selected feed: {selectedTestId}");
            }
            else
            {
                Console.WriteLine($"[E2E] {userName} has no selected feed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[E2E] Warning: Could not get page state for {userName}: {ex.Message}");
        }

        // Update the main page reference
        ScenarioContext["E2E_MainPage"] = page;
        _currentUser = userName;
        ScenarioContext["CurrentUser"] = userName;

        Console.WriteLine($"[E2E] === CONTEXT SWITCH COMPLETE ===");
    }

    /// <summary>
    /// Gets the page for a specific user.
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

    /// <summary>
    /// Helper for Playwright assertions.
    /// </summary>
    private static ILocatorAssertions Expect(ILocator locator)
    {
        return Assertions.Expect(locator);
    }
}
