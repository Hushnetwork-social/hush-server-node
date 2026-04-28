using Microsoft.Extensions.Logging;
using Npgsql;

namespace HushNode.Elections.Storage;

public interface IElectionSensitiveStorageMaintenance
{
    Task CompactAdminOnlyProtectedTallyEnvelopeStorageAsync(CancellationToken cancellationToken = default);
}

public sealed class PostgresElectionSensitiveStorageMaintenance(
    string? connectionString,
    ILogger<PostgresElectionSensitiveStorageMaintenance> logger) : IElectionSensitiveStorageMaintenance
{
    private const string AdminOnlyProtectedTallyEnvelopeTable =
        "\"Elections\".\"ElectionAdminOnlyProtectedTallyEnvelopeRecord\"";

    public async Task CompactAdminOnlyProtectedTallyEnvelopeStorageAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning(
                "[ElectionSensitiveStorageMaintenance] Skipping admin-only tally envelope compaction because the database connection string is not configured.");
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteNonQueryAsync(connection, "SET lock_timeout = '10s';", cancellationToken);
        await ExecuteNonQueryAsync(connection, "SET statement_timeout = '60s';", cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            $"VACUUM (FULL, ANALYZE) {AdminOnlyProtectedTallyEnvelopeTable};",
            cancellationToken);

        logger.LogInformation(
            "[ElectionSensitiveStorageMaintenance] Compacted admin-only protected tally envelope storage after scalar destruction.");
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
