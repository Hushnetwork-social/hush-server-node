using System.Text.Json;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 / FEAT-007 / Phase 7 Task 7.2 / AC-007-021.
// Product task: Phase 4 Task 4.1 (duplicate rejection and safe action state).
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class IdentityReviewSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    IdentitySubmissionSteps submission)
{
    private bool _disabledDuringAdmission;

    [Given("the review screen with a valid authorization")]
    public async Task ReviewAsync()
    {
        // Observe operation names only. Forward original messages unchanged and
        // never retain capabilities, password transfers or signed payloads here.
        await scenario.Page.AddInitScriptAsync("""
            (() => {
                const counts = { provisionFromValidatedBundle: 0, submitIdentityTransaction: 0 };
                const send = MessagePort.prototype.postMessage;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation' && Object.hasOwn(counts, message.operation))
                        counts[message.operation]++;
                    return Reflect.apply(send, this, args);
                };
                Object.defineProperty(window, '__hvReviewOperationCounts', { get: () => ({ ...counts }) });
            })();
            """);
        await submission.ReviewAsync();
        await submission.SafeReviewAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(0);
        (await scenario.Page.EvaluateAsync<int>("window.__hvReviewOperationCounts.provisionFromValidatedBundle")).Should().Be(1);
    }

    [When("Create Identity is invoked")]
    public async Task DoubleCreateAsync()
    {
        // Delay the real node response, keeping its genuine lookup/result and
        // admission pipeline intact while observing the in-flight control.
        scenario.Faults.IdentityResponseDelay = TimeSpan.FromSeconds(5);
        try
        {
            using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
            await scenario.Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true }).DblClickAsync();
            // ActionButton replaces its supplied label with an ellipsis while
            // busy. Keep the assertion on the actual review action element.
            var busy = scenario.Page.GetByTestId("create-action");
            await Expect(busy).ToBeDisabledAsync();
            _disabledDuringAdmission = true;
            scenario.Faults.SubmittedTransactions.Count.Should().Be(0, "the delayed lookup must finish before submission");
            await received.WaitAsync();
        }
        finally { scenario.Faults.IdentityResponseDelay = TimeSpan.Zero; }
        await submission.WaitingAsync();
    }

    [Then("the full reviewed fields are bound to the operation-scoped authorization")]
    public async Task ReviewedFieldsAsync()
    {
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
        using var document = JsonDocument.Parse(scenario.Faults.SubmittedTransactions.Single());
        var payload = document.RootElement.GetProperty("Payload");
        // Compare independently derived keys in memory; failures expose facts only.
        (payload.GetProperty("IdentityAlias").GetString() == HushVotingIdentityJourney.Alias
            && !payload.GetProperty("IsPublic").GetBoolean()
            && payload.GetProperty("PublicSigningAddress").GetString() == identity.Keys.SigningPublicKey
            && payload.GetProperty("PublicEncryptAddress").GetString() == identity.Keys.EncryptPublicKey
            && document.RootElement.GetProperty("UserSignature").GetProperty("Signatory").GetString() == identity.Keys.SigningPublicKey)
            .Should().BeTrue("the real signed submission must use the reviewed profile and original candidate");
        var vault = await HushVotingVaultInspection.InspectAsync(scenario.Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        vault.KeysMatch.Should().BeTrue();
        vault.DevicePasswordProtected.Should().BeTrue();
    }

    [Then("the action is disabled during the in-flight command with a single provisioning owner")]
    public async Task OneOwnerAsync()
    {
        _disabledDuringAdmission.Should().BeTrue();
        (await scenario.Page.EvaluateAsync<int>("window.__hvReviewOperationCounts.provisionFromValidatedBundle")).Should().Be(1);
        (await scenario.Page.EvaluateAsync<int>("window.__hvReviewOperationCounts.submitIdentityTransaction")).Should().Be(1);
        await submission.ConfirmAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "one identity and one baseline licence are indexed");
        var profile = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = identity.Keys.SigningPublicKey },
            deadline: DateTime.UtcNow.AddSeconds(10));
        (profile.Successfull && profile.ProfileName == HushVotingIdentityJourney.Alias && !profile.IsPublic
            && profile.PublicSigningAddress == identity.Keys.SigningPublicKey && profile.PublicEncryptAddress == identity.Keys.EncryptPublicKey)
            .Should().BeTrue();
    }
}
