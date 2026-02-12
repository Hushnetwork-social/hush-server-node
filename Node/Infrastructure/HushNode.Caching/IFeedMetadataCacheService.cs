using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Caching;

/// <summary>
/// Service for caching per-user feed metadata in Redis (FEAT-060).
/// Currently handles lastBlockIndex only. FEAT-065 will extend with full metadata.
/// Implements graceful degradation: cache failures return null/false and are logged.
/// </summary>
public interface IFeedMetadataCacheService
{
    /// <summary>
    /// Gets all feed lastBlockIndex values for a user via HGETALL + JSON parse.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>
    /// Dictionary mapping feed IDs to their lastBlockIndex, or null on cache miss/error.
    /// </returns>
    Task<IReadOnlyDictionary<FeedId, BlockIndex>?> GetAllLastBlockIndexesAsync(string userId);

    /// <summary>
    /// Sets a single feed's lastBlockIndex via HSET with JSON value.
    /// Refreshes the Hash TTL on write.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="lastBlockIndex">The last block index for this feed.</param>
    /// <returns>True on success, false on error.</returns>
    Task<bool> SetLastBlockIndexAsync(string userId, FeedId feedId, BlockIndex lastBlockIndex);

    /// <summary>
    /// Bulk-sets multiple feed lastBlockIndex values via HMSET.
    /// Used for cache-miss repopulation from PostgreSQL.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="blockIndexes">Dictionary mapping feed IDs to their lastBlockIndex.</param>
    /// <returns>True on success, false on error.</returns>
    Task<bool> SetMultipleLastBlockIndexesAsync(string userId, IReadOnlyDictionary<FeedId, BlockIndex> blockIndexes);

    /// <summary>
    /// Removes a feed entry from the user's feed_meta HASH via HDEL.
    /// Used when a user leaves or is banned from a group feed.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID to remove.</param>
    /// <returns>True if the field was removed, false on error or if field didn't exist.</returns>
    Task<bool> RemoveFeedMetaAsync(string userId, FeedId feedId);
}
