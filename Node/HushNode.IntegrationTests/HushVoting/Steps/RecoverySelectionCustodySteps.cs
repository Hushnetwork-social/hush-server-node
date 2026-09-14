using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-021 -> Phase 3 Tasks 3.5/3.6, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoverySelectionCustodySteps(HushVotingScenario scenario, RecoveryRecreateSteps recreate)
{
    [Given("Alice resolves both approved recovery candidates with a disposal observer")]
    public async Task ReadyAsync()
    {
        await recreate.AbsentAsync();
        await scenario.Page.EvaluateAsync("""
            () => {
                const send = MessagePort.prototype.postMessage;
                let port, discarded, disposed = false, replay = null, provisions = 0, selectedOnly = true, unexpectedDisposal = false;
                const observed = new WeakSet();
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation' && message.operation === 'destroyCandidate') {
                        if (discarded) { unexpectedDisposal = true; return Reflect.apply(send, this, args); }
                        port = this;
                        discarded = { ...message, payload: { ...message.payload } };
                        if (!observed.has(this)) {
                            observed.add(this);
                            this.addEventListener('message', event => {
                                const result = event.data;
                                if (result?.kind !== 'operation-outcome') return;
                                if (result.operationId === discarded.operationId) disposed = result.outcome === 'OK';
                                if (result.operationId === discarded.operationId + '-negative') replay = { outcome: result.outcome, reason: result.payload?.reason };
                            });
                        }
                    }
                    if (message?.kind === 'operation' && message.operation === 'provisionFromValidatedBundle') {
                        provisions++;
                        selectedOnly &&= Boolean(discarded) && message.payload?.candidateRef !== discarded.payload.candidateRef;
                    }
                    return Reflect.apply(send, this, args);
                };
                window.hvCheckDiscardedCandidate = async () => {
                    if (!disposed || !discarded || !port) return false;
                    // A negative request reaches the unchanged real worker; only public command metadata is retained.
                    Reflect.apply(send, port, [{ ...discarded, operationId: discarded.operationId + '-negative' }]);
                    const deadline = Date.now() + 5000;
                    while (replay === null && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 20));
                    return replay?.outcome === 'INVALID_INPUT' && replay.reason === 'unknown-candidate';
                };
                window.hvSelectedCandidateProvisioned = () => disposed && !unexpectedDisposal && selectedOnly && provisions === 1;
            }
            """);
    }

    [When("Alice selects one candidate and the worker acknowledges disposal of the other")]
    public async Task SelectAsync()
    {
        await recreate.SelectAsync();
        (await scenario.Page.EvaluateAsync<bool>("() => hvCheckDiscardedCandidate()")).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [Then("only the selected recovered keys enter the vault and complete real registration")]
    public async Task ProtectSelectedAsync()
    {
        await recreate.ReviewAsync();
        await recreate.RegisterAsync();
        (await scenario.Page.EvaluateAsync<bool>("() => hvSelectedCandidateProvisioned()")).Should().BeTrue();
        var selected = HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art")));
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(scenario.Page, selected, "Recovered voting identity", true)).Should().BeTrue();
    }
}
