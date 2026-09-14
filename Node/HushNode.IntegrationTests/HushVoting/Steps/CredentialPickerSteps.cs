using System.Text;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialPickerSteps(HushVotingScenario scenario, CredentialFileSteps file, AuthenticationSteps authentication)
{
    private IPage Page => scenario.Page;
    private byte[] _backup = [];
    private const string Password = "browser-only-backup-secret";
    private bool _leaked;

    [Given("the credential picker has a real bounded backup available")]
    public async Task ArrangeAsync()
    {
        await Page.GotoAsync("/");
        await file.OpenAsync();
        var keys = HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art")));
        _backup = HushVotingCredentialFile.Create(keys, "Picker identity", Password);
        var probes = new[] { Password, keys.SigningPrivateKey, keys.EncryptPrivateKey, Convert.ToBase64String(_backup), Convert.ToHexString(_backup) };
        Page.Request += (_, request) =>
        {
            var body = request.PostDataBuffer;
            var wire = request.Url + "\n" + (body is null ? "" : Encoding.UTF8.GetString(body));
            if (probes.Any(probe => wire.Contains(probe, StringComparison.Ordinal))) _leaked = true;
        };
    }

    [When("Alice opens and cancels the single-file browser picker")]
    public async Task CancelPickerAsync()
    {
        var picker = await Page.RunAndWaitForFileChooserAsync(() => Page.GetByTestId("choose-file").ClickAsync());
        picker.IsMultiple.Should().BeFalse();
        await picker.SetFilesAsync(Array.Empty<FilePayload>());
        await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("selected-file-name")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("restore-panel").GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        scenario.Faults.IdentityQueryCount.Should().Be(0);
    }

    [Then("cancellation stays neutral and a subsequent explicit file selection can proceed")]
    public async Task AfterCancelAsync()
    {
        await file.ChooseAsync(_backup);
        await Expect(Page.GetByTestId("backup-password-input")).ToBeVisibleAsync();
        await NoNetworkSecretsAsync();
    }

    [When("Alice selects a backup with a long path-like display name and then replaces it")]
    public async Task DisplayNameAsync()
    {
        var longName = "private-provider/folder/\u202e" + new string('x', 130) + ".dat";
        await file.ChooseAsync(_backup, longName);
        var display = await Page.GetByTestId("selected-file-name").InnerTextAsync();
        display.EnumerateRunes().Count().Should().Be(96);
        display.Should().NotContain("private-provider").And.NotContain("folder").And.NotContain("\u202e").And.NotContain("/");
        await Page.GetByTestId("choose-different-file").ClickAsync();
        await Expect(Page.GetByTestId("selected-file-name")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
        await file.ChooseAsync(_backup, "replacement.backup");
        await Expect(Page.GetByTestId("selected-file-name")).ToHaveTextAsync("replacement.backup");
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), Password);
        await Page.GetByTestId("submit-password").ClickAsync();
        await Expect(Page.GetByTestId("create-identity")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Then("only the bounded sanitized basename appears transiently and confirmation removes it")]
    public async Task DisplayVerifiedAsync()
    {
        await Expect(Page.GetByTestId("selected-file-name")).ToHaveCountAsync(0);
        scenario.Faults.IdentityQueryCount.Should().Be(1);
        await NoNetworkSecretsAsync();
    }

    [When("Alice selects a valid backup without a dat extension")]
    public async Task ExtensionAsync()
    {
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveAttributeAsync("accept", ".dat,application/octet-stream");
        await file.ChooseAsync(_backup, "approved-content.backup");
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), Password);
        await Page.GetByTestId("submit-password").ClickAsync();
        await Expect(Page.GetByTestId("create-identity")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Then("binary validation and real identity lookup succeed independently of the extension")]
    public async Task ExtensionVerifiedAsync()
    {
        scenario.Faults.IdentityQueryCount.Should().Be(1);
        await NoNetworkSecretsAsync();
    }

    [Then("the browser sends no backup bytes, password, or private keys to the BFF or node")]
    public async Task NoNetworkSecretsAsync()
    {
        _leaked.Should().BeFalse("backup secrets must remain inside the browser authority");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
    }

    [When("Alice locks her identity and requests credential-file restoration")]
    public async Task LockedAsync()
    {
        await authentication.LockAsync();
        await authentication.SafeLockedPreviewAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Credential File") })).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveCountAsync(0);
        await Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
    }

    [Then("only completed explicit removal exposes the credential-file picker")]
    public async Task RemovedAsync()
    {
        await authentication.RemoveAsync();
        await authentication.StorageRemovedAsync();
        await file.OpenAsync();
        await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
    }
}
