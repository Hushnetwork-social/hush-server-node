using System.Text.Json;
using FluentAssertions;
using HushNetwork.proto;
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
internal sealed class EntitlementSteps(HushVotingScenario scenario, HushVotingIdentityJourney identity, HushVotingLicenceActor actor)
{
    private IPage Page => scenario.Page;
    private ILocator Gate => Page.GetByTestId("entitlement-gate");
    private string? _pending;
    private int _submissions;
    private IPage? _secondTab;
    private int _queries;
    private int _heldQueryOffset;
    private int _restartMethodOffset;
    private int _restartSubmissionOffset;
    private string[] _restartTransactions = [];

    private async Task<LicenceAssignmentEntity[]> AssignmentsAsync()
    {
        using var scope = scenario.Node.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HushNodeDbContext>().Set<LicenceAssignmentEntity>()
            .AsNoTracking().Where(a => a.LicenceSubject!.CanonicalPublicSigningAddress == identity.Keys.SigningPublicKey).ToArrayAsync();
    }

    [Given("Alice has no active indexed HushVoting entitlement")]
    public async Task NoActiveAsync()
    {
        (await AssignmentsAsync()).Should().BeEmpty();
        _submissions = scenario.Faults.SubmittedTransactions.Count;
        _queries = scenario.Faults.EntitlementRequests;
    }

    [When("the entitlement authority requests Alice's current entitlement")]
    public async Task RequestAsync()
    {
        using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        scenario.Faults.ReleaseEntitlementQueries();
        await received.WaitAsync();
        _pending = scenario.Faults.SubmittedTransactions.Last();
        await WorkspaceUnmountedAsync();
    }

    [Then("the workspace remains unmounted")]
    [Then("ACCEPTED or PENDING does not open the workspace")]
    public async Task WorkspaceUnmountedAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
    }

    [Then("the authority signs and submits one server-templated Direct Free transaction as Alice")]
    public async Task SignedBaselineAsync()
    {
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissions + 1);
        var template = scenario.Faults.Entitlements.First(e => e.DirectFreeTemplate is not null).DirectFreeTemplate;
        template.RequestedPlanId.Should().Be("hushvoting.direct.free");
        // Inspect the signed transaction in memory only. Acceptance by the real validator proves its signature.
        _pending.Should().NotBeNull();
        using var document = JsonDocument.Parse(_pending!);
        var payload = document.RootElement.GetProperty("Payload");
        payload.GetProperty("RequestedPlanId").GetString().Should().Be(template.RequestedPlanId);
        payload.GetProperty("ObservedCatalogueVersion").GetString().Should().Be(template.ObservedCatalogueVersion);
        (await AssignmentsAsync()).Should().BeEmpty();
    }

    [When("a fresh signed query returns the indexed Direct Free entitlement")]
    [Then("only a later active indexed query opens the workspace")]
    public async Task IndexBaselineAsync()
    {
        scenario.Faults.HoldMempoolDrain = false;
        await scenario.Blocks.ProduceBlockAsync();
        await WorkspaceAsync();
    }

    [Then("HushVoting opens immediately with that safe entitlement in session memory")]
    [Then("HushVoting opens workspace immediately")]
    public async Task WorkspaceAsync()
    {
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Gate).ToHaveCountAsync(0);
        scenario.Faults.Entitlements.Last().State.Should().Be(LicenceEntitlementState.Active);
    }

    [Then("exactly one effective Direct Free assignment exists")]
    public async Task OneAssignmentAsync()
    {
        var rows = await AssignmentsAsync();
        rows.Should().ContainSingle();
        rows.Single().PlanId.Should().Be("hushvoting.direct.free");
        rows.Single().LifecycleStatus.Should().Be("active");
        rows.Single().OriginatingTransactionId.Should().NotBeNull();
    }

    [When("a fresh signed query returns compatible active indexed entitlement")]
    public async Task ExistingActiveAsync()
    {
        // The real response is held before its read. A separate device indexes Alice's entitlement first.
        var reply = await actor.BaselineAsync();
        reply.Status.Should().Be(TransactionStatus.Accepted);
        await scenario.Blocks.ProduceBlockAsync();
        _submissions = scenario.Faults.SubmittedTransactions.Count;
        scenario.Faults.ReleaseEntitlementQueries();
        await WorkspaceAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissions);
    }

    [Then("no licence success or Continue screen appears")]
    public async Task NoContinueAsync()
    {
        await Expect(Page.GetByTestId("licence-result")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true })).ToHaveCountAsync(0);
        await OneAssignmentAsync();
    }

    [Given("two tabs share Alice's authenticated SharedWorker")]
    public async Task TwoTabsAsync()
    {
        await identity.AuthenticateUntilEntitlementAsync();
        await NoActiveAsync();
        await RequestAsync();
        _secondTab = await scenario.Context.NewPageAsync();
        await _secondTab.GotoAsync("/");
        await Expect(_secondTab.GetByLabel("Device password", new() { Exact = true })).ToBeVisibleAsync();
    }

    [When("both require entitlement bootstrap")]
    public async Task BothRequireAsync()
    {
        _heldQueryOffset = scenario.Faults.EntitlementRequests;
        scenario.Faults.HoldEntitlementQueries();
        // Each new page starts locked; authenticate its channel through the ordinary UI.
        await HushVotingIdentityJourney.FillSecretAsync(_secondTab!.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await _secondTab.GetByRole(AriaRole.Button, new() { Name = "Unlock HushVoting!", Exact = true }).ClickAsync();
        await Expect(_secondTab.GetByTestId("entitlement-gate")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Then("exactly one signed query and reconciliation loop owns the operation")]
    public async Task SingleOwnerAsync()
    {
        await scenario.Faults.EntitlementArrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var heldQueries = scenario.Faults.EntitlementRequests;
        try
        {
            heldQueries.Should().Be(_heldQueryOffset + 1, "the two tabs must share one held query, including arrivals before this assertion");
            // Cross the ordinary three-second poll interval while the real
            // node request is held. A second tab/loop must not issue another.
            await Task.Delay(3_500);
            scenario.Faults.EntitlementRequests.Should().Be(heldQueries);
            await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
            await Expect(_secondTab!.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        }
        finally { scenario.Faults.ReleaseEntitlementQueries(); }
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissions + 1);
        await SignedBaselineAsync();
        await Expect(_secondTab!.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
    }

    [Then("tabs receive only the same safe progress and active projection")]
    public async Task TabsAgreeAsync()
    {
        await Expect(_secondTab!.GetByTestId("entitlement-gate-heading")).ToHaveTextAsync(await Page.GetByTestId("entitlement-gate-heading").InnerTextAsync());
        await IndexBaselineAsync();
        await Expect(_secondTab.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await OneAssignmentAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(_submissions + 1);
    }

    [Given("Alice's Direct Free transaction awaits indexed confirmation")]
    [Given("Alice's signed Direct Free transaction is sealed and pending")]
    [Given("Alice has an exact sealed pending Direct Free transaction")]
    [Given("Alice is authenticated and waiting for indexed entitlement")]
    public async Task PendingAsync()
    {
        await identity.AuthenticateUntilEntitlementAsync();
        await NoActiveAsync();
        await RequestAsync();
    }

    [When("connectivity authority reports the chain paused")]
    public async Task PausedAsync()
    {
        await Expect(Page.GetByTestId("gate-network-state")).ToHaveTextAsync("Network paused", new() { Timeout = 20_000 });
    }

    [Then("HushVoting immediately shows delayed confirmation with Retry and Lock")]
    public async Task DelayedAsync()
    {
        await Expect(Page.GetByTestId("entitlement-gate-heading")).ToHaveTextAsync("Licence activation is taking longer than expected.");
        await Expect(Gate.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync();
        await Expect(Gate.GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true })).ToBeVisibleAsync();
        await WorkspaceUnmountedAsync();
    }

    [Then("Retry never creates a replacement transaction")]
    public async Task ExactRetryAsync()
    {
        await RetryWithoutIndexAsync();
        await IndexBaselineAsync();
        await OneAssignmentAsync();
    }

    [When("Alice chooses Retry from delayed confirmation")]
    public async Task RetryWithoutIndexAsync()
    {
        await Gate.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (scenario.Faults.SubmittedTransactions.Count < _submissions + 2) await Task.Delay(100, deadline.Token);
        // Never include either transaction in assertion diagnostics.
        if (!string.Equals(scenario.Faults.SubmittedTransactions.Last(), _pending, StringComparison.Ordinal))
            throw new InvalidOperationException("Retry replaced the sealed transaction.");
        await WorkspaceUnmountedAsync();
    }

    [Given("blocks continue but indexed entitlement is absent for 30 seconds")]
    public async Task DelayedInclusionAsync()
    {
        scenario.Faults.HoldMempoolDrain = true;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            await scenario.Blocks.ProduceBlockAsync();
            await Task.Delay(1_000);
        }
        (await AssignmentsAsync()).Should().BeEmpty();
        await DelayedAsync();
        await Expect(Page.GetByTestId("gate-network-state")).ToHaveTextAsync("Online");
    }

    [Then("the authority resubmits the original UUID timestamp payload and signature")]
    public void SameRetryTransaction()
    {
        if (scenario.Faults.SubmittedTransactions.Last() != _pending) throw new InvalidOperationException("Retry changed the signed transaction.");
    }

    [Then("PENDING or ALREADY_EXISTS is reconciliation rather than failure")]
    public async Task PendingIsReconciliationAsync()
    {
        scenario.Faults.Submissions.Last().Status.Should().BeOneOf(TransactionStatus.Pending, TransactionStatus.AlreadyExists);
        await WorkspaceUnmountedAsync();
        await Expect(Page.GetByTestId("entitlement-gate-heading")).Not.ToContainTextAsync("couldn’t verify");
    }

    [Given("Alice is authenticated and entitlement resolution is in progress")]
    public async Task ResolvingAsync() => await identity.AuthenticateUntilEntitlementAsync();

    [When("Alice Locks HushVoting")]
    public async Task LockAsync()
    {
        await Gate.GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true }).ClickAsync();
        try { await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Unlock HushVoting!", Exact = true })).ToBeVisibleAsync(); }
        catch (Exception error) when (error is PlaywrightException or TimeoutException)
        {
            var surfaces = await Page.Locator("[data-testid]").EvaluateAllAsync<string[]>("nodes => nodes.map(n => n.dataset.testid)");
            var headings = await Page.GetByRole(AriaRole.Heading).AllTextContentsAsync();
            throw new InvalidOperationException("Lock did not return to unlock. Surfaces: " + string.Join(',', surfaces) + "; headings: " + string.Join(';', headings));
        }
    }

    [When("the old operation completes later")]
    public async Task LateResultAsync()
    {
        scenario.Faults.ReleaseEntitlementQueries();
        await scenario.Faults.EntitlementArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // A new block drives the normal connectivity observation after the late RPC finishes.
        await scenario.Blocks.ProduceBlockAsync();
    }

    [Then("its stale epoch result is ignored")]
    public async Task StaleIgnoredAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Unlock HushVoting!", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        (await AssignmentsAsync()).Should().BeEmpty();
    }

    [Then("no Alice entitlement is available to a later identity")]
    public async Task NoStaleEntitlementAsync()
    {
        // Reload creates a new page authority epoch; only locked safe preview is available.
        await Page.ReloadAsync();
        await StaleIgnoredAsync();
        await Expect(Page.Locator(".licence-account-summary")).ToHaveCountAsync(0);
    }

    [When("Alice uses browser or platform Back")]
    public async Task BackAsync() => await Page.GoBackAsync();

    [Then("HushVoting remains on the authenticated entitlement gate")]
    [Then("it neither exposes workspace nor returns to pre-authentication UI")]
    public async Task GateBackAsync()
    {
        await WorkspaceUnmountedAsync();
        await Expect(Gate).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Unlock HushVoting!", Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Create User", Exact = true })).ToHaveCountAsync(0);
    }

    [Given("Alice is inside workspace with active same-session entitlement")]
    public async Task ActiveSessionAsync() { await identity.AuthenticateAsync(); _queries = scenario.Faults.EntitlementRequests; }

    [When("connection is lost")]
    public async Task DisconnectAsync()
    {
        scenario.Faults.TransportUnavailable = true;
        await Expect(Page.GetByTestId("gate-network-state")).ToHaveTextAsync("Offline", new() { Timeout = 15_000 });
    }

    [Then("workspace is gated and previous entitlement cannot authorize or render as current")]
    public async Task NoCachedAccessAsync()
    {
        await WorkspaceUnmountedAsync();
        await Expect(Page.Locator(".licence-current-name,.licence-account-summary")).ToHaveCountAsync(0);
    }

    [When("connectivity returns in the same authenticated session")]
    public async Task ReconnectAsync() { scenario.Faults.TransportUnavailable = false; await scenario.Blocks.ProduceBlockAsync(); }

    [Then("a fresh signed query runs automatically")]
    [Then("only compatible active result restores workspace")]
    public async Task FreshRestoresAsync()
    {
        await WorkspaceAsync();
        scenario.Faults.EntitlementRequests.Should().BeGreaterThan(_queries);
        await OneAssignmentAsync();
    }

    [When("HushVoting restarts and Alice authenticates")]
    public async Task RestartPendingAsync()
    {
        _restartMethodOffset = scenario.Faults.RequestMethods.Count;
        _restartSubmissionOffset = scenario.Faults.SubmittedTransactions.Count;
        await Page.ReloadAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Unlock HushVoting!", Exact = true }).ClickAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (scenario.Faults.SubmittedTransactions.Count == _restartSubmissionOffset) await Task.Delay(100, deadline.Token);
        _restartTransactions = scenario.Faults.SubmittedTransactions.Skip(_restartSubmissionOffset).ToArray();
    }

    [Then("authority queries indexed truth before resubmission")]
    public void QueryFirst()
    {
        var methods = scenario.Faults.RequestMethods.Skip(_restartMethodOffset).ToArray();
        var query = Array.IndexOf(methods, "GetMyEntitlement");
        var submit = Array.IndexOf(methods, "SubmitSignedTransaction");
        query.Should().BeGreaterThanOrEqualTo(0);
        submit.Should().BeGreaterThan(query);
    }

    [Then("clears pending when it or another valid entitlement is active")]
    public async Task ClearPendingAsync()
    {
        await IndexBaselineAsync();
        await OneAssignmentAsync();
        var count = scenario.Faults.SubmittedTransactions.Count;
        await Page.ReloadAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Unlock HushVoting!", Exact = true }).ClickAsync();
        await WorkspaceAsync();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(count);
    }

    [Then("resubmits only exact stored transaction when truth remains no-active")]
    public void SameRestartTransaction()
    {
        _restartTransactions.Length.Should().BeGreaterThan(0);
        if (_restartTransactions.Any(transaction => transaction != _pending)) throw new InvalidOperationException("Restart replaced the sealed baseline transaction.");
    }

    [Given("no valid Redis projection can be returned")]
    public async Task CacheAbsentAsync() => await scenario.ClearCacheAsync();

    [Given("indexed PostgreSQL entitlement authority is unavailable")]
    public async Task IndexUnavailableAsync()
    {
        using var scope = scenario.Node.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HushNodeDbContext>().Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"HushVoting\".\"LicenceAssignment\" RENAME TO \"LicenceAssignment_TestUnavailable\"");
    }

    [When("HushVoting requests Alice's entitlement")]
    public void RequestUnavailable() { _queries = scenario.Faults.EntitlementRequests; scenario.Faults.ReleaseEntitlementQueries(); }

    [Then("HushVoting says it cannot verify the licence")]
    public async Task CannotVerifyAsync()
    {
        await Expect(Page.GetByTestId("entitlement-gate-heading")).ToHaveTextAsync(
            "We couldn’t verify your licence. HushVoting! cannot open until verification succeeds.", new() { Timeout = 20_000 });
        await WorkspaceUnmountedAsync();
    }

    [Then("it does not show Direct Free or a previous entitlement")]
    public async Task NoInventedPlanAsync()
    {
        await Expect(Page.Locator(".licence-account-summary,.licence-current-detail")).ToHaveCountAsync(0);
        await Expect(Gate).Not.ToContainTextAsync("Direct Free");
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1, "only identity creation preceded the failed entitlement query");
    }

    [When("authority recovers and Alice retries")]
    public async Task RecoverAuthorityAsync()
    {
        using var scope = scenario.Node.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HushNodeDbContext>().Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"HushVoting\".\"LicenceAssignment_TestUnavailable\" RENAME TO \"LicenceAssignment\"");
        await scenario.ClearCacheAsync();
        await scenario.Blocks.ProduceBlockAsync();
        using var received = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(20));
        await Gate.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await received.WaitAsync();
    }

    [Then("a fresh signed query controls the next state")]
    public async Task RecoveryQueryAsync()
    {
        scenario.Faults.EntitlementRequests.Should().BeGreaterThan(_queries);
        await IndexBaselineAsync();
        await OneAssignmentAsync();
    }
}
