using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-002 -> Phase 6 Tasks 6.1/6.2, Phase 7 Task 7.2.
// Web acceptance only; no native qualification is inferred.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityPreflightSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity)
{
    [Given("Web creation starts with controlled unavailable and denied shared-worker capability")]
    public async Task ArrangeAsync()
    {
        await scenario.Page.AddInitScriptAsync("""
            (() => {
                if (window.name === 'hv-create-worker-missing') Object.defineProperty(window, 'SharedWorker', { value: undefined });
                if (window.name === 'hv-create-worker-denied') Object.defineProperty(window, 'SharedWorker', {
                    value: class { constructor() { throw new DOMException('Controlled capability denial', 'SecurityError'); } }
                });
            })();
            """);
    }

    [When("creation preflight and explicit Retry encounter those unavailable Web authorities")]
    public async Task BlockedAsync()
    {
        foreach (var mode in new[] { "hv-create-worker-missing", "hv-create-worker-denied" })
        {
            await scenario.Page.GotoAsync("/");
            await scenario.Page.EvaluateAsync("mode => { window.name = mode; }", mode);
            await scenario.Page.ReloadAsync();
            await NoCollectionAsync();
            await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Try again", Exact = true }).ClickAsync();
            await NoCollectionAsync();
        }
    }

    private async Task NoCollectionAsync()
    {
        await Expect(scenario.Page.Locator(".error-surface")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(scenario.Page.GetByLabel("Profile name / alias", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(scenario.Page.Locator("input")).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [Then("alias and secrets remain absent until real Web preflight passes and Alice creates through the live node")]
    public async Task RecoverAsync()
    {
        await scenario.Page.EvaluateAsync("() => { window.name = ''; }");
        await scenario.Page.ReloadAsync();
        // Successful production worker boot runs real crypto/transfer checks
        // and the native storage probe; creation then rechecks current custody.
        await identity.AuthenticateAsync();
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }
}
