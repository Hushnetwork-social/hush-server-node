using HushNode.HushVoting.Licensing.Storage;
using Microsoft.EntityFrameworkCore;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Dispatcher seam used by the bounded hosted outbox worker (Phase 6) so the worker can be unit
/// tested with a deterministic fake and never needs Redis/PostgreSQL to prove lifecycle behaviour.
/// </summary>
public interface ILicenceCacheOutboxDispatcher
{
    Task<int> ProcessOnceAsync(CancellationToken cancellationToken);

    Task<int> PurgeDeliveredOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Pure retry scheduling: capped exponential backoff with deterministic jitter derived from the row
/// id (never wall-clock sleeps; tests use the returned instant directly).
/// </summary>
public static class LicenceCacheOutboxBackoffCalculator
{
    public const int MaxBackoffSeconds = 300; // 5-minute cap
    public const int BaseBackoffSeconds = 2;

    /// <summary>Computes the UTC instant a row may next be claimed after <paramref name="attemptCount"/> failures.</summary>
    public static DateTime ComputeNextAvailableAfterUtc(
        Guid rowId,
        int attemptCount,
        DateTime nowUtc)
    {
        var cappedAttempts = Math.Clamp(attemptCount, 1, 32);
        var exponential = Math.Min(BaseBackoffSeconds * (long)Math.Pow(2, cappedAttempts - 1), MaxBackoffSeconds);
        var jitterMs = rowId.ToByteArray()[0] % 500; // deterministic jitter: 0..499 ms
        var delay = TimeSpan.FromSeconds(exponential) + TimeSpan.FromMilliseconds(jitterMs);
        return nowUtc + delay;
    }
}

/// <summary>
/// Durable convergence service: lease-safe dispatcher claims that reload the latest authoritative
/// projection, deliver through monotonic Redis CAS, retry indefinitely with capped backoff, retain
/// delivered rows for the configured window, and preserve every undelivered row. Immediate
/// post-commit best-effort publication is delegated to <see cref="LicenceCacheRedisWriter"/>.
/// </summary>
public sealed class LicenceCacheOutboxDispatcherService : ILicenceCacheOutboxDispatcher
{
    private readonly ILicenceCacheOutboxStore _outbox;
    private readonly Func<DbContext> _contextFactory;
    private readonly IEntitlementAuthorityResolver _authority;
    private readonly LicenceCacheRedisWriter _redisWriter;
    private readonly LicenceCacheOptions _options;
    private readonly LicenceCacheTelemetry _telemetry;

    public LicenceCacheOutboxDispatcherService(
        ILicenceCacheOutboxStore outbox,
        Func<DbContext> contextFactory,
        IEntitlementAuthorityResolver authority,
        LicenceCacheRedisWriter redisWriter,
        LicenceCacheOptions options,
        LicenceCacheTelemetry telemetry)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _redisWriter = redisWriter ?? throw new ArgumentNullException(nameof(redisWriter));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    /// <summary>
    /// One bounded dispatch pass: claim up to the configured batch size, reload each subject's latest
    /// authoritative entitlement, deliver with monotonic CAS, and bookkeep success/failure.
    /// Returns the number of rows processed. Never holds a user transaction or request open.
    /// </summary>
    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var leaseOwner = LicenceCacheOutboxLeaseOwnership.NewOwner();
        var leaseExpiry = now + TimeSpan.FromSeconds(60);

        var claimed = await _outbox.ClaimBatchAsync(
            leaseOwner,
            leaseExpiry,
            now,
            _options.OutboxClaimBatchSize,
            cancellationToken).ConfigureAwait(false);

        var processed = 0;
        foreach (var row in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessRowAsync(row, cancellationToken).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    /// <summary>
    /// Best-effort immediate publish after a committed FEAT-013 mutation. Failure never reverses or
    /// hides the committed result; the durable outbox row (already committed in the same transaction)
    /// is the recovery path.
    /// </summary>
    public async Task<bool> TryPublishCommittedAsync(
        AuthenticatedIdentitySubject subject,
        EffectiveLicenceEntitlement entitlement,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _redisWriter.TryWriteAsync(subject, entitlement, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _telemetry.Count("immediate_publish_error");
            return false;
        }
    }

    /// <summary>Purges delivered rows older than the retention window in bounded batches.</summary>
    public async Task<int> PurgeDeliveredOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.DeliveredRetentionDays);
        var purged = await _outbox.PurgeDeliveredAsync(cutoff, _options.OutboxClaimBatchSize, cancellationToken)
            .ConfigureAwait(false);
        if (purged > 0)
        {
            _telemetry.Count("outbox_purge_delivered");
        }

        return purged;
    }

    private async Task ProcessRowAsync(LicenceCacheOutboxEntity row, CancellationToken cancellationToken)
    {
        try
        {
            var subject = await LoadSubjectAsync(row.LicenceSubjectId, cancellationToken).ConfigureAwait(false);
            if (subject is null)
            {
                await RecordFailureAsync(row, "subject_unavailable", cancellationToken).ConfigureAwait(false);
                return;
            }

            var resolution = await _authority.ResolveEffectiveEntitlementAsync(subject, cancellationToken)
                .ConfigureAwait(false);
            if (!resolution.IsSuccess || resolution.Entitlement is null)
            {
                await RecordFailureAsync(
                    row,
                    resolution.StableErrorCode ?? "authority_unavailable",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var delivered = await _redisWriter.TryWriteAsync(subject, resolution.Entitlement, cancellationToken)
                .ConfigureAwait(false);
            if (!delivered)
            {
                await RecordFailureAsync(row, "cache_write_failed", cancellationToken).ConfigureAwait(false);
                return;
            }

            await _outbox.MarkDeliveredAsync(row.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            _telemetry.Count("outbox_delivered");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            await RecordFailureAsync(row, "delivery_error", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecordFailureAsync(LicenceCacheOutboxEntity row, string safeErrorCode, CancellationToken cancellationToken)
    {
        var attempt = row.AttemptCount + 1;
        var now = DateTime.UtcNow;
        var next = LicenceCacheOutboxBackoffCalculator.ComputeNextAvailableAfterUtc(row.Id, attempt, now);
        await _outbox.RecordAttemptAsync(row.Id, attempt, now, next, safeErrorCode, cancellationToken)
            .ConfigureAwait(false);
        _telemetry.Count("outbox_failure");
    }

    private async Task<AuthenticatedIdentitySubject?> LoadSubjectAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        await using var context = _contextFactory();
        var entity = await context.Set<LicenceSubjectEntity>()
            .SingleOrDefaultAsync(e => e.LicenceSubjectId == subjectId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        return AuthenticatedIdentitySubject.TryCreate(
            entity.SubjectType,
            entity.CanonicalPublicSigningAddress,
            entity.IdentityCreationBlockIndex,
            out var subject,
            out _)
            ? subject
            : null;
    }
}

/// <summary>Random dispatcher claim-owner token (never logged).</summary>
public static class LicenceCacheOutboxLeaseOwnership
{
    public static string NewOwner() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
