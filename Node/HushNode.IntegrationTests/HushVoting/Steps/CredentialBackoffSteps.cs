using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialBackoffSteps(HushVotingScenario scenario)
{
    private IPage Page => scenario.Page;
    private const string Password = "valid-backup-password";
    private const string Error = "The backup password is incorrect or the credential file is damaged.";
    private byte[] _backup = [];
    private readonly List<int> _observed = [];

    private async Task ChooseAsync(IPage page, string name)
    {
        try { await page.GetByTestId("credential-file-input").SetInputFilesAsync(new FilePayload { Name = name, MimeType = "application/octet-stream", Buffer = _backup }); }
        catch (Exception error) when (error is PlaywrightException or TimeoutException) { throw new InvalidOperationException("Backoff fixture selection failed; file diagnostics omitted."); }
        await Expect(page.GetByTestId("backup-password-input")).ToBeVisibleAsync();
    }

    private async Task OpenAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Credential File") }).ClickAsync();
        await ChooseAsync(page, "backoff.dat");
    }

    [Given("a real credential backup is awaiting password authentication")]
    public async Task ArrangeAsync()
    {
        _backup = HushVotingCredentialFile.Create(HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art"))), "Backoff identity", Password);
        await Page.GotoAsync("/");
        await OpenAsync(Page);
    }

    private async Task AttemptAsync(IPage page, string password)
    {
        await HushVotingIdentityJourney.FillSecretAsync(page.GetByTestId("backup-password-input"), password);
        await Expect(page.GetByTestId("submit-password")).ToBeEnabledAsync(new() { Timeout = 35_000 });
        await page.GetByTestId("submit-password").ClickAsync();
    }

    private async Task FailureAsync(int expectedSeconds)
    {
        await AttemptAsync(Page, "incorrect-backup-password");
        await Expect(Page.GetByText(Error, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveValueAsync("");
        if (expectedSeconds == 0) await Expect(Page.GetByTestId("backoff-countdown")).ToHaveCountAsync(0);
        else await Expect(Page.GetByTestId("backoff-countdown")).ToHaveTextAsync($"Please wait {expectedSeconds} seconds before trying again.");
        _observed.Add(expectedSeconds);
        scenario.Faults.IdentityQueryCount.Should().Be(0);
    }

    [When("Alice submits seven incorrect passwords through the real worker")]
    public async Task ScheduleAsync()
    {
        foreach (var seconds in new[] { 0, 0, 2, 4, 8, 16, 30 }) await FailureAsync(seconds);
    }

    [Then("the first two failures have no extra delay and the next delays are exactly two four eight sixteen and thirty seconds")]
    public async Task ScheduleVerifiedAsync()
    {
        _observed.Should().Equal(0, 0, 2, 4, 8, 16, 30);
        await AttemptAsync(Page, Password);
        await Expect(Page.GetByTestId("create-identity")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityQueryCount.Should().Be(1);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [When("Alice changes files and then moves restoration to another tab during an active delay")]
    public async Task AcrossTabsAsync()
    {
        foreach (var seconds in new[] { 0, 0, 2, 4 }) await FailureAsync(seconds);
        await Page.GetByTestId("choose-different-file").ClickAsync();
        await ChooseAsync(Page, "different-file.backup");
        await Expect(Page.GetByTestId("backoff-countdown")).ToBeVisibleAsync();
        await Page.GoBackAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Credential File") })).ToBeVisibleAsync();
        var second = await scenario.Context.NewPageAsync();
        try
        {
            await second.GotoAsync(Page.Url);
            await OpenAsync(second);
            await AttemptAsync(second, Password);
            await Expect(second.GetByTestId("backoff-countdown")).ToBeVisibleAsync();
            await Expect(second.GetByTestId("create-identity")).ToHaveCountAsync(0);
            scenario.Faults.IdentityQueryCount.Should().Be(0);
            await AttemptAsync(second, Password);
            await Expect(second.GetByTestId("create-identity")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            scenario.Faults.IdentityQueryCount.Should().Be(1);
            await second.GoBackAsync();
            // FEAT-009 AC-009-067: successful validation changes Back's destination.
            await Expect(second.GetByTestId("choose-file")).ToBeVisibleAsync();
            await Expect(second.GetByTestId("credential-file-input")).ToHaveValueAsync("");
            await Expect(second.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        }
        finally { await second.CloseAsync(); }
    }

    [Then("the worker preserves backoff across files and tabs and complete validation resets its failure counter")]
    public async Task ResetVerifiedAsync()
    {
        await OpenAsync(Page);
        await AttemptAsync(Page, "incorrect-after-success");
        await Expect(Page.GetByText(Error, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("backoff-countdown")).ToHaveCountAsync(0);
        scenario.Faults.IdentityQueryCount.Should().Be(1);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }
}
