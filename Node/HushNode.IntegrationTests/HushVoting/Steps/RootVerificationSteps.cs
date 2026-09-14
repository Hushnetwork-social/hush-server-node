using FluentAssertions;
using HushNode.Identity.Storage;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-001 -> FEAT-010 AC-010-013 -> Phase 3 Tasks 3.1/3.2,
// Phase 6 Tasks 6.7/6.8, Phase 7 Tasks 7.3/7.4.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RootVerificationSteps(HushVotingScenario scenario, IdentitySubmissionSteps submission,
    HushVotingIdentityJourney identity, AuthenticationSteps authentication)
{
    private IPage Page => scenario.Page;

    [Given("Alice's real creation child has a reviewed identity and a root verification barrier")]
    public async Task ReadyAsync()
    {
        await submission.ReviewAsync();
        await submission.SafeReviewAsync();
        await Page.EvaluateAsync("""
            () => {
                const send = MessagePort.prototype.postMessage;
                const observed = new WeakSet();
                let promotionId = null, promoted = false, held = null, rootId = null;
                const facts = { promoted: false, held: false, result: null, holds: 0, promotions: 0, verifications: 0, replies: 0 };
                window.hvRootVerification = facts;
                MessagePort.prototype.postMessage = function(...args) {
                    const message = args[0];
                    if (message?.kind === 'operation') {
                        if (!observed.has(this)) {
                            observed.add(this);
                            this.addEventListener('message', event => {
                                const result = event.data;
                                if (result?.kind !== 'operation-outcome') return;
                                facts.replies++;
                                if (result.operationId === promotionId && result.outcome === 'OK')
                                    facts.promoted = promoted = true;
                                if (result.operationId === rootId) facts.result = result.outcome;
                            });
                        }
                        if (message.operation === 'promoteLifecycle') { promotionId = message.operationId; facts.promotions++; }
                        if (message.operation === 'verifyOnlineIdentity') facts.verifications++;
                        // The client promise may continue before this observer's reply
                        // listener runs. Arm from the outgoing promotion, then separately
                        // require its actual OK before accepting the held-root arrangement.
                        if (promotionId !== null && message.operation === 'verifyOnlineIdentity' && facts.holds === 0) {
                            rootId = message.operationId;
                            held = { port: this, args };
                            facts.held = true;
                            facts.holds++;
                            return;
                        }
                    }
                    return Reflect.apply(send, this, args);
                };
                window.hvReleaseRootVerification = () => {
                    if (!held) return false;
                    const current = held;
                    held = null;
                    facts.held = false;
                    Reflect.apply(send, current.port, current.args);
                    return true;
                };
            }
            """);
    }

    [When("the child completes after real indexing while root verification is held")]
    public async Task CompleteChildAsync()
    {
        await submission.SubmitAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await WaitForFactAsync("() => hvRootVerification.promoted && hvRootVerification.held");
        await Expect(Page.GetByTestId("create-action")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await NoAccessAsync();
        var stored = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        (stored.Active && stored.KeysMatch && stored.MetadataMatches).Should().BeTrue("a completed child and active local vault still cannot bypass root verification");
    }

    [Then("the root rejects a changed encryption key without granting licence or protected access")]
    public async Task RejectChangedKeyAsync()
    {
        var before = scenario.Faults.IdentityQueryCount;
        // Controlled negative projection corruption in this scenario's own DB.
        // The signing address stays exact; the actual node serves the mismatched
        // encryption address through its ordinary gRPC/BFF route.
        await SetEncryptionAddressAsync(identity.Keys.SigningPublicKey);
        (await Page.EvaluateAsync<bool>("() => hvReleaseRootVerification()")).Should().BeTrue();
        await WaitForFactAsync("() => hvRootVerification.result === 'ENCRYPTION_KEY_MISMATCH'");
        await Expect(Page.Locator(".error-surface[role=alert]")).ToBeVisibleAsync();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(before);
        var reply = scenario.Faults.IdentityLookups.Last().Reply;
        (reply.Successfull && reply.PublicSigningAddress == identity.Keys.SigningPublicKey
            && reply.PublicEncryptAddress != identity.Keys.EncryptPublicKey).Should().BeTrue();
        await NoAccessAsync();
    }

    [Then("only a repaired exact profile and fresh password unlock complete root authentication")]
    public async Task RecoverAsync()
    {
        await SetEncryptionAddressAsync(identity.Keys.EncryptPublicKey);
        await Page.ReloadAsync();
        await authentication.SafeLockedPreviewAsync();
        var before = scenario.Faults.IdentityQueryCount;
        await identity.UnlockAndBootstrapAsync();
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(before);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2, "only the original identity and indexed root licence may be submitted");
    }

    private async Task NoAccessAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
        scenario.Faults.EntitlementRequests.Should().Be(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1);
    }

    private async Task SetEncryptionAddressAsync(string address)
    {
        using var scope = scenario.Node.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.Profiles.Where(profile => profile.PublicSigningAddress == identity.Keys.SigningPublicKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(profile => profile.PublicEncryptAddress, address))).Should().Be(1);
        await scenario.ClearCacheAsync();
    }

    private async Task WaitForFactAsync(string expression)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { while (!await Page.EvaluateAsync<bool>(expression)) await Task.Delay(50, deadline.Token); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            var facts = await Page.EvaluateAsync<string>("() => JSON.stringify(hvRootVerification)");
            var surfaces = await Page.Locator("[data-testid]").EvaluateAllAsync<string[]>("nodes => nodes.map(node => node.dataset.testid)");
            throw new InvalidOperationException("Root verification barrier timed out; boolean/count facts=" + facts
                + "; surface identifiers=" + string.Join(',', surfaces) + "; RPC outcomes=" + string.Join(';', scenario.Faults.Outcomes));
        }
    }
}
