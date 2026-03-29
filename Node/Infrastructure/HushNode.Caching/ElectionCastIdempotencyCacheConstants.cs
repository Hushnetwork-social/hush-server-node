namespace HushNode.Caching;

/// <summary>
/// Constants for the FEAT-099 committed cast idempotency cache.
/// </summary>
public static class ElectionCastIdempotencyCacheConstants
{
    /// <summary>
    /// Time-to-live for committed election-scoped idempotency markers.
    /// The database remains the source of truth; Redis is only a short-term accelerator.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private const string KeyPrefix = "election-cast-idempotency:";

    /// <summary>
    /// Gets the Redis key for a committed election-scoped idempotency marker.
    /// Pattern: election-cast-idempotency:{electionId}:{idempotencyKeyHash}
    /// </summary>
    public static string GetCommittedMarkerKey(string electionId, string idempotencyKeyHash) =>
        $"{KeyPrefix}{electionId}:{idempotencyKeyHash}";
}
