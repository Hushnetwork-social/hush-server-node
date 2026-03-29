namespace HushNode.Caching;

/// <summary>
/// Provides short-term Redis acceleration for committed FEAT-099 idempotency markers.
/// </summary>
public interface IElectionCastIdempotencyCacheService
{
    /// <summary>
    /// Checks whether a committed marker is present in the Redis cache.
    /// Returns <c>true</c> when found and <c>null</c> when the cache misses or is unavailable.
    /// </summary>
    Task<bool?> ExistsAsync(string electionId, string idempotencyKeyHash);

    /// <summary>
    /// Stores a committed marker in the Redis cache.
    /// </summary>
    Task SetAsync(string electionId, string idempotencyKeyHash);
}
