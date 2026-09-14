using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-063 -> Phase 3 Tasks 3.7/3.8, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryResumeLookupSteps(HushVotingScenario scenario, RecoveryProtectionSteps protection,
    AuthenticationSteps authentication, HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;
    private string _candidateRef = "";
    private int _requests;

    [Given("Alice has protected recovered keys awaiting final verification in a restartable browser")]
    public async Task StageAsync()
    {
        await scenario.UseRestartableBrowserAsync();
        await protection.ReadyAsync();
        await Page.EvaluateAsync("""
            () => {
                const send = MessagePort.prototype.postMessage;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation' && message.operation === 'provisionFromValidatedBundle')
                        window.hvStagedCandidateRef = message.payload?.candidateRef;
                    return Reflect.apply(send, this, args);
                };
            }
            """);
        await protection.OfflineAsync();
        _candidateRef = await Page.EvaluateAsync<string>("() => window.hvStagedCandidateRef");
        string.IsNullOrWhiteSpace(_candidateRef).Should().BeFalse();
        var facts = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        (facts.KeysMatch && facts.ConcreteKeysOnly && !facts.Active).Should().BeTrue();
    }

    [When("the browser process crashes and recovery resumes from its encrypted storage")]
    public async Task RestartAsync()
    {
        await scenario.CrashAndRestartBrowserAsync();
        await Page.AddInitScriptAsync("""
            (() => {
                const send = MessagePort.prototype.postMessage, observed = new WeakSet();
                let port, envelope, negativeId, negative = null, forbidden = 0, wordsReturned = false;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'secret-transfer' && message.purpose === 'mnemonic') forbidden++;
                    if (message?.kind === 'operation') {
                        port = this; envelope = { ...message, payload: undefined };
                        if (['createCandidate','deriveRecoveryCandidates'].includes(message.operation)) forbidden++;
                        if (!observed.has(this)) {
                            observed.add(this);
                            this.addEventListener('message', event => {
                                const result = event.data;
                                if (result?.kind !== 'operation-outcome') return;
                                wordsReturned ||= Array.isArray(result.payload?.words);
                                if (result.operationId === negativeId)
                                    negative = result.outcome === 'INVALID_INPUT' && result.payload?.reason === 'unknown-candidate';
                            });
                        }
                    }
                    return Reflect.apply(send, this, args);
                };
                window.hvCannotRevealOldWords = async candidateRef => {
                    if (!port || !envelope) return false;
                    negative = null; negativeId = 'hv-resume-negative-' + crypto.randomUUID();
                    Reflect.apply(send, port, [{ ...envelope, operation: 'revealCandidateWords', operationId: negativeId, payload: { candidateRef } }]);
                    const deadline = Date.now() + 5000;
                    while (negative === null && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 20));
                    return negative === true && forbidden === 0 && !wordsReturned;
                };
            })();
            """);
        await Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
        await NoWordsAsync();
        _requests = scenario.Faults.RequestMethods.Count;
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }

    [Then("resumed recovery looks up the original public keys first and activates without reconstructing words")]
    public async Task ActivateAsync()
    {
        scenario.Faults.IdentityUnavailable = false;
        await identity.UnlockAndBootstrapAsync();
        var requests = scenario.Faults.RequestMethods.Skip(_requests).ToArray();
        requests.First(method => method is "GetIdentity" or "SubmitSignedTransaction").Should().Be("GetIdentity",
            "resumption must query the real profile before any transaction submission; read-only height queries are independent");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "resumption must not recreate the existing identity");
        protection.FreshPair();
        await NoWordsAsync();
        var facts = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        (facts.KeysMatch && facts.MetadataMatches && facts.ConcreteKeysOnly && facts.Active && facts.PendingTransactionCleared).Should().BeTrue();
    }

    private async Task NoWordsAsync()
    {
        await Expect(Page.GetByTestId("word-grid")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("recovery-list")).ToHaveCountAsync(0);
        (await Page.EvaluateAsync<bool>("ref => window.hvCannotRevealOldWords(ref)", _candidateRef)).Should().BeTrue();
    }
}
