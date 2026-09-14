using System.Text.Json;
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

// EPIC-001 -> FEAT-008 AC-008-057 -> Phase 3 Tasks 3.7/3.8, Phase 7 Tasks 7.1/7.2.
[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class RecoveryDisappearanceSteps(HushVotingScenario scenario, RecoveryProtectionSteps protection,
    HushVotingIdentityJourney identity)
{
    private IPage Page => scenario.Page;
    private const string ReviewedAlias = "Explicitly recreated identity";

    [Given("Alice reviewed her real recovery profile before it disappeared from the controlled node")]
    public async Task DisappearAsync()
    {
        await protection.ReadyAsync();
        // Negative fixture arrangement representing the specified final-lookup disappearance.
        // No positive RPC reply is fabricated, and no production deletion API is introduced.
        using var scope = scenario.Node.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.Profiles.Where(profile => profile.PublicSigningAddress == identity.Keys.SigningPublicKey).ExecuteDeleteAsync()).Should().Be(1);
        // A controlled node reset must also forget the original process-local
        // admission reservation. Use the admission instance that served the RPC.
        var admission = scope.ServiceProvider.GetRequiredService<IFullIdentityAdmissionService>();
        await ((IFullIdentityReservationService)admission).ReleaseAsync(identity.Keys.SigningPublicKey, CancellationToken.None);
        await scenario.ClearCacheAsync();
    }

    [When("final online verification finds authoritative absence after recovery protection")]
    public async Task StageAndVerifyAsync()
    {
        await Page.GetByTestId("recovery-no-retention-ack").CheckAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Confirm device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("recreate-alias")).ToBeVisibleAsync();
        scenario.Faults.IdentityLookups.Last().Reply.Successfull.Should().BeFalse();
        scenario.Faults.SubmittedTransactions.Count.Should().Be(1, "only the fixture's original identity was submitted before fresh consent");
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("entitlement-gate")).ToHaveCountAsync(0);
        var stage = await HushVotingVaultInspection.InspectAsync(Page, identity.Keys, HushVotingIdentityJourney.Alias, false);
        (stage.KeysMatch && stage.ConcreteKeysOnly && stage.DevicePasswordProtected && !stage.Active && !stage.HasPendingTransaction).Should().BeTrue();
    }

    [Then("fresh profile consent reuses the protected recovered keys and only actual indexing grants access")]
    public async Task ExplicitRecreationAsync()
    {
        await Expect(Page.GetByTestId("recreate-alias")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("visibility-private")).ToBeCheckedAsync();
        var create = Page.GetByRole(AriaRole.Button, new() { Name = "Create HushNetwork identity", Exact = true });
        await Expect(create).ToBeDisabledAsync();
        await Page.GetByTestId("recreate-alias").FillAsync(ReviewedAlias);
        await Page.GetByTestId("visibility-public").CheckAsync();
        await Expect(create).ToBeDisabledAsync();
        await Page.GetByTestId("public-ack").CheckAsync();
        using (var accepted = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30)))
        {
            await create.ClickAsync();
            try { await accepted.WaitAsync(); }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("Recreation was not admitted; submitted count=" + scenario.Faults.SubmittedTransactions.Count
                    + "; closed server outcomes=" + string.Join(',', scenario.Faults.Submissions.Select(reply => reply.Status.ToString())));
            }
        }
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Waiting for your restored identity", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Device password", new() { Exact = true })).ToHaveCountAsync(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        using (var submitted = JsonDocument.Parse(scenario.Faults.SubmittedTransactions.Last()))
        {
            submitted.RootElement.GetProperty("PayloadKind").GetString().Should().Be("351cd60b-3fdf-48d4-b608-e93c0100f7d0");
            var payload = submitted.RootElement.GetProperty("Payload");
            (payload.GetProperty("PublicSigningAddress").GetString() == identity.Keys.SigningPublicKey
                && payload.GetProperty("PublicEncryptAddress").GetString() == identity.Keys.EncryptPublicKey).Should().BeTrue();
            payload.GetProperty("IdentityAlias").GetString().Should().Be(ReviewedAlias);
            payload.GetProperty("IsPublic").GetBoolean().Should().BeTrue();
        }
        await Expect(Page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        using var baseline = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await scenario.Blocks.ProduceBlockAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Check again", Exact = true }).ClickAsync();
        await baseline.WaitAsync();
        await scenario.Blocks.ProduceBlockAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = ReviewedAlias, Exact = true })).ToBeVisibleAsync();
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(Page, identity.Keys, ReviewedAlias, true)).Should().BeTrue();
        await Page.GetByRole(AriaRole.Button, new() { Name = ReviewedAlias, Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog, new() { Name = "User information" }).GetByRole(AriaRole.Button, new() { Name = "Lock", Exact = true }).ClickAsync();
        await Page.ReloadAsync();
        await HushVotingIdentityJourney.FillSecretAsync(Page.GetByLabel("Device password", new() { Exact = true }), HushVotingScenario.DevicePassword);
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Unlock HushVoting") }).ClickAsync();
        await Expect(Page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        scenario.Faults.SubmittedTransactions.Count.Should().Be(3, "original fixture identity, explicitly recreated identity and one root-owned licence only");
    }
}
