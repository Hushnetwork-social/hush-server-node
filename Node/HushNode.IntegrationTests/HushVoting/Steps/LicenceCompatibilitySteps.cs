using FluentAssertions;
using HushNetwork.proto;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;
using Corruption = HushVoting.IntegrationTests.Infrastructure.HushVotingFaultInterceptor.EntitlementCorruption;

namespace HushVoting.IntegrationTests.Steps;

// EPIC-002 AT-LIC-012 -> FEAT-016 AC-016-018 -> Phase 7 Tasks 7.1–7.4;
// FEAT-028 Phase 2 Tasks 2.1–2.3 owns the explicit negative transport fixture.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class LicenceCompatibilitySteps(HushVotingScenario scenario, HushVotingIdentityJourney identity,
    HushVotingLicenceActor actor)
{
    private IPage Page => scenario.Page;
    private int _submissions;
    private LicenceActiveEntitlementView _indexed = null!;

    [Given("HushServerNode returns active entitlement with incompatible critical semantics")]
    public Task CriticalVersionAsync() => ArrangeAsync(Corruption.CriticalVersion);

    [Given("the real active entitlement reply is corrupted with an unknown plan")]
    public Task UnknownPlanAsync() => ArrangeAsync(Corruption.UnknownPlan);

    [Given("the real active entitlement reply is corrupted with an unknown family")]
    public Task UnknownFamilyAsync() => ArrangeAsync(Corruption.UnknownFamily);

    [Given("the real active entitlement reply is corrupted with unknown governance")]
    public Task UnknownGovernanceAsync() => ArrangeAsync(Corruption.UnknownGovernance);

    private async Task ArrangeAsync(Corruption corruption)
    {
        // UI creates and seals Alice; real admission and block indexing create
        // the licence before the browser unlocks. No response provider is replaced.
        await identity.ProvisionAsync();
        (await actor.BaselineAsync()).Status.Should().Be(TransactionStatus.Accepted);
        await scenario.Blocks.ProduceBlockAsync();
        var actual = await actor.QueryAsync();
        actual.State.Should().Be(LicenceEntitlementState.Active);
        actual.DirectFreeTemplate.Should().BeNull();
        actual.Active.PlanId.Should().Be("hushvoting.direct.free");
        _indexed = actual.Active.Clone();
        _submissions = scenario.Faults.SubmittedTransactions.Count;
        scenario.Faults.CorruptActiveEntitlement = corruption;
        await UnlockAsync();
    }

    private async Task UnlockAsync()
    {
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Unlock HushVoting!", Exact = true }).ClickAsync();
    }

    [When("HushVoting validates the response")]
    [Then("workspace remains gated with compatible-client guidance")]
    public async Task UnsupportedAsync()
    {
        await Expect(Page.GetByTestId("entitlement-gate-heading")).ToHaveTextAsync(
            "Your client or licence needs an update before HushVoting! can open.", new() { Timeout = 30_000 });
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("licence-options")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "User information" })).ToHaveCountAsync(0);
        scenario.Faults.CorruptedEntitlements.Should().NotBeEmpty();
        // The interceptor preserves ordinary node responses before applying the fault.
        scenario.Faults.Entitlements.Last().Active.Should().BeEquivalentTo(_indexed);
        var corrupted = scenario.Faults.CorruptedEntitlements.Last();
        corrupted.State.Should().Be(LicenceEntitlementState.Active);
        corrupted.Active.LicenceReference.Should().Be(_indexed.LicenceReference);
        corrupted.DirectFreeTemplate.Should().BeNull();
    }

    [Then("it is not mapped to Direct Free or known Veritas")]
    public async Task NoFabricatedPlanAsync()
    {
        await Expect(Page.Locator("body")).Not.ToContainTextAsync("Direct Free");
        await Expect(Page.Locator("body")).Not.ToContainTextAsync("Veritas");
        await Expect(Page.Locator(".licence-account-summary,.licence-current-detail")).ToHaveCountAsync(0);
    }

    [Then("no baseline transaction is created")]
    public async Task NoSubmissionAsync()
    {
        // A real new block and a reload/unlock cannot bypass the unsupported gate.
        await scenario.Blocks.ProduceBlockAsync();
        await Page.ReloadAsync();
        await UnlockAsync();
        await UnsupportedAsync();
        await NoFabricatedPlanAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissions,
            "neither a fallback baseline nor an upgrade is permitted after setup");
    }

    [Then("an uncorrupted fresh node response restores the same indexed entitlement")]
    public async Task HealthyControlAsync()
    {
        await Page.GetByTestId("entitlement-gate").GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true }).ClickAsync();
        scenario.Faults.CorruptActiveEntitlement = null;
        await UnlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        (await actor.QueryAsync()).Active.Should().BeEquivalentTo(_indexed);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissions);
    }
}
