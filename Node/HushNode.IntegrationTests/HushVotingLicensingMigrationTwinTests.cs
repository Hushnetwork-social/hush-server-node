using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-013 real-PostgreSQL migration TwinTests (Task 2.4). Proves clean install,
/// exact-predecessor upgrade, idempotent restart, empty-schema rollback, and
/// destructive-rollback refusal against an isolated postgres:16 container.
/// </summary>
[Collection("FEAT-013 Licensing PostgreSQL")]
[Trait("Category", "FEAT-013")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicensingMigrationTwinTests
{
    private const string PredecessorMigration =
        "20260531012505_Feat155FailedFinalizeGovernedOutcomeArtifactsNullable";

    private readonly LicensingPostgresFixture _fixture;

    public HushVotingLicensingMigrationTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migration_lifecycle_clean_install_predecessor_restart_empty_down_and_guarded_down()
    {
        var databaseName = $"feat013_mig_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        try
        {
            // 1) Exact production predecessor: no licensing schema yet.
            await _fixture.MigrateToAsync(databaseName, PredecessorMigration);
            (await TableCountInHushVotingAsync(databaseName)).Should().Be(0);

            // 2) Clean upgrade to head installs the five FEAT-013 licensing tables plus the FEAT-014
            //    cache outbox (six HushVoting tables) once.
            await _fixture.MigrateToHeadAsync(databaseName);
            (await TableCountInHushVotingAsync(databaseName)).Should().Be(6);

            // Representative check constraints and unique indexes are present.
            (await ConstraintCountAsync(databaseName, "CK_")).Should().BeGreaterThan(10);

            // 3) Restart is idempotent (already migrated).
            await _fixture.MigrateToHeadAsync(databaseName);
            (await TableCountInHushVotingAsync(databaseName)).Should().Be(6);

            // 4) Empty-schema down migration succeeds and removes the schema.
            await _fixture.MigrateToAsync(databaseName, PredecessorMigration);
            (await TableCountInHushVotingAsync(databaseName)).Should().Be(0);

            // 5) After durable history exists, destructive rollback refuses.
            await _fixture.MigrateToHeadAsync(databaseName);
            await InsertDurableHistoryAsync(databaseName);

            var act = async () => await _fixture.MigrateToAsync(databaseName, PredecessorMigration);
            var exception = await act.Should().ThrowAsync<PostgresException>();
            exception.And.MessageText.Should().Contain("Destructive rollback refused");

            // Forward-fix posture: EF applies Down migrations in reverse order, so the FEAT-014
            // outbox migration (no pending rows) rolls back before the FEAT-013 guard refuses; the
            // database rests at FEAT-013. Re-applying head restores the full six-table state.
            await _fixture.MigrateToHeadAsync(databaseName);
            (await TableCountInHushVotingAsync(databaseName)).Should().Be(6);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    private async Task InsertDurableHistoryAsync(string databaseName)
    {
        await using var context = _fixture.CreateContext(databaseName);
        var now = DateTime.UtcNow;

        var subject = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
            CanonicalPublicSigningAddress = "feat013-durable-history-address-0001",
            IdentityCreationBlockIndex = 42,
            CreatedAtUtc = now,
            EntitlementRevision = 1
        };
        context.Set<LicenceSubjectEntity>().Add(subject);
        await context.SaveChangesAsync();
    }

    private async Task<int> TableCountInHushVotingAsync(string databaseName)
    {
        await using var context = _fixture.CreateContext(databaseName);
        return await context.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'HushVoting'")
            .SingleAsync();
    }

    private async Task<int> ConstraintCountAsync(string databaseName, string prefix)
    {
        await using var context = _fixture.CreateContext(databaseName);
        return await context.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = 'HushVoting' AND c.conname LIKE {0}",
                $"{prefix}%")
            .SingleAsync();
    }
}
