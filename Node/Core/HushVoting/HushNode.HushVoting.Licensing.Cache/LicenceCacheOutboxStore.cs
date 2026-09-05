using HushNode.HushVoting.Licensing.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Durable outbox data access over the unified PostgreSQL model (FEAT-014). Claiming uses
/// database-safe skip-locked semantics with bounded batches and lease bookkeeping; pending rows are
/// never discarded or dead-lettered; delivered rows become cleanup candidates only after the
/// configured retention window. A fresh DbContext per operation keeps claims isolated and cancellable.
/// </summary>
public interface ILicenceCacheOutboxStore
{
    Task<Guid> EnqueueAsync(
        Guid licenceSubjectId,
        long committedRevision,
        string changeKind,
        DateTime createdUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LicenceCacheOutboxEntity>> ClaimBatchAsync(
        string leaseOwnerToken,
        DateTime leaseExpiresUtc,
        DateTime nowUtc,
        int maxRows,
        CancellationToken cancellationToken);

    Task MarkDeliveredAsync(
        Guid id,
        DateTime deliveredUtc,
        CancellationToken cancellationToken);

    Task RecordAttemptAsync(
        Guid id,
        int attemptCount,
        DateTime lastAttemptUtc,
        DateTime? nextAvailableAfterUtc,
        string? safeErrorCode,
        CancellationToken cancellationToken);

    Task<int> PurgeDeliveredAsync(
        DateTime deliveredBeforeUtc,
        int maxRows,
        CancellationToken cancellationToken);

    Task<(long PendingDepth, DateTime? OldestPendingCreatedUtc)> ReadHealthAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation over <see cref="DbContext"/>. Claim ordering and skip-locked semantics are
/// executed with raw SQL so one dispatcher never blocks another and leases expire safely.
/// </summary>
public sealed class LicenceCacheOutboxStore : ILicenceCacheOutboxStore
{
    private readonly Func<DbContext> _contextFactory;
    private readonly LicenceCacheOptions _options;

    public LicenceCacheOutboxStore(Func<DbContext> contextFactory, LicenceCacheOptions options)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<Guid> EnqueueAsync(
        Guid licenceSubjectId,
        long committedRevision,
        string changeKind,
        DateTime createdUtc,
        CancellationToken cancellationToken)
    {
        if (!LicenceCacheOutboxChangeKinds.TryValidate(changeKind, out var code))
        {
            throw new ArgumentException(code, nameof(changeKind));
        }

        var id = Guid.CreateVersion7();
        await using var context = _contextFactory();
        context.Set<LicenceCacheOutboxEntity>().Add(new LicenceCacheOutboxEntity
        {
            Id = id,
            LicenceSubjectId = licenceSubjectId,
            CommittedRevision = committedRevision,
            ChangeKind = changeKind,
            CreatedUtc = createdUtc,
            AvailableAfterUtc = createdUtc,
            AttemptCount = 0,
            LeaseOwnerToken = null,
            LeaseExpiresUtc = null,
            DeliveredUtc = null,
            LastSafeErrorCode = null,
            LastAttemptUtc = null,
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task<IReadOnlyList<LicenceCacheOutboxEntity>> ClaimBatchAsync(
        string leaseOwnerToken,
        DateTime leaseExpiresUtc,
        DateTime nowUtc,
        int maxRows,
        CancellationToken cancellationToken)
    {
        await using var context = _contextFactory();
        var sql =
            """
            UPDATE "HushVoting"."LicenceCacheOutbox" AS o
            SET "LeaseOwnerToken" = @owner, "LeaseExpiresUtc" = @expires, "LastAttemptUtc" = @now
            WHERE o."Id" IN (
                SELECT inner."Id" FROM "HushVoting"."LicenceCacheOutbox" AS inner
                WHERE inner."DeliveredUtc" IS NULL
                  AND inner."AvailableAfterUtc" <= @now
                  AND (inner."LeaseExpiresUtc" IS NULL OR inner."LeaseExpiresUtc" < @now)
                ORDER BY inner."CreatedUtc", inner."Id"
                LIMIT @limit
                FOR UPDATE SKIP LOCKED
            )
            RETURNING "Id", "LicenceSubjectId", "CommittedRevision", "ChangeKind", "CreatedUtc",
                      "AvailableAfterUtc", "AttemptCount", "LeaseOwnerToken", "LeaseExpiresUtc",
                      "DeliveredUtc", "LastSafeErrorCode", "LastAttemptUtc"
            """;

        var rows = await context.Database.SqlQueryRaw<LicenceCacheOutboxEntity>(
                sql,
                new NpgsqlParameter("owner", leaseOwnerToken),
                new NpgsqlParameter("expires", leaseExpiresUtc),
                new NpgsqlParameter("now", nowUtc),
                new NpgsqlParameter("limit", maxRows))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows;
    }

    public async Task MarkDeliveredAsync(Guid id, DateTime deliveredUtc, CancellationToken cancellationToken)
    {
        await using var context = _contextFactory();
        var entity = await context.Set<LicenceCacheOutboxEntity>()
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        entity.DeliveredUtc = deliveredUtc;
        entity.LeaseOwnerToken = null;
        entity.LeaseExpiresUtc = null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordAttemptAsync(
        Guid id,
        int attemptCount,
        DateTime lastAttemptUtc,
        DateTime? nextAvailableAfterUtc,
        string? safeErrorCode,
        CancellationToken cancellationToken)
    {
        await using var context = _contextFactory();
        var entity = await context.Set<LicenceCacheOutboxEntity>()
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        entity.AttemptCount = attemptCount;
        entity.LastAttemptUtc = lastAttemptUtc;
        entity.AvailableAfterUtc = nextAvailableAfterUtc ?? lastAttemptUtc;
        entity.LastSafeErrorCode = safeErrorCode;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> PurgeDeliveredAsync(DateTime deliveredBeforeUtc, int maxRows, CancellationToken cancellationToken)
    {
        await using var context = _contextFactory();
        var sql =
            """
            DELETE FROM "HushVoting"."LicenceCacheOutbox"
            WHERE "DeliveredUtc" IS NOT NULL AND "DeliveredUtc" < @cutoff
              AND "Id" IN (
                  SELECT inner."Id" FROM "HushVoting"."LicenceCacheOutbox" AS inner
                  WHERE inner."DeliveredUtc" IS NOT NULL AND inner."DeliveredUtc" < @cutoff
                  ORDER BY inner."DeliveredUtc"
                  LIMIT @limit
              )
            """;
        var affected = await context.Database.ExecuteSqlRawAsync(
            sql,
            new NpgsqlParameter("cutoff", deliveredBeforeUtc),
            new NpgsqlParameter("limit", maxRows)).ConfigureAwait(false);
        return affected;
    }

    public async Task<(long PendingDepth, DateTime? OldestPendingCreatedUtc)> ReadHealthAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var context = _contextFactory();
        var depth = await context.Set<LicenceCacheOutboxEntity>()
            .LongCountAsync(e => e.DeliveredUtc == null, cancellationToken).ConfigureAwait(false);

        var oldest = await context.Set<LicenceCacheOutboxEntity>()
            .Where(e => e.DeliveredUtc == null)
            .OrderBy(e => e.CreatedUtc)
            .Select(e => (DateTime?)e.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return (depth, oldest);
    }
}
