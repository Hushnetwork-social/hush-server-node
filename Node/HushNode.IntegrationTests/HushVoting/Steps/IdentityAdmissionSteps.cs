using System.Text.Json;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityAdmissionSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity, IdentitySubmissionSteps submission, AuthenticationSteps authentication)
{
    private int _methodOffset;

    [When("a real indexed profile has Alice's signing address but a different encryption address")]
    public async Task MismatchedProfileAsync()
    {
        // Another valid public key, registered through the actual validator/indexer.
        await HushVotingServerIdentity.RegisterAsync(scenario, identity.Keys, HushVotingIdentityJourney.Alias,
            encryptionAddress: identity.Keys.SigningPublicKey);
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true }).ClickAsync();
    }

    [Then("creation fails closed without submitting or activating that mismatched identity")]
    public async Task MismatchClosedAsync()
    {
        await Expect(scenario.Page.GetByRole(AriaRole.Heading, new() { Name = "This device is locked out", Exact = true })).ToBeVisibleAsync();
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1, "only the independently registered fixture was submitted");
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.PendingRegistration.Should().BeTrue();
        stored.Active.Should().BeFalse();
        await scenario.Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
        await authentication.RemoveAsync();
        await authentication.StorageRemovedAsync();
    }

    [Then("a fresh candidate with both keys already indexed completes without recreating its identity")]
    public async Task ExactProfileAsync()
    {
        await submission.ReviewAsync();
        await HushVotingServerIdentity.RegisterAsync(scenario, identity.Keys, HushVotingIdentityJourney.Alias);
        var before = scenario.Faults.SubmittedTransactions.Count;
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true }).ClickAsync();
            await received.WaitAsync();
        }
        var requests = scenario.Faults.SubmittedTransactions.Skip(before).ToArray();
        requests.Length.Should().Be(1, "the already indexed identity only needs its baseline licence");
        using var transaction = JsonDocument.Parse(requests[0]);
        transaction.RootElement.GetProperty("PayloadKind").ValueEquals("351cd60b-3fdf-48d4-b608-e93c0100f7d0").Should().BeFalse();
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 45_000 });
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.Active.Should().BeTrue();
        stored.KeysMatch.Should().BeTrue();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(before + 1);
    }

    [Given("Alice has approved creation review but has not queried or submitted the new identity")]
    public async Task ReviewAsync()
    {
        await submission.ReviewAsync();
        scenario.Faults.IdentityQueryCount.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        _methodOffset = scenario.Faults.RequestMethods.Count;
    }

    [When("Alice creates the identity and later restarts before its first authenticated unlock")]
    public async Task SubmitRestartAsync()
    {
        await submission.SubmitAsync();
        LookupPrecedesSubmission();
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true }).ClickAsync();
        await authentication.SafeLockedPreviewAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await scenario.Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
        _methodOffset = scenario.Faults.RequestMethods.Count;
        await identity.UnlockAndBootstrapAsync();
    }

    private void LookupPrecedesSubmission()
    {
        var calls = scenario.Faults.RequestMethods.Skip(_methodOffset).Where(method => method is "GetIdentity" or "SubmitSignedTransaction").ToArray();
        calls.Length.Should().BeGreaterThanOrEqualTo(2);
        calls[0].Should().Be("GetIdentity");
        Array.IndexOf(calls, "SubmitSignedTransaction").Should().BeGreaterThan(0);
    }

    [Then("initial admission and restart both query the exact public address before any subsequent transaction")]
    public async Task LookupFirstAsync()
    {
        LookupPrecedesSubmission();
        scenario.Faults.IdentityLookups.All(lookup => lookup.SigningAddress == identity.Keys.SigningPublicKey).Should().BeTrue();
        var identities = scenario.Faults.SubmittedTransactions.Count(json =>
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("PayloadKind").ValueEquals("351cd60b-3fdf-48d4-b608-e93c0100f7d0");
        });
        identities.Should().Be(1, "restart must not recreate the registered identity");
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync();
    }

    [When("Alice requests creation while the real initial identity lookup is unavailable")]
    public async Task UnavailableAsync()
    {
        scenario.Faults.IdentityUnavailable = true;
        await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true }).ClickAsync();
        await Expect(scenario.Page.GetByRole(AriaRole.Heading, new() { Name = "Waiting for connection", Exact = true })).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Then("no transaction is submitted until explicit Retry obtains authoritative absence from the node")]
    public async Task AbsenceOnlyAsync()
    {
        scenario.Faults.RejectedIdentityQueries.Should().Be(1);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        await Expect(scenario.Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(scenario.Page.GetByRole(AriaRole.Heading, new() { Name = "Waiting for blockchain final approval", Exact = true })).ToHaveCountAsync(0);
        var stored = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        stored.PendingRegistration.Should().BeTrue();
        stored.KeysMatch.Should().BeTrue();
        scenario.Faults.IdentityUnavailable = false;
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Try again", Exact = true }).ClickAsync();
            await received.WaitAsync();
        }
        LookupPrecedesSubmission();
        scenario.Faults.IdentityLookups.First().Reply.Successfull.Should().BeFalse();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        await submission.ConfirmAsync();
    }
}
