using FluentAssertions;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Step definitions for E2E infrastructure verification.
/// Verifies that Playwright, Docker, and HushWebClient work together.
/// </summary>
[Binding]
public class InfrastructureVerificationSteps
{
    private readonly ScenarioContext _scenarioContext;
    private IBrowserContext? _browserContext;
    private IPage? _page;

    public InfrastructureVerificationSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private HushServerNodeCore GetNode()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.NodeKey, out var nodeObj)
            && nodeObj is HushServerNodeCore node)
        {
            return node;
        }
        throw new InvalidOperationException("Node not found in ScenarioContext.");
    }

    private PlaywrightFixture GetPlaywright()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.PlaywrightKey, out var obj)
            && obj is PlaywrightFixture playwright)
        {
            return playwright;
        }
        throw new InvalidOperationException("PlaywrightFixture not found. Ensure scenario is tagged with @E2E.");
    }

    private WebClientFixture GetWebClient()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.WebClientKey, out var obj)
            && obj is WebClientFixture webClient)
        {
            return webClient;
        }
        throw new InvalidOperationException("WebClientFixture not found. Ensure scenario is tagged with @E2E.");
    }

    [Given(@"a HushServerNode is running")]
    public void GivenAHushServerNodeIsRunning()
    {
        // Node is started by ScenarioHooks.BeforeScenario
        var node = GetNode();
        node.Should().NotBeNull("HushServerNode should be running");
    }

    [Given(@"HushWebClient is running in Docker")]
    public void GivenHushWebClientIsRunningInDocker()
    {
        // WebClient is started by ScenarioHooks.BeforeE2EScenario
        var webClient = GetWebClient();
        webClient.Should().NotBeNull("WebClientFixture should be available");
        webClient.IsStarted.Should().BeTrue("WebClient container should be running");
    }

    [Given(@"a browser page is created")]
    public async Task GivenABrowserPageIsCreated()
    {
        var playwright = GetPlaywright();
        (_browserContext, _page) = await playwright.CreatePageAsync();
        _page.Should().NotBeNull("Browser page should be created");
    }

    [When(@"the browser navigates to the web client")]
    public async Task WhenTheBrowserNavigatesToTheWebClient()
    {
        var webClient = GetWebClient();
        _page.Should().NotBeNull("Page must be created first");

        // Navigate to the web client with a reasonable timeout
        await _page!.GotoAsync(webClient.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });
    }

    [Then(@"the page should load successfully")]
    public void ThenThePageShouldLoadSuccessfully()
    {
        _page.Should().NotBeNull("Page must exist");
        _page!.Url.Should().StartWith("http://localhost:3000", "Page should be at the web client URL");
    }

    [Then(@"the page title should contain ""(.*)""")]
    public async Task ThenThePageTitleShouldContain(string expectedText)
    {
        _page.Should().NotBeNull("Page must exist");
        var title = await _page!.TitleAsync();
        title.Should().Contain(expectedText, $"Page title should contain '{expectedText}'");
    }

    [AfterScenario]
    public async Task Cleanup()
    {
        // Clean up browser context
        if (_browserContext != null)
        {
            try
            {
                await _browserContext.CloseAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
