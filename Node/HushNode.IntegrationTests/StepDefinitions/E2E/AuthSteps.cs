using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for authentication and identity creation via browser.
/// </summary>
[Binding]
internal sealed class AuthSteps : BrowserStepsBase
{
    public AuthSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [Given(@"a browser is launched")]
    public async Task GivenABrowserIsLaunched()
    {
        var page = await GetOrCreatePageAsync();
        page.Should().NotBeNull("Browser page should be created");
    }

    [When(@"the user navigates to ""(.*)""")]
    public async Task WhenTheUserNavigatesTo(string path)
    {
        var page = await GetOrCreatePageAsync();
        await NavigateToAsync(page, path);

        // Wait for page to be stable
        await WaitForNetworkIdleAsync(page);
    }

    [When(@"the user creates a new identity with display name ""(.*)""")]
    public async Task WhenTheUserCreatesIdentity(string displayName)
    {
        var page = await GetOrCreatePageAsync();

        // Wait for client-side hydration (Next.js needs time to initialize)
        await Task.Delay(3000);

        // Find the input by placeholder (more reliable for E2E tests)
        var inputLocator = page.GetByPlaceholder("Satoshi Nakamoto", new PageGetByPlaceholderOptions { Exact = false });
        await inputLocator.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await inputLocator.FillAsync(displayName);

        // Click "Generate Recovery Words" button
        var generateButton = page.GetByText("Generate Recovery Words");
        await generateButton.ClickAsync();

        // Wait for mnemonic words to be generated (checkbox becomes visible)
        var checkbox = page.Locator("input[type='checkbox']");
        await checkbox.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // Check the checkbox to confirm mnemonic has been saved
        await checkbox.CheckAsync();

        // Click "Create Account" button (use Last to get the submit button, not the tab)
        // There are 2 buttons with "Create Account" - the tab and the submit button
        var createButton = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create Account" }).Last;
        await createButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await createButton.ClickAsync();

        // Wait for navigation to dashboard (this includes blockchain transaction processing)
        await page.WaitForURLAsync("**/dashboard**", new PageWaitForURLOptions { Timeout = 60000 });

        // Store display name for later steps
        ScenarioContext["UserDisplayName"] = displayName;
    }

    [Given(@"the user has created identity ""(.*)"" via browser")]
    public async Task GivenTheUserHasCreatedIdentity(string displayName)
    {
        await WhenTheUserNavigatesTo("/auth");
        await WhenTheUserCreatesIdentity(displayName);
    }

    [Then(@"the user should be redirected to ""(.*)""")]
    public async Task ThenTheUserShouldBeRedirectedTo(string expectedPath)
    {
        var page = await GetOrCreatePageAsync();
        await page.WaitForURLAsync($"**{expectedPath}**", new PageWaitForURLOptions { Timeout = 10000 });
    }
}
