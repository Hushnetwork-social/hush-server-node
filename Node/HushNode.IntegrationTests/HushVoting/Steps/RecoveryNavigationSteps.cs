using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-065 -> Phase 3 Tasks 3.9/3.10,
// Phase 6 Tasks 6.1/6.2, Phase 7 Tasks 7.1/7.2. Web navigation evidence.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryNavigationSteps(HushVotingScenario scenario, RecoveryWordEntrySteps entry,
    RecoveryProfileSteps profile, RecoveryProtectionSteps protection, AuthenticationSteps authentication,
    HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;

    // FEAT-008 AC-008-064/065/070, Phase 3 Tasks 3.9/3.10: failed cleanup cannot authorize navigation.
    [When("real worker contention keeps recovery Back blocked until cleanup is acknowledged on Retry")]
    public async Task CleanupContentionAsync()
    {
        await profile.RestoreAsync();
        var queries = scenario.Faults.IdentityQueryCount;
        await HushVotingCleanupContention.InstallAsync(Page);
        await Page.GoBackAsync();
        await Expect(Page.Locator(".error-surface")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^(Create User|Restore Credential File|Restore Recovery Words)") })).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("candidate-list")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        (await Page.EvaluateAsync<bool>("() => { const f = hvCandidateCleanupFacts(); return f.rejected && !f.discarded && f.attempts === 1 && f.inspections === 0; }")).Should().BeTrue();
        scenario.Faults.IdentityQueryCount.Should().Be(queries);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        (await Page.EvaluateAsync<bool>("() => hvReleaseCandidateCleanup()")).Should().BeTrue();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await authentication.FirstRunChoicesAsync();
        // Both actual candidate references must be discarded, the first only after its failed attempt is retried.
        (await Page.EvaluateAsync<bool>("() => { const f = hvCandidateCleanupFacts(); return f.discarded && f.attempts === 3 && f.inspections === 1; }")).Should().BeTrue();
        scenario.Faults.IdentityQueryCount.Should().Be(queries);
        await authentication.StorageRemovedAsync();
    }

    [Given("Alice exercises Web recovery Back from entered words with opaque history")]
    public async Task InputHistoryAsync()
    {
        foreach (var browserBack in new[] { true, false })
        {
            await entry.EntryAsync();
            await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-1"), "abandon");
            await Page.EvaluateAsync("""
                () => {
                    const input = document.querySelector('#rw-1');
                    window.hvInputReleased = () => !input.isConnected && input.value === '';
                }
                """);
            var token = await TokenAsync();
            await BackAsync(browserBack);
            await authentication.FirstRunChoicesAsync();
            (await Page.EvaluateAsync<bool>("() => hvInputReleased()")).Should().BeTrue();
            scenario.Faults.IdentityQueryCount.Should().Be(0);
            await ReplayHistoryAsync(token, staged: false);
            await authentication.StorageRemovedAsync();
        }
    }

    [When("browser and in-app Back discard recovery review and reject stale or forged history")]
    public async Task ReviewHistoryAsync()
    {
        await profile.RegisteredAsync();
        foreach (var browserBack in new[] { true, false })
        {
            await profile.RestoreAsync();
            var token = await TokenAsync();
            var queries = scenario.Faults.IdentityQueryCount;
            await BackAsync(browserBack);
            await authentication.FirstRunChoicesAsync();
            await ReplayHistoryAsync(token, staged: false);
            scenario.Faults.IdentityQueryCount.Should().Be(queries, "history cannot restore a candidate or trigger another lookup");
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
            await authentication.StorageRemovedAsync();
        }
    }

    [Then("both Web Back controls preserve staged keys behind inspection until fresh exact online unlock")]
    public async Task StagedHistoryAsync()
    {
        try
        {
            foreach (var browserBack in new[] { true, false })
            {
                await profile.RestoreAsync();
                await profile.ConfirmAsync();
                var token = await TokenAsync();
                await protection.OfflineAsync();
                await BackAsync(browserBack);
                await AssertGateAsync(staged: true);
                await ReplayHistoryAsync(token, staged: true);
                var facts = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
                facts.KeysMatch.Should().BeTrue();
                facts.MetadataMatches.Should().BeTrue();
                facts.Active.Should().BeFalse();
                scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
                scenario.Faults.IdentityUnavailable = false;
                if (browserBack)
                {
                    // Explicit removal, not Back, authorizes deletion before the second control is exercised.
                    await authentication.RemoveAsync();
                    await authentication.StorageRemovedAsync();
                }
            }
            var queries = scenario.Faults.IdentityQueryCount;
            await identity.UnlockAndBootstrapAsync();
            scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(queries);
            var lookup = scenario.Faults.IdentityLookups.Last();
            if (!lookup.Reply.Successfull || lookup.SigningAddress != identity.Keys.SigningPublicKey
                || lookup.Reply.PublicSigningAddress != identity.Keys.SigningPublicKey
                || lookup.Reply.PublicEncryptAddress != identity.Keys.EncryptPublicKey)
                throw new InvalidOperationException("Navigation recovery did not freshly verify the original exact public pair.");
            scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        }
        finally { scenario.Faults.IdentityUnavailable = false; }
    }

    private async Task<string> TokenAsync()
    {
        new Uri(Page.Url).AbsolutePath.Should().Be("/");
        new Uri(Page.Url).Query.Should().BeEmpty();
        return await Page.EvaluateAsync<string>("() => history.state.hvToken");
    }

    private async Task BackAsync(bool browserBack)
    {
        if (browserBack) await Page.GoBackAsync();
        else await Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
    }

    private async Task ReplayHistoryAsync(string oldToken, bool staged)
    {
        foreach (var token in new[] { oldToken, "forged-recovery-token" })
        {
            // Exercise actual history traversal; preserve Next.js router markers.
            await Page.EvaluateAsync("token => history.pushState({...history.state, hvToken:token}, '', '/')", token);
            await Page.GoBackAsync();
            await AssertGateAsync(staged);
            await Page.GoForwardAsync();
            await AssertGateAsync(staged);
        }
        await Page.ReloadAsync();
        await AssertGateAsync(staged);
        await Page.GotoAsync("/?onboarding=restoreRecoveryWords&phase=candidateReview");
        await AssertGateAsync(staged);
        await TokenAsync();
    }

    private async Task AssertGateAsync(bool staged)
    {
        if (staged) await authentication.SafeLockedPreviewAsync();
        else await authentication.FirstRunChoicesAsync();
        await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("candidate-list")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
        if (staged) await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Recovery Words") })).ToHaveCountAsync(0);
    }
}
