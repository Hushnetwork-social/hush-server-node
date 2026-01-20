namespace HushNode.Caching;

/// <summary>
/// Constants for the user feeds list cache (FEAT-049).
/// </summary>
public static class UserFeedsCacheConstants
{
    /// <summary>
    /// Time-to-live for user feeds cache entries.
    /// 5 minutes is short because feed list changes more frequently than identity.
    /// TTL is refreshed on all operations (read, write, add, remove).
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Key prefix for user feeds cache entries.
    /// </summary>
    private const string KeyPrefix = "user:";

    /// <summary>
    /// Key suffix for user feeds cache entries.
    /// </summary>
    private const string KeySuffix = ":feeds";

    /// <summary>
    /// Gets the Redis key for a user's feed list cache.
    /// Pattern: user:{userId}:feeds
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>The Redis key for this user's feed list cache.</returns>
    public static string GetUserFeedsKey(string userId) => $"{KeyPrefix}{userId}{KeySuffix}";
}
