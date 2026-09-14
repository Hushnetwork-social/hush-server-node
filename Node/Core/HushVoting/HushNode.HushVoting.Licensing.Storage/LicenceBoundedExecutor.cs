using System.Diagnostics;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Bounded execution policy for entitlement operations:
///   - recognized transient races (first-insert uniqueness, serialization, deadlock) retry at most
///     <see cref="MaxTransientRetries"/> times with short deterministic backoff;
///   - an ambiguous commit is reconciled before any new mutation: a discovered committed result is
///     returned, an authoritative absence re-executes the attempt (bounded), and an unreadable state
///     reports storage unavailability;
///   - unknown failures are never retried.
/// Generic over the operation result so unit tests can drive it with delegates (Task 3.8).
/// </summary>
public static class LicenceBoundedExecutor
{
    /// <summary>Maximum recognized-transient retries per operation (spec: at most three).</summary>
    public const int MaxTransientRetries = 3;

    /// <summary>Maximum ambiguous-commit re-executions after an authoritative absence.</summary>
    public const int MaxAmbiguousAbsenceRedos = 3;

    public static async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> attemptAsync,
        Func<CancellationToken, Task<TResult?>> reconcileCommittedAsync,
        string operationName,
        LicenceTelemetry? telemetry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attemptAsync);
        ArgumentNullException.ThrowIfNull(reconcileCommittedAsync);

        var transientRetries = 0;
        var ambiguousAbsenceRedos = 0;

        while (true)
        {
            try
            {
                return await attemptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (LicenceTransientConflictException exception)
            {
                transientRetries++;
                telemetry?.RecordConcurrencyConflict(operationName);
                if (transientRetries > MaxTransientRetries)
                {
                    throw new LicenceExecutionExhaustedException(
                        LicenceEntitlementFailureCodes.ConcurrencyExhausted,
                        "Recognized transient conflicts exceeded the bounded retry policy.",
                        exception);
                }

                telemetry?.RecordTransientRetry(operationName);
                await BoundedBackoffAsync(transientRetries, cancellationToken).ConfigureAwait(false);
            }
            catch (LicenceAmbiguousCommitException exception)
            {
                TResult? discovered = default;
                try
                {
                    discovered = await reconcileCommittedAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception reconcileException)
                {
                    throw new LicenceExecutionExhaustedException(
                        LicenceEntitlementFailureCodes.StorageUnavailable,
                        "Ambiguous commit could not be reconciled; authority cannot be established.",
                        reconcileException);
                }

                if (discovered is not null)
                {
                    return discovered;
                }

                // Authoritative absence: the operation did not commit. Re-execute (bounded).
                ambiguousAbsenceRedos++;
                if (ambiguousAbsenceRedos > MaxAmbiguousAbsenceRedos)
                {
                    throw new LicenceExecutionExhaustedException(
                        LicenceEntitlementFailureCodes.StorageUnavailable,
                        "Ambiguous commit recurred beyond the bounded absence-redo policy.",
                        exception);
                }
            }
        }
    }

    /// <summary>Short deterministic backoff with bounded micro-jitter; total worst case &lt; 50 ms.</summary>
    public static TimeSpan BackoffForRetry(int retryNumber)
    {
        if (retryNumber < 1)
        {
            retryNumber = 1;
        }

        var jitterMs = retryNumber % 3;
        return TimeSpan.FromMilliseconds((5 * retryNumber) + jitterMs);
    }

    private static async Task BoundedBackoffAsync(
        int retryNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(BackoffForRetry(retryNumber), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }
}

/// <summary>
/// Typed signal that the bounded execution policy was exhausted. The per-operation public surface
/// maps <see cref="StableCode"/> to the closed stable result vocabulary (concurrency_exhausted or
/// storage_unavailable); it is never surfaced as exception text to callers.
/// </summary>
public sealed class LicenceExecutionExhaustedException : Exception
{
    public LicenceExecutionExhaustedException(string stableCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StableCode = stableCode;
    }

    public string StableCode { get; }
}
