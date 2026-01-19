using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Caching;

/// <summary>
/// Service for caching feed messages in Redis.
/// Implements write-through and cache-aside patterns for the Recent Messages Cache (FEAT-046).
/// </summary>
public interface IFeedMessageCacheService
{
    /// <summary>
    /// Adds a message to the feed's cache.
    /// Implements LPUSH + LTRIM + EXPIRE pattern to maintain last 100 messages with 24h TTL.
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="message">The message to cache.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddMessageAsync(FeedId feedId, FeedMessage message);

    /// <summary>
    /// Gets cached messages for a feed, optionally filtered by block index.
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="sinceBlockIndex">Optional: Only return messages with BlockIndex greater than this value.</param>
    /// <returns>
    /// The cached messages, or null if cache miss (key doesn't exist).
    /// Returns empty collection if cache exists but no messages match the filter.
    /// </returns>
    Task<IEnumerable<FeedMessage>?> GetMessagesAsync(FeedId feedId, BlockIndex? sinceBlockIndex = null);

    /// <summary>
    /// Invalidates (deletes) the cache for a specific feed.
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InvalidateCacheAsync(FeedId feedId);

    /// <summary>
    /// Populates cache from a list of messages (cache-aside pattern).
    /// Used when repopulating after a cache miss.
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="messages">The messages to populate the cache with.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PopulateCacheAsync(FeedId feedId, IEnumerable<FeedMessage> messages);
}
