using FluentAssertions;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-015 real-PostgreSQL TwinTests (Phase 6 Task 6.4) for the licence block-index writer —
/// the only activation path. Proves baseline Direct Free indexing, higher-Veritas supersede with
/// history, containing-block time provenance, replay convergence (no second row / no revision
/// bump), atomic projection + outbox row, and stale lower/same transitions never reactivating at
/// block time.
/// </summary>
[Collection("FEAT-015 Licensing PostgreSQL")]
[Trait("Category", "FEAT-015")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceBlockIndexWriterTwinTests : IAsyncLifetime
{
    private readonly LicensingPostgresFixture _fixture;
    private readonly string _databaseName;
    private long _counter;

    public HushVotingLicenceBlockIndexWriterTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"feat015_idxw_{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        await _fixture.CreateDatabaseAsync(_databaseName);
        await _fixture.MigrateToAsync(_databaseName, "20260906122828_Feat015LicenceIndexProjectionAndReservation");
    }

    public async Task DisposeAsync()
    {
        await _fixture.DropDatabaseAsync(_databaseName);
    }

    private string NextAddress() => $"feat015-idxw-{Interlocked.Increment(ref _counter):D4}";

    private static readonly HushVotingLicenceCatalogue Catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();

    private static readonly LicenceServiceConfiguration Configuration =
        LicenceServiceConfiguration.CreateDefault(catalogue: Catalogue);

    private static readonly DateTime BlockTime =
        DateTime.Parse("2026-01-01T00:00:00Z", null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();

    private static readonly Guid BaselineTx = Guid.Parse("5f2d9e11-3c44-4a80-b8e7-6b2f1a0c9d3e");

    private async Task<AuthenticatedIdentitySubject> InsertSubjectAsync(string? canonicalAddress = null)
    {
        var canonical = canonicalAddress ?? NextAddress();
        var subject = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
            CanonicalPublicSigningAddress = canonical,
            IdentityCreationBlockIndex = 100,
            CreatedAtUtc = BlockTime,
            EntitlementRevision = 0,
        };
        await using (var context = _fixture.CreateContext(_databaseName))
        {
            context.Set<LicenceSubjectEntity>().Add(subject);
            await context.SaveChangesAsync();
        }

        var ok = AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity,
            canonical,
            100,
            out var trusted,
            out _);
        ok.Should().BeTrue();
        return trusted!;
    }

    private static HushVotingLicenceAssignmentPayload BaselinePayload() =>
        new(HushVotingLicenceTransitionIntent.BaselineFree, "hushvoting.direct.free", Catalogue.Version.Value);

    private static HushVotingLicenceAssignmentPayload UpgradePayload() =>
        new(
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
            "hushvoting.veritas.2000",
            Catalogue.Version.Value,
            BaselineTx,
            "hushvoting.direct.free");

    private static ValidatedTransaction<HushVotingLicenceAssignmentPayload> BuildValidated(
        Guid txId,
        HushVotingLicenceAssignmentPayload payload)
    {
        var unsigned = new UnsignedTransaction<HushVotingLicenceAssignmentPayload>(
            new TransactionId(txId),
            HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind,
            new HushShared.Blockchain.Model.Timestamp(BlockTime),
            payload,
            HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload));
        var signed = new SignedTransaction<HushVotingLicenceAssignmentPayload>(
            unsigned,
            new HushShared.Blockchain.Model.SignatureInfo("signatory", "sig"));
        return signed.SignByValidator(new HushShared.Blockchain.Model.SignatureInfo("validator", "vsig"));
    }

    private async Task<LicenceBlockIndexResult> IndexAsync(
        AuthenticatedIdentitySubject subject,
        Guid txId,
        HushVotingLicenceAssignmentPayload payload,
        long blockIndex,
        DateTime blockTime,
        LicenceCacheOutboxPolicy? cacheOutbox = null) =>
        await LicenceBlockIndexWriter.IndexAsync(
            () => _fixture.CreateContext(_databaseName),
            Configuration,
            subject,
            BuildValidated(txId, payload),
            blockIndex,
            blockTime,
            cacheOutbox,
            CancellationToken.None);

    [Fact]
    public async Task Baseline_direct_free_indexes_from_containing_block_time()
    {
        var subject = await InsertSubjectAsync();
        var writerResult = await IndexAsync(subject, BaselineTx, BaselinePayload(), 42, BlockTime);

        writerResult.Indexed.Should().BeTrue();
        writerResult.EntitlementRevision.Should().Be(1);

        await using var context = _fixture.CreateContext(_databaseName);
        var assignment = await context.Set<LicenceAssignmentEntity>()
            .SingleAsync(a => a.OriginatingTransactionId == BaselineTx);

        assignment.PlanId.Should().Be("hushvoting.direct.free");
        assignment.LifecycleStatus.Should().Be(LicencePersistenceVocabulary.LifecycleActive);
        assignment.EffectiveFromUtc.Should().Be(BlockTime);
        assignment.OriginatingBlockIndex.Should().Be(42);
        assignment.OriginatingBlockTimeStampUtc.Should().Be(BlockTime);
        assignment.ExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Upgrade_indexes_supersedes_history_and_sets_annual_expiry()
    {
        var subject = await InsertSubjectAsync();
        await IndexAsync(subject, BaselineTx, BaselinePayload(), 42, BlockTime);

        var upgradeTx = Guid.Parse("8c6a1b77-4d2e-4f91-a4c0-9e7b2d8f1a55");
        var upgradeTime = BlockTime.AddDays(30);
        var upgrade = await IndexAsync(subject, upgradeTx, UpgradePayload(), 55, upgradeTime);

        upgrade.Indexed.Should().BeTrue();
        upgrade.EntitlementRevision.Should().Be(2);

        await using var context = _fixture.CreateContext(_databaseName);
        var assignments = await context.Set<LicenceAssignmentEntity>().ToListAsync();
        assignments.Should().HaveCount(2);

        var active = assignments.Single(a => a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive);
        active.PlanId.Should().Be("hushvoting.veritas.2000");
        active.EffectiveFromUtc.Should().Be(upgradeTime);
        active.ExpiresAtUtc.Should().Be(upgradeTime.AddYears(1));
        active.OriginatingBlockIndex.Should().Be(55);

        var superseded = assignments.Single(a => a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleSuperseded);
        superseded.PlanId.Should().Be("hushvoting.direct.free");
        superseded.SupersededByAssignmentId.Should().Be(active.LicenceAssignmentId);
    }

    [Fact]
    public async Task Replay_of_an_indexed_transaction_converges_without_duplicate_or_revision_bump()
    {
        var subject = await InsertSubjectAsync();
        await IndexAsync(subject, BaselineTx, BaselinePayload(), 42, BlockTime);

        var replay = await IndexAsync(subject, BaselineTx, BaselinePayload(), 42, BlockTime);

        replay.Indexed.Should().BeTrue(); // converged duplicate
        await using var context = _fixture.CreateContext(_databaseName);
        (await context.Set<LicenceAssignmentEntity>()
            .CountAsync(a => a.OriginatingTransactionId == BaselineTx)).Should().Be(1);

        var subjectRow = await context.Set<LicenceSubjectEntity>().SingleAsync();
        subjectRow.EntitlementRevision.Should().Be(1);
    }

    [Fact]
    public async Task Atomic_index_write_enqueues_cache_outbox_row_when_policy_enabled()
    {
        var policy = new LicenceCacheOutboxPolicy(true, null);
        var subject = await InsertSubjectAsync();

        await IndexAsync(subject, BaselineTx, BaselinePayload(), 42, BlockTime, policy);

        await using var context = _fixture.CreateContext(_databaseName);
        var outboxRows = await context.Set<LicenceCacheOutboxEntity>().ToListAsync();
        outboxRows.Should().ContainSingle();
        outboxRows[0].ChangeKind.Should().Be(LicenceCacheOutboxChangeKinds.ActivatedHigherPlan);
        outboxRows[0].CommittedRevision.Should().Be(1);
    }

    [Fact]
    public async Task Stale_lower_or_same_transition_never_reactivates_at_block_time()
    {
        var subject = await InsertSubjectAsync();
        await IndexAsync(subject, BaselineTx, BaselinePayload(), 42, BlockTime);

        // Higher Veritas 2000 upgrade indexes normally (expected current = the indexed baseline).
        var veritas2000Tx = Guid.Parse("8c6a1b77-4d2e-4f91-a4c0-9e7b2d8f1a55");
        await IndexAsync(subject, veritas2000Tx, UpgradePayload(), 55, BlockTime.AddDays(30));

        // A stale lower Veritas 500 "upgrade" that referenced the baseline is now a downgrade at
        // block time (active is Veritas 2000) and must never index or change the active assignment.
        var lowerTx = Guid.Parse("bbbbbbbb-3333-4444-8555-666666666666");
        var lowerPayload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
            "hushvoting.veritas.500",
            Catalogue.Version.Value,
            BaselineTx,
            "hushvoting.direct.free");

        var stale = await IndexAsync(subject, lowerTx, lowerPayload, 60, BlockTime.AddDays(60));

        stale.Indexed.Should().BeFalse();
        stale.StableErrorCode.Should().NotBeNull();

        await using var context = _fixture.CreateContext(_databaseName);
        var active = await context.Set<LicenceAssignmentEntity>()
            .SingleAsync(a => a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive);
        active.PlanId.Should().Be("hushvoting.veritas.2000"); // unchanged
    }
}
