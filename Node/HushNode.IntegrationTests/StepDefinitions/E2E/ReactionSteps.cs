using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for adding and verifying reactions.
/// </summary>
[Binding]
internal sealed class ReactionSteps : BrowserStepsBase
{
    // The emojis in the same order as the client's EMOJIS constant
    private static readonly string[] Emojis = ["👍", "❤️", "😂", "😮", "😢", "😡"];

    public ReactionSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [When(@"the user adds reaction (\d+) to the message ""(.*)""")]
    public async Task WhenTheUserAddsReaction(int emojiIndex, string messageText)
    {
        emojiIndex.Should().BeInRange(0, Emojis.Length - 1, "Emoji index must be valid");

        var page = await GetOrCreatePageAsync();
        var emoji = Emojis[emojiIndex];

        // Find the message container
        var message = page.GetByTestId("message").Filter(new LocatorFilterOptions
        {
            HasText = messageText
        });

        // Hover over the message to reveal the reaction button
        await message.First.HoverAsync();

        // Small delay to let hover UI appear
        await Task.Delay(300);

        // Click add reaction button
        await ClickTestIdAsync(page, "add-reaction-button");

        // Wait for the emoji picker to appear and click the specific emoji
        var emojiTestId = $"emoji-{emoji}";
        await WaitForTestIdAsync(page, emojiTestId);
        await ClickTestIdAsync(page, emojiTestId);

        // Store for verification
        ScenarioContext["LastReactionEmojiIndex"] = emojiIndex;
        ScenarioContext["LastReactionEmoji"] = emoji;
        ScenarioContext["LastReactionMessage"] = messageText;
    }

    [Then(@"the message ""(.*)"" should show a reaction badge")]
    public async Task ThenMessageShouldShowReactionBadge(string messageText)
    {
        var page = await GetOrCreatePageAsync();

        // Find the message container
        var message = page.GetByTestId("message").Filter(new LocatorFilterOptions
        {
            HasText = messageText
        });

        // Look for any reaction badge within the message area
        // Reaction badges have testid like "reaction-badge-👍"
        var reactionBadges = page.Locator("[data-testid^='reaction-badge-']");

        // Wait for at least one reaction badge to appear
        await Expect(reactionBadges.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 15000
        });
    }

    [Then(@"the message ""(.*)"" should show (\d+) reactions? of type (\d+)")]
    public async Task ThenMessageShouldShowReactionCount(string messageText, int expectedCount, int emojiIndex)
    {
        emojiIndex.Should().BeInRange(0, Emojis.Length - 1, "Emoji index must be valid");

        var page = await GetOrCreatePageAsync();
        var emoji = Emojis[emojiIndex];

        // Find the message
        var message = page.GetByTestId("message").Filter(new LocatorFilterOptions
        {
            HasText = messageText
        });

        // Find the specific reaction badge
        var reactionBadgeTestId = $"reaction-badge-{emoji}";
        var reactionBadge = page.GetByTestId(reactionBadgeTestId);

        // Verify reaction badge exists and shows count
        await Expect(reactionBadge.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 15000
        });

        var badgeText = await reactionBadge.First.TextContentAsync();
        badgeText.Should().NotBeNullOrEmpty("Reaction badge should have text content");
        badgeText.Should().Contain(expectedCount.ToString(), $"Reaction badge should show count {expectedCount}");
    }

    /// <summary>
    /// Helper method for Playwright assertions.
    /// </summary>
    private static ILocatorAssertions Expect(ILocator locator)
    {
        return Assertions.Expect(locator);
    }
}
