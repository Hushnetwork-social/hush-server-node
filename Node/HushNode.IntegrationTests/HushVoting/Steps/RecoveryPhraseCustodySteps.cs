using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-016 -> Phase 5 Tasks 5.1/5.2, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryPhraseCustodySteps(HushVotingScenario scenario, RecoveryWordEntrySteps entry,
    AuthenticationSteps authentication, RecoveryRecreateSteps recreate)
{
    private IPage Page => scenario.Page;

    [Given("Alice enters controlled recovery words with a bounded handoff observer")]
    public async Task ArrangeAsync()
    {
        await Page.AddInitScriptAsync("""
            (() => {
                const facts = { transfers: 0, derives: 0, normalized: true };
                window.hvPhraseCustody = facts;
                const post = MessagePort.prototype.postMessage;
                MessagePort.prototype.postMessage = function(message, ...rest) {
                    if (message?.kind === 'secret-transfer' && message.purpose === 'mnemonic') {
                        facts.transfers++;
                        // Compare only the public BIP39 test vector; retain no secret payload.
                        facts.normalized &&= message.value === [...Array(23).fill('abandon'), 'art'].join(' ');
                    }
                    if (message?.kind === 'operation' && message.operation === 'deriveRecoveryCandidates') facts.derives++;
                    return Reflect.apply(post, this, [message, ...rest]);
                };
            })();
            """);
        await entry.EntryAsync();
        await Page.GetByTestId("count-24").CheckAsync();
        for (var position = 1; position <= 24; position++)
            await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + position), position == 24 ? " ＡＲＴ " : " ＡＢＡＮＤＯＮ ");
        await Page.EvaluateAsync("""
            () => {
                const inputs = [...document.querySelectorAll('[data-testid="word-grid"] input')];
                window.hvCheckReleasedInputs = () => {
                    const cleared = inputs.length === 24 && inputs.every(input => !input.isConnected && input.value === '');
                    inputs.length = 0;
                    return cleared;
                };
            }
            """);
    }

    [When("Alice verifies once and the real worker resolves the recovered candidates")]
    public async Task VerifyAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("candidate-list").Locator("li")).ToHaveCountAsync(2);
        scenario.Faults.IdentityQueryCount.Should().Be(2);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [Then("one normalized phrase reaches the worker and cleared page inputs cannot return through history")]
    public async Task ClearedAsync()
    {
        (await Page.EvaluateAsync<bool>("() => hvPhraseCustody.transfers === 1 && hvPhraseCustody.derives === 1 && hvPhraseCustody.normalized && hvCheckReleasedInputs()")).Should().BeTrue();
        await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
        await Page.GoBackAsync();
        await authentication.FirstRunChoicesAsync();
        await Page.GoForwardAsync();
        (await Page.EvaluateAsync<bool>("() => [...document.querySelectorAll('[data-testid=word-grid] input')].every(input => input.value === '') && !JSON.stringify(history.state).includes('abandon') && !location.href.includes('abandon')")).Should().BeTrue();
        await Page.ReloadAsync();
        await entry.EntryAsync();
        (await Page.GetByTestId("word-grid").EvaluateAsync<bool>("grid => [...grid.querySelectorAll('input')].every(input => input.value === '')")).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        // A new explicit entry must use the real worker/node again and complete the full journey.
        await recreate.AbsentAsync();
        await recreate.SelectAsync();
        await recreate.ReviewAsync();
        await recreate.RegisterAsync();
    }
}
