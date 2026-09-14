namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Closed vocabulary describing how an ordinary entitlement read was satisfied. Cache provenance is
/// internal to HushServerNode: downstream API/UI consumers (FEAT-015/016/017) receive the same
/// client-safe projection and revision regardless of source and never see this outcome.
/// </summary>
public enum EntitlementCacheReadOutcome
{
    /// <summary>Returned from Redis (current or migrated previous key) without a FEAT-013 call.</summary>
    CacheHit = 0,

    /// <summary>Redis missed/held no valid value; FEAT-013 resolved the effective entitlement and the successful projection was cached.</summary>
    AuthorityFallback = 1,

    /// <summary>No valid cache entry and FEAT-013 authority was unavailable; no entitlement was invented or cached.</summary>
    AuthorityUnavailable = 2,

    /// <summary>The licence cache is intentionally disabled; FEAT-013 resolved the read directly.</summary>
    CacheDisabled = 3,

    /// <summary>FEAT-015: verified indexed absence. Never cached and never confused with unavailability.</summary>
    AuthorityNoActive = 4,
}

/// <summary>
/// Result of one ordinary effective-entitlement display read through <see cref="ICachedEntitlementReader"/>.
/// A successful read carries the non-authoritative projection with its FEAT-013 revision. A failed read
/// never carries a remembered or fabricated plan.
/// </summary>
public sealed record CachedEntitlementReadResult
{
    private CachedEntitlementReadResult(
        bool isSuccess,
        EntitlementCacheReadOutcome outcome,
        CachedEntitlementProjection? projection,
        string? stableErrorCode,
        string? safeErrorReason)
    {
        IsSuccess = isSuccess;
        Outcome = outcome;
        Projection = projection;
        StableErrorCode = stableErrorCode;
        SafeErrorReason = safeErrorReason;
    }

    public bool IsSuccess { get; }

    public EntitlementCacheReadOutcome Outcome { get; }

    /// <summary>Present only when <see cref="IsSuccess"/> is true.</summary>
    public CachedEntitlementProjection? Projection { get; }

    /// <summary>Bounded stable code present only on failure (for example authority unavailable).</summary>
    public string? StableErrorCode { get; }

    /// <summary>Bounded safe reason present only on failure; never contains identity or key material.</summary>
    public string? SafeErrorReason { get; }

    public static CachedEntitlementReadResult Success(
        EntitlementCacheReadOutcome outcome,
        CachedEntitlementProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new CachedEntitlementReadResult(true, outcome, projection, null, null);
    }

    /// <summary>FEAT-015: verified no-active entitlement (indexed absence). Success with no projection
    /// and no cache entry — absence is never negative-cached and is distinct from unavailability.</summary>
    public static CachedEntitlementReadResult NoActive() =>
        new(true, EntitlementCacheReadOutcome.AuthorityNoActive, null, null, null);

    public static CachedEntitlementReadResult Failure(
        EntitlementCacheReadOutcome outcome,
        string stableErrorCode,
        string safeErrorReason) =>
        new(false, outcome, null, stableErrorCode, safeErrorReason);
}
