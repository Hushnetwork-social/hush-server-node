using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-071 -> Phase 4/5 presentation and Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoverySuccessSteps(HushVotingScenario scenario, RecoveryProfileSteps recovery, AuthenticationSteps authentication)
{
    private IPage Page => scenario.Page;
    private ILocator Announcement => Page.GetByTestId("restoration-announcement");

    [Given("Alice has reviewed her registered recovery identity and the root announcement is observed")]
    public async Task ReviewAsync()
    {
        await recovery.RegisteredAsync();
        await recovery.RestoreAsync();
        await recovery.ConfirmAsync();
        await Expect(Announcement).ToHaveCountAsync(1);
        await Expect(Announcement).ToBeEmptyAsync();
        await Page.EvaluateAsync("""
            () => {
                const region = document.querySelector('[data-testid="restoration-announcement"]');
                let announcements = 0;
                const observer = new MutationObserver(records => {
                    announcements += records.filter(record => [...record.addedNodes]
                        .some(node => node.textContent === 'Identity restored')).length;
                });
                observer.observe(region, { childList: true });
                window.__hvRecoveryAnnouncements = () => ({ announcements,
                    stable: region === document.querySelector('[data-testid="restoration-announcement"]') });
                window.__hvStopRecoveryAnnouncements = () => observer.disconnect();
            }
            """);
    }

    [When("separate device protection waits for actual identity verification and indexed licence access")]
    public async Task VerifyAsync()
    {
        await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        var lookups = scenario.Faults.IdentityLookups.Count;
        scenario.Faults.HoldIdentityQueries();
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        try
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await scenario.Faults.IdentityQueryArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            scenario.Faults.IdentityLookups.Count.Should().Be(lookups);
            await Expect(Announcement).ToBeEmptyAsync();
            await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        }
        finally { scenario.Faults.ReleaseIdentityQueries(); }
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await baseline.WaitAsync();
        scenario.Faults.IdentityLookups.Count.Should().BeGreaterThan(lookups);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        await Expect(Announcement).ToBeEmptyAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await scenario.Blocks.ProduceBlockAsync();
    }

    [Then("recovery announces success once and enters the dashboard without another Continue action")]
    public async Task AutomaticSuccessAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Announcement).ToHaveTextAsync("Identity restored");
        await Expect(Announcement).ToHaveAttributeAsync("role", "status");
        await Expect(Announcement).ToHaveAttributeAsync("aria-live", "polite");
        await Expect(Announcement).ToHaveAttributeAsync("aria-atomic", "true");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = HushVotingIdentityJourney.Alias, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("backup-preservation-notice")).ToHaveCountAsync(0);
        await OnceAsync();
        authentication.RootOnlyUrl();
    }

    [Then("ordinary Lock and unlock do not replay the recovery announcement")]
    public async Task NoReplayAsync()
    {
        try
        {
            await authentication.LockAsync();
            await authentication.SafeLockedPreviewAsync();
            await Expect(Announcement).ToBeEmptyAsync();
            await authentication.AttemptVerificationAsync();
            await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Announcement).ToBeEmptyAsync();
            scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
            await OnceAsync();
        }
        finally { await Page.EvaluateAsync("() => window.__hvStopRecoveryAnnouncements?.()"); }
    }

    private async Task OnceAsync() => (await Page.EvaluateAsync<bool>("() => { const value = window.__hvRecoveryAnnouncements(); return value.stable && value.announcements === 1; }")).Should().BeTrue();
}
