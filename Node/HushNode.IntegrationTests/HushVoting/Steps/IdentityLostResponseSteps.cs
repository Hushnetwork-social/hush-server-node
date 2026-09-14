using System.Text.Json;
using FluentAssertions;
using HushNetwork.proto;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-007 AC-007-036/053 -> Phase 7 Task 7.2.
// Real admission/indexing; only the server's completed transport reply is lost.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityLostResponseSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    IdentitySubmissionSteps submission)
{
    private string _exact = "";
    private int _queries;

    [Given("Alice's identity is accepted by the real node but its submission response is lost")]
    public async Task LostAsync()
    {
        await submission.ReviewAsync();
        scenario.Faults.DropNextSubmissionResponse = true;
        using var accepted = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true }).ClickAsync();
        await accepted.WaitAsync();
        await Expect(scenario.Page.GetByRole(AriaRole.Heading, new() { Name = "Waiting for connection", Exact = true })).ToBeVisibleAsync();
        scenario.Faults.DroppedSubmissionResponses.Should().Be(1);
        scenario.Faults.Submissions.Single().Status.Should().Be(TransactionStatus.Accepted);
        _exact = scenario.Faults.SubmittedTransactions.Single();
        _queries = scenario.Faults.IdentityQueryCount;
        await PreserveAsync();
    }

    [When("Alice retries the ambiguous connection before the identity is indexed")]
    public async Task RetryBeforeIndexAsync()
    {
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Try again", Exact = true }).ClickAsync();
        await submission.WaitingAsync();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(_queries);
        scenario.Faults.IdentityLookups.Last().Reply.Successfull.Should().BeFalse();
    }

    [Then("the encrypted pending identity retains the exact signed transaction and digest without replacement")]
    public async Task PreserveAsync()
    {
        await submission.NoShellAsync();
        var facts = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys,
            HushVotingIdentityJourney.Alias, false, expectedTransaction: _exact);
        facts.KeysMatch.Should().BeTrue();
        facts.MetadataMatches.Should().BeTrue();
        facts.PendingRegistration.Should().BeTrue();
        facts.Active.Should().BeFalse();
        facts.PendingTransactionMatches.Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        // Secret-bearing equality stays a boolean, never a diagnostic payload.
        scenario.Faults.SubmittedTransactions.All(value => value == _exact).Should().BeTrue();
    }

    [When("the accepted identity indexes before Alice retries the lost response")]
    public async Task IndexThenRetryAsync()
    {
        await scenario.Blocks.ProduceBlockAsync();
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(25));
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Try again", Exact = true }).ClickAsync();
        await baseline.WaitAsync();
        await Expect(scenario.Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync();
        await submission.NoShellAsync();
        await scenario.Blocks.ProduceBlockAsync();
    }

    [Then("real identity and licence indexing complete access without another identity transaction")]
    public async Task ConfirmPendingAsync()
    {
        await submission.ConfirmAsync();
        await OneIdentityAsync();
    }

    [Then("fresh exact lookup and separate licence indexing restore access with only the original identity transaction")]
    public async Task OneIdentityAsync()
    {
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.IdentityLookups.Any(item => item.Reply.Successfull
            && item.SigningAddress == identity.Keys.SigningPublicKey).Should().BeTrue();
        var identities = scenario.Faults.SubmittedTransactions.Where(json =>
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("PayloadKind").ValueEquals("351cd60b-3fdf-48d4-b608-e93c0100f7d0");
        }).ToArray();
        identities.Length.Should().Be(1);
        (identities[0] == _exact).Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "one identity and one indexed baseline licence");
        var facts = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        facts.KeysMatch.Should().BeTrue();
        facts.Active.Should().BeTrue();
        facts.PendingTransactionCleared.Should().BeTrue();
    }
}
