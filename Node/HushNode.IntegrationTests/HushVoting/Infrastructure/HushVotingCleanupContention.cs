using Microsoft.Playwright;

namespace HushVoting.IntegrationTests.Infrastructure;

// FEAT-007/008 cleanup tests: actual worker contention, no fabricated outcome.
internal static class HushVotingCleanupContention
{
    public static Task InstallAsync(IPage page)
    {
        return page.EvaluateAsync("""
            () => {
                const send = MessagePort.prototype.postMessage, listening = new WeakSet();
                let blocked = null, blockerOutcome = null, rejected = false, discarded = false, attempts = 0, inspections = 0;
                const observe = port => {
                    if (listening.has(port)) return;
                    listening.add(port);
                    port.addEventListener('message', event => {
                        const result = event.data;
                        if (result?.kind !== 'operation-outcome' || !blocked) return;
                        if (result.operationId === blocked.blockerId) blockerOutcome = result.outcome;
                        if (result.operationId === blocked.cleanupId && result.outcome === 'AUTHORITY_BUSY') rejected = true;
                        if (result.operationId === blocked.retryId && result.outcome === 'OK') discarded = true;
                    });
                };
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation' && message.operation === 'inspectStartup') inspections++;
                    if (message?.kind === 'operation' && message.operation === 'destroyCandidate') {
                        observe(this); attempts++;
                        if (blocked === null) {
                            // The real worker waits for a public test password; cleanup must receive actual BUSY.
                            const blockerId = 'recovery-cleanup-contention-' + crypto.randomUUID();
                            blocked = { port: this, blockerId, cleanupId: message.operationId,
                                channel: message.clientChannel, epoch: message.authorityEpoch };
                            Reflect.apply(send, this, [{ kind: 'operation', operation: 'unlockPassword', operationVersion: 1,
                                operationId: blockerId, clientChannel: message.clientChannel, authorityEpoch: message.authorityEpoch }]);
                        } else blocked.retryId = message.operationId;
                    }
                    return Reflect.apply(send, this, args);
                };
                window.hvCandidateCleanupFacts = () => ({ rejected, discarded, attempts, inspections });
                window.hvReleaseCandidateCleanup = async () => {
                    if (!blocked) return false;
                    Reflect.apply(send, blocked.port, [{ kind: 'secret-transfer', operationId: blocked.blockerId,
                        clientChannel: blocked.channel, authorityEpoch: blocked.epoch, purpose: 'devicePassword',
                        value: 'public-test-contention-release' }]);
                    const deadline = Date.now() + 5000;
                    while (blockerOutcome === null && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 20));
                    return blockerOutcome === 'CORRUPT_VAULT';
                };
            }
            """);
    }
}
