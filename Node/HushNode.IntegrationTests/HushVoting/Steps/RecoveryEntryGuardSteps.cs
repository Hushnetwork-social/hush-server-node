using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryEntryGuardSteps(HushVotingScenario scenario, AuthenticationSteps authentication, RecoveryWordEntrySteps entry)
{
    private IPage Page => scenario.Page;
    private ILocator Button(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    [When("Alice locks the app before attempting recovery")]
    public async Task LockAsync() => await authentication.LockAsync();

    [Then("the local identity remains and recovery choices stay unavailable")]
    public async Task RetainedAsync()
    {
        await authentication.SafeLockedPreviewAsync();
        await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Recovery Words") })).ToHaveCountAsync(0);
        await Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
    }

    [Then("only confirmed local removal makes Recovery Words available")]
    public async Task ConfirmOnlyAsync()
    {
        await RequestResetAsync();
        await Button("Cancel").ClickAsync();
        await RetainedAsync();
        await RequestResetAsync();
        await WarningAsync();
        await ClearedAsync();
    }

    [When("Alice requests local reset without knowing the device password")]
    public async Task RequestResetAsync()
    {
        await Button("Remove local user").ClickAsync();
        await Expect(Page.GetByLabel("Type REMOVE to continue", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("input[type=password]")).ToHaveCountAsync(0);
    }

    [Then("the destructive warning and explicit confirmation precede deletion")]
    public async Task WarningAsync()
    {
        await Expect(Page.GetByText("Remove local credentials from this device? This cannot be undone.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("I understand this removes local data only; it does not delete my on-chain identity.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Button("Remove local user")).ToBeDisabledAsync();
        await Page.GetByLabel("Type REMOVE to continue", new() { Exact = true }).FillAsync("REMOVE");
        await Expect(Button("Remove local user")).ToBeDisabledAsync();
        await Page.GetByRole(AriaRole.Checkbox).CheckAsync();
        await Expect(Button("Remove local user")).ToBeEnabledAsync();
        await Button("Remove local user").ClickAsync();
    }

    [Then("verified cleanup completes before the recovery choices appear")]
    public async Task ClearedAsync()
    {
        await authentication.StorageRemovedAsync();
        await authentication.FirstRunChoicesAsync();
        await entry.EntryAsync();
        await Expect(Page.GetByTestId("word-grid").Locator("input")).ToHaveCountAsync(24);
        authentication.RootOnlyUrl();
    }
}
