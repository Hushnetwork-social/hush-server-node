using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for FEAT-058: Message retry and status icon verification.
/// Tests the visual feedback for message delivery status (pending, confirming, confirmed, failed).
/// </summary>
[Binding]
internal sealed class MessageRetrySteps : BrowserStepsBase
{
    public MessageRetrySteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    /// <summary>
    /// Verifies that a message shows the pending status icon (Clock).
    /// The pending icon has data-testid="message-pending".
    /// </summary>
    [Then(@"the message ""(.*)"" should show pending status icon")]
    public async Task ThenMessageShouldShowPendingStatusIcon(string messageText)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E Retry] Looking for pending status icon on message: '{messageText}'");

        // Find the message container that contains the message text
        var messageContainer = await FindMessageContainerByTextAsync(page, messageText);

        // Look for the pending status icon within this message
        var pendingIcon = messageContainer.Locator("[data-testid='message-pending']");

        await Expect(pendingIcon).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });

        Console.WriteLine($"[E2E Retry] Found pending icon for message: '{messageText}'");
    }

    /// <summary>
    /// Verifies that a message shows the confirmed status icon (Check).
    /// The confirmed icon has data-testid="message-confirmed".
    /// </summary>
    [Then(@"the message ""(.*)"" should show confirmed status icon")]
    public async Task ThenMessageShouldShowConfirmedStatusIcon(string messageText)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E Retry] Looking for confirmed status icon on message: '{messageText}'");

        // Find the message container that contains the message text
        var messageContainer = await FindMessageContainerByTextAsync(page, messageText, timeoutMs: 20000);

        // Look for the confirmed status icon within this message
        var confirmedIcon = messageContainer.Locator("[data-testid='message-confirmed']");

        // Wait with retry for the icon to appear (may need to wait for sync cycle)
        await Expect(confirmedIcon).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 15000
        });

        Console.WriteLine($"[E2E Retry] Found confirmed icon for message: '{messageText}'");
    }

    /// <summary>
    /// Verifies that a message shows the failed status icon (AlertTriangle).
    /// The failed icon has data-testid="message-failed".
    /// </summary>
    [Then(@"the message ""(.*)"" should show failed status icon")]
    public async Task ThenMessageShouldShowFailedStatusIcon(string messageText)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E Retry] Looking for failed status icon on message: '{messageText}'");

        // Find the message container that contains the message text
        var messageContainer = await FindMessageContainerByTextAsync(page, messageText);

        // Look for the failed status icon within this message
        var failedIcon = messageContainer.Locator("[data-testid='message-failed']");

        await Expect(failedIcon).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });

        Console.WriteLine($"[E2E Retry] Found failed icon for message: '{messageText}'");
    }

    /// <summary>
    /// Verifies that a message has a timestamp displayed.
    /// Timestamps are shown when status is 'confirmed' or 'confirming'.
    /// </summary>
    [Then(@"the message should have a timestamp displayed")]
    public async Task ThenMessageShouldHaveTimestamp()
    {
        var page = await GetOrCreatePageAsync();

        // Get the last sent message from ScenarioContext
        var messageText = ScenarioContext["LastSentMessage"] as string;
        messageText.Should().NotBeNullOrEmpty("A message must have been sent before checking timestamp");

        // Find the message container
        var messageContainer = await FindMessageContainerByTextAsync(page, messageText!);

        // Look for a timestamp (typically in format HH:MM)
        // The timestamp is displayed in a span with text matching time format
        var timestampRegex = @"\d{1,2}:\d{2}";
        var content = await messageContainer.TextContentAsync();

        content.Should().NotBeNull("Message container should have content");
        System.Text.RegularExpressions.Regex.IsMatch(content!, timestampRegex)
            .Should().BeTrue($"Message should contain a timestamp (e.g., 12:34). Found: {content}");

        Console.WriteLine($"[E2E Retry] Found timestamp in message content");
    }

    /// <summary>
    /// Clicks on the failed status icon to trigger manual retry.
    /// </summary>
    [When(@"the user clicks on the failed message icon for ""(.*)""")]
    public async Task WhenUserClicksOnFailedMessageIcon(string messageText)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E Retry] Clicking failed icon to retry message: '{messageText}'");

        // Find the message container
        var messageContainer = await FindMessageContainerByTextAsync(page, messageText);

        // Find and click the failed icon
        var failedIcon = messageContainer.Locator("[data-testid='message-failed']");
        await failedIcon.ClickAsync();

        Console.WriteLine($"[E2E Retry] Clicked retry icon for message: '{messageText}'");

        // Brief delay for the retry to start
        await Task.Delay(500);
    }

    /// <summary>
    /// Verifies that no status icon is shown for a specific message.
    /// This is used to verify status icons only appear for own messages.
    /// </summary>
    [Then(@"the message ""(.*)"" should not show any status icon")]
    public async Task ThenMessageShouldNotShowAnyStatusIcon(string messageText)
    {
        var page = await GetOrCreatePageAsync();

        // Find the message container
        var messageContainer = await FindMessageContainerByTextAsync(page, messageText);

        // Check that no status icons are visible
        var pendingIcon = messageContainer.Locator("[data-testid='message-pending']");
        var confirmingIcon = messageContainer.Locator("[data-testid='message-confirming']");
        var confirmedIcon = messageContainer.Locator("[data-testid='message-confirmed']");
        var failedIcon = messageContainer.Locator("[data-testid='message-failed']");

        await Expect(pendingIcon).ToHaveCountAsync(0);
        await Expect(confirmingIcon).ToHaveCountAsync(0);
        await Expect(confirmedIcon).ToHaveCountAsync(0);
        await Expect(failedIcon).ToHaveCountAsync(0);

        Console.WriteLine($"[E2E Retry] Verified no status icons for message: '{messageText}'");
    }

    /// <summary>
    /// Waits for the sync cycle to complete (triggered manually or automatic 3s interval).
    /// </summary>
    [When(@"the sync cycle completes")]
    public async Task WhenTheSyncCycleCompletes()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E Retry] Waiting for sync cycle to complete...");

        // Trigger manual sync
        await TriggerSyncAsync(page);

        // Wait a moment for the sync to process
        await Task.Delay(1000);

        Console.WriteLine("[E2E Retry] Sync cycle completed");
    }

    /// <summary>
    /// Finds a message container by its text content.
    /// </summary>
    private async Task<ILocator> FindMessageContainerByTextAsync(IPage page, string messageText, int timeoutMs = 10000)
    {
        // Find the message container (data-testid="message") that contains the message content
        var messages = page.Locator("[data-testid='message']");

        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var count = await messages.CountAsync();
            for (int i = 0; i < count; i++)
            {
                var message = messages.Nth(i);
                var content = await message.TextContentAsync();
                if (content?.Contains(messageText) == true)
                {
                    return message;
                }
            }
            await Task.Delay(200);
        }

        // Take screenshot for debugging
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = $"e2e-message-not-found-{DateTime.Now:HHmmss}.png" });
        throw new TimeoutException($"Message container not found for text: '{messageText}' within {timeoutMs}ms");
    }

    /// <summary>
    /// Helper method for Playwright assertions.
    /// </summary>
    private static ILocatorAssertions Expect(ILocator locator)
    {
        return Assertions.Expect(locator);
    }
}
