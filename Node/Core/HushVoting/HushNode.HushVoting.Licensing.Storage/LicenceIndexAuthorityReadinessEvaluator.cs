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
}

/// <summary>Typed readiness outcome. Expected refusal is data, never an exception.</summary>
public sealed record LicenceIndexAuthorityReadinessResult(
    bool Ready,
    string? StableCode,
    string? SafeReason,
    long? LegacyAssignmentCount = null)
{
    public static LicenceIndexAuthorityReadinessResult Ok() =>
        new(true, null, null);

    public static LicenceIndexAuthorityReadinessResult Refuse(
        string stableCode,
        string safeReason,
        long legacyAssignmentCount) =>
        new(false, stableCode, safeReason, legacyAssignmentCount);
}

/// <summary>
/// Read-only readiness evaluation over the licence projection. Queries are bounded (no
/// full-table scans beyond the necessary count), deterministic, and never write. Every
/// assignment row without an originating transaction is legacy off-chain authority and
/// refuses serving with a bounded code. Block-provenance integrity is already enforced by
/// the DB CHECK (all-or-none), so no separate invariant scan is needed here. Storage
/// unavailability fails closed with a generic bounded code.
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
            var legacyOffChainCount = await db.Set<LicenceAssignmentEntity>()
                .AsNoTracking()
                .CountAsync(assignment => assignment.OriginatingTransactionId == null, cancellationToken)
                .ConfigureAwait(false);

            if (legacyOffChainCount > 0)
            {
                return LicenceIndexAuthorityReadinessResult.Refuse(
                    LicenceIndexAuthorityReadinessCodes.LegacyOffChainAssignmentPresent,
                    "Legacy off-chain licence assignments exist without an originating blockchain transaction; they are never converted or deleted.",
                    legacyOffChainCount);
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
                0);
        }
    }
}
