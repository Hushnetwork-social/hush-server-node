using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-014 real-PostgreSQL TwinTests for the transactional cache outbox (Phase 2 Task 2.4).
/// Proves the FEAT-014 migration after the exact FEAT-013 predecessor, clean install,
/// constraint/check enforcement, privacy column inventory, bounded query shapes, and the
/// destructive-rollback guard that refuses to discard undelivered rows.
/// </summary>
[Collection("FEAT-014 Licensing PostgreSQL")]
[Trait("Category", "FEAT-014")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceCacheOutboxTwinTests
{
    private const string PredecessorMigration =
        "20260903135312_Feat013HushVotingLicensingPersistence";

    private const string HeadMigration =
        "20260905114409_Feat014LicenceCacheOutbox";

    private readonly LicensingPostgresFixture _fixture;

    public HushVotingLicenceCacheOutboxTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Clean_install_and_exact_predecessor_upgrade_install_outbox_once()
    {
        var databaseName = $"feat014_mig_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        try
        {
            // 1) Clean install straight to FEAT-014 head installs the outbox table.
            await _fixture.MigrateToHeadAsync(databaseName);
            (await TableExistsAsync(databaseName, "LicenceCacheOutbox")).Should().BeTrue();
            (await ConstraintCountAsync(databaseName, "CK_LicenceCacheOutbox_")).Should().Be(6);
            (await IndexCountAsync(databaseName, "IX_LicenceCacheOutbox_PendingClaimOrder")).Should().Be(1);
            (await IndexCountAsync(databaseName, "IX_LicenceCacheOutbox_DeliveredCleanup")).Should().Be(1);
            (await IndexCountAsync(databaseName, "IX_LicenceCacheOutbox_Subject")).Should().Be(1);

            // 2) Restart is idempotent.
            await _fixture.MigrateToHeadAsync(databaseName);
            (await TableExistsAsync(databaseName, "LicenceCacheOutbox")).Should().BeTrue();
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Predecessor_upgrade_adds_only_outbox_and_keeps_licensing_tables()
    {
        var databaseName = $"feat014_upg_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        try
        {
            // Exact FEAT-013 predecessor: five licensing tables, no outbox.
            await _fixture.MigrateToAsync(databaseName, PredecessorMigration);
            (await TableCountInHushVotingAsync(databaseName)).Should().Be(5);
            (await TableExistsAsync(databaseName, "LicenceCacheOutbox")).Should().BeFalse();

            // Upgrading to the FEAT-014 head adds exactly one table and preserves licensing data.
            await InsertLicensingDataAsync(databaseName);
            await _fixture.MigrateToHeadAsync(databaseName);
            (await TableCountInHushVotingAsync(databaseName)).Should().Be(6);
            (await TableExistsAsync(databaseName, "LicenceCacheOutbox")).Should().BeTrue();
            (await SubjectCountAsync(databaseName)).Should().Be(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Outbox_privacy_columns_never_contain_address_or_projection()
    {
        var databaseName = $"feat014_priv_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        try
        {
            await _fixture.MigrateToHeadAsync(databaseName);
            var columns = await ColumnNamesAsync(databaseName, "LicenceCacheOutbox");

            columns.Should().NotContain("CanonicalPublicSigningAddress");
            columns.Should().NotContain("PlanId");
            columns.Should().NotContain("Projection");
            columns.Should().NotContain("Payload");
            columns.Should().NotContain("RawAddress");
            columns.Should().Contain(new[]
            {
                "LicenceSubjectId", "CommittedRevision", "ChangeKind",
                "CreatedUtc", "AvailableAfterUtc", "AttemptCount",
                "LeaseOwnerToken", "LeaseExpiresUtc", "DeliveredUtc",
                "LastSafeErrorCode", "LastAttemptUtc",
            });
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Constraint_matrix_rejects_invalid_outbox_state()
    {
        var databaseName = $"feat014_con_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        try
        {
            await _fixture.MigrateToHeadAsync(databaseName);
            var subject = await InsertSubjectReturningIdAsync(databaseName);

            // Valid row shape is accepted.
            await InsertOutboxRowAsync(databaseName, subject, row =>
            {
                row.ChangeKind = LicenceCacheOutboxChangeKinds.ProvisionedDefault;
            });

            // Each invariant violation is rejected by PostgreSQL.
            await AssertViolatesAsync(databaseName, subject,
                r => r.ChangeKind = "unknown_kind",
                "CK_LicenceCacheOutbox_ChangeKind");

            await AssertViolatesAsync(databaseName, subject,
                r => r.CommittedRevision = -1,
                "CK_LicenceCacheOutbox_RevisionNonNegative");

            await AssertViolatesAsync(databaseName, subject,
                r => r.AttemptCount = -1,
                "CK_LicenceCacheOutbox_AttemptNonNegative");

            await AssertViolatesAsync(databaseName, subject,
                r => r.AvailableAfterUtc = r.CreatedUtc.AddSeconds(-1),
                "CK_LicenceCacheOutbox_AvailableAfterCreated");

            await AssertViolatesAsync(databaseName, subject,
                r =>
                {
                    r.LeaseOwnerToken = "owner";
                    r.LeaseExpiresUtc = null;
                },
                "CK_LicenceCacheOutbox_LeaseConsistent");

            await AssertViolatesAsync(databaseName, subject,
                r => r.LastSafeErrorCode = string.Empty,
                "CK_LicenceCacheOutbox_ErrorCodeBounded");

            // Unknown subject violates the restrict FK.
            await AssertForeignKeyViolatesAsync(databaseName, Guid.NewGuid());
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Pending_rows_are_excluded_from_delivered_cleanup_eligibility()
    {
        var databaseName = $"feat014_ret_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        try
        {
            await _fixture.MigrateToHeadAsync(databaseName);
            var subject = await InsertSubjectReturningIdAsync(databaseName);

            // One delivered row older than the 30-day retention and one never-delivered row.
            await InsertOutboxRowAsync(databaseName, subject, row =>
            {
                row.DeliveredUtc = DateTime.UtcNow.AddDays(-40);
                row.LeaseOwnerToken = null;
                row.LeaseExpiresUtc = null;
            });
            await InsertOutboxRowAsync(databaseName, subject, row =>
            {
                row.DeliveredUtc = null;
            });

            var cutoff = DateTime.UtcNow.AddDays(-30);
            var cleanupEligible = await CountRowsAsync(
                databaseName,
                $"\"DeliveredUtc\" IS NOT NULL AND \"DeliveredUtc\" < '{cutoff:O}'");
            var pending = await CountRowsAsync(databaseName, "\"DeliveredUtc\" IS NULL");

            cleanupEligible.Should().Be(1);
            pending.Should().Be(1);

            // The pending partial index serves the claim shape; the cleanup shape serves retention.
            (await IndexCountAsync(databaseName, "IX_LicenceCacheOutbox_PendingClaimOrder")).Should().Be(1);
            (await IndexCountAsync(databaseName, "IX_LicenceCacheOutbox_DeliveredCleanup")).Should().Be(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Destructive_rollback_refuses_when_undelivered_rows_exist()
    {
        var databaseName = $"feat014_down_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        try
        {
            await _fixture.MigrateToHeadAsync(databaseName);
            var subject = await InsertSubjectReturningIdAsync(databaseName);

            // Delivered-only history rolls back (no pending work to strand).
            await InsertOutboxRowAsync(databaseName, subject, row =>
            {
                row.DeliveredUtc = DateTime.UtcNow;
                row.LeaseOwnerToken = null;
                row.LeaseExpiresUtc = null;
            });
            await _fixture.MigrateToAsync(databaseName, PredecessorMigration);
            (await TableExistsAsync(databaseName, "LicenceCacheOutbox")).Should().BeFalse();

            // Re-apply head, add an undelivered row: rollback now refuses.
            await _fixture.MigrateToHeadAsync(databaseName);
            await InsertOutboxRowAsync(databaseName, subject, _ => { });

            var act = async () => await _fixture.MigrateToAsync(databaseName, PredecessorMigration);
            var exception = await act.Should().ThrowAsync<PostgresException>();
            exception.And.MessageText.Should().Contain("Destructive rollback refused");

            // Forward-fix posture: head state remains fully intact with the pending row.
            (await TableExistsAsync(databaseName, "LicenceCacheOutbox")).Should().BeTrue();
            (await CountRowsAsync(databaseName, "\"DeliveredUtc\" IS NULL")).Should().Be(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    // ---- helpers -----------------------------------------------------------

    private async Task<Guid> InsertSubjectReturningIdAsync(string databaseName)
    {
        await using var context = _fixture.CreateContext(databaseName);
        var now = DateTime.UtcNow;
        var subject = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
            CanonicalPublicSigningAddress = "feat014-outbox-address-" + Guid.NewGuid().ToString("N")[..12],
            IdentityCreationBlockIndex = 1,
            CreatedAtUtc = now,
            EntitlementRevision = 1,
        };
        context.Set<LicenceSubjectEntity>().Add(subject);
        await context.SaveChangesAsync();
        return subject.LicenceSubjectId;
    }

    private static LicenceCacheOutboxEntity NewRow(Guid subjectId, DateTime now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            LicenceSubjectId = subjectId,
            CommittedRevision = 1,
            ChangeKind = LicenceCacheOutboxChangeKinds.ProvisionedDefault,
            CreatedUtc = now,
            AvailableAfterUtc = now,
            AttemptCount = 0,
            LeaseOwnerToken = null,
            LeaseExpiresUtc = null,
            DeliveredUtc = null,
            LastSafeErrorCode = null,
            LastAttemptUtc = null,
        };

    private async Task InsertOutboxRowAsync(
        string databaseName,
        Guid subjectId,
        Action<LicenceCacheOutboxEntity> mutate)
    {
        await using var context = _fixture.CreateContext(databaseName);
        var row = NewRow(subjectId, DateTime.UtcNow);
        mutate(row);
        context.Set<LicenceCacheOutboxEntity>().Add(row);
        await context.SaveChangesAsync();
    }

    private async Task AssertViolatesAsync(
        string databaseName,
        Guid subjectId,
        Action<LicenceCacheOutboxEntity> mutate,
        string constraint)
    {
        var act = async () => await InsertOutboxRowAsync(databaseName, subjectId, mutate);
        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        var postgres = exception.And.InnerException as PostgresException;
        postgres.Should().NotBeNull();
        postgres!.MessageText.Should().Contain(constraint);
    }

    private async Task AssertForeignKeyViolatesAsync(string databaseName, Guid subjectId)
    {
        var act = async () => await InsertOutboxRowAsync(databaseName, subjectId, _ => { });
        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        var postgres = exception.And.InnerException as PostgresException;
        postgres.Should().NotBeNull();
        postgres!.SqlState.Should().Be("23503"); // foreign_key_violation
    }

    private async Task InsertLicensingDataAsync(string databaseName)
    {
        await using var context = _fixture.CreateContext(databaseName);
        var now = DateTime.UtcNow;
        context.Set<LicenceSubjectEntity>().Add(new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
            CanonicalPublicSigningAddress = "feat014-upgrade-address-0001",
            IdentityCreationBlockIndex = 7,
            CreatedAtUtc = now,
            EntitlementRevision = 1,
        });
        await context.SaveChangesAsync();
    }

    private async Task<long> SubjectCountAsync(string databaseName)
    {
        await using var connection = await OpenAsync(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM \"HushVoting\".\"LicenceSubject\"";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> CountRowsAsync(string databaseName, string predicate)
    {
        await using var connection = await OpenAsync(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM \"HushVoting\".\"LicenceCacheOutbox\" WHERE {predicate}";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<bool> TableExistsAsync(string databaseName, string table)
    {
        await using var connection = await OpenAsync(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'HushVoting' AND table_name = @table";
        command.Parameters.AddWithValue("table", table);
        return (long)(await command.ExecuteScalarAsync())! == 1;
    }

    private async Task<long> TableCountInHushVotingAsync(string databaseName)
    {
        await using var connection = await OpenAsync(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'HushVoting'";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> ConstraintCountAsync(string databaseName, string prefix)
    {
        await using var connection = await OpenAsync(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM information_schema.table_constraints WHERE constraint_schema = 'HushVoting' AND constraint_name LIKE @prefix";
        command.Parameters.AddWithValue("prefix", prefix + "%");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> IndexCountAsync(string databaseName, string name)
    {
        await using var connection = await OpenAsync(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM pg_indexes WHERE schemaname = 'HushVoting' AND indexname = @name";
        command.Parameters.AddWithValue("name", name);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string[]> ColumnNamesAsync(string databaseName, string table)
    {
        await using var connection = await OpenAsync(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'HushVoting' AND table_name = @table";
        command.Parameters.AddWithValue("table", table);
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

[CollectionDefinition("FEAT-014 Licensing PostgreSQL")]
public sealed class Feat014LicensingPostgresCollection : ICollectionFixture<LicensingPostgresFixture>
{
}
