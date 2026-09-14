using System.Text.Json;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-054 -> Phase 3 Tasks 3.3–3.6,
// Phase 5 Tasks 5.5/5.6, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityPromotionFailureSteps(HushVotingScenario scenario,
    HushVotingIdentityJourney identity, IdentitySubmissionSteps submission)
{
    private IPage Page => scenario.Page;
    private string _exact = "";

    [Given("Alice has a real accepted identity and the next local lifecycle commit will abort")]
    public async Task PendingAsync()
    {
        await Page.AddInitScriptAsync("""
            (() => {
                const send = MessagePort.prototype.postMessage;
                let promoted = 0, arm = false, release = null;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation' && message.operation === 'promoteLifecycle') {
                        promoted++; arm = true;
                    }
                    if (arm && message?.kind === 'operation' && message.operation === 'verifyOnlineIdentity') {
                        arm = false;
                        release = () => Reflect.apply(send, this, args);
                        return;
                    }
                    return Reflect.apply(send, this, args);
                };
                window.__hvPromotionFailedOnce = () => promoted === 1 && release === null;
                window.__hvPromotionRetriedLocally = () => promoted === 2 && release !== null;
                window.__hvReleasePromotionRoot = () => { const resume = release; release = null; resume?.(); };
            })();
            """);
        await submission.PendingAsync();
        _exact = scenario.Faults.SubmittedTransactions.Single();
    }

    [When("the real worker journal commit aborts and Alice retries only the local save")]
    public async Task FailAndRetryAsync()
    {
        var session = await scenario.Context.NewCDPSessionAsync(Page);
        string? workerSession = null;
        try
        {
            var targets = await session.SendAsync("Target.getTargets");
            var worker = targets!.Value.GetProperty("targetInfos").EnumerateArray().Single(target =>
                target.GetProperty("type").GetString() == "shared_worker"
                && Uri.TryCreate(target.GetProperty("url").GetString(), UriKind.Absolute, out var url)
                && url.GetLeftPart(UriPartial.Path) == scenario.BaseUrl + "/workers/vault-shared-worker.js");
            var attached = await session.SendAsync("Target.attachToTarget", new() { ["targetId"] = worker.GetProperty("targetId").GetString()!, ["flatten"] = false });
            workerSession = attached!.Value.GetProperty("sessionId").GetString()!;
            (await EvaluateWorkerAsync(session, workerSession, """
                (() => {
                    const put = IDBObjectStore.prototype.put;
                    globalThis.hvPromotionCommitAborted = false;
                    IDBObjectStore.prototype.put = function(...args) {
                        const request = Reflect.apply(put, this, args);
                        if (this.name === 'vaultJournal' && args[1] === 'current' && !globalThis.hvPromotionCommitAborted) {
                            globalThis.hvPromotionCommitAborted = true;
                            IDBObjectStore.prototype.put = put;
                            this.transaction.abort();
                        }
                        return request;
                    };
                    globalThis.hvRestorePromotionCommit = () => { IDBObjectStore.prototype.put = put; };
                    return true;
                })()
                """)).Should().BeTrue();
            await scenario.Blocks.ProduceBlockAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Could not save your identity", Exact = true })).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeEnabledAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true })).ToBeVisibleAsync();
            await submission.NoShellAsync();
            (await EvaluateWorkerAsync(session, workerSession, "globalThis.hvPromotionCommitAborted === true")).Should().BeTrue();
            var stored = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys,
                HushVotingIdentityJourney.Alias, false, expectedTransaction: _exact);
            stored.KeysMatch.Should().BeTrue();
            stored.PendingTransactionMatches.Should().BeTrue();
            stored.PendingRegistration.Should().BeTrue();
            stored.Active.Should().BeFalse();
            var queries = scenario.Faults.IdentityQueryCount;
            await Task.Delay(6_500);
            (await Page.EvaluateAsync<bool>("window.__hvPromotionFailedOnce()")).Should().BeTrue();
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
            using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(25));
            await Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await Page.EvaluateAsync<bool>("window.__hvPromotionRetriedLocally()")) await Task.Delay(25, deadline.Token);
            // Hold root's independent verification after promotion to distinguish
            // it from a forbidden repeated child lookup on the local save retry.
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
            stored = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
            stored.KeysMatch.Should().BeTrue();
            stored.Active.Should().BeTrue();
            stored.PendingTransactionCleared.Should().BeTrue();
            await Page.EvaluateAsync("window.__hvReleasePromotionRoot()");
            await baseline.WaitAsync();
            await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync();
            await scenario.Blocks.ProduceBlockAsync();
        }
        finally
        {
            await Page.EvaluateAsync("window.__hvReleasePromotionRoot?.()");
            if (workerSession is not null)
            {
                try { await EvaluateWorkerAsync(session, workerSession, "(() => { globalThis.hvRestorePromotionCommit?.(); return true; })()"); }
                finally { await session.SendAsync("Target.detachFromTarget", new() { ["sessionId"] = workerSession }); }
            }
            await session.DetachAsync();
        }
    }

    [Then("the preserved identity becomes active without resubmission and real licence indexing opens the workspace")]
    public async Task ActiveAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        (scenario.Faults.SubmittedTransactions.First() == _exact).Should().BeTrue();
    }

    // Fixed fault expressions and boolean facts only; no worker secrets leave CDP.
    private static async Task<bool> EvaluateWorkerAsync(ICDPSession session, string workerSession, string expression)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        const int id = 1;
        EventHandler<JsonElement?> handler = (sender, payload) =>
        {
            if (payload is not { } envelope || envelope.GetProperty("sessionId").GetString() != workerSession) return;
            using var response = JsonDocument.Parse(envelope.GetProperty("message").GetString()!);
            var root = response.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) return;
            completed.TrySetResult(root.TryGetProperty("result", out var result) && !result.TryGetProperty("exceptionDetails", out _)
                && result.TryGetProperty("result", out var value) && value.TryGetProperty("value", out var boolean)
                && boolean.ValueKind == JsonValueKind.True);
        };
        session.Event("Target.receivedMessageFromTarget").OnEvent += handler;
        try
        {
            await session.SendAsync("Target.sendMessageToTarget", new()
            {
                ["sessionId"] = workerSession,
                ["message"] = JsonSerializer.Serialize(new { id, method = "Runtime.evaluate", @params = new { expression, returnByValue = true } })
            });
            return await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { session.Event("Target.receivedMessageFromTarget").OnEvent -= handler; }
    }
}
