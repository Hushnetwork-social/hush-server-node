using Microsoft.EntityFrameworkCore;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Reconciles the FEAT-012 immutable snapshot release into the append-only catalogue
/// release ledger and captures the singleton rollout watermark transactionally on first
/// initialization. Append-only: ledger rows are never overwritten or deleted; exactly one
/// release is current. Readiness failures are typed, never exceptions.
/// </summary>
public static class LicenceCatalogueLedgerCoordinator
{
    public const string FailureCatalogueMismatch = "catalogue_incompatible";
    public const string FailureRolloutWatermarkUnavailable = "rollout_watermark_unavailable";

    /// <summary>Rollout watermark advisory-lock key so concurrent instances initialize once.</summary>
    private const string RolloutAdvisoryKey = "hushvoting_licence_rollout_init";

    /// <summary>
    /// Reconciles the configured release against the ledger. Call inside a short-lived scope;
    /// commits only when the ledger is consistent.
    /// </summary>
    public static async Task<LicenceLedgerReadinessState> ReconcileAsync(
        DbContext db,
        LicenceReleaseInstallSpec spec,
        Func<CancellationToken, Task<long>> authoritativeBlockHeightFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(authoritativeBlockHeightFactory);

        var releases = db.Set<LicenceCatalogueReleaseEntity>();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var current = await releases.SingleOrDefaultAsync(r => r.IsCurrent, cancellationToken);

        if (current is not null && string.Equals(current.CatalogueVersion, spec.CatalogueVersion, StringComparison.Ordinal))
        {
            if (!string.Equals(current.ReleaseDigestSha256, spec.ReleaseDigestSha256, StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync(cancellationToken);
                return LicenceLedgerReadinessState.Fail(
                    FailureCatalogueMismatch,
                    "The database already holds a release with the configured version but a different digest; ledger rows are never overwritten.");
            }

            await transaction.CommitAsync(cancellationToken);
            return LicenceLedgerReadinessState.Ok(
                LicenceLedgerReconcileOutcome.NoChange,
                current.RolloutWatermarkBlockHeight);
        }

        if (current is not null && IsNewer(current.CatalogueVersion, spec.CatalogueVersion) is true)
        {
            await transaction.RollbackAsync(cancellationToken);
            return LicenceLedgerReadinessState.Fail(
                FailureCatalogueMismatch,
                "The database holds a newer release than the configured one; this server cannot validate it.");
        }

        // Serialize concurrent first initialization (and ledger appends) per database.
        await db.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('{RolloutAdvisoryKey}'))",
            cancellationToken);

        // Another instance may have appended while we waited for the advisory lock.
        var recheckedCurrent = await releases.SingleOrDefaultAsync(r => r.IsCurrent, cancellationToken);
        if (recheckedCurrent is not null
            && string.Equals(recheckedCurrent.CatalogueVersion, spec.CatalogueVersion, StringComparison.Ordinal)
            && string.Equals(recheckedCurrent.ReleaseDigestSha256, spec.ReleaseDigestSha256, StringComparison.OrdinalIgnoreCase))
        {
            await transaction.CommitAsync(cancellationToken);
            return LicenceLedgerReadinessState.Ok(
                LicenceLedgerReconcileOutcome.NoChange,
                recheckedCurrent.RolloutWatermarkBlockHeight);
        }

        // The rollout watermark is captured once, on the first (earliest) installation.
        var firstRelease = await releases
            .Where(r => r.RolloutWatermarkBlockHeight != null)
            .OrderBy(r => r.InstalledAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        long? rolloutWatermark = firstRelease?.RolloutWatermarkBlockHeight;
        if (rolloutWatermark is null)
        {
            try
            {
                rolloutWatermark = await authoritativeBlockHeightFactory(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return LicenceLedgerReadinessState.Fail(
                    FailureRolloutWatermarkUnavailable,
                    $"Authoritative indexed block height unavailable: {exception.GetType().Name}. Readiness fails closed; no watermark is guessed.");
            }
        }

        var installedAt = DateTime.UtcNow;
        if (recheckedCurrent is not null)
        {
            recheckedCurrent.IsCurrent = false;
        }

        releases.Add(new LicenceCatalogueReleaseEntity
        {
            LicenceCatalogueReleaseId = Guid.CreateVersion7(),
            CatalogueVersion = spec.CatalogueVersion,
            ReleaseDigestSha256 = spec.ReleaseDigestSha256,
            SchemaVersion = spec.SchemaVersion,
            InstalledByServerRelease = spec.ServerRelease,
            InstalledByServerHost = spec.ServerHost,
            InstalledAtUtc = installedAt,
            IsCurrent = true,
            RolloutWatermarkBlockHeight = firstRelease is null ? rolloutWatermark : null
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return LicenceLedgerReadinessState.Ok(
            LicenceLedgerReconcileOutcome.AppendedConfiguredAsCurrent,
            rolloutWatermark);
    }

    /// <summary>
    /// Reads the committed rollout watermark (null when licensing was never initialized).
    /// Never guesses an unavailable value.
    /// </summary>
    public static async Task<long?> ReadRolloutWatermarkAsync(
        DbContext db,
        CancellationToken cancellationToken)
    {
        return await db.Set<LicenceCatalogueReleaseEntity>()
            .Where(r => r.RolloutWatermarkBlockHeight != null)
            .OrderBy(r => r.InstalledAtUtc)
            .Select(r => (long?)r.RolloutWatermarkBlockHeight)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Deterministic version ordering for release contract versions of the shape
    /// "prefix/vMAJOR.MINOR.PATCH". Null when a version cannot be parsed (treated as
    /// incompatible rather than guessed).
    /// </summary>
    public static bool? IsNewer(string leftVersion, string rightVersion)
    {
        var left = TryParseVersion(leftVersion);
        var right = TryParseVersion(rightVersion);
        if (left is null || right is null)
        {
            return null;
        }

        return left.Value.CompareTo(right.Value) > 0;
    }

    private static (int Major, int Minor, int Patch)? TryParseVersion(string version)
    {
        var marker = version.LastIndexOf("/v", StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var parts = version[(marker + 2)..].Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || !int.TryParse(parts[2], out var patch))
        {
            return null;
        }

        return (major, minor, patch);
    }
}
