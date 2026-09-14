// EPIC-002 AT-LIC-010 -> FEAT-016 AC-016-016/017, Phase 6 Tasks 6.7/6.8;
// FEAT-015 AC-015-011/013/014, Phase 6 Tasks 6.3/6.4; FEAT-011 migration Tasks 7.M1–7.M3.
using System.Text.Json;
using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TechTalk.SpecFlow;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class LicenceExpirySteps(
    HushVotingScenario scenario, HushVotingIdentityJourney identity,
    HushVotingLicenceActor actor, RecoveryWordEntrySteps entry)
{
    private DateTime _expiry;
    private int _submissions;
    private int _queries;
    private int _responses;
    private IPage Page => scenario.Page;

    private async Task<LicenceAssignmentEntity[]> AssignmentsAsync()
    {
        using var scope = scenario.Node.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HushNodeDbContext>()
            .Set<LicenceAssignmentEntity>().AsNoTracking()
            .Where(a => a.LicenceSubject!.CanonicalPublicSigningAddress == identity.Keys.SigningPublicKey).ToArrayAsync();
    }

    [Given("Alice is using an active annual Veritas entitlement")]
    public async Task AnnualAsync()
    {
        var clock = scenario.HistoricalBlockClock ?? throw new InvalidOperationException("Expiry requires its dedicated historical block fixture.");
        await scenario.UseRestartableBrowserAsync();
        var words = await identity.GenerateCandidateAsync();
        await HushVotingServerIdentity.RegisterAsync(scenario, identity.Keys, HushVotingIdentityJourney.Alias);
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            (await actor.BaselineAsync()).Successfull.Should().BeTrue();
            await received.WaitAsync();
        }
        await scenario.Blocks.ProduceBlockAsync();
        var free = await actor.QueryAsync();
        free.Active.Should().NotBeNull();
        free.Active.PlanId.Should().Be("hushvoting.direct.free");

        // Real historical signed upgrade; keep the catalogue's complete calendar-year term.
        clock.AdvanceTo(DateTimeOffset.UtcNow.AddMinutes(1).AddYears(-1));
        using (var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            (await actor.UpgradeAsync(free.Active, "hushvoting.veritas.2000")).Successfull.Should().BeTrue();
            await received.WaitAsync();
        }
        await scenario.Blocks.ProduceBlockAsync();
        var annual = (await AssignmentsAsync()).Single(a => a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive);
        annual.PlanId.Should().Be("hushvoting.veritas.2000");
        annual.ExpiresAtUtc.Should().Be(annual.EffectiveFromUtc.AddYears(1));
        annual.OriginatingBlockTimeStampUtc.Should().Be(annual.EffectiveFromUtc);
        _expiry = annual.ExpiresAtUtc!.Value;
        clock.UseSystemTime();

        // The historical setup never submitted the browser's unsealed creation candidate.
        // Restart and restore the registered identity through ordinary Web UI.
        await scenario.CrashAndRestartBrowserAsync();
        await entry.EntryAsync();
        for (var i = 0; i < words.Count; i++)
            await HushVotingIdentityJourney.FillSecretAsync(Page.Locator("#rw-" + (i + 1)), words[i]);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("candidate-list").Locator("li")).ToHaveCountAsync(1);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to protect this device", Exact = true }).ClickAsync();
        await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.Entitlements.Last().Active.PlanId.Should().Be("hushvoting.veritas.2000");
        DateTime.UtcNow.Should().BeBefore(_expiry);
        _submissions = scenario.Faults.SubmittedTransactions.Count;
        _submissions.Should().Be(3, "identity, historical baseline and annual upgrade are the only setup transactions");
        _queries = scenario.Faults.EntitlementRequests;
        _responses = scenario.Faults.Entitlements.Count;
        scenario.Faults.HoldEntitlementQueries();
    }

    [When("its upper-exclusive expiry trigger is reached")]
    public async Task BoundaryAsync()
    {
        // No Date.now, worker state, response, assignment or expiry mutation: wait for real time.
        await scenario.Faults.EntitlementArrived.Task.WaitAsync(TimeSpan.FromSeconds(150));
        DateTime.UtcNow.Should().BeOnOrAfter(_expiry);
    }

    [Then("HushVoting gates workspace and makes a fresh signed query")]
    public async Task GatedAsync()
    {
        await Expect(Page.GetByTestId("entitlement-gate")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        scenario.Faults.EntitlementRequests.Should().Be(_queries + 1);
    }

    [Then("it does not declare expiry from client clock alone")]
    public async Task AwaitingAuthorityAsync()
    {
        await Expect(Page.GetByTestId("entitlement-gate")).ToContainTextAsync("Checking your HushVoting! licence");
        scenario.Faults.Entitlements.Count.Should().Be(_responses);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissions);
    }

    [When("HushServerNode returns no active entitlement")]
    public async Task NoActiveAsync()
    {
        using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        scenario.Faults.ReleaseEntitlementQueries();
        try { await received.WaitAsync(); }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("Expiry baseline not admitted; query/submission outcomes="
                + string.Join(';', scenario.Faults.Outcomes)
                + "; submission codes=" + string.Join(',', scenario.Faults.Submissions.Select(s => s.ValidationCode)));
        }
        scenario.Faults.Entitlements.Skip(_responses).Should().Contain(r => r.DirectFreeTemplate != null && r.Active == null);
    }

    [Then("Alice signs one Direct Free transaction automatically")]
    public async Task SignedAsync()
    {
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissions + 1);
        using var transaction = JsonDocument.Parse(scenario.Faults.SubmittedTransactions.Last());
        var payload = transaction.RootElement.GetProperty("Payload");
        payload.GetProperty("TransitionIntent").GetString().Should().Be("baseline_free");
        payload.GetProperty("RequestedPlanId").GetString().Should().Be("hushvoting.direct.free");
        // Successful real admission verifies the signature; mempool acceptance still grants no access.
        (await AssignmentsAsync()).Should().HaveCount(2);
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
    }

    [Then("workspace reopens only after Direct Free is indexed")]
    public async Task IndexedAsync()
    {
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var assignments = await AssignmentsAsync();
        assignments.Should().HaveCount(3);
        var active = assignments.Single(a => a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive);
        active.PlanId.Should().Be("hushvoting.direct.free");
        active.EffectiveFromUtc.Should().BeOnOrAfter(_expiry);
        scenario.Faults.Entitlements.Last().Active.LicenceReference.Should().Be(active.OriginatingTransactionId.ToString());
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissions + 1);
    }
}
