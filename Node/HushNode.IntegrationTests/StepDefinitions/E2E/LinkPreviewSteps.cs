using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for link preview E2E interactions.
/// Verifies LinkPreviewCard rendering, LinkPreviewCarousel navigation,
/// and notification toast absence of rich preview metadata.
/// </summary>
[Binding]
internal sealed class LinkPreviewSteps : BrowserStepsBase
{
    public LinkPreviewSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    /// <summary>
    /// Verifies that a link preview card is visible within a message.
    /// LinkPreviewCard renders as: role="button" aria-label="Link preview: {title} from {domain}"
    /// </summary>
    [Then(@"(\w+) should see a link preview in message containing ""(.*)""")]
    public async Task ThenUserShouldSeeLinkPreviewInMessage(string userName, string messageText)
    {
        var page = GetPageForUser(userName);

        Console.WriteLine($"[E2E] Verifying link preview in message containing '{messageText}' for {userName}...");

        // Find the message bubble containing the text
        var messageBubble = page.Locator("[data-testid='message']")
            .Filter(new LocatorFilterOptions { HasText = messageText });

        await Expect(messageBubble.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 10000
        });

        // Within that message, find the LinkPreviewCard (role="button" with aria-label starting with "Link preview:")
        var linkPreviewDirect = messageBubble.First
            .Locator("[role='button'][aria-label^='Link preview:']");

        // Wait up to 20s for metadata fetch from external URL
        await Expect(linkPreviewDirect.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 20000
        });

        var ariaLabel = await linkPreviewDirect.First.GetAttributeAsync("aria-label");
        Console.WriteLine($"[E2E] Link preview visible: {ariaLabel}");
    }

    /// <summary>
    /// Verifies that a message contains N link previews displayed in a carousel.
    /// LinkPreviewCarousel renders as: role="region" aria-label="Link previews, showing N of {count}"
    /// </summary>
    [Then(@"(\w+) should see (\d+) link previews in message containing ""(.*)""")]
    public async Task ThenUserShouldSeeMultipleLinkPreviewsInMessage(string userName, int count, string messageText)
    {
        var page = GetPageForUser(userName);

        Console.WriteLine($"[E2E] Verifying {count} link previews in message containing '{messageText}' for {userName}...");

        // Find the message bubble containing the text
        var messageBubble = page.Locator("[data-testid='message']")
            .Filter(new LocatorFilterOptions { HasText = messageText });

        await Expect(messageBubble.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 10000
        });

        // First wait for the link preview card to load (no skeleton)
        var linkPreview = messageBubble.First
            .Locator("[role='button'][aria-label^='Link preview:']");

        await Expect(linkPreview.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 20000
        });

        // Now check the carousel region which includes "of {count}" in its aria-label
        var carousel = messageBubble.First
            .Locator($"[role='region'][aria-label*='of {count}']");

        await Expect(carousel.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });

        // Verify the page indicator shows "1 / {count}"
        var pageIndicator = carousel.First.Locator($"text=1 / {count}");
        await Expect(pageIndicator).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });

        var ariaLabel = await carousel.First.GetAttributeAsync("aria-label");
        Console.WriteLine($"[E2E] Carousel visible: {ariaLabel}");
    }

    /// <summary>
    /// Clicks the "Next link preview" button in a carousel within a message.
    /// Uses DispatchEvent to bypass potential overlay interference from message action buttons
    /// (reply/react icons) that appear near the carousel navigation arrows.
    /// </summary>
    [When(@"(\w+) clicks next link preview in message containing ""(.*)""")]
    public async Task WhenUserClicksNextLinkPreview(string userName, string messageText)
    {
        var page = GetPageForUser(userName);

        Console.WriteLine($"[E2E] {userName} clicking next link preview in message containing '{messageText}'...");

        var messageBubble = page.Locator("[data-testid='message']")
            .Filter(new LocatorFilterOptions { HasText = messageText });

        var nextButton = messageBubble.First.Locator("[aria-label='Next link preview']");
        await Expect(nextButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });

        // Use DispatchEvent instead of ClickAsync to bypass message action button overlay
        await nextButton.DispatchEventAsync("click");

        // Wait for carousel state update
        await Task.Delay(500);

        Console.WriteLine($"[E2E] Clicked next link preview button");
    }

    /// <summary>
    /// Verifies the carousel page indicator shows the expected position (e.g., "2 of 2").
    /// </summary>
    [Then(@"(\w+) should see link preview (\d+) of (\d+) in message containing ""(.*)""")]
    public async Task ThenUserShouldSeeLinkPreviewPosition(string userName, int current, int total, string messageText)
    {
        var page = GetPageForUser(userName);

        Console.WriteLine($"[E2E] Verifying link preview {current} of {total} in message containing '{messageText}' for {userName}...");

        var messageBubble = page.Locator("[data-testid='message']")
            .Filter(new LocatorFilterOptions { HasText = messageText });

        // Find the page indicator text "{current} / {total}"
        var pageIndicator = messageBubble.First.Locator($"text={current} / {total}");
        await Expect(pageIndicator).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });

        Console.WriteLine($"[E2E] Verified: showing link preview {current} / {total}");
    }

    /// <summary>
    /// Verifies that a carousel navigation button (previous/next) is disabled or enabled.
    /// The carousel buttons use the HTML disabled attribute when at the start/end of the list.
    /// </summary>
    [Then(@"(\w+) should see (previous|next) link preview button (disabled|enabled) in message containing ""(.*)""")]
    public async Task ThenUserShouldSeeLinkPreviewButtonState(string userName, string direction, string expectedState, string messageText)
    {
        var page = GetPageForUser(userName);
        var ariaLabel = direction == "previous" ? "Previous link preview" : "Next link preview";

        Console.WriteLine($"[E2E] Verifying {direction} link preview button is {expectedState} in message containing '{messageText}' for {userName}...");

        var messageBubble = page.Locator("[data-testid='message']")
            .Filter(new LocatorFilterOptions { HasText = messageText });

        var button = messageBubble.First.Locator($"[aria-label='{ariaLabel}']");
        await Expect(button).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });

        if (expectedState == "disabled")
        {
            await Expect(button).ToBeDisabledAsync(new LocatorAssertionsToBeDisabledOptions
            {
                Timeout = 5000
            });
        }
        else
        {
            await Expect(button).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions
            {
                Timeout = 5000
            });
        }

        Console.WriteLine($"[E2E] Verified: {direction} link preview button is {expectedState}");
    }

    /// <summary>
    /// Verifies that the notification toast area does NOT contain link preview metadata cards.
    /// Toasts should show the raw URL text, not rich LinkPreviewCard elements.
    /// </summary>
    [Then(@"the notification toast should not contain link preview metadata")]
    public async Task ThenNotificationToastShouldNotContainLinkPreviewMetadata()
    {
        // Get the current page (from most recently used user context)
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E] Verifying notification toast does NOT contain link preview metadata...");

        // The toast container is a fixed div at top-right
        var toastContainer = page.Locator("div.fixed");

        await Expect(toastContainer.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 5000
        });

        // Assert that no LinkPreviewCard elements exist inside the toast container.
        // LinkPreviewCard renders with role="button" and aria-label starting with "Link preview:"
        var linkPreviewInToast = toastContainer.First
            .Locator("[role='button'][aria-label^='Link preview:']");

        var count = await linkPreviewInToast.CountAsync();
        if (count > 0)
        {
            var ariaLabel = await linkPreviewInToast.First.GetAttributeAsync("aria-label");
            throw new Exception($"Notification toast should NOT contain link preview cards, but found {count}: {ariaLabel}");
        }

        Console.WriteLine("[E2E] Verified: notification toast contains no link preview metadata");
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
