using HushShared.Feeds.Model;

namespace HushNode.Caching;

/// <summary>
/// Constants for the feed message cache (FEAT-046).
/// </summary>
public static class FeedMessageCacheConstants
{
    /// <summary>
    /// Maximum number of messages cached per feed.
    /// Older messages are trimmed when this limit is exceeded.
    /// </summary>
    public const int MaxMessagesPerFeed = 100;

    /// <summary>
    /// Time-to-live for feed message cache entries.
    /// TTL is refreshed on every write operation.
    /// Active feeds stay cached; abandoned feeds expire after 24 hours.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets the Redis key for a feed's message cache.
    /// Pattern: feed:{feedId}:messages
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>The Redis key for this feed's message cache.</returns>
    public static string GetFeedMessagesKey(FeedId feedId) => $"feed:{feedId}:messages";
}
