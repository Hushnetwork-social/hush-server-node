using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for mention E2E interactions in group feeds.
/// Handles sending messages with @mentions and verifying mention rendering.
/// </summary>
[Binding]
internal sealed class MentionSteps : BrowserStepsBase
{
    public MentionSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    /// <summary>
    /// Sends a message mentioning another user using the MentionOverlay.
    /// Types @ to trigger the overlay, selects the user, types the rest of the message.
    /// </summary>
    [When(@"(\w+) sends message mentioning ""(.*)"" with ""(.*)"" and waits for confirmation")]
    public async Task WhenUserSendsMentionMessage(string userName, string mentionedUser, string messageText)
    {
        var page = GetPageForUser(userName);

        Console.WriteLine($"[E2E] {userName} sending message mentioning '{mentionedUser}' with '{messageText}'...");

        // 1. Focus message input
        var messageInput = await WaitForTestIdAsync(page, "message-input");
        await messageInput.ClickAsync();

        // 2. Type @ to trigger the MentionOverlay
        await messageInput.PressAsync("@");

        // 3. Wait for the MentionOverlay to appear (role="dialog")
        var overlay = page.Locator("[role='dialog'][aria-label='Select participant to mention']");
        await Expect(overlay).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });
        Console.WriteLine("[E2E] MentionOverlay visible");

        // 4. Type first few characters to filter to the target user
        var filterChars = mentionedUser.Length >= 3
            ? mentionedUser.Substring(0, 3)
            : mentionedUser;
        await messageInput.PressSequentiallyAsync(filterChars);
        Console.WriteLine($"[E2E] Typed filter '{filterChars}' to narrow mention results");

        // 5. Wait briefly for filter to apply
        await Task.Delay(300);

        // 6. Click matching participant in the mention listbox
        var listbox = page.Locator("#mention-listbox");
        var matchingOption = listbox.Locator("[role='option']")
            .Filter(new LocatorFilterOptions { HasText = mentionedUser });
        await Expect(matchingOption.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });
        await matchingOption.First.ClickAsync();
        Console.WriteLine($"[E2E] Selected mention '{mentionedUser}' from overlay");

        // 7. Wait for React to process the mention selection state update.
        // handleMentionSelect calls setMessage() which replaces "@Bob" with "@[Bob](id) "
        // and uses requestAnimationFrame to position the cursor. We must wait for both
        // the re-render and the cursor positioning before typing.
        await Expect(overlay).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions
        {
            Timeout = 5000
        });
        // Allow requestAnimationFrame to fire for cursor positioning
        await Task.Delay(300);

        var inputValue = await messageInput.InputValueAsync();
        Console.WriteLine($"[E2E] Input value after mention selection: '{inputValue}'");

        // 8. Type the rest of the message after the mention
        // After selecting a mention, the input should have "@[DisplayName](identityId) " already inserted
        // We just need to type the remaining message text
        await messageInput.PressSequentiallyAsync(messageText);

        inputValue = await messageInput.InputValueAsync();
        Console.WriteLine($"[E2E] Input value after typing message: '{inputValue}'");

        // 9. Start listening for transaction BEFORE clicking send
        var waiter = StartListeningForTransactions(minTransactions: 1);

        // 10. Click send button
        await ClickTestIdAsync(page, "send-button");
        Console.WriteLine("[E2E] Sent mention message, waiting for transaction...");

        // 11. Wait for transaction and produce block
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

        // 12. Trigger sync
        await page.EvaluateAsync("() => window.__e2e_triggerSync()");

        // 13. Wait for message to show confirmed status
        // The mention message renders as: <MentionText>@Bob</MentionText> what do you think?
        // HasText does substring matching so "what do you think?" matches
        var sentMessage = page.Locator("[data-testid='message']")
            .Filter(new LocatorFilterOptions { HasText = messageText });

        // Debug: log how many matching messages we find
        var matchCount = await sentMessage.CountAsync();
        Console.WriteLine($"[E2E] Found {matchCount} messages matching '{messageText}'");

        await sentMessage.First.Locator("[data-testid='message-confirmed']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        Console.WriteLine($"[E2E] {userName} mention message confirmed: '@{mentionedUser} {messageText}'");
    }

    /// <summary>
    /// Verifies that a mention is rendered as a clickable element within a message.
    /// MentionText renders as: <span role="button">@{displayName}</span>
    /// </summary>
    [Then(@"(\w+) should see mention ""@(.*)"" in message containing ""(.*)""")]
    public async Task ThenUserShouldSeeMentionInMessage(string userName, string mentionedName, string messageText)
    {
        var page = GetPageForUser(userName);

        Console.WriteLine($"[E2E] Verifying mention '@{mentionedName}' in message containing '{messageText}' for {userName}...");

        // 1. Find the message bubble containing the message text
        var messageBubble = page.Locator("[data-testid='message']")
            .Filter(new LocatorFilterOptions { HasText = messageText });

        await Expect(messageBubble.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 10000
        });

        // 2. Within that message, find the MentionText element (span with role="button" containing @displayName)
        var mentionElement = messageBubble.First
            .Locator("[role='button']")
            .Filter(new LocatorFilterOptions { HasText = $"@{mentionedName}" });

        await Expect(mentionElement.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });

        Console.WriteLine($"[E2E] Verified: mention '@{mentionedName}' rendered in message containing '{messageText}'");
    }

    /// <summary>
    /// Verifies that a notification toast is visible showing the group name and formatted @mention.
    /// The toast is produced by the gRPC notification stream when a new message arrives.
    /// Toast structure: sender name | "in {groupName}" | message preview with @mentions.
    /// In E2E mode, toasts persist for 30s (vs 5s in production) so screenshots capture them.
    /// </summary>
    [Then(@"(\w+) should see a notification toast from ""(.*)"" in group ""(.*)"" with ""(.*)""")]
    public async Task ThenUserShouldSeeNotificationToast(string userName, string senderName, string groupName, string mentionText)
    {
        var page = GetPageForUser(userName);

        Console.WriteLine($"[E2E] Verifying notification toast for {userName}: from '{senderName}' in '{groupName}' containing '{mentionText}'...");

        // Wait for a toast element that contains the group name.
        // The InAppToast renders "in {groupName}" as a <p> element.
        // Use a broad selector that searches the whole page for text "in {groupName}"
        // within the fixed toast container area.
        var toastWithGroup = page.Locator("div")
            .Filter(new LocatorFilterOptions { HasText = $"in {groupName}" })
            .Filter(new LocatorFilterOptions { HasText = senderName });

        await Expect(toastWithGroup.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 15000
        });
        Console.WriteLine($"[E2E] Toast with 'in {groupName}' from '{senderName}' is visible");

        // Verify the mention text is displayed (e.g., "@Bob" in bold, not raw "@[Bob](id)")
        var mentionElement = toastWithGroup.First.Locator("span")
            .Filter(new LocatorFilterOptions { HasText = mentionText });
        await Expect(mentionElement.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        Console.WriteLine($"[E2E] Toast mention '{mentionText}' rendered correctly");

        // Verify the toast does NOT contain raw mention format (e.g., no "(03c3a..." identity IDs)
        var toastText = await toastWithGroup.First.InnerTextAsync();
        if (toastText.Contains("]("))
        {
            throw new Exception($"Toast still contains raw mention format: {toastText}");
        }
        Console.WriteLine($"[E2E] Toast text verified (no raw mention format): '{toastText}'");
    }

    /// <summary>
    /// Verifies that the mention badge ("@") is visible on a group feed item in the feed list.
    /// MentionBadge renders as: <span role="status" aria-label="Unread mentions">@</span>
    /// </summary>
    [Then(@"(\w+) should see mention badge on group ""(.*)""")]
    public async Task ThenUserShouldSeeMentionBadgeOnGroup(string userName, string groupName)
    {
        var page = GetPageForUser(userName);

        var sanitizedGroup = SanitizeName(groupName);
        var testId = $"feed-item:group:{sanitizedGroup}";

        Console.WriteLine($"[E2E] Verifying {userName} sees mention badge '@' on group '{groupName}' (testId={testId})...");

        var feedItem = page.GetByTestId(testId);
        await Expect(feedItem.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // MentionBadge uses role="status" with aria-label="Unread mentions"
        var mentionBadge = feedItem.First.Locator("[role='status']");
        await Expect(mentionBadge).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        var badgeText = await mentionBadge.TextContentAsync();
        Console.WriteLine($"[E2E] {userName} sees mention badge '{badgeText}' on group '{groupName}'");
    }

    /// <summary>
    /// Verifies that the mention badge ("@") is NOT visible on a group feed item.
    /// Used to confirm mention badge clears after reading.
    /// </summary>
    [Then(@"(\w+) should NOT see mention badge on group ""(.*)""")]
    public async Task ThenUserShouldNotSeeMentionBadgeOnGroup(string userName, string groupName)
    {
        var page = GetPageForUser(userName);

        var sanitizedGroup = SanitizeName(groupName);
        var testId = $"feed-item:group:{sanitizedGroup}";

        Console.WriteLine($"[E2E] Verifying {userName} does NOT see mention badge on group '{groupName}'...");

        var feedItem = page.GetByTestId(testId);
        await Expect(feedItem.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        var mentionBadge = feedItem.First.Locator("[role='status']");
        await Expect(mentionBadge).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });

        Console.WriteLine($"[E2E] {userName} does NOT see mention badge on group '{groupName}'");
    }

    /// <summary>
    /// Sanitizes a name for use in test IDs (lowercase, non-alphanumeric → hyphens).
    /// </summary>
    private static string SanitizeName(string name)
    {
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            name.ToLowerInvariant(), "[^a-z0-9]", "-");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "-+", "-");
        return sanitized.Trim('-');
    }

    /// <summary>
    /// Gets the page for a specific user from ScenarioContext.
    /// </summary>
    private IPage GetPageForUser(string userName)
    {
        var key = $"E2E_Page_{userName}";
        if (ScenarioContext.TryGetValue(key, out var pageObj) && pageObj is IPage page)
        {
            ScenarioContext["E2E_MainPage"] = page;
            ScenarioContext["CurrentUser"] = userName;
            return page;
        }

        throw new InvalidOperationException($"No browser page found for user '{userName}'");
    }

    private static ILocatorAssertions Expect(ILocator locator)
    {
        return Assertions.Expect(locator);
    }
}
