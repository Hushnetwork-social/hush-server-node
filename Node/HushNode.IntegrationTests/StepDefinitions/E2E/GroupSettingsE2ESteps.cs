using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for group settings panel operations in E2E tests.
/// Covers group title change via the browser UI (Group Settings panel).
///
/// Includes both single-user steps ("the user opens group settings via settings button")
/// and multi-user steps ("Alice opens group settings via settings button") using
/// negative lookahead to avoid binding collisions with GroupSteps.cs.
/// </summary>
[Binding]
internal sealed class GroupSettingsE2ESteps : BrowserStepsBase
{
    public GroupSettingsE2ESteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    // =============================================================================
    // Single-user steps (used with "the user ...")
    // =============================================================================

    [When(@"the user opens group settings via settings button")]
    public async Task WhenTheUserOpensGroupSettingsViaSettingsButton()
    {
        var page = await GetOrCreatePageAsync();
        await OpenGroupSettingsOnPageAsync(page);
    }

    [When(@"the user changes the group title to ""(.*)""")]
    public async Task WhenTheUserChangesTheGroupTitleTo(string newTitle)
    {
        var page = await GetOrCreatePageAsync();
        await ChangeGroupTitleOnPageAsync(page, newTitle);
    }

    [When(@"the user saves the group settings")]
    public async Task WhenTheUserSavesTheGroupSettings()
    {
        var page = await GetOrCreatePageAsync();
        await SaveGroupSettingsOnPageAsync(page);
    }

    // =============================================================================
    // Multi-user steps (used with "Alice ..." / "Bob ..." etc.)
    // =============================================================================

    [When(@"(?!the user)(\w+) opens group settings via settings button")]
    public async Task WhenUserOpensGroupSettingsViaSettingsButton(string userName)
    {
        SwitchToUser(userName);
        var page = GetUserPage(userName);
        await OpenGroupSettingsOnPageAsync(page);
    }

    [When(@"(?!the user)(\w+) changes the group title to ""(.*)""")]
    public async Task WhenUserChangesTheGroupTitleTo(string userName, string newTitle)
    {
        SwitchToUser(userName);
        var page = GetUserPage(userName);
        await ChangeGroupTitleOnPageAsync(page, newTitle);
    }

    [When(@"(?!the user)(\w+) saves the group settings")]
    public async Task WhenUserSavesTheGroupSettings(string userName)
    {
        SwitchToUser(userName);
        var page = GetUserPage(userName);
        await SaveGroupSettingsOnPageAsync(page);
    }

    // =============================================================================
    // Shared implementation methods
    // =============================================================================

    private async Task OpenGroupSettingsOnPageAsync(IPage page)
    {
        Console.WriteLine("[E2E GroupSettings] Opening group settings via settings button...");
        Console.WriteLine($"[E2E GroupSettings] Current URL: {page.Url}");

        // Diagnostic: check if the ChatView header is loaded and what buttons are visible
        var feedIdAttr = await page.Locator("[data-feed-id]").First.GetAttributeAsync("data-feed-id");
        Console.WriteLine($"[E2E GroupSettings] ChatView feed-id: {feedIdAttr ?? "not found"}");

        // Check for group-specific header elements to verify isGroupFeed is true
        var memberButton = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "View group members" });
        var memberCount = await memberButton.CountAsync();
        Console.WriteLine($"[E2E GroupSettings] Members button visible: {memberCount > 0}");

        var settingsButtonByLabel = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Group settings" });
        var settingsLabelCount = await settingsButtonByLabel.CountAsync();
        Console.WriteLine($"[E2E GroupSettings] Settings button (by aria-label): {settingsLabelCount > 0}");

        var settingsButtonByTestId = page.GetByTestId("group-settings-button");
        var settingsTestIdCount = await settingsButtonByTestId.CountAsync();
        Console.WriteLine($"[E2E GroupSettings] Settings button (by data-testid): {settingsTestIdCount > 0}");

        // Click the settings button in the ChatView header (gear icon)
        await ClickTestIdAsync(page, "group-settings-button");

        // Wait for the settings panel to appear (it's a slide-in panel with role="dialog")
        var settingsPanel = page.Locator("div[role='dialog']");
        await Assertions.Expect(settingsPanel).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        Console.WriteLine("[E2E GroupSettings] Group settings panel is open");
    }

    private async Task ChangeGroupTitleOnPageAsync(IPage page, string newTitle)
    {
        Console.WriteLine($"[E2E GroupSettings] Changing group title to '{newTitle}'...");

        // Wait for the group name input to be visible
        var nameInput = await WaitForTestIdAsync(page, "group-settings-name-input", 5000);

        // Clear existing text and fill with new title
        await nameInput.ClearAsync();
        await nameInput.FillAsync(newTitle);

        // Store for later verification
        ScenarioContext["NewGroupTitle"] = newTitle;

        Console.WriteLine($"[E2E GroupSettings] Group title input set to '{newTitle}'");
    }

    private async Task SaveGroupSettingsOnPageAsync(IPage page)
    {
        Console.WriteLine("[E2E GroupSettings] Saving group settings...");

        // Click the save button (appears in the footer when changes are detected)
        await ClickTestIdAsync(page, "group-settings-save-button");

        // Wait for the settings panel to close (the panel calls onClose() on success)
        // The panel is a div[role='dialog'] - wait for it to disappear
        var settingsPanel = page.Locator("div[role='dialog']");
        await Assertions.Expect(settingsPanel).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 10000 });

        Console.WriteLine("[E2E GroupSettings] Group settings saved and panel closed");

        // Allow time for the client to update the feed list with the new title
        await Task.Delay(1000);
        await TriggerSyncAsync(page);
        await Task.Delay(2000);
    }

    // =============================================================================
    // Multi-user helpers (same pattern as MultiUserSteps.cs)
    // =============================================================================

    /// <summary>
    /// Switches the active user context by updating the main page reference.
    /// </summary>
    private void SwitchToUser(string userName)
    {
        var page = GetUserPage(userName);
        ScenarioContext["E2E_MainPage"] = page;
        ScenarioContext["CurrentUser"] = userName;
        Console.WriteLine($"[E2E GroupSettings] Switched to user '{userName}'");
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

        // Fall back to main page (for single-user scenarios)
        if (ScenarioContext.TryGetValue("E2E_MainPage", out var mainPageObj) && mainPageObj is IPage mainPage)
        {
            return mainPage;
        }

        throw new InvalidOperationException($"No browser page found for user '{userName}'");
    }
}
