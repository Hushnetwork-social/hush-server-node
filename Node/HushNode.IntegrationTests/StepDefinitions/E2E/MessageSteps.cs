using FluentAssertions;
using HushServerNode;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for sending and verifying messages.
/// </summary>
[Binding]
internal sealed class MessageSteps : BrowserStepsBase
{
    public MessageSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [When(@"the user sends message ""(.*)""")]
    public async Task WhenTheUserSendsMessage(string message)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E] Sending message: '{message}'");

        // Wait for message input to be available
        var messageInput = await WaitForTestIdAsync(page, "message-input");

        // Fill message input
        await messageInput.FillAsync(message);

        // Find and click send button
        var sendButton = await WaitForVisibleElementAsync(page, "send-button");
        var isDisabled = await sendButton.IsDisabledAsync();

        if (isDisabled)
        {
            // Take screenshot for debugging
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = "e2e-send-disabled.png" });
            throw new InvalidOperationException("Send button is disabled - feed may not have encryption key ready");
        }

        // Start listening for the transaction BEFORE clicking send
        // Store in ScenarioContext so "the transaction is processed" step can await it
        var waiter = StartListeningForTransactions(1);
        ScenarioContext["PendingTransactionWaiter"] = waiter;

        await sendButton.ClickAsync();
        Console.WriteLine("[E2E] Clicked send button, waiter is listening");

        // Brief delay for the async submission to start
        await Task.Delay(500);

        // Store message for later verification
        ScenarioContext["LastSentMessage"] = message;
    }

    /// <summary>
    /// Waits for a visible element with the specified test ID.
    /// </summary>
    private async Task<ILocator> WaitForVisibleElementAsync(IPage page, string testId, int timeoutMs = 10000)
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
            await Task.Delay(100);
        }

        throw new TimeoutException($"No visible element found with data-testid='{testId}' within {timeoutMs}ms");
    }

    [Given(@"the user has sent message ""(.*)"" to their personal feed")]
    public async Task GivenTheUserHasSentMessage(string message)
    {
        var page = await GetOrCreatePageAsync();

        // Navigate to dashboard if not already there
        if (!page.Url.Contains("/dashboard"))
        {
            await NavigateToAsync(page, "/dashboard");
            await WaitForNetworkIdleAsync(page);
        }

        // Click on personal feed using semantic testid
        // Personal feeds have data-testid="feed-item:personal"
        var personalFeed = await WaitForVisibleFeedAsync(page, "feed-item:personal", 30000);
        await personalFeed.ClickAsync();

        // Wait for message input
        await WaitForTestIdAsync(page, "message-input", 15000);

        // Send the message - this creates the waiter and stores it in ScenarioContext
        await WhenTheUserSendsMessage(message);

        // Retrieve waiter from ScenarioContext and wait for transaction + produce block
        if (ScenarioContext.TryGetValue("PendingTransactionWaiter", out var waiterObj)
            && waiterObj is HushServerNodeCore.TransactionWaiter waiter)
        {
            ScenarioContext.Remove("PendingTransactionWaiter");
            try
            {
                await AwaitTransactionsAndProduceBlockAsync(waiter);
            }
            finally
            {
                waiter.Dispose();
            }
        }
        else
        {
            throw new InvalidOperationException("No PendingTransactionWaiter found in ScenarioContext after sending message");
        }
    }

    /// <summary>
    /// Waits for a visible feed item with the specified test ID that is also ready for messaging.
    /// Handles responsive layouts where feeds appear in both sidebar and main content.
    /// Ready means the feed's encryption key has been decrypted (data-feed-ready="true").
    /// </summary>
    private async Task<ILocator> WaitForVisibleFeedAsync(IPage page, string testId, int timeoutMs = 10000)
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
                    // Also check if feed is ready (has encryption key decrypted)
                    var readyAttr = await feed.GetAttributeAsync("data-feed-ready");
                    if (readyAttr == "true")
                    {
                        return feed;
                    }
                    Console.WriteLine($"[E2E] Found visible feed {testId} but not ready yet (data-feed-ready={readyAttr})");
                }
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"No visible ready feed found with data-testid='{testId}' within {timeoutMs}ms");
    }

    [Then(@"the message ""(.*)"" should be visible in the chat")]
    public async Task ThenMessageShouldBeVisible(string messageText)
    {
        var page = await GetOrCreatePageAsync();

        // Wait for message to appear (with retry for sync timing)
        var messageContent = page.GetByTestId("message-content").Filter(new LocatorFilterOptions
        {
            HasText = messageText
        });

        // Use Playwright's auto-retry mechanism
        await Expect(messageContent.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 15000
        });
    }

    [Then(@"the message should show ""(.*)"" as the sender")]
    public async Task ThenMessageShouldShowSender(string senderName)
    {
        var page = await GetOrCreatePageAsync();
        var lastMessage = ScenarioContext["LastSentMessage"] as string;

        lastMessage.Should().NotBeNullOrEmpty("A message must have been sent before checking sender");

        // Find the message container that contains the message text
        var message = page.GetByTestId("message").Filter(new LocatorFilterOptions
        {
            HasText = lastMessage
        });

        // Verify message container is visible
        await Expect(message.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 10000
        });

        // Verify sender name is visible somewhere on the page (in header, message, etc.)
        // For personal feeds, the user's own name should be visible in the UI
        var senderLocator = page.GetByText(senderName);
        await Expect(senderLocator.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });
    }

    /// <summary>
    /// Helper method for Playwright assertions.
    /// </summary>
    private static ILocatorAssertions Expect(ILocator locator)
    {
        return Assertions.Expect(locator);
    }
}
