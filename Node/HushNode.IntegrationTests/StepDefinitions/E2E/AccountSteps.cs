using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for Account Details page operations in E2E tests.
/// Covers identity name changes via the browser UI.
/// </summary>
[Binding]
internal sealed class AccountSteps : BrowserStepsBase
{
    public AccountSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [When(@"the user opens the user menu")]
    public async Task WhenTheUserOpensTheUserMenu()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E Account] Opening user menu...");
        await ClickTestIdAsync(page, "user-menu-trigger");

        // Wait for menu dropdown to appear
        await Task.Delay(300);
        Console.WriteLine("[E2E Account] User menu opened");
    }

    [When(@"the user clicks Account Details")]
    public async Task WhenTheUserClicksAccountDetails()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E Account] Clicking Account Details...");
        await ClickTestIdAsync(page, "menu-account-details");

        // Wait for navigation to /account page
        await Assertions.Expect(page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex(@"/account"),
            new PageAssertionsToHaveURLOptions { Timeout = 10000 });

        Console.WriteLine("[E2E Account] Navigated to Account Details page");
    }

    [When(@"the user changes display name to ""(.*)""")]
    public async Task WhenTheUserChangesDisplayNameTo(string newName)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E Account] Changing display name to '{newName}'...");

        // Wait for the input to be visible
        var input = await WaitForTestIdAsync(page, "account-display-name-input", 10000);

        // Clear existing text and fill with new name
        await input.ClearAsync();
        await input.FillAsync(newName);

        // Store for later verification
        ScenarioContext["NewDisplayName"] = newName;

        Console.WriteLine($"[E2E Account] Display name input set to '{newName}'");
    }

    [When(@"the user saves the display name change")]
    public async Task WhenTheUserSavesTheDisplayNameChange()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E Account] Saving display name change...");

        // IMPORTANT: Start listening for the UpdateIdentity transaction BEFORE clicking Save.
        // The save button submits a blockchain transaction, so we need the waiter pattern.
        var waiter = StartListeningForTransactions(minTransactions: 1);
        ScenarioContext["PendingNameChangeTransactionWaiter"] = waiter;

        // Click the save button
        await ClickTestIdAsync(page, "account-save-button");

        Console.WriteLine("[E2E Account] Save button clicked, waiter listening for name change transaction");
    }

    [When(@"the name change transaction is processed")]
    public async Task WhenTheNameChangeTransactionIsProcessed()
    {
        Console.WriteLine("[E2E Account] Waiting for name change transaction to be processed...");

        // Retrieve the waiter that was created in WhenTheUserSavesTheDisplayNameChange
        if (!ScenarioContext.TryGetValue("PendingNameChangeTransactionWaiter", out var waiterObj)
            || waiterObj is not HushServerNode.HushServerNodeCore.TransactionWaiter waiter)
        {
            throw new InvalidOperationException(
                "No PendingNameChangeTransactionWaiter found. " +
                "This step must follow 'the user saves the display name change'.");
        }

        ScenarioContext.Remove("PendingNameChangeTransactionWaiter");

        try
        {
            await AwaitTransactionsAndProduceBlockAsync(waiter);
            Console.WriteLine("[E2E Account] Name change transaction processed and block produced");
        }
        finally
        {
            waiter.Dispose();
        }

        // Allow time for the client to sync and update the UI with the new name
        var page = await GetOrCreatePageAsync();
        await Task.Delay(2000);
        await TriggerSyncAsync(page);
        await Task.Delay(2000);
    }

    [When(@"the user returns to the dashboard")]
    public async Task WhenTheUserReturnsToDashboard()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E Account] Navigating back to dashboard...");
        await NavigateToAsync(page, "/dashboard");

        // Wait for the feed list to render instead of WaitForNetworkIdleAsync
        // (NetworkIdle never fires on the dashboard due to polling/WebSocket connections)
        var feedList = page.GetByTestId("feed-item:personal");
        await Assertions.Expect(feedList.First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });

        Console.WriteLine("[E2E Account] Back on dashboard, feed list visible");
    }

    [Then(@"the personal feed should show name ""(.*)""")]
    public async Task ThenThePersonalFeedShouldShowName(string expectedName)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E Account] Verifying personal feed shows name '{expectedName}'...");

        // Trigger sync to pick up the latest state
        await TriggerSyncAsync(page);
        await Task.Delay(2000);

        // Personal feeds have data-testid="feed-item:personal"
        var personalFeed = page.GetByTestId("feed-item:personal");

        await Assertions.Expect(personalFeed.First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });

        var feedText = await personalFeed.First.TextContentAsync();
        feedText.Should().Contain(expectedName,
            $"Personal feed should display the updated name '{expectedName}'");

        Console.WriteLine($"[E2E Account] Personal feed shows name '{expectedName}'");
    }

    [Then(@"the sidebar should show username ""(.*)""")]
    public async Task ThenTheSidebarShouldShowUsername(string expectedName)
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine($"[E2E Account] Verifying sidebar shows username '{expectedName}'...");

        // The user menu trigger shows the display name
        var userMenuTrigger = await WaitForTestIdAsync(page, "user-menu-trigger", 10000);
        var triggerText = await userMenuTrigger.TextContentAsync();

        triggerText.Should().Contain(expectedName,
            $"Sidebar user menu should display the updated name '{expectedName}'");

        Console.WriteLine($"[E2E Account] Sidebar shows username '{expectedName}'");
    }
}
