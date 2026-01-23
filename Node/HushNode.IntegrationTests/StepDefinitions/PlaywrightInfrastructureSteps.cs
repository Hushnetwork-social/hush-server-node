using FluentAssertions;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// Step definitions for verifying Playwright infrastructure is working correctly.
/// These tests ensure browser automation works before building E2E tests.
/// </summary>
[Binding]
public class PlaywrightInfrastructureSteps
{
    private readonly ScenarioContext _scenarioContext;
    private PlaywrightFixture? _fixture;
    private IBrowserContext? _context1;
    private IBrowserContext? _context2;
    private IPage? _page;
    private bool _disposeCompleted;
    private Exception? _disposeException;

    public PlaywrightInfrastructureSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"the Playwright browser is initialized")]
    public async Task GivenThePlaywrightBrowserIsInitialized()
    {
        _fixture = new PlaywrightFixture();
        await _fixture.InitializeAsync();
        _fixture.IsInitialized.Should().BeTrue("Playwright should be initialized");
    }

    [When(@"I create a new browser page")]
    public async Task WhenICreateANewBrowserPage()
    {
        _fixture.Should().NotBeNull("Fixture must be initialized first");
        var (context, page) = await _fixture!.CreatePageAsync();
        _context1 = context;
        _page = page;
    }

    [When(@"I navigate to ""(.*)""")]
    public async Task WhenINavigateTo(string url)
    {
        _page.Should().NotBeNull("Page must be created first");
        await _page!.GotoAsync(url);
    }

    [Then(@"the page URL should be ""(.*)""")]
    public void ThenThePageUrlShouldBe(string expectedUrl)
    {
        _page.Should().NotBeNull("Page must be created first");
        _page!.Url.Should().Be(expectedUrl);
    }

    [When(@"I create two separate browser contexts")]
    public async Task WhenICreateTwoSeparateBrowserContexts()
    {
        _fixture.Should().NotBeNull("Fixture must be initialized first");
        _context1 = await _fixture!.CreateContextAsync();
        _context2 = await _fixture!.CreateContextAsync();
    }

    [Then(@"the contexts should be different instances")]
    public void ThenTheContextsShouldBeDifferentInstances()
    {
        _context1.Should().NotBeNull("First context should exist");
        _context2.Should().NotBeNull("Second context should exist");
        _context1.Should().NotBeSameAs(_context2, "Browser contexts should be isolated");
    }

    [When(@"I dispose the Playwright browser")]
    public async Task WhenIDisposeThePlaywrightBrowser()
    {
        _fixture.Should().NotBeNull("Fixture must be initialized first");

        try
        {
            await _fixture!.DisposeAsync();
            _disposeCompleted = true;
        }
        catch (Exception ex)
        {
            _disposeException = ex;
            _disposeCompleted = false;
        }
    }

    [Then(@"the browser should no longer be initialized")]
    public void ThenTheBrowserShouldNoLongerBeInitialized()
    {
        _fixture.Should().NotBeNull("Fixture reference should still exist");
        _fixture!.IsInitialized.Should().BeFalse("Browser should be disposed");
    }

    [Then(@"no errors should occur")]
    public void ThenNoErrorsShouldOccur()
    {
        _disposeCompleted.Should().BeTrue("Dispose should complete successfully");
        _disposeException.Should().BeNull("No exception should be thrown during dispose");
    }

    [AfterScenario]
    public async Task Cleanup()
    {
        // Clean up any browser contexts that weren't disposed
        if (_context1 != null)
        {
            try { await _context1.CloseAsync(); } catch { /* ignore */ }
        }
        if (_context2 != null)
        {
            try { await _context2.CloseAsync(); } catch { /* ignore */ }
        }

        // Dispose fixture if not already disposed
        if (_fixture != null && _fixture.IsInitialized)
        {
            await _fixture.DisposeAsync();
        }
    }
}
