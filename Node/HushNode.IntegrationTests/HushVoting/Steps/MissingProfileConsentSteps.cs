// EPIC-001 -> FEAT-010 AC-010-025/040 -> Phase 7 Task 7.3;
// FEAT-002 Phase 3 Tasks 3.1/3.2; FEAT-019 Phase 0 Task 0.1.
// Supplemental real-root regression, not the full FEAT-007 AC-007-049 reset flow.
using System.Text.RegularExpressions;
using FluentAssertions;
using HushNode.Identity.Storage;
using HushShared.Identity.Model;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class MissingProfileConsentSteps(HushVotingScenario scenario,
    HushVotingIdentityJourney identity, AuthenticationSteps authentication)
{
    private IPage Page => scenario.Page;
    private int _lookups;

    [Given("a returning HushVoting vault whose own indexed profile has disappeared")]
    public async Task ArrangeAsync()
    {
        await HushVotingArtifactClient.RequireAsync();
        await identity.AuthenticateAsync();
        await authentication.LockAsync();
        await authentication.LockedAsync();
        // Remove only this scenario's profile, through the existing negative
        // fixture boundary. This is not an assertion of a full chain reset.
        using var scope = scenario.Node.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.Profiles.Where(p => p.PublicSigningAddress == identity.Keys.SigningPublicKey).ExecuteDeleteAsync()).Should().Be(1);
        var admission = scope.ServiceProvider.GetRequiredService<IFullIdentityAdmissionService>();
        await ((IFullIdentityReservationService)admission).ReleaseAsync(identity.Keys.SigningPublicKey, CancellationToken.None);
        await scenario.ClearCacheAsync();
        // Observe only operation names and closed success outcomes. Never retain
        // credentials/payloads, replace worker behavior or fabricate RPC replies.
        await Page.AddInitScriptAsync("""
            (() => {
                const send = MessagePort.prototype.postMessage, listening = new WeakSet(), pending = new Map();
                const facts = { confirmations: 0, locks: 0, lockedThenInspected: false };
                window.__hvMissingProfileFacts = facts;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation') {
                        if (message.operation === 'verifyOnlineIdentity') facts.confirmations++;
                        if (message.operation === 'inspectStartup' && facts.locks > 0) facts.lockedThenInspected = true;
                        if (message.operation === 'lockAll') pending.set(message.operationId, true);
                        if (!listening.has(this)) {
                            listening.add(this);
                            this.addEventListener('message', event => {
                                const reply = event.data;
                                if (reply?.kind === 'operation-outcome' && pending.delete(reply.operationId) && reply.outcome === 'OK') facts.locks++;
                            });
                        }
                    }
                    return Reflect.apply(send, this, args);
                };
            })();
            """);
        await Page.ReloadAsync();
        await authentication.LockedAsync();
        _lookups = scenario.Faults.IdentityQueryCount;
    }

    [When("returning unlock finds real profile absence before creation consent")]
    public async Task UnlockAsync()
    {
        await authentication.AttemptVerificationAsync();
        await MissingAsync();
        scenario.Faults.IdentityLookups.Last().Reply.Successfull.Should().BeFalse();
        // A bounded quiet interval supplements deterministic intent/operation Twins.
        await Task.Delay(TimeSpan.FromSeconds(1));
        scenario.Faults.IdentityQueryCount.Should().Be(_lookups + 1);
        (await Page.EvaluateAsync<int>("window.__hvMissingProfileFacts.confirmations")).Should().Be(1);
        await ClosedAsync();
    }

    [Then("only explicit confirmation invokes the missing-profile action")]
    public async Task ConfirmAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create the identity", Exact = true }).ClickAsync();
        await Page.WaitForFunctionAsync("window.__hvMissingProfileFacts.confirmations === 2");
        await MissingAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));
        scenario.Faults.IdentityQueryCount.Should().Be(_lookups + 2);
        scenario.Faults.IdentityLookups.Last().Reply.Successfull.Should().BeFalse();
        await ClosedAsync();
    }

    [Then("Back completes the worker lock and preserves the returning vault")]
    public async Task BackAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
        await authentication.LockedAsync();
        (await Page.EvaluateAsync<bool>("window.__hvMissingProfileFacts.locks === 1 && window.__hvMissingProfileFacts.lockedThenInspected")).Should().BeTrue();
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Create User|Restore Credential File|Restore Recovery Words)") })).ToHaveCountAsync(0);
        var vault = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        (vault.KeysMatch && vault.ConcreteKeysOnly && vault.DevicePasswordProtected && vault.Active).Should().BeTrue();
        await authentication.AttemptVerificationAsync();
        await MissingAsync();
        scenario.Faults.IdentityQueryCount.Should().Be(_lookups + 3);
        await ClosedAsync();
        await HushVotingArtifactClient.CheckAsync();
    }

    private async Task MissingAsync() => await Expect(Page.GetByText("No profile was found for this identity.", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 30_000 });

    private async Task ClosedAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "only the original fixture identity and its licence were submitted");
        scenario.Faults.RequestMethods.Should().NotContain(method => method.Contains("Feed", StringComparison.OrdinalIgnoreCase));
    }
}
