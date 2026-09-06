// FEAT-015 Task 2.5 — fail-closed legacy-authority readiness for the licence index projection.
//
// PostgreSQL is a rebuildable block-indexed projection. A FEAT-013-era assignment row
// that lacks an originating licence transaction is legacy off-chain authority and MUST
// refuse serving: it is never deleted, converted, grandfathered, or auto-published
// (AC-015-019, AT-LIC-015-014). The host bootstrapper (Phase 6) invokes this evaluator
// after the catalogue-release reconciliation; this class owns the storage query and the
// bounded stable code.

using Microsoft.EntityFrameworkCore;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>Stable failure codes for licence index-authority readiness (bounded, safe).</summary>
public static class LicenceIndexAuthorityReadinessCodes
{
    /// <summary>At least one legacy off-chain assignment row has no originating licence transaction.</summary>
    public const string LegacyOffChainAssignmentPresent = "legacy_offchain_assignment_present";

    /// <summary>At least one indexed assignment references an absent originating block index/time.</summary>
    public const string IndexedRowMissingBlockProvenance = "indexed_row_missing_block_provenance";
}

/// <summary>Typed readiness outcome. Expected refusal is data, never an exception.</summary>
public sealed record LicenceIndexAuthorityReadinessResult(
    bool Ready,
    string? StableCode,
    string? SafeReason,
    long? LegacyAssignmentCount = null,
    long? InvalidIndexedRowCount = null)
{
    public static LicenceIndexAuthorityReadinessResult Ok() =>
        new(true, null, null);

    public static LicenceIndexAuthorityReadinessResult Refuse(
        string stableCode,
        string safeReason,
        long legacyAssignmentCount,
        long invalidIndexedRowCount) =>
        new(false, stableCode, safeReason, legacyAssignmentCount, invalidIndexedRowCount);
}

/// <summary>
/// Read-only readiness evaluation over the licence projection. Queries are bounded (no
/// full-table scans beyond the necessary count), deterministic, and never write. The
/// evaluator treats every assignment row without an originating transaction as legacy
/// off-chain authority, and every row that has an originating transaction but lacks the
/// block index/time pair as an invariant violation — both refuse serving with stable codes.
/// </summary>
public sealed class LicenceIndexAuthorityReadinessEvaluator
{
    private readonly Func<DbContext> _contextFactory;

    public LicenceIndexAuthorityReadinessEvaluator(Func<DbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    /// <summary>Evaluates readiness at the caller's bounded scope. Never mutates state.</summary>
    public async Task<LicenceIndexAuthorityReadinessResult> EvaluateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var db = _contextFactory();
            var assignments = db.Set<LicenceAssignmentEntity>().AsNoTracking();

            var legacyOffChainCount = await assignments
                .CountAsync(assignment => assignment.OriginatingTransactionId == null, cancellationToken)
                .ConfigureAwait(false);

            var invalidIndexedCount = await assignments
                .CountAsync(
                    assignment => assignment.OriginatingTransactionId != null
                        && (assignment.OriginatingBlockIndex == null
                            || assignment.OriginatingBlockTimeStampUtc == null),
                    cancellationToken)
                .ConfigureAwait(false);

            if (legacyOffChainCount > 0)
            {
                return LicenceIndexAuthorityReadinessResult.Refuse(
                    LicenceIndexAuthorityReadinessCodes.LegacyOffChainAssignmentPresent,
                    "Legacy off-chain licence assignments exist without an originating blockchain transaction; they are never converted or deleted.",
                    legacyOffChainCount,
                    invalidIndexedCount);
            }

            if (invalidIndexedCount > 0)
            {
                return LicenceIndexAuthorityReadinessResult.Refuse(
                    LicenceIndexAuthorityReadinessCodes.IndexedRowMissingBlockProvenance,
                    "Indexed assignment rows are missing required block provenance.",
                    legacyOffChainCount,
                    invalidIndexedCount);
            }

            return LicenceIndexAuthorityReadinessResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Storage unavailability is never a green light; readiness fails closed with a
            // generic bounded code (the caller maps to its own infrastructure status).
            return LicenceIndexAuthorityReadinessResult.Refuse(
                "licence_index_readiness_unavailable",
                "The licence index projection could not be evaluated for readiness.",
                0,
                0);
        }
    }
}
