using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-015 real-PostgreSQL TwinTests (Phase 2 Task 2.6) for the projection schema,
/// forward migration, rebuild posture, constraints, rollback refusal, and fail-closed
/// legacy-authority readiness. Proves: forward upgrade from the exact FEAT-014 head,
/// empty/repeat rebuild, originating-transaction uniqueness, all-or-none block
/// provenance, supersession pairing, reservation constraints, destructive-rollback
/// refusal, and that legacy off-chain rows refuse serving without being deleted,
/// grandfathered, or converted (AT-LIC-015-013/014).
/// </summary>
[Collection("FEAT-015 Licensing PostgreSQL")]
[Trait("Category", "FEAT-015")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceIndexProjectionTwinTests : IAsyncLifetime
{
    private const string Feat014HeadMigration = "20260905114409_Feat014LicenceCacheOutbox";
    private const string Feat015HeadMigration = "20260906122828_Feat015LicenceIndexProjectionAndReservation";

    private const string ConstraintViolation = "23514";
    private const string UniqueViolation = "23505";

    private readonly LicensingPostgresFixture _fixture;
    private readonly string _databaseName;
    private long _counter;

    public HushVotingLicenceIndexProjectionTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"feat015_idx_{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        await _fixture.CreateDatabaseAsync(_databaseName);
    }

    public async Task DisposeAsync()
    {
        await _fixture.DropDatabaseAsync(_databaseName);
    }

    private string NextAddress() => $"feat015-index-{Interlocked.Increment(ref _counter):D4}";

    private async Task<PostgresException> CapturePostgresExceptionAsync(Func<Task> act)
    {
        var exception = await Record.ExceptionAsync(act);
        exception.Should().BeOfType<DbUpdateException>();
        var postgres = exception!.InnerException as PostgresException;
        postgres.Should().NotBeNull();
        return postgres!;
    }

    // ------------------------------------------------------------------ migration / rebuild

    [Fact]
    public async Task Forward_upgrade_from_exact_feat014_head_installs_projection_once()
    {
        await _fixture.MigrateToAsync(_databaseName, Feat014HeadMigration);
        (await ColumnNamesAsync(_databaseName, "LicenceAssignment"))
            .Should().NotContain(new[] { "OriginatingTransactionId", "OriginatingBlockIndex", "OriginatingBlockTimeStampUtc", "SupersededByAssignmentId" });
        (await TableExistsAsync(_databaseName, "LicencePendingReservation")).Should().BeFalse();

        // Forward upgrade to the FEAT-015 head is idempotent and installs the projection once.
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        (await ColumnNamesAsync(_databaseName, "LicenceAssignment"))
            .Should().Contain(new[] { "OriginatingTransactionId", "OriginatingBlockIndex", "OriginatingBlockTimeStampUtc", "SupersededByAssignmentId" });
        (await TableExistsAsync(_databaseName, "LicencePendingReservation")).Should().BeTrue();

        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        (await TableExistsAsync(_databaseName, "LicencePendingReservation")).Should().BeTrue();
    }

    [Fact]
    public async Task Empty_rebuild_and_repeat_rebuild_reach_identical_schema()
    {
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        (await ColumnNamesAsync(_databaseName, "LicenceAssignment"))
            .Should().Contain(new[] { "OriginatingTransactionId" });

        // Down to the exact FEAT-014 head (empty schema — no FEAT-015 rows yet) then rebuild.
        await _fixture.MigrateToAsync(_databaseName, Feat014HeadMigration);
        (await ColumnNamesAsync(_databaseName, "LicenceAssignment"))
            .Should().NotContain(new[] { "OriginatingTransactionId", "OriginatingBlockIndex", "OriginatingBlockTimeStampUtc" });

        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        (await ColumnNamesAsync(_databaseName, "LicenceAssignment"))
            .Should().Contain(new[] { "OriginatingTransactionId" });

        // Repeat rebuild is idempotent.
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        (await ColumnNamesAsync(_databaseName, "LicenceAssignment"))
            .Should().Contain(new[] { "OriginatingTransactionId" });
    }

    // ------------------------------------------------------------------ constraints

    [Fact]
    public async Task Assignment_requires_origin_columns_all_or_none()
    {
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        var subjectId = await InsertSubjectReturningIdAsync();

        await using var context = _fixture.CreateContext(_databaseName);
        var partial = NewAssignment(subjectId);
        partial.OriginatingTransactionId = Guid.CreateVersion7();
        partial.OriginatingBlockIndex = 1;
        partial.OriginatingBlockTimeStampUtc = null; // all-or-none CHECK must reject
        context.Set<LicenceAssignmentEntity>().Add(partial);
        var act = async () => await context.SaveChangesAsync();
        (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(ConstraintViolation);
    }

    [Fact]
    public async Task Assignment_originating_transaction_is_unique()
    {
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        var subjectA = await InsertSubjectReturningIdAsync();
        var subjectB = await InsertSubjectReturningIdAsync();
        var originatingTransactionId = Guid.CreateVersion7();

        await using (var context = _fixture.CreateContext(_databaseName))
        {
            context.Set<LicenceAssignmentEntity>().Add(NewIndexedAssignment(
                subjectA, originatingTransactionId, 1L, LicencePersistenceVocabulary.SourceBaselineFree));
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext(_databaseName))
        {
            // Same originating transaction id on a different subject must be rejected.
            context.Set<LicenceAssignmentEntity>().Add(NewIndexedAssignment(
                subjectB, originatingTransactionId, 1L, LicencePersistenceVocabulary.SourceBaselineFree));
            var act = async () => await context.SaveChangesAsync();
            (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(UniqueViolation);
        }
    }

    [Fact]
    public async Task Supersession_relationship_requires_superseded_lifecycle()
    {
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        var subjectId = await InsertSubjectReturningIdAsync();

        // A: active baseline; then supersede it (no pointer yet) so B can become the active row.
        Guid supersedingId;
        await using (var context = _fixture.CreateContext(_databaseName))
        {
            var a = NewIndexedAssignment(
                subjectId, Guid.CreateVersion7(), 1L, LicencePersistenceVocabulary.SourceBaselineFree);
            context.Set<LicenceAssignmentEntity>().Add(a);
            await context.SaveChangesAsync();
            var aId = a.LicenceAssignmentId;

            a.LifecycleStatus = LicencePersistenceVocabulary.LifecycleSuperseded;
            a.LifecycleChangedAtUtc = DateTime.UtcNow;
            a.LifecycleReason = "upgraded";
            await context.SaveChangesAsync();

            var b = NewIndexedAssignment(
                subjectId, Guid.CreateVersion7(), 2L, LicencePersistenceVocabulary.SourceConfirmedUpgrade);
            context.Set<LicenceAssignmentEntity>().Add(b);
            await context.SaveChangesAsync();
            supersedingId = b.LicenceAssignmentId;

            // A may now point at its successor (A is superseded).
            a.SupersededByAssignmentId = supersedingId;
            await context.SaveChangesAsync();
        }

        // Invalid: superseding pointer while lifecycle stays ACTIVE must be rejected.
        await using (var context = _fixture.CreateContext(_databaseName))
        {
            var invalidSubject = await InsertSubjectReturningIdAsync();
            var invalid = NewIndexedAssignment(
                invalidSubject, Guid.CreateVersion7(), 3L, LicencePersistenceVocabulary.SourceBaselineFree);
            invalid.SupersededByAssignmentId = supersedingId;
            context.Set<LicenceAssignmentEntity>().Add(invalid);
            var act = async () => await context.SaveChangesAsync();
            (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(ConstraintViolation);
        }
    }

    [Fact]
    public async Task Reservation_fingerprint_length_is_enforced()
    {
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        var subjectId = await InsertSubjectReturningIdAsync();

        await using var context = _fixture.CreateContext(_databaseName);
        var reservation = NewReservation(subjectId);
        reservation.CanonicalPayloadFingerprintSha256 = "too-short";
        context.Set<LicencePendingReservationEntity>().Add(reservation);
        var act = async () => await context.SaveChangesAsync();
        (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(ConstraintViolation);
    }

    [Fact]
    public async Task Reservation_permits_only_one_pending_per_subject()
    {
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        var subjectId = await InsertSubjectReturningIdAsync();

        await using (var context = _fixture.CreateContext(_databaseName))
        {
            context.Set<LicencePendingReservationEntity>().Add(NewReservation(subjectId));
            await context.SaveChangesAsync();
        }

        // A second pending reservation for the same subject is rejected by the partial unique index.
        await using (var context = _fixture.CreateContext(_databaseName))
        {
            context.Set<LicencePendingReservationEntity>().Add(NewReservation(subjectId));
            var act = async () => await context.SaveChangesAsync();
            (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(UniqueViolation);
        }
    }

    [Fact]
    public async Task Reservation_originating_transaction_is_unique()
    {
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        var subjectA = await InsertSubjectReturningIdAsync();
        var subjectB = await InsertSubjectReturningIdAsync();

        var transactionId = Guid.CreateVersion7();
        await using (var context = _fixture.CreateContext(_databaseName))
        {
            var reservation = NewReservation(subjectA);
            reservation.OriginatingTransactionId = transactionId;
            context.Set<LicencePendingReservationEntity>().Add(reservation);
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext(_databaseName))
        {
            var duplicate = NewReservation(subjectB);
            duplicate.OriginatingTransactionId = transactionId;
            context.Set<LicencePendingReservationEntity>().Add(duplicate);
            var act = async () => await context.SaveChangesAsync();
            (await CapturePostgresExceptionAsync(act)).SqlState.Should().Be(UniqueViolation);
        }
    }

    // ------------------------------------------------------------------ rollback refusal

    [Fact]
    public async Task Destructive_rollback_refuses_when_feat015_data_exists()
    {
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        var subjectId = await InsertSubjectReturningIdAsync();

        await using (var context = _fixture.CreateContext(_databaseName))
        {
            context.Set<LicenceAssignmentEntity>().Add(NewIndexedAssignment(
                subjectId, Guid.CreateVersion7(), 1L, LicencePersistenceVocabulary.SourceBaselineFree));
            await context.SaveChangesAsync();
        }

        // FEAT-015 Down guard refuses once an indexed assignment exists.
        var act = async () => await _fixture.MigrateToAsync(_databaseName, Feat014HeadMigration);
        var exception = await act.Should().ThrowAsync<PostgresException>();
        exception.And.MessageText.Should().Contain("Destructive rollback refused");
    }

    [Fact]
    public async Task Empty_schema_down_to_feat014_head_succeeds_when_no_feat015_rows()
    {
        // Migration state only (no rows): down succeeds so the FEAT-013/014 lifecycle twin tests
        // that exercise clean rollback continue to pass with the extended head.
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        await _fixture.MigrateToAsync(_databaseName, Feat014HeadMigration);
        (await TableExistsAsync(_databaseName, "LicencePendingReservation")).Should().BeFalse();
    }

    // ------------------------------------------------------------------ legacy readiness

    [Fact]
    public async Task Legacy_offchain_assignment_refuses_readiness_without_mutation()
    {
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        var subjectId = await InsertSubjectReturningIdAsync();

        // A FEAT-013-era row: lifecycle + source but NO originating transaction.
        await using (var context = _fixture.CreateContext(_databaseName))
        {
            var legacy = NewAssignment(subjectId);
            legacy.Source = LicencePersistenceVocabulary.SourceDefaultFree;
            legacy.OriginatingTransactionId = null;
            legacy.OriginatingBlockIndex = null;
            legacy.OriginatingBlockTimeStampUtc = null;
            context.Set<LicenceAssignmentEntity>().Add(legacy);
            await context.SaveChangesAsync();
        }

        var evaluator = new LicenceIndexAuthorityReadinessEvaluator(
            () => _fixture.CreateContext(_databaseName));
        var result = await evaluator.EvaluateAsync(CancellationToken.None);

        result.Ready.Should().BeFalse();
        result.StableCode.Should().Be(LicenceIndexAuthorityReadinessCodes.LegacyOffChainAssignmentPresent);
        result.LegacyAssignmentCount.Should().Be(1);

        // No row was deleted, converted, or grandfathered.
        await using (var context = _fixture.CreateContext(_databaseName))
        {
            var rows = await context.Set<LicenceAssignmentEntity>().ToListAsync();
            rows.Should().HaveCount(1);
            rows[0].OriginatingTransactionId.Should().BeNull();
            rows[0].Source.Should().Be(LicencePersistenceVocabulary.SourceDefaultFree);
        }
    }

    [Fact]
    public async Task Readiness_is_ready_on_empty_and_indexed_only_projection()
    {
        await _fixture.MigrateToAsync(_databaseName, Feat015HeadMigration);
        var evaluator = new LicenceIndexAuthorityReadinessEvaluator(
            () => _fixture.CreateContext(_databaseName));

        var empty = await evaluator.EvaluateAsync(CancellationToken.None);
        empty.Ready.Should().BeTrue();

        // An indexed row (full provenance) keeps readiness green.
        var subjectId = await InsertSubjectReturningIdAsync();
        await using (var context = _fixture.CreateContext(_databaseName))
        {
            context.Set<LicenceAssignmentEntity>().Add(NewIndexedAssignment(
                subjectId, Guid.CreateVersion7(), 1L, LicencePersistenceVocabulary.SourceBaselineFree));
            await context.SaveChangesAsync();
        }

        var indexed = await evaluator.EvaluateAsync(CancellationToken.None);
        indexed.Ready.Should().BeTrue();
        indexed.StableCode.Should().BeNull();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Guid> InsertSubjectReturningIdAsync()
    {
        await using var context = _fixture.CreateContext(_databaseName);
        var subject = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
            CanonicalPublicSigningAddress = NextAddress(),
            IdentityCreationBlockIndex = 1,
            CreatedAtUtc = DateTime.UtcNow,
            EntitlementRevision = 0,
        };
        context.Set<LicenceSubjectEntity>().Add(subject);
        await context.SaveChangesAsync();
        return subject.LicenceSubjectId;
    }

    private static LicenceAssignmentEntity NewAssignment(Guid subjectId) =>
        new()
        {
            LicenceAssignmentId = Guid.CreateVersion7(),
            LicenceSubjectId = subjectId,
            PlanId = "hushvoting.direct.free",
            AssignedCatalogueVersion = "hushvoting-licence-catalogue/v1.0.0",
            AssignedCatalogueDigestSha256 = new string('a', 64),
            LifecycleStatus = LicencePersistenceVocabulary.LifecycleActive,
            Source = LicencePersistenceVocabulary.SourceBaselineFree,
            EffectiveFromUtc = DateTime.UtcNow,
            ExpiresAtUtc = null,
            PlanFamily = LicencePersistenceVocabulary.PlanFamilyDirect,
            UpgradeRank = 0,
            EligibleVoterCap = 100,
            UnlimitedElectionPolicy = true,
            TermKind = LicencePersistenceVocabulary.TermPerpetual,
            TermYears = 0,
            AllowedGovernanceOptionIds = new[] { "gov-direct" },
        };

    private static LicenceAssignmentEntity NewIndexedAssignment(
        Guid subjectId,
        Guid originatingTransactionId,
        long blockIndex,
        string source)
    {
        var assignment = NewAssignment(subjectId);
        assignment.Source = source;
        assignment.OriginatingTransactionId = originatingTransactionId;
        assignment.OriginatingBlockIndex = blockIndex;
        assignment.OriginatingBlockTimeStampUtc = DateTime.UtcNow;
        return assignment;
    }

    private static LicencePendingReservationEntity NewReservation(Guid subjectId) =>
        new()
        {
            LicencePendingReservationId = Guid.CreateVersion7(),
            LicenceSubjectId = subjectId,
            OriginatingTransactionId = Guid.CreateVersion7(),
            CanonicalPayloadFingerprintSha256 = new string('b', 64),
            TransitionIntent = LicencePersistenceVocabulary.SourceBaselineFree,
            RequestedPlanId = "hushvoting.direct.free",
            ObservedCatalogueVersion = "hushvoting-licence-catalogue/v1.0.0",
            ExpectedCurrentLicenceTransactionId = null,
            ExpectedCurrentPlanId = null,
            LifecycleStatus = LicencePersistenceVocabulary.ReservationLifecyclePending,
            RequestedUpgradeRank = 0,
            CreatedAtUtc = DateTime.UtcNow,
            ResolvedAtUtc = null,
        };

    private async Task<bool> TableExistsAsync(string databaseName, string tableName)
    {
        await using var connection = await OpenAsync(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'HushVoting' AND table_name = @table)";
        command.Parameters.AddWithValue("table", tableName);
        var result = await command.ExecuteScalarAsync();
        return result is bool exists && exists;
    }

    private async Task<string[]> ColumnNamesAsync(string databaseName, string tableName)
    {
        await using var connection = await OpenAsync(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'HushVoting' AND table_name = @table ORDER BY column_name";
        command.Parameters.AddWithValue("table", tableName);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private async Task<NpgsqlConnection> OpenAsync(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(_fixture.AdminConnectionString)
        {
            Database = databaseName,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
