using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class CredentialCompletionSteps(HushVotingScenario scenario, CredentialFileSteps file, CredentialProtectionSteps protection)
{
    private IPage Page => scenario.Page;

    [Given("Alice begins restoring a registered backup while success announcements are observed")]
    public async Task ObserveAsync()
    {
        await file.BackupAsync();
        await Page.EvaluateAsync("""
            () => {
                window.__hvRestoreSuccessCount = 0;
                const announces = node => node?.nodeType === Node.ELEMENT_NODE &&
                    node.matches('h1,[role=status]') && node.textContent === 'Identity restored';
                const matches = node => node.nodeType === Node.ELEMENT_NODE &&
                    (announces(node) || [...node.querySelectorAll('h1,[role=status]')].some(announces));
                new MutationObserver(records => {
                    for (const record of records) {
                        if (record.type === 'characterData' && announces(record.target.parentElement)) window.__hvRestoreSuccessCount++;
                        if (record.type === 'childList' && (announces(record.target) || [...record.addedNodes].some(matches))) window.__hvRestoreSuccessCount++;
                    }
                }).observe(document.body, {subtree:true, childList:true, characterData:true});
            }
            """);
    }

    [When("selection and decryption succeed but the restored stage cannot verify online")]
    public async Task StageWithoutVerificationAsync()
    {
        await file.DecryptAsync();
        await file.ImportedAsync();
        await protection.OfflineStagingAsync();
    }

    [Then("no intermediate restore phase announces completed restoration or enters the dashboard")]
    public async Task NoPrematureSuccessAsync()
    {
        (await Page.EvaluateAsync<int>("window.__hvRestoreSuccessCount")).Should().Be(0);
        await Expect(Page.GetByText("Identity restored", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
        scenario.Faults.RejectedIdentityQueries.Should().BeGreaterThan(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }

    [When("the restored identity and its root licence complete exact live verification")]
    public async Task CompleteAsync()
    {
        await file.DecryptAsync();
        await file.ImportedAsync();
        (await Page.EvaluateAsync<int>("window.__hvRestoreSuccessCount")).Should().Be(0);
        await file.ProtectAsync();
    }

    [Then("restoration is announced once and the authenticated dashboard opens automatically")]
    public async Task SuccessAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("restoration-announcement")).ToHaveTextAsync("Identity restored");
        (await Page.EvaluateAsync<int>("window.__hvRestoreSuccessCount")).Should().Be(1);
        await Expect(Page.GetByTestId("restoration-announcement")).ToHaveAttributeAsync("aria-live", "polite");
        await Expect(Page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("restore-panel")).ToHaveCountAsync(0);
    }
}
