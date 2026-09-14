using System.Text.Json;
using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.HushVoting.Licensing.Model;
using Npgsql;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-013 Phase 7 query-shape qualification: authoritative resolution/idempotency/ledger and
/// per-subject event ordering queries use their explicit indexes (never a history/assignment scan).
/// Index usage is proven with <c>enable_seqscan=off</c> EXPLAIN over real PostgreSQL.
/// </summary>
[Collection("FEAT-013 Licensing PostgreSQL")]
[Trait("Category", "FEAT-013")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicensingQueryShapeTwinTests
{
    private readonly LicensingPostgresFixture _fixture;

    public HushVotingLicensingQueryShapeTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Authoritative_lookups_use_their_explicit_indexes_and_never_scan_history()
    {
        var databaseName = $"feat013_shape_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        await _fixture.MigrateToHeadAsync(databaseName);

        try
        {
            // Seed one subject row so predicates reference a real key shape.
            await using (var seed = _fixture.CreateContext(databaseName))
            {
                seed.Add(new LicenceSubjectEntity
                {
                    LicenceSubjectId = Guid.CreateVersion7(),
                    SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
                    CanonicalPublicSigningAddress = "shape-identity",
                    IdentityCreationBlockIndex = 42,
                    CreatedAtUtc = DateTime.UtcNow,
                    EntitlementRevision = 0,
                });
                await seed.SaveChangesAsync();
            }

            var queries = new (string[] IndexNames, string Sql)[]
            {
                (
                    ["IX_LicenceSubject_Type_CanonicalAddress"],
                    """
                    SELECT * FROM "HushVoting"."LicenceSubject"
                    WHERE "SubjectType" = 'Identity' AND "CanonicalPublicSigningAddress" = 'shape-identity'
                    """),
                (
                    // Either the single-active partial unique index or the subject/lifecycle index is
                    // a valid non-scan plan; both are explicit indexes over the predicate columns.
                    ["IX_LicenceAssignment_SingleActivePerSubject", "IX_LicenceAssignment_Subject_Lifecycle"],
                    """
                    SELECT * FROM "HushVoting"."LicenceAssignment"
                    WHERE "LicenceSubjectId" = '00000000-0000-0000-0000-000000000000' AND "LifecycleStatus" = 'active'
                    """),
                (
                    ["IX_LicenceActivationOperation_Subject_IdempotencyKey"],
                    """
                    SELECT * FROM "HushVoting"."LicenceActivationOperation"
                    WHERE "LicenceSubjectId" = '00000000-0000-0000-0000-000000000000'
                      AND "IdempotencyKey" = '00000000-0000-0000-0000-000000000001'
                    """),
                (
                    ["IX_LicenceCatalogueRelease_SingleCurrent"],
                    """
                    SELECT * FROM "HushVoting"."LicenceCatalogueRelease" WHERE "IsCurrent" = TRUE
                    """),
                (
                    ["IX_LicenceTransitionEvent_Subject_Sequence"],
                    """
                    SELECT MAX("EventSequence") FROM "HushVoting"."LicenceTransitionEvent"
                    WHERE "LicenceSubjectId" = '00000000-0000-0000-0000-000000000000'
                    """),
            };

            await using var connection = new NpgsqlConnection(_fixture.AdminConnectionString);
            connection.ConnectionString = new NpgsqlConnectionStringBuilder(connection.ConnectionString)
            {
                Database = databaseName,
            }.ConnectionString;
            await connection.OpenAsync();

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SET enable_seqscan = off";
                await command.ExecuteNonQueryAsync();
            }

            foreach (var (indexNames, sql) in queries)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"EXPLAIN (FORMAT JSON) {sql}";
                var json = (string)(await command.ExecuteScalarAsync())!;

                var plan = JsonDocument.Parse(json);
                var used = CollectIndexNames(plan.RootElement);
                used.Intersect(indexNames).Should().NotBeEmpty(
                    $"query should use an explicit index from {{{string.Join(", ", indexNames)}}}");
            }
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    private static List<string> CollectIndexNames(JsonElement element)
    {
        var names = new List<string>();
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("Index Name", out var indexName) && indexName.ValueKind == JsonValueKind.String)
            {
                names.Add(indexName.GetString()!);
            }

            foreach (var property in element.EnumerateObject())
            {
                names.AddRange(CollectIndexNames(property.Value));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                names.AddRange(CollectIndexNames(item));
            }
        }

        return names;
    }
}
