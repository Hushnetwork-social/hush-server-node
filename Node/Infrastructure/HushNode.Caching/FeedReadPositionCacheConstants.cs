using HushShared.Feeds.Model;

namespace HushNode.Caching;

/// <summary>
/// Constants for the feed read position cache (FEAT-051).
/// </summary>
public static class FeedReadPositionCacheConstants
{
    /// <summary>
    /// Time-to-live for read position cache entries.
    /// Read positions expire after 30 days of inactivity.
    /// TTL is refreshed on every write operation.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets the Redis key for a user's read position in a feed.
    /// Pattern: user:{userId}:read:{feedId}
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>The Redis key for this user's read position in the feed.</returns>
    public static string GetReadPositionKey(string userId, FeedId feedId) => $"user:{userId}:read:{feedId}";
}
