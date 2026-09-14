using System.Collections.Concurrent;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-063 -> Phase 6 Tasks 6.1/6.2, Phase 7 Tasks 7.1/7.2.
// Web runtime evidence. Native static-build exclusion remains separately qualified.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class WebCreationAuthoritySteps(HushVotingScenario scenario, HushVotingIdentityJourney identity)
{
    private readonly ConcurrentQueue<(string Origin, string Path, string Method)> _requests = new();

    [Given("the ordinary Web creation worker and page storage boundary are observed")]
    public async Task ObserveAsync()
    {
        scenario.Context.Request += (_, request) =>
        {
            var uri = new Uri(request.Url);
            if (uri.Scheme is "http" or "https")
                _requests.Enqueue((uri.GetLeftPart(UriPartial.Authority), uri.AbsolutePath, request.Method));
        };
        await scenario.Page.AddInitScriptAsync("""
            (() => {
                let workers = 0, wrongWorker = false, pageWrites = 0;
                const operations = new Set();
                const worker = globalThis.SharedWorker;
                globalThis.SharedWorker = new Proxy(worker, {
                    construct(target, args, newTarget) {
                        const url = new URL(String(args[0]), location.href);
                        workers++;
                        wrongWorker ||= url.origin !== location.origin || url.pathname !== '/workers/vault-shared-worker.js';
                        return Reflect.construct(target, args, newTarget);
                    }
                });
                const send = MessagePort.prototype.postMessage;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation') operations.add(message.operation);
                    return Reflect.apply(send, this, args);
                };
                for (const method of ['put', 'add', 'delete', 'clear']) {
                    const original = IDBObjectStore.prototype[method];
                    IDBObjectStore.prototype[method] = function(...args) {
                        if (this.transaction.db.name === 'hushvoting-vault') pageWrites++;
                        return Reflect.apply(original, this, args);
                    };
                }
                window.hvWebAuthorityFacts = () => ({ workers, wrongWorker, pageWrites, operations: [...operations] });
            })();
            """);
    }

    [When("Alice creates protects and activates an identity through that Web authority")]
    public async Task CreateAsync()
    {
        // ProvisionAsync reloads after indexing; capture first-page authority facts
        // before that reload so observation covers actual candidate creation/storage.
        var words = await identity.GenerateCandidateAsync();
        await identity.ConfirmRecoveryAndProtectAsync(words);
        await AssertWorkerAsync("createCandidate", "provisionFromValidatedBundle");
        var staged = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        (staged.KeysMatch && staged.ConcreteKeysOnly && staged.DevicePasswordProtected && staged.PendingRegistration).Should().BeTrue();
        staged.Active.Should().BeFalse();
        await identity.SubmitAndIndexAsync();
        await identity.UnlockAndBootstrapAsync();
        await AssertWorkerAsync("unlockPassword");
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(
            scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false)).Should().BeTrue();
    }

    [Then("the Web worker owns encrypted custody and real node requests use the same-origin BFF")]
    public void VerifyTransport()
    {
        _requests.Should().NotBeEmpty();
        _requests.All(r => r.Origin == scenario.BaseUrl).Should().BeTrue("the browser must not address the node or another origin directly");
        var api = _requests.Where(r => r.Path.StartsWith("/api/", StringComparison.Ordinal)).ToArray();
        api.Should().NotBeEmpty();
        api.All(r => r.Method == "POST").Should().BeTrue();
        api.Any(r => r.Path == "/api/identity").Should().BeTrue();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
    }

    private async Task AssertWorkerAsync(params string[] requiredOperations)
    {
        var facts = await scenario.Page.EvaluateAsync<int[]>("required => { const f = hvWebAuthorityFacts(); return [f.workers, Number(f.wrongWorker), f.pageWrites, required.filter(op => !f.operations.includes(op)).length]; }", requiredOperations);
        facts[0].Should().BeGreaterThan(0);
        facts[1].Should().Be(0, "the actual worker must be the same-origin vault authority");
        facts[2].Should().Be(0, "only the worker may mutate the credential vault");
        facts[3].Should().Be(0, "the required custody operations must cross the worker boundary");
    }
}
