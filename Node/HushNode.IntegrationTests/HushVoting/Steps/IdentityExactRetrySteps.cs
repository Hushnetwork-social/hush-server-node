using System.Text.Json;
using FluentAssertions;
using HushNetwork.proto;
using HushVoting.IntegrationTests.Infrastructure;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-045 -> Phase 7 Tasks 7.1/7.2.
// Exercise the actual sealed retry seam; UI Try again remains lookup-only.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityExactRetrySteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    IdentityLostResponseSteps lost, IdentitySubmissionSteps submission)
{
    private string _exact = "";

    [Given("Alice loses a real accepted response while a public sealed submission command is observed")]
    public async Task LostAsync()
    {
        await scenario.Page.AddInitScriptAsync("""
            (() => {
                const send = MessagePort.prototype.postMessage;
                let port, command;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation' && message.operation === 'submitIdentityTransaction') {
                        // This closed public command contains profile metadata,
                        // never the signing key or retained signed transaction.
                        port = this; command = structuredClone(message);
                    }
                    return Reflect.apply(send, this, args);
                };
                window.__hvRetryExactSubmission = () => new Promise((resolve, reject) => {
                    if (!port || !command) { reject(new Error('No actual submission observed')); return; }
                    const operationId = 'exact-retry-' + crypto.randomUUID();
                    const receive = event => {
                        const reply = event.data;
                        if (reply?.kind !== 'operation-outcome' || reply.operationId !== operationId) return;
                        clearTimeout(timer); port.removeEventListener('message', receive);
                        resolve(reply.outcome === 'OK' && reply.payload?.status === 'pending');
                    };
                    const timer = setTimeout(() => {
                        port.removeEventListener('message', receive);
                        reject(new Error('Exact sealed retry timed out'));
                    }, 15000);
                    port.addEventListener('message', receive);
                    Reflect.apply(send, port, [{ ...command, operationId }]);
                });
            })();
            """);
        await lost.LostAsync();
        _exact = scenario.Faults.SubmittedTransactions.Single();
    }

    [When("her actual Web worker retries the sealed transaction after lookup confirms absence")]
    public async Task RetryAsync()
    {
        var queries = scenario.Faults.IdentityQueryCount;
        var requests = scenario.Faults.RequestMethods.Count;
        (await scenario.Page.EvaluateAsync<bool>("window.__hvRetryExactSubmission()")).Should().BeTrue();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(queries);
        scenario.Faults.RequestMethods.Skip(requests)
            .Where(method => method is "GetIdentity" or "SubmitSignedTransaction")
            .Should().Equal("GetIdentity", "SubmitSignedTransaction");
        scenario.Faults.IdentityLookups.Last().Reply.Successfull.Should().BeFalse();
        scenario.Faults.Submissions.Select(reply => reply.Status).Should().Equal(TransactionStatus.Accepted, TransactionStatus.Pending);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        scenario.Faults.SubmittedTransactions.All(value => value == _exact).Should().BeTrue(
            "the real node must receive identical signed bytes, not a newly generated transaction");
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys,
            HushVotingIdentityJourney.Alias, false, expectedTransaction: _exact);
        stored.KeysMatch.Should().BeTrue();
        stored.PendingTransactionMatches.Should().BeTrue();
        stored.PendingRegistration.Should().BeTrue();
        stored.Active.Should().BeFalse();
        await lost.RetryBeforeIndexAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "the separate UI Try again action performs lookup only");
    }

    [Then("the real node returns PENDING for identical bytes and normal confirmation indexes one identity and its licence")]
    public async Task ConfirmAsync()
    {
        await submission.ConfirmAsync();
        var identities = scenario.Faults.SubmittedTransactions.Where(json =>
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("PayloadKind").ValueEquals("351cd60b-3fdf-48d4-b608-e93c0100f7d0");
        }).ToArray();
        identities.Length.Should().Be(2);
        identities.All(value => value == _exact).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(3, "two deliveries of one identity transaction and one baseline licence");
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.KeysMatch.Should().BeTrue();
        stored.Active.Should().BeTrue();
        stored.PendingTransactionCleared.Should().BeTrue();
    }
}
