using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-024 -> Phase 7 Tasks 7.1/7.2.
// Current migration target is Web; this does not qualify native adapters.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class SealedOperationBoundarySteps(HushVotingScenario scenario,
    IdentityProtectionBoundarySteps protection, IdentitySubmissionSteps submission)
{
    [Given("Alice protects a Web identity while only public worker operation metadata is observed")]
    public async Task ReadyAsync()
    {
        await scenario.Page.AddInitScriptAsync("""
            (() => {
                const send = MessagePort.prototype.postMessage, listening = new WeakSet();
                const operations = new Set(), rejectedIds = new Set(), barriers = new Map();
                let port, binding, unexpectedReply = false;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation') {
                        // Do not retain payloads, secret transfers or secret-bearing outcomes.
                        port = this;
                        binding = { clientChannel: message.clientChannel, authorityEpoch: message.authorityEpoch };
                        operations.add(message.operation);
                        if (!listening.has(this)) {
                            listening.add(this);
                            this.addEventListener('message', event => {
                                const reply = event.data;
                                if (rejectedIds.has(reply?.operationId)) unexpectedReply = true;
                                if (barriers.has(reply?.operationId) && reply.kind === 'operation-outcome')
                                    barriers.set(reply.operationId, reply.outcome);
                            });
                        }
                    }
                    return Reflect.apply(send, this, args);
                };
                const storage = async () => {
                    const db = await new Promise((resolve, reject) => {
                        const request = indexedDB.open('hushvoting-vault');
                        request.onsuccess = () => resolve(request.result);
                        request.onerror = () => reject(new Error('Sealed boundary storage unavailable'));
                    });
                    try {
                        const result = {};
                        for (const name of ['vaultSlots', 'vaultJournal', 'operationalSidecars']) {
                            result[name] = await new Promise((resolve, reject) => {
                                const tx = db.transaction(name, 'readonly'), rows = [];
                                const cursor = tx.objectStore(name).openCursor();
                                cursor.onsuccess = () => {
                                    const value = cursor.result;
                                    if (value) { rows.push([value.key, value.value]); value.continue(); }
                                };
                                tx.oncomplete = () => resolve(rows);
                                tx.onerror = () => reject(new Error('Sealed boundary read failed'));
                            });
                        }
                        return JSON.stringify(result, (_, value) => value instanceof Uint8Array ? Array.from(value) : value);
                    } finally { db.close(); }
                };
                window.__hvProbeSealedBoundary = async () => {
                    if (!port || !binding) throw new Error('No actual worker operation observed');
                    const before = await storage();
                    for (const operation of ['genericSign', 'genericDecrypt', 'exportPrivateKey']) {
                        const operationId = 'sealed-negative-' + crypto.randomUUID();
                        rejectedIds.add(operationId);
                        Reflect.apply(send, port, [{ kind: 'operation', operation, operationVersion: 1, ...binding, operationId }]);
                    }
                    // The same MessagePort is FIFO: an actual permitted response is
                    // the processing barrier after the three silently dropped requests.
                    const operationId = 'sealed-barrier-' + crypto.randomUUID();
                    barriers.set(operationId, null);
                    Reflect.apply(send, port, [{ kind: 'operation', operation: 'inspectStartup', operationVersion: 1, ...binding, operationId }]);
                    const deadline = Date.now() + 10000;
                    while (barriers.get(operationId) === null) {
                        if (Date.now() > deadline) throw new Error('Sealed boundary processing barrier timed out');
                        await new Promise(resolve => setTimeout(resolve, 20));
                    }
                    return barriers.get(operationId) === 'OK' && !unexpectedReply && before === await storage();
                };
                window.__hvUsedSealedCreation = () => ['createCandidate', 'provisionFromValidatedBundle',
                    'submitIdentityTransaction', 'verifyOnlineIdentity'].every(operation => operations.has(operation));
            })();
            """);
        await protection.SequenceAsync();
        await protection.ProtectAsync();
    }

    [When("generic signing decryption and private-key export requests reach her real Web worker")]
    public async Task ProbeAsync()
    {
        var queries = scenario.Faults.IdentityQueryCount;
        var submissions = scenario.Faults.SubmittedTransactions.Count;
        (await scenario.Page.EvaluateAsync<bool>("window.__hvProbeSealedBoundary()")).Should().BeTrue(
            "unknown operations must be dropped before the permitted processing barrier without storage mutation");
        scenario.Faults.IdentityQueryCount.Should().Be(queries);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(submissions);
    }

    [Then("the requests produce no result or storage mutation and sealed creation completes real identity and licence indexing")]
    public async Task CompleteAsync()
    {
        // Stay in this page: the generic register/unlock helper deliberately
        // reloads after Lock, which would discard this page's metadata observer.
        await submission.SubmitAsync();
        await submission.ConfirmAsync();
        (await scenario.Page.EvaluateAsync<bool>("window.__hvUsedSealedCreation()")).Should().BeTrue();
        await ProbeAsync();
    }
}
