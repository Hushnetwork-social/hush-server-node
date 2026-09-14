using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialNavigationSteps(HushVotingScenario scenario, CredentialFileSteps file, CredentialProtectionSteps protection, AuthenticationSteps authentication, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;

    [When("Alice goes Back after credential validation but before device staging")]
    public async Task LeaveValidatedAsync()
    {
        await Expect(Page.GetByTestId("restore-device-password")).ToBeVisibleAsync();
        await Page.EvaluateAsync("""
            () => {
                const send = MessagePort.prototype.postMessage, listening = new WeakSet(), pending = new Set();
                let completed = 0;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation' && message.operation === 'destroyCandidate') {
                        pending.add(message.operationId);
                        if (!listening.has(this)) {
                            listening.add(this);
                            this.addEventListener('message', event => {
                                const outcome = event.data;
                                if (outcome?.kind === 'operation-outcome' && pending.delete(outcome.operationId)
                                    && outcome.outcome === 'OK') completed++;
                            });
                        }
                    }
                    return Reflect.apply(send, this, args);
                };
                window.__hvDestroyedFileCandidates = () => completed;
            }
            """);
        await Page.GoBackAsync();
    }

    [Then("empty credential selection returns and only a newly selected backup can complete live restoration")]
    public async Task ValidatedBackAsync()
    {
        await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
        (await Page.EvaluateAsync<int>("() => window.__hvDestroyedFileCandidates()")).Should().Be(1);
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("selected-file-name")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("restore-device-password")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        authentication.RootOnlyUrl();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
        await authentication.FirstRunChoicesAsync();
        await file.OpenAsync();
        await file.DecryptAsync();
        await file.ImportedAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("choose-file")).ToBeVisibleAsync();
        (await Page.EvaluateAsync<int>("() => window.__hvDestroyedFileCandidates()")).Should().Be(2);
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await file.DecryptAsync();
        await file.ImportedAsync();
        await file.ProtectAsync();
    }

    [When("Alice leaves the backup password screen through browser Back")]
    public async Task LeavePasswordAsync()
    {
        var bytes = HushVotingCredentialFile.Create(HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art"))), "Unimported backup", "backup-only");
        await file.ChooseAsync(bytes);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("backup-password-input"), "not-submitted");
        await Page.GoBackAsync();
    }

    [Then("the three-choice root returns and reopening restore has no previous file or password")]
    public async Task EmptyAgainAsync()
    {
        await authentication.FirstRunChoicesAsync();
        authentication.RootOnlyUrl();
        await Page.GoForwardAsync();
        await authentication.FirstRunChoicesAsync();
        await file.OpenAsync();
        await Expect(Page.GetByTestId("selected-file-name")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("credential-file-input")).ToHaveValueAsync("");
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [When("Alice uses browser Back after encrypted staging fails to activate online")]
    public async Task LeaveStageAsync()
    {
        await protection.OfflineStagingAsync();
        await Page.GoBackAsync();
    }

    [When("Alice uses browser Back while the real post-staging identity lookup is still pending")]
    public async Task LeavePendingStageAsync()
    {
        var before = scenario.Faults.IdentityQueryCount;
        scenario.Faults.StallIdentityQueryNumber = before + 1;
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password"), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByTestId("restore-device-password-confirmation"), HushVotingScenario.DevicePassword);
        await Page.GetByTestId("submit-protection").ClickAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (scenario.Faults.IdentityQueryCount == before) await Task.Delay(20, deadline.Token);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Page.GoBackAsync();
    }

    [Then("the staged vault stays locked through history navigation and cannot reopen file import")]
    public async Task StageLockedAsync()
    {
        await authentication.SafeLockedPreviewAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Credential File") })).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Page.GoForwardAsync();
        await authentication.SafeLockedPreviewAsync();
        await Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
        authentication.RootOnlyUrl();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        scenario.Faults.IdentityUnavailable = false;
        await identity.UnlockAndBootstrapAsync();
    }
}
