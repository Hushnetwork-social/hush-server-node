using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialPasswordSteps(HushVotingScenario scenario, CredentialFileSteps file)
{
    private IPage Page => scenario.Page;
    private int _accepted;
    private int _rejected;
    private static byte[] Backup(string password) => HushVotingCredentialFile.Create(
        HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 11).Append("about"))),
        "Password boundary identity", password);

    [Given("the Backup-file password field is ready")]
    public async Task OpenAsync()
    {
        await Page.GotoAsync("/");
        await file.OpenAsync();
    }

    private async Task AcceptedAsync()
    {
        await Expect(Page.GetByTestId("create-identity")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        _accepted++;
    }

    [When("Alice decrypts independently encrypted backups at the raw UTF-8 password boundaries")]
    public async Task BoundariesAsync()
    {
        foreach (var password in new[] { "x", " ", "  päss 🔑  ", "e\u0301", new string('x', 4096), string.Concat(Enumerable.Repeat("🔑", 1024)) })
        {
            await file.ChooseAsync(Backup(password));
            await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), password);
            await Page.GetByTestId("submit-password").ClickAsync();
            await AcceptedAsync();
            await Page.GoBackAsync();
            // FEAT-009 AC-009-067: validated Back starts an empty picker.
            await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
            await Expect(Page.GetByTestId("credential-file-input")).ToHaveValueAsync("");
            await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        }
        foreach (var password in new[] { new string('x', 4097), string.Concat(Enumerable.Repeat("🔑", 1025)) })
        {
            await file.ChooseAsync(Backup(password));
            var queries = scenario.Faults.IdentityQueryCount;
            await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), password);
            await Page.GetByTestId("submit-password").ClickAsync();
            await Expect(Page.GetByText("The backup-file password exceeds 4096 UTF-8 bytes.", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(Page.GetByTestId("submit-password")).ToBeDisabledAsync();
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
            await Page.GetByTestId("choose-different-file").ClickAsync();
            await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
            _rejected++;
        }
    }

    [Then("short and unnormalized passwords reach real identity lookup and oversized passwords do not")]
    public void BoundariesVerified()
    {
        _accepted.Should().Be(6);
        _rejected.Should().Be(2);
        scenario.Faults.IdentityQueryCount.Should().Be(6);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [When("Alice explicitly confirms that her backup was created without a password")]
    public async Task EmptyAsync()
    {
        await file.ChooseAsync(Backup(""));
        await Expect(Page.GetByTestId("empty-password-option")).Not.ToBeCheckedAsync();
        await Expect(Page.GetByTestId("submit-password")).ToBeDisabledAsync();
        await Expect(Page.GetByText("This file has no password protection beyond possession.", new() { Exact = true })).ToBeVisibleAsync();
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        await Page.GetByTestId("empty-password-option").CheckAsync();
        await Expect(Page.GetByTestId("backup-password-input")).ToBeDisabledAsync();
        await Page.GetByTestId("submit-password").ClickAsync();
    }

    [Then("the empty-password backup decrypts only after unchecked-by-default consent and a warning")]
    public async Task EmptyVerifiedAsync()
    {
        await AcceptedAsync();
        scenario.Faults.IdentityQueryCount.Should().Be(1);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [When("Alice pastes and explicitly reveals and conceals her backup password")]
    public async Task AccessibleAsync()
    {
        const string password = "  pasted 🔑 backup  ";
        await file.ChooseAsync(Backup(password));
        var input = Page.GetByLabel("Backup-file password", new() { Exact = true });
        await Expect(input).ToHaveAttributeAsync("type", "password");
        await Expect(input).ToHaveAttributeAsync("autocomplete", "off");
        await scenario.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        try
        {
            await Page.EvaluateAsync("value => navigator.clipboard.writeText(value)", password);
            await input.PressAsync("Control+V");
            if (await input.InputValueAsync() != password) throw new InvalidOperationException("Backup password paste changed the raw value.");
        }
        catch (Exception error) when (error is PlaywrightException or TimeoutException) { throw new InvalidOperationException("Backup password paste failed; input omitted."); }
        await Page.GetByRole(AriaRole.Button, new() { Name = "Show backup-file password", Exact = true }).ClickAsync();
        await Expect(input).ToHaveAttributeAsync("type", "text");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Hide backup-file password", Exact = true })).ToHaveAttributeAsync("aria-pressed", "true");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Hide backup-file password", Exact = true }).ClickAsync();
        await Expect(input).ToHaveAttributeAsync("type", "password");
        await Expect(Page.Locator("input[autocomplete=new-password], input[autocomplete=current-password], input[autocomplete=username]")).ToHaveCountAsync(0);
        await Page.GetByTestId("submit-password").ClickAsync();
    }

    [Then("the labelled password field keeps its backup-only purpose and decrypts through the real worker")]
    public async Task AccessibleVerifiedAsync()
    {
        await AcceptedAsync();
        scenario.Faults.IdentityQueryCount.Should().Be(1);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }
}
