namespace HushNode.Caching;

/// <summary>
/// Constants for the feed metadata cache (FEAT-060).
/// Stores per-user feed metadata (currently lastBlockIndex only, extended by FEAT-065).
/// </summary>
public static class FeedMetadataCacheConstants
{
    /// <summary>
    /// Time-to-live for feed metadata cache entries.
    /// Active feeds refresh TTL on every message. Inactive feeds expire and rebuild from PostgreSQL.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets the Redis HASH key for a user's feed metadata.
    /// Each field in the Hash is a feedId, value is JSON containing lastBlockIndex (and later more metadata).
    /// Pattern: user:{userId}:feed_meta
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>The Redis Hash key for this user's feed metadata.</returns>
    public static string GetFeedMetaHashKey(string userId) => $"user:{userId}:feed_meta";
}
