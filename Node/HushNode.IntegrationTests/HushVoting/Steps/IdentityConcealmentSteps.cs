using System.Text.Json;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-011 -> Phase 7 Task 7.2. Web target only.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityConcealmentSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity)
{
    private int _verifiedTriggers;

    [Given("Alice reveals recovery words through the real Web creation authority")]
    public async Task PrepareAsync()
    {
        await scenario.Page.AddInitScriptAsync("""
            (() => {
                const send = MessagePort.prototype.postMessage;
                let current;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation') current = {
                        port: this, clientChannel: message.clientChannel, authorityEpoch: message.authorityEpoch
                    };
                    return Reflect.apply(send, this, args);
                };
                // Negative lifecycle fault: invoke the actual closed worker Lock
                // operation. No application state or successful response is injected.
                window.__hvRevokeReveal = () => {
                    if (!current) throw new Error('No real worker channel');
                    const { port, clientChannel, authorityEpoch } = current;
                    port.postMessage({ kind: 'operation', operation: 'lockAll', operationVersion: 1,
                        operationId: 'concealment-' + crypto.randomUUID(), clientChannel, authorityEpoch });
                };
            })();
            """);
        await identity.GenerateCandidateAsync();
    }

    [When("Alice exercises Web reveal timeout Back navigation pagehide regeneration and worker Lock")]
    public async Task TriggersAsync()
    {
        foreach (var trigger in new[] { "timeout", "back", "navigation", "pagehide", "regeneration", "lock-revocation" })
        {
            if (_verifiedTriggers > 0) await identity.GenerateCandidateAsync();
            var session = await scenario.Context.NewCDPSessionAsync(scenario.Page);
            try
            {
                // Record only backend DOM node IDs belonging to the revealed list.
                // AX names/values are never copied to an artifact or assertion.
                var document = await session.SendAsync("DOM.getDocument");
                var rootId = document!.Value.GetProperty("root").GetProperty("nodeId").GetInt32();
                var selected = await session.SendAsync("DOM.querySelector", new Dictionary<string, object> { ["nodeId"] = rootId, ["selector"] = "[data-testid=recovery-list]" });
                var nodeId = selected!.Value.GetProperty("nodeId").GetInt32();
                nodeId.Should().BeGreaterThan(0);
                var described = await session.SendAsync("DOM.describeNode", new Dictionary<string, object> { ["nodeId"] = nodeId, ["depth"] = -1 });
                var ids = new HashSet<int>();
                CollectIds(described!.Value.GetProperty("node"), ids);
                var before = await session.SendAsync("Accessibility.getFullAXTree");
                HasVisibleNodes(before!.Value, ids).Should().BeTrue("the revealed list must initially be accessible");

                switch (trigger)
                {
                    case "timeout": break; // Actual production 60-second timer.
                    case "back":
                        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync(); break;
                    case "navigation": await scenario.Page.GotoAsync("/?concealment-navigation=1"); break;
                    case "pagehide":
                        await scenario.Page.EvaluateAsync("() => dispatchEvent(new PageTransitionEvent('pagehide'))"); break;
                    case "regeneration":
                        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Regenerate", Exact = true }).ClickAsync(); break;
                    case "lock-revocation": await scenario.Page.EvaluateAsync("window.__hvRevokeReveal()"); break;
                }
                await Expect(scenario.Page.GetByTestId("recovery-list")).ToHaveCountAsync(0,
                    new() { Timeout = trigger == "timeout" ? 60_500 : 15_000 });
                var after = await session.SendAsync("Accessibility.getFullAXTree");
                HasVisibleNodes(after!.Value, ids).Should().BeFalse("concealed recovery nodes must leave the accessibility tree: " + trigger);
                scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
                _verifiedTriggers++;
            }
            finally { await session.DetachAsync(); }
        }
    }

    private static void CollectIds(JsonElement node, HashSet<int> ids)
    {
        if (node.TryGetProperty("backendNodeId", out var id)) ids.Add(id.GetInt32());
        if (node.TryGetProperty("children", out var children))
            foreach (var child in children.EnumerateArray()) CollectIds(child, ids);
    }

    private static bool HasVisibleNodes(JsonElement tree, HashSet<int> ids) =>
        tree.GetProperty("nodes").EnumerateArray().Any(node =>
            node.TryGetProperty("backendDOMNodeId", out var id) && ids.Contains(id.GetInt32())
            && (!node.TryGetProperty("ignored", out var ignored) || !ignored.GetBoolean()));

    [Then("all exercised triggers remove the recovery nodes from visual and accessibility rendering and a fresh identity can register")]
    public async Task FreshRegistrationAsync()
    {
        _verifiedTriggers.Should().Be(6);
        var words = await identity.GenerateCandidateAsync();
        await identity.ConfirmRecoveryAndProtectAsync(words);
        await identity.SubmitAndIndexAsync();
        await identity.UnlockAndBootstrapAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
    }
}
