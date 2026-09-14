using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-019 -> Phase 7 Task 7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityProvisionCapabilitySteps(HushVotingScenario scenario,
    IdentityProtectionBoundarySteps protection)
{
    [Given("Alice reaches device protection with a real worker capability observer")]
    public async Task ReadyAsync()
    {
        await scenario.Page.AddInitScriptAsync("""
            (() => {
                const send = MessagePort.prototype.postMessage;
                const listening = new WeakSet();
                let port, request, operation, issued, issueCount = 0, successful = false;
                const outcomes = new Map(), issuances = [];
                MessagePort.prototype.postMessage = function(...args) {
                    if (!listening.has(this)) {
                        listening.add(this);
                        this.addEventListener('message', event => {
                            const message = event.data;
                            if (message?.kind === 'capability-issued') {
                                const fact = { ...message, receivedAtMs: Date.now() };
                                issuances.push(fact);
                                if (!issued && message.purpose === 'provision') issued = fact;
                            }
                            if (message?.kind === 'operation-outcome') {
                                if (message.operationId === operation?.operationId) successful = message.outcome === 'OK';
                                if (message.operationId?.startsWith('cap-test-')) outcomes.set(message.operationId, message.outcome);
                            }
                        });
                    }
                    const message = args[0];
                    if (message?.kind === 'issue-capability' && message.purpose === 'provision') {
                        issueCount++;
                        if (!request) { request = { ...message }; port = this; }
                    }
                    // This is the public command envelope, never secret-transfer/value.
                    if (message?.kind === 'operation' && message.operation === 'provisionFromValidatedBundle' && !operation)
                        operation = structuredClone(message);
                    return Reflect.apply(send, this, args);
                };
                const until = async predicate => {
                    const deadline = Date.now() + 10000;
                    while (!predicate()) {
                        if (Date.now() >= deadline) throw new Error('Capability probe timed out');
                        await new Promise(resolve => setTimeout(resolve, 20));
                    }
                };
                const invoke = async capabilityId => {
                    const operationId = 'cap-test-' + crypto.randomUUID();
                    Reflect.apply(send, port, [{ ...operation, operationId, freshCapabilityId: capabilityId }]);
                    await until(() => outcomes.has(operationId));
                    const outcome = outcomes.get(operationId); outcomes.delete(operationId);
                    return outcome;
                };
                const issue = async purpose => {
                    const offset = issuances.length;
                    Reflect.apply(send, port, [{ ...request, purpose }]);
                    await until(() => issuances.length > offset);
                    return issuances[offset];
                };
                window.__hvCapabilityFacts = () => ({
                    oneSuccessfulProvision: issueCount === 1 && successful,
                    bindingMatches: issued?.purpose === 'provision' && issued.clientChannel === request?.clientChannel
                        && operation?.clientChannel === request?.clientChannel && operation.authorityEpoch === request?.authorityEpoch
                        && operation.freshCapabilityId === issued.capabilityId,
                    boundedLifetime: issued?.expiresAtMs > issued?.receivedAtMs && issued.expiresAtMs - issued.receivedAtMs <= 60000
                });
                window.__hvReplayCapability = () => invoke(issued.capabilityId);
                window.__hvWrongPurposeCapability = async () => invoke((await issue('changePassword')).capabilityId);
                window.__hvExpiredCapability = async () => {
                    const fresh = await issue('provision');
                    const remaining = fresh.expiresAtMs - Date.now();
                    if (!(remaining > 0 && remaining <= 60000)) throw new Error('Capability lifetime is not bounded');
                    // Actual worker wall time, not a forged client clock or reply.
                    await new Promise(resolve => setTimeout(resolve, remaining + 25));
                    return invoke(fresh.capabilityId);
                };
            })();
            """);
        await protection.SequenceAsync();
    }

    [When("Alice validates her device password and provisions the identity through the ordinary UI")]
    public async Task ProvisionAsync() => await protection.ProtectAsync();

    [Then("one successful provisioning command uses its issued purpose channel epoch and bounded lifetime")]
    public async Task BoundAsync()
    {
        (await scenario.Page.EvaluateAsync<bool>("() => Object.values(window.__hvCapabilityFacts()).every(value => value === true)")).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
    }

    [Then("the real worker rejects capability replay wrong purpose and expiry without replacing the protected identity")]
    public async Task RejectAsync()
    {
        (await scenario.Page.EvaluateAsync<string>("window.__hvReplayCapability()")).Should().Be("AUTHORITY_REJECTED");
        (await scenario.Page.EvaluateAsync<string>("window.__hvWrongPurposeCapability()")).Should().Be("AUTHORITY_REJECTED");
        (await scenario.Page.EvaluateAsync<string>("window.__hvExpiredCapability()")).Should().Be("AUTHORITY_REJECTED");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        await protection.RegisterAsync();
    }
}
