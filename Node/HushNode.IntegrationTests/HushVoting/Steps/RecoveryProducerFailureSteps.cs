using System.Text.Json;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-008 AC-008-020 -> Phase 3 Tasks 3.1/3.2, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryProducerFailureSteps(HushVotingScenario scenario,
    RecoveryWordEntrySteps entry, RecoveryRecreateSteps recreate)
{
    private IPage Page => scenario.Page;
    private bool _rejected;

    [Given("Alice enters valid recovery words before a controlled worker producer failure")]
    public async Task ReadyAsync()
    {
        await entry.EntryAsync();
        foreach (var position in Enumerable.Range(1, 24))
            await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + position), position == 24 ? "art" : "abandon");
    }

    [When("the second applicable producer encounters an actual encoding exception")]
    public async Task FailProducerAsync()
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
                    const encode = TextEncoder.prototype.encode;
                    const facts = { firstProducerCompletedEncoding: false, failed: false };
                    globalThis.hvProducerFault = facts;
                    TextEncoder.prototype.encode = function(value) {
                        if (value === 'encryption') facts.firstProducerCompletedEncoding = true;
                        if (value === 'hush/signing/secp256k1/v1' && !facts.failed) {
                            facts.failed = true;
                            TextEncoder.prototype.encode = encode;
                            throw new Error('Controlled producer encoding failure');
                        }
                        return Reflect.apply(encode, this, [value]);
                    };
                    globalThis.hvRestoreProducerEncoding = () => { TextEncoder.prototype.encode = encode; };
                    return true;
                })()
                """)).Should().BeTrue();
            var queries = scenario.Faults.IdentityQueryCount;
            await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
            await Expect(Page.GetByTestId("candidate-list")).ToHaveCountAsync(0);
            await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
            (await EvaluateWorkerAsync(session, workerSession,
                "hvProducerFault.firstProducerCompletedEncoding && hvProducerFault.failed")).Should().BeTrue();
            scenario.Faults.IdentityQueryCount.Should().Be(queries);
            scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
            _rejected = true;
        }
        finally
        {
            if (workerSession is not null)
            {
                try { await EvaluateWorkerAsync(session, workerSession, "(() => { globalThis.hvRestoreProducerEncoding?.(); return true; })()"); }
                finally { await session.SendAsync("Target.detachFromTarget", new() { ["sessionId"] = workerSession }); }
            }
            await session.DetachAsync();
        }
    }

    [Then("no partial candidate reaches lookup and fresh recovery can register the original keys")]
    public async Task RecoverAsync()
    {
        _rejected.Should().BeTrue();
        // Back invokes actual cleanup and the root's fresh worker inspection.
        // inspectStartup rejects any candidate left in memory, so this checks
        // real empty authority before the successful independent-key journey.
        await Page.GoBackAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Recovery Words") })).ToBeVisibleAsync();
        await recreate.AbsentAsync();
        await recreate.SelectAsync();
        await recreate.ReviewAsync();
        await recreate.RegisterAsync();
        var keys = HushVotingTestIdentity.DeriveP01(string.Join(" ", Enumerable.Repeat("abandon", 23).Append("art")));
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(Page, keys, "Recovered voting identity", true)).Should().BeTrue();
    }

    // Only fixed fixture expressions and boolean facts cross CDP; never secret
    // values, candidate records, stack traces or arbitrary evaluated results.
    private static async Task<bool> EvaluateWorkerAsync(ICDPSession session, string workerSession, string expression)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        const int id = 1;
        EventHandler<JsonElement?> handler = (sender, payload) =>
        {
            if (payload is not { } envelope) return;
            if (envelope.GetProperty("sessionId").GetString() != workerSession) return;
            using var response = JsonDocument.Parse(envelope.GetProperty("message").GetString()!);
            var root = response.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) return;
            var ok = root.TryGetProperty("result", out var result) && !result.TryGetProperty("exceptionDetails", out _)
                && result.TryGetProperty("result", out var value) && value.TryGetProperty("value", out var boolean)
                && boolean.ValueKind == JsonValueKind.True;
            completed.TrySetResult(ok);
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
