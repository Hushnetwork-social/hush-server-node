namespace HushNode.Caching;

/// <summary>
/// Constants for the push token cache (FEAT-047).
/// </summary>
public static class PushTokenCacheConstants
{
    /// <summary>
    /// Time-to-live for push token cache entries.
    /// 7 days balances cache hits vs memory for inactive users.
    /// TTL is refreshed on every write operation.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    /// <summary>
    /// Key prefix for push token cache entries.
    /// Versioned (v1) for easy invalidation on schema changes.
    /// </summary>
    private const string KeyPrefix = "push:v1:user:";

    /// <summary>
    /// Gets the Redis key for a user's push tokens cache.
    /// Pattern: push:v1:user:{userId}
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>The Redis key for this user's push tokens cache.</returns>
    public static string GetUserKey(string userId) => $"{KeyPrefix}{userId}";
}
