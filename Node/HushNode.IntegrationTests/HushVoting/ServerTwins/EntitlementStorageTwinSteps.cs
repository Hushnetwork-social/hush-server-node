using FluentAssertions;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

[Binding]
[Scope(Tag = "HV-ENTITLEMENT-STORAGE-TWIN")]
internal sealed class EntitlementStorageTwinSteps(HushVotingScenario scenario)
{
    private ElectionRecord _election = null!;
    private ElectionEntitlementCapture _capture = null!;
    private ElectionRosterLinkBoundary _link = null!;
    private static readonly DateTime At = new(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private async Task WithContext(Func<ElectionsDbContext, Task> work)
    {
        using var scope = scenario.Node.Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<ElectionsDbContext>());
    }

    [Given("an owned PostgreSQL election with supported capture contract facts")]
    public async Task Prepare()
    {
        scenario.Page.Should().BeNull();
        _election = ElectionModelFactory.CreateDraftRecord(
            ElectionId.NewElectionId, "Storage contract election", null, "storage-contract-owner", null,
            ElectionClass.OrganizationalRemoteVoting, ElectionBindingStatus.Binding,
            "admin-prod-1of1", false, ElectionGovernanceMode.AdminOnly, ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice, VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType.OrganizationImportedRoster, EligibilityMutationPolicy.FrozenAtOpen,
            new OutcomeRuleDefinition(OutcomeRuleKind.SingleWinner, "single_winner", 1, true, true, false, "tie_unresolved", "highest_non_blank_votes"),
            [new ApprovedClientApplicationRecord("hushvoting", "1.0.0")], "omega-v1.0.0",
            ReportingPolicy.DefaultPhaseOnePackage, ReviewWindowPolicy.NoReviewWindow,
            [new ElectionOptionDefinition("one", "One", null, 1, false)], createdAt: At);
        _capture = new(_election.ElectionId, Guid.NewGuid(), Guid.NewGuid(), "hushvoting.veritas.500", "veritas", 1,
            500, true, "annual", 1, At.AddMonths(-1), At.AddMonths(11), "[\"no-customer-trustees\"]",
            "hushvoting-licence-catalogue/v1.0.0", new string('b', 64), 1, "admin-prod-1of1", "no-customer-trustees",
            500, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10, 0, At);
        _link = new(_election.ElectionId, Guid.NewGuid(), At);
        _capture.HasSupportedSemantics().Should().BeTrue();
        await WithContext(async db => { db.Elections.Add(_election); await db.SaveChangesAsync(); });
    }

    private async Task WriteCapture(bool commit)
    {
        await WithContext(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var election = await db.Elections.SingleAsync(x => x.ElectionId == _election.ElectionId);
            db.Entry(election).CurrentValues.SetValues(election with { LifecycleState = ElectionLifecycleState.Open, OpenedAt = At });
            (await ElectionEntitlementStorage.AddCaptureAsync(db, _capture)).Should().Be(ElectionEvidenceWriteOutcome.Added);
            await db.SaveChangesAsync();
            if (commit) await tx.CommitAsync();
            // Disposal without commit models interruption after SaveChanges, before transaction commit.
        });
    }

    [When("an election and capture transaction is interrupted before commit")]
    public Task Interrupt() => WriteCapture(false);

    [Then("neither Open state nor capture persists and a committed retry persists both")]
    public async Task Atomic()
    {
        await Absent();
        await WriteCapture(true);
        await WithContext(async db => {
            (await db.Elections.SingleAsync(x => x.ElectionId == _election.ElectionId)).LifecycleState.Should().Be(ElectionLifecycleState.Open);
            (await db.ElectionEntitlementCaptures.SingleAsync()).Should().Be(_capture);
        });
    }

    [When("the same capture is replayed and different terms are attempted")]
    public async Task Replay()
    {
        await WriteCapture(true);
        await WithContext(async db => {
            (await ElectionEntitlementStorage.AddCaptureAsync(db, _capture)).Should().Be(ElectionEvidenceWriteOutcome.IdenticalReplay);
            (await ElectionEntitlementStorage.AddCaptureAsync(db, _capture with { EligibleVoterCap = 2000 })).Should().Be(ElectionEvidenceWriteOutcome.Conflict);
            await db.SaveChangesAsync();
        });
    }

    [Then("exactly one unchanged capture survives both application and direct SQL overwrite attempts")]
    public async Task Immutable()
    {
        await WithContext(async db => {
            Func<Task> mutate = async () => await db.Database.ExecuteSqlRawAsync("UPDATE \"Elections\".\"ElectionEntitlementCapture\" SET \"EligibleVoterCap\" = 2000");
            await mutate.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
        });
        await WithContext(async db => {
            (await db.ElectionEntitlementCaptures.CountAsync()).Should().Be(1);
            (await db.ElectionEntitlementCaptures.SingleAsync()).Should().Be(_capture);
        });
    }

    [When("first link evidence is committed and the context is reopened")]
    public Task Link() => WithContext(async db => {
        (await ElectionEntitlementStorage.AddFirstLinkAsync(db, _link)).Should().Be(ElectionEvidenceWriteOutcome.Added);
        await db.SaveChangesAsync();
    });

    [Then("the original link boundary remains and direct deletion is rejected")]
    public async Task LinkSurvives()
    {
        await WithContext(async db => {
            (await db.ElectionRosterLinkBoundaries.SingleAsync()).Should().Be(_link);
            (await ElectionEntitlementStorage.AddFirstLinkAsync(db, _link with { SourceTransactionId = Guid.NewGuid() })).Should().Be(ElectionEvidenceWriteOutcome.Conflict);
            Func<Task> clear = async () => await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Elections\".\"ElectionRosterLinkBoundary\"");
            await clear.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
        });
        await WithContext(async db => (await db.ElectionRosterLinkBoundaries.SingleAsync()).Should().Be(_link));
    }

    [When("a capture with an unsupported schema is submitted")]
    public Task Unsupported() => WithContext(async db => {
        (await ElectionEntitlementStorage.AddCaptureAsync(db, _capture with { SchemaVersion = 999 })).Should().Be(ElectionEvidenceWriteOutcome.Unsupported);
        await db.SaveChangesAsync();
    });

    [Then("no capture is persisted and the election stays Draft")]
    public Task Absent() => WithContext(async db => {
        (await db.ElectionEntitlementCaptures.CountAsync()).Should().Be(0);
        (await db.Elections.SingleAsync(x => x.ElectionId == _election.ElectionId)).LifecycleState.Should().Be(ElectionLifecycleState.Draft);
    });

    [When("a schema downgrade is attempted after a capture commits")]
    public async Task Downgrade()
    {
        await PrepareHistoricalStorageSchema();
        await WriteCapture(true);
        using var scope = scenario.Node.Services.CreateScope();
        var hostDb = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var applied = (await hostDb.Database.GetAppliedMigrationsAsync()).ToArray();
        applied.Last().Should().EndWith("Feat018RejectedOpenOutcome");
        Func<Task> downgrade = () => hostDb.GetService<IMigrator>().MigrateAsync(applied[Array.FindIndex(applied, id => id.EndsWith("Feat018ElectionEntitlementEvidence", StringComparison.Ordinal)) - 1]);
        await downgrade.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
    }

    [Then("the migration and all captured evidence remain intact")]
    public async Task Preserved()
    {
        using var scope = scenario.Node.Services.CreateScope();
        var hostDb = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        (await hostDb.Database.GetAppliedMigrationsAsync()).Last().Should().EndWith("Feat018RejectedOpenOutcome");
        await WithContext(async db => (await db.ElectionEntitlementCaptures.SingleAsync()).Should().Be(_capture));
    }

    [When("the populated prior schema is upgraded through the entitlement migration")]
    public async Task UpgradePopulatedSchema()
    {
        await PrepareHistoricalStorageSchema();
        using var scope = scenario.Node.Services.CreateScope();
        var hostDb = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var applied = (await hostDb.Database.GetAppliedMigrationsAsync()).ToArray();
        applied.Last().Should().EndWith("Feat018RejectedOpenOutcome");
        // No new evidence exists yet: compatible rollback is safe, including the existing Draft.
        await hostDb.GetService<IMigrator>().MigrateAsync(applied[Array.FindIndex(applied, id => id.EndsWith("Feat018ElectionEntitlementEvidence", StringComparison.Ordinal)) - 1]);
        (await hostDb.Database.GetAppliedMigrationsAsync()).Should().NotContain(applied[^1]);
        (await hostDb.Set<ElectionRecord>().AsNoTracking().SingleAsync()).Title.Should().Be(_election.Title);
        await hostDb.GetService<IMigrator>().MigrateAsync(applied[^1]);
    }

    [Then("the existing Draft is unchanged and no historical entitlement is invented")]
    public async Task NoInventedEvidence()
    {
        await Absent();
        await WithContext(async db => {
            var retained = await db.Elections.SingleAsync();
            retained.ElectionId.Should().Be(_election.ElectionId);
            retained.OwnerPublicAddress.Should().Be(_election.OwnerPublicAddress);
            retained.SelectedProfileId.Should().Be(_election.SelectedProfileId);
            retained.CurrentDraftRevision.Should().Be(_election.CurrentDraftRevision);
            (await db.ElectionRosterLinkBoundaries.CountAsync()).Should().Be(0);
        });
    }
    [When("a negative Open outcome is committed and its schema downgrade is attempted")]
    public async Task RejectOpenAndDowngrade()
    {
        await PrepareHistoricalStorageSchema();
        var rejection = new ElectionOpenRejection(Guid.NewGuid(), _election.ElectionId, Guid.NewGuid(),
            12, 2, At, null, 4, ElectionEntitlementReason.LimitExceeded);
        rejection.HasSupportedSemantics().Should().BeTrue();
        await WithContext(async db => { db.ElectionOpenRejections.Add(rejection); await db.SaveChangesAsync(); });
        using var scope = scenario.Node.Services.CreateScope();
        var hostDb = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        var applied = (await hostDb.Database.GetAppliedMigrationsAsync()).ToArray();
        Func<Task> downgrade = () => hostDb.GetService<IMigrator>().MigrateAsync(applied[^2]);
        await downgrade.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
    }

    [Then("negative Open evidence survives restart of the context and cannot be erased or changed")]
    public async Task RejectionImmutable()
    {
        await WithContext(async db => {
            var original = await db.ElectionOpenRejections.AsNoTracking().SingleAsync();
            original.Reason.Should().Be(ElectionEntitlementReason.LimitExceeded);
            original.HasSupportedSemantics().Should().BeTrue();
            Func<Task> change = () => db.Database.ExecuteSqlRawAsync("UPDATE \"Elections\".\"ElectionOpenRejection\" SET \"Reason\" = 0");
            await change.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
            Func<Task> delete = () => db.Database.ExecuteSqlRawAsync("DELETE FROM \"Elections\".\"ElectionOpenRejection\"");
            await delete.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
            (await db.ElectionOpenRejections.AsNoTracking().SingleAsync()).Should().Be(original);
        });
    }

    private async Task PrepareHistoricalStorageSchema()
    {
        // These three cases qualify the historical capture/rejection migrations, not
        // the later checkpoint migration. The scenario owns this PostgreSQL instance.
        // Represent the prior schema explicitly so the new checkpoint guard cannot
        // mask a broken older guard. Current-schema rollback refusal is exercised by
        // HV-ENTITLEMENT-ROLLOUT-TWIN, with its real checkpoints left intact.
        using var scope = scenario.Node.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HushNodeDbContext>();
        (await db.Set<ElectionIndexCheckpoint>().CountAsync()).Should().BeGreaterThan(0);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Elections\".\"ElectionIndexCheckpoint\" DISABLE TRIGGER \"ImmutableIndexCheckpoint\"");
            await db.Set<ElectionIndexCheckpoint>().ExecuteDeleteAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Elections\".\"ElectionIndexCheckpoint\" ENABLE TRIGGER \"ImmutableIndexCheckpoint\"");
            await transaction.CommitAsync();
        }
        var historical = (await db.Database.GetAppliedMigrationsAsync())
            .Single(id => id.EndsWith("Feat018RejectedOpenOutcome", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(historical);
        (await db.Database.GetAppliedMigrationsAsync()).Last().Should().Be(historical);
    }

}
