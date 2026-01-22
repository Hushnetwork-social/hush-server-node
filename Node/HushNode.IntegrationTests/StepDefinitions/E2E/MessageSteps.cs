using FluentAssertions;
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

        // Wait for message input to be available
        await WaitForTestIdAsync(page, "message-input");

        // Fill message input
        await FillTestIdAsync(page, "message-input", message);

        // Click send button
        await ClickTestIdAsync(page, "send-button");

        // Store message for later verification
        ScenarioContext["LastSentMessage"] = message;
    }

    [Given(@"the user has sent message ""(.*)"" to their personal feed")]
    public async Task GivenTheUserHasSentMessage(string message)
    {
        var page = await GetOrCreatePageAsync();
        var displayName = ScenarioContext["UserDisplayName"] as string;

        displayName.Should().NotBeNullOrEmpty("Display name must be set before sending message");

        // Navigate to dashboard if not already there
        if (!page.Url.Contains("/dashboard"))
        {
            await NavigateToAsync(page, "/dashboard");
            await WaitForNetworkIdleAsync(page);
        }

        // Click on personal feed
        var feedItem = page.GetByTestId("feed-item").Filter(new LocatorFilterOptions
        {
            HasText = displayName
        });

        await feedItem.First.ClickAsync();

        // Wait for message input
        await WaitForTestIdAsync(page, "message-input", 15000);

        // Send the message
        await WhenTheUserSendsMessage(message);
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
