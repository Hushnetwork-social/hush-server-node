using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityNavigationSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity, IdentitySubmissionSteps submission, AuthenticationSteps authentication)
{
    private string? _oldToken;

    // FEAT-007 cleanup portions of AC-007-055/060; FEAT-010 AC-010-084.
    [Given("Alice retries rejected creation cleanup before starting a fresh provisional review")]
    public async Task CleanupBeforeReviewAsync()
    {
        await identity.GenerateCandidateAsync();
        var discardedKeys = identity.Keys;
        await HushVotingCleanupContention.InstallAsync(scenario.Page);
        await scenario.Page.GoBackAsync();
        await Expect(scenario.Page.Locator(".error-surface")).ToBeVisibleAsync();
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync();
        await Expect(scenario.Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Create User|Restore Credential File|Restore Recovery Words)") })).ToHaveCountAsync(0);
        (await scenario.Page.EvaluateAsync<bool>("() => { const f = hvCandidateCleanupFacts(); return f.rejected && !f.discarded && f.attempts === 1 && f.inspections === 0; }")).Should().BeTrue();
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        (await scenario.Page.EvaluateAsync<bool>("() => hvReleaseCandidateCleanup()")).Should().BeTrue();
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync();
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await authentication.FirstRunChoicesAsync();
        (await scenario.Page.EvaluateAsync<bool>("() => { const f = hvCandidateCleanupFacts(); return f.discarded && f.attempts === 2 && f.inspections === 1; }")).Should().BeTrue();
        await authentication.StorageRemovedAsync();
        await StagedReviewAsync();
        if (identity.Keys.SigningPublicKey == discardedKeys.SigningPublicKey || identity.Keys.EncryptPublicKey == discardedKeys.EncryptPublicKey)
            throw new InvalidOperationException("Fresh creation reused the abandoned candidate.");
    }

    [Given("Alice's creation review has a real encrypted provisional vault")]
    public async Task StagedReviewAsync()
    {
        await submission.ReviewAsync();
        _oldToken = await scenario.Page.EvaluateAsync<string>("() => history.state.hvToken");
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.KeysMatch.Should().BeTrue();
        stored.Active.Should().BeFalse();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [When("Alice checks the provisional review and then leaves it without submitting")]
    public async Task ProvisionalGuardAsync()
    {
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Create User|^Restore Credential File|^Restore Recovery Words") })).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        await scenario.Page.GoBackAsync();
        await LockedAsync();
    }

    [When("Alice navigates Back then Forward and reloads the provisional creation history")]
    public async Task HistoryAsync()
    {
        await scenario.Page.GoBackAsync();
        await LockedAsync();
        await scenario.Page.GoForwardAsync();
        await LockedAsync();
        await scenario.Page.ReloadAsync();
        await LockedAsync();
    }

    [When("Alice presents stale and forged creation tokens and manually navigates to a creation query")]
    public async Task ForgedAsync()
    {
        await scenario.Page.GoBackAsync();
        await LockedAsync();
        foreach (var token in new[] { _oldToken, "forged-create-user-token" })
        {
            await scenario.Page.EvaluateAsync("token => { history.pushState({...history.state, hvToken:token}, '', '/'); dispatchEvent(new PopStateEvent('popstate', {state:history.state})); }", token);
            await LockedAsync();
        }
        await scenario.Page.GotoAsync("/?onboarding=create&phase=recovery");
        await LockedAsync();
        authentication.RootOnlyUrl();
    }

    private async Task LockedAsync()
    {
        await authentication.SafeLockedPreviewAsync();
        await Expect(scenario.Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Create User|^Restore Credential File|^Restore Recovery Words|^Create HushNetwork identity") })).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [Then("the same provisional keys stay locked until independent registration and a fresh online unlock")]
    public async Task SameIdentityAsync()
    {
        await LockedAsync();
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.KeysMatch.Should().BeTrue();
        stored.ConcreteKeysOnly.Should().BeTrue();
        await HushVotingServerIdentity.RegisterAsync(scenario, identity.Keys, HushVotingIdentityJourney.Alias);
        await identity.UnlockAndBootstrapAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }
}
