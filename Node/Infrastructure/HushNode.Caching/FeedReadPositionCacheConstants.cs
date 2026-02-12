using HushShared.Feeds.Model;

namespace HushNode.Caching;

/// <summary>
/// Constants for the feed read position cache (FEAT-051, FEAT-060).
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
    /// Gets the Redis HASH key for a user's read positions (FEAT-060).
    /// All read positions for a user are stored as fields in this single Hash.
    /// Pattern: user:{userId}:read_positions
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>The Redis Hash key for this user's read positions.</returns>
    public static string GetReadPositionsHashKey(string userId) => $"user:{userId}:read_positions";

    /// <summary>
    /// Gets the Redis key for a user's read position in a feed (legacy individual STRING key).
    /// Pattern: user:{userId}:read:{feedId}
    /// Kept for migration fallback from old individual keys to new HASH.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>The Redis key for this user's read position in the feed.</returns>
    public static string GetReadPositionKey(string userId, FeedId feedId) => $"user:{userId}:read:{feedId}";

    /// <summary>
    /// Gets the SCAN pattern for finding all legacy individual read position keys for a user.
    /// Used during migration from individual STRING keys to HASH.
    /// Pattern: user:{userId}:read:*
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>The SCAN pattern for this user's legacy read position keys.</returns>
    public static string GetReadPositionScanPattern(string userId) => $"user:{userId}:read:*";
}
