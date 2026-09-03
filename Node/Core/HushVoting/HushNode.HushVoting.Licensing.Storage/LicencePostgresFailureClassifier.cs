using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Typed PostgreSQL failure classification for entitlement operations. Retry decisions are made on
/// SQLSTATEs (or typed exceptions), never by parsing exception text:
///   - 23505 unique_violation / 40001 serialization_failure / 40P01 deadlock_detected are the only
///     recognized transient races that receive bounded retries;
///   - integrity/data-class failures (23xxx, 22xxx) are persistence-invariant violations, never retried;
///   - connection/availability failures are storage unavailability, never retried and never guessed.
/// </summary>
public static class LicencePostgresFailureClassifier
{
    public const string SqlStateUniqueViolation = "23505";
    public const string SqlStateSerializationFailure = "40001";
    public const string SqlStateDeadlockDetected = "40P01";

    public static bool IsRecognizedTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LicenceTransientConflictException)
        {
            return true;
        }

        return FindPostgresSqlState(exception) is string sqlState
            && sqlState is SqlStateUniqueViolation or SqlStateSerializationFailure or SqlStateDeadlockDetected;
    }

    /// <summary>True when the failure is an integrity/data-class invariant violation (never retried).</summary>
    public static bool IsPersistenceInvariantViolation(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return FindPostgresSqlState(exception) is string sqlState
            && (sqlState.StartsWith("23", StringComparison.Ordinal)
                || sqlState.StartsWith("22", StringComparison.Ordinal));
    }

    /// <summary>True when the failure is a connection/availability failure (never retried, never guessed).</summary>
    public static bool IsStorageUnavailable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            // PostgresException derives from NpgsqlException: classify SQLSTATEs precisely first so
            // integrity failures (23xxx/22xxx) are never mistaken for storage outages.
            if (current is PostgresException { SqlState: var sqlState })
            {
                if (sqlState.StartsWith("08", StringComparison.Ordinal))
                {
                    return true;
                }

                continue;
            }

            if (current is NpgsqlException)
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindPostgresSqlState(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres.SqlState;
            }
        }

        return null;
    }

    /// <summary>Resolves a DbUpdateException to a stable authority/invariant outcome for callers.</summary>
    public static ExceptionClassifyResult ClassifyDbUpdate(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (IsRecognizedTransient(exception))
        {
            return ExceptionClassifyResult.RecognizedTransient;
        }

        if (IsPersistenceInvariantViolation(exception))
        {
            return ExceptionClassifyResult.PersistenceInvariantViolation;
        }

        return ExceptionClassifyResult.StorageUnavailable;
    }

    public enum ExceptionClassifyResult
    {
        RecognizedTransient,
        PersistenceInvariantViolation,
        StorageUnavailable,
    }
}
