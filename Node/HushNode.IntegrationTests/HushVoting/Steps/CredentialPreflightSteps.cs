using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-009 AC-009-003 -> Phase 7 Task 7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialPreflightSteps(HushVotingScenario scenario, CredentialFileSteps file)
{
    [Given("the Web browser cannot create its isolated credential authority")]
    public async Task ArrangeAsync()
    {
        await scenario.Page.AddInitScriptAsync("""
            (() => {
                window.__hvSourceReads = 0;
                const slice = File.prototype.slice;
                File.prototype.slice = function(...args) { window.__hvSourceReads++; return Reflect.apply(slice, this, args); };
                if (window.name === 'hv-authority-missing') Object.defineProperty(window, 'SharedWorker', { value: undefined });
                if (window.name === 'hv-authority-denied') Object.defineProperty(window, 'SharedWorker', {
                    value: class { constructor() { throw new DOMException('Controlled authority denial', 'SecurityError'); } }
                });
            })();
            """);
    }

    [When("credential restore is attempted with an absent and then a denied browser authority")]
    public async Task BlockedAsync()
    {
        foreach (var mode in new[] { "hv-authority-missing", "hv-authority-denied" })
        {
            await scenario.Page.GotoAsync("/");
            await scenario.Page.EvaluateAsync("mode => { window.name = mode; }", mode);
            await scenario.Page.ReloadAsync();
            await Expect(scenario.Page.Locator(".error-surface")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Credential File") })).ToHaveCountAsync(0);
            await Expect(scenario.Page.GetByTestId("choose-file")).ToHaveCountAsync(0);
            await Expect(scenario.Page.GetByTestId("credential-file-input")).ToHaveCountAsync(0);
            await Expect(scenario.Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
            await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
            (await scenario.Page.EvaluateAsync<int>("() => window.__hvSourceReads")).Should().Be(0);
            scenario.Faults.IdentityQueryCount.Should().Be(0);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        }
    }

    [Then("preflight blocks the picker and all reads until real authority is available and a backup restores through the live node")]
    public async Task RestoredAsync()
    {
        await scenario.Page.EvaluateAsync("() => { window.name = ''; }");
        await scenario.Page.ReloadAsync();
        await file.SourceWithWordsAsync();
        await file.DecryptAsync();
        await file.ImportedAsync();
        await file.ProtectAsync();
        await file.SourceUnchangedAsync();
    }
}
