using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Caching;

/// <summary>
/// Service for caching per-user feed metadata in Redis.
/// FEAT-060: lastBlockIndex-only methods (legacy, to be removed in Phase 3).
/// FEAT-065: Full metadata methods (new, replaces FEAT-060 methods).
/// </summary>
public interface IFeedMetadataCacheService
{
    // ==========================================
    // FEAT-060 Legacy Methods (to be removed in Phase 3)
    // ==========================================

    /// <summary>
    /// [FEAT-060 LEGACY] Gets all feed lastBlockIndex values for a user via HGETALL + JSON parse.
    /// Will be replaced by GetAllFeedMetadataAsync in Phase 3.
    /// </summary>
    Task<IReadOnlyDictionary<FeedId, BlockIndex>?> GetAllLastBlockIndexesAsync(string userId);

    /// <summary>
    /// [FEAT-060 LEGACY] Sets a single feed's lastBlockIndex via HSET with JSON value.
    /// Will be replaced by SetFeedMetadataAsync in Phase 3.
    /// </summary>
    Task<bool> SetLastBlockIndexAsync(string userId, FeedId feedId, BlockIndex lastBlockIndex);

    /// <summary>
    /// [FEAT-060 LEGACY] Bulk-sets multiple feed lastBlockIndex values via HMSET.
    /// Will be replaced by SetMultipleFeedMetadataAsync in Phase 3.
    /// </summary>
    Task<bool> SetMultipleLastBlockIndexesAsync(string userId, IReadOnlyDictionary<FeedId, BlockIndex> blockIndexes);

    /// <summary>
    /// [FEAT-060 LEGACY] Removes a feed entry from the user's feed_meta HASH via HDEL.
    /// Will be replaced by RemoveFeedMetadataAsync in Phase 3.
    /// </summary>
    Task<bool> RemoveFeedMetaAsync(string userId, FeedId feedId);

    // ==========================================
    // FEAT-065 Full Metadata Methods (new)
    // ==========================================

    /// <summary>
    /// Gets all feed metadata for a user via HGETALL + JSON deserialization.
    /// Returns FeedMetadataEntry per feed with full 6-field metadata.
    /// Legacy FEAT-060 entries (lastBlockIndex-only) are detected via IsLegacyFormat flag.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>
    /// Dictionary mapping feed IDs to their full metadata, or null on cache miss/error.
    /// </returns>
    Task<IReadOnlyDictionary<FeedId, FeedMetadataEntry>?> GetAllFeedMetadataAsync(string userId);

    /// <summary>
    /// Sets a single feed's full metadata via HSET with JSON value.
    /// Refreshes the Hash TTL on write.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="entry">The full metadata entry to store.</param>
    /// <returns>True on success, false on error.</returns>
    Task<bool> SetFeedMetadataAsync(string userId, FeedId feedId, FeedMetadataEntry entry);

    /// <summary>
    /// Bulk-sets multiple feeds' full metadata via HMSET.
    /// Used for cache-miss repopulation from PostgreSQL.
    /// Sets TTL atomically after HMSET.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="entries">Dictionary mapping feed IDs to their full metadata.</param>
    /// <returns>True on success, false on error.</returns>
    Task<bool> SetMultipleFeedMetadataAsync(string userId, IReadOnlyDictionary<FeedId, FeedMetadataEntry> entries);

    /// <summary>
    /// Removes a feed entry from the user's feed_meta HASH via HDEL.
    /// Used when a user leaves or is banned from a group feed.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID to remove.</param>
    /// <returns>True if the field was removed, false on error or if field didn't exist.</returns>
    Task<bool> RemoveFeedMetadataAsync(string userId, FeedId feedId);

    /// <summary>
    /// Updates only the title field of an existing feed metadata entry.
    /// Reads the current entry, updates the title, and writes back.
    /// Used for identity name change and group title change cascades.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID whose title to update.</param>
    /// <param name="newTitle">The new display title.</param>
    /// <returns>True on success, false on error or if entry doesn't exist.</returns>
    Task<bool> UpdateFeedTitleAsync(string userId, FeedId feedId, string newTitle);
}
