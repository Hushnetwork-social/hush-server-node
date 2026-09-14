using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-064 -> Phase 3 Tasks 3.9/3.10, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryBackBoundarySteps(HushVotingScenario scenario, RecoveryWordEntrySteps entry,
    RecoveryRecreateSteps recreate, RecoveryProtectionSteps protection, AuthenticationSteps authentication,
    HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;

    [Given("Alice has entered recovery words before verification with observed input custody")]
    public async Task InputsAsync()
    {
        await entry.EntryAsync();
        await Page.GetByTestId("count-24").CheckAsync();
        for (var position = 1; position <= 24; position++)
            await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + position), position == 24 ? "art" : "abandon");
        await Page.EvaluateAsync("""
            () => {
                const inputs = [...document.querySelectorAll('[data-testid="word-grid"] input')];
                window.hvInputsReleased = () => {
                    const cleared = inputs.length === 24 && inputs.every(input => !input.isConnected && input.value === '');
                    inputs.length = 0;
                    return cleared;
                };
            }
            """);
    }

    [When("Alice uses root Back before verification and after both candidates are resolved")]
    public async Task UnstagedBackAsync()
    {
        await Page.GoBackAsync();
        await authentication.FirstRunChoicesAsync();
        (await Page.EvaluateAsync<bool>("() => hvInputsReleased()")).Should().BeTrue();
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        await authentication.StorageRemovedAsync();
        await FreshEmptyEntryAsync();
        await Page.GoBackAsync();
        await authentication.FirstRunChoicesAsync();

        await recreate.AbsentAsync();
        await Page.EvaluateAsync("""
            () => {
                const send = MessagePort.prototype.postMessage;
                const requests = new Set(), acknowledged = new Set(), observed = new WeakSet();
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation' && message.operation === 'destroyCandidate') {
                        requests.add(message.operationId);
                        if (!observed.has(this)) {
                            observed.add(this);
                            this.addEventListener('message', event => {
                                const result = event.data;
                                if (result?.kind === 'operation-outcome' && requests.has(result.operationId) && result.outcome === 'OK')
                                    acknowledged.add(result.operationId);
                            });
                        }
                    }
                    return Reflect.apply(send, this, args);
                };
                window.hvCandidatesDestroyed = () => requests.size === 2 && acknowledged.size === 2;
            }
            """);
        await Page.GoBackAsync();
        await authentication.FirstRunChoicesAsync();
        (await Page.EvaluateAsync<bool>("() => hvCandidatesDestroyed()")).Should().BeTrue();
        await authentication.StorageRemovedAsync();
        // Entry performs real worker inspection, which rejects residual candidate custody.
        await FreshEmptyEntryAsync();
        await Page.GoBackAsync();
        await authentication.FirstRunChoicesAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    private async Task FreshEmptyEntryAsync()
    {
        await entry.EntryAsync();
        (await Page.GetByTestId("word-grid").EvaluateAsync<bool>("grid => [...grid.querySelectorAll('input')].every(input => input.value === '')")).Should().BeTrue();
    }

    [Then("Back after protected recovery locks and preserves the same keys for real online activation")]
    public async Task StagedBackAsync()
    {
        await protection.ReadyAsync();
        try
        {
            await protection.OfflineAsync();
            await AssertProtectedKeysAsync(active: false);
            await Page.GoBackAsync();
            await authentication.SafeLockedPreviewAsync();
            await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Recovery Words") })).ToHaveCountAsync(0);
            await AssertProtectedKeysAsync(active: false);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
            scenario.Faults.IdentityUnavailable = false;
            await identity.UnlockAndBootstrapAsync();
            await AssertProtectedKeysAsync(active: true);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        }
        finally { scenario.Faults.IdentityUnavailable = false; }
    }

    private async Task AssertProtectedKeysAsync(bool active)
    {
        var facts = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        facts.KeysMatch.Should().BeTrue();
        facts.MetadataMatches.Should().BeTrue();
        facts.DevicePasswordProtected.Should().BeTrue();
        facts.Active.Should().Be(active);
    }
}
