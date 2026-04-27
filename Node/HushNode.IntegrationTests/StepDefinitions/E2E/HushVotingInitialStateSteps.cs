using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for HushVoting empty-database initial screen coverage.
/// </summary>
[Binding]
internal sealed class HushVotingInitialStateSteps : BrowserStepsBase
{
    public HushVotingInitialStateSteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [When(@"the user opens HushVoting")]
    public async Task WhenTheUserOpensHushVoting()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E HushVoting] Navigating to HushVoting...");
        await NavigateToAsync(page, "/elections");

        await Assertions.Expect(page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex(@"/elections"),
            new PageAssertionsToHaveURLOptions { Timeout = 10000 });
        await ExpectVisibleTextAsync(page, "HushVoting! Hub");

        Console.WriteLine("[E2E HushVoting] HushVoting hub opened");
    }

    [When(@"the user opens Create Election from the HushVoting menu")]
    public async Task WhenTheUserOpensCreateElectionFromTheHushVotingMenu()
    {
        var page = await GetOrCreatePageAsync();

        Console.WriteLine("[E2E HushVoting] Opening Create Election from the voting menu...");
        await ClickTestIdAsync(page, "voting-menu-create");

        await Assertions.Expect(page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex(@"/elections/owner"),
            new PageAssertionsToHaveURLOptions { Timeout = 10000 });

        Console.WriteLine("[E2E HushVoting] Create Election workspace opened");
    }

    [Then(@"the HushVoting hub should show the empty linked-election state")]
    public async Task ThenTheHushVotingHubShouldShowTheEmptyLinkedElectionState()
    {
        var page = await GetOrCreatePageAsync();

        await ExpectVisibleTextAsync(page, "HushVoting! Hub");
        await ExpectVisibleTextAsync(page, "No linked election surfaces available");
        await ExpectVisibleTextAsync(page, "Use Search Election");
        await ExpectVisibleTextAsync(page, "Use Create Election");

        Console.WriteLine("[E2E HushVoting] Empty hub state is visible");
    }

    [Then(@"the HushVoting create-election workspace should show a blank draft")]
    public async Task ThenTheHushVotingCreateElectionWorkspaceShouldShowABlankDraft()
    {
        var page = await GetOrCreatePageAsync();

        await Assertions.Expect(page.GetByTestId("elections-workspace")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        await ExpectVisibleTextAsync(page, "Election Lifecycle Workspace");
        await ExpectVisibleTextAsync(page, "New election draft");
        await ExpectVisibleTextAsync(page, "Untitled election draft");
        await ExpectVisibleTextAsync(page, "Before you can create this draft");
        await ExpectVisibleTextAsync(page, "Election title is required.");

        Console.WriteLine("[E2E HushVoting] Blank create-election draft state is visible");
    }

    [Then(@"the HushVoting screen should not show an election query proxy error")]
    public async Task ThenTheHushVotingScreenShouldNotShowAnElectionQueryProxyError()
    {
        var page = await GetOrCreatePageAsync();

        await Task.Delay(1000);

        var bodyText = await page.Locator("body").InnerTextAsync();

        bodyText.Should().NotContain("Election query proxy failed");
        bodyText.Should().NotContain("fetch failed");
        bodyText.Should().NotContain("ENOENT");
        bodyText.Should().NotContain("Unable to resolve hushElections");
        bodyText.Should().NotContain("/hush-server-node/Protos/hushElections.proto");
        bodyText.Should().NotContain("gRPC upstream");

        Console.WriteLine("[E2E HushVoting] No election query proxy error is visible");
    }

    private static async Task ExpectVisibleTextAsync(IPage page, string text)
    {
        await Assertions.Expect(page.GetByText(text, new PageGetByTextOptions { Exact = false }).First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
    }
}
