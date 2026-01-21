using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Caching;

/// <summary>
/// Service for caching user read positions in Redis.
/// Implements graceful degradation: cache failures return null/false and are logged.
/// </summary>
public interface IFeedReadPositionCacheService
{
    /// <summary>
    /// Gets the cached read position for a user in a feed.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>
    /// The cached block index, or null if not cached or on cache error.
    /// Returns null (not 0) for cache miss to distinguish from "read up to block 0".
    /// </returns>
    Task<BlockIndex?> GetReadPositionAsync(string userId, FeedId feedId);

    /// <summary>
    /// Gets all cached read positions for a user across all feeds.
    /// Used for bulk retrieval when syncing feed list.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>
    /// Dictionary mapping feed IDs to their read positions, or null on cache error.
    /// Note: This operation is O(n) scan and should be used sparingly.
    /// Prefer GetReadPositionAsync for single feed lookups.
    /// </returns>
    Task<IReadOnlyDictionary<FeedId, BlockIndex>?> GetReadPositionsForUserAsync(string userId);

    /// <summary>
    /// Sets the cached read position for a user in a feed.
    /// Applies a 30-day TTL to the cache entry.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="blockIndex">The block index up to which the user has read.</param>
    /// <returns>True if cache was updated successfully, false on cache error (graceful degradation).</returns>
    Task<bool> SetReadPositionAsync(string userId, FeedId feedId, BlockIndex blockIndex);

    /// <summary>
    /// Invalidates (deletes) the cached read position for a user in a feed.
    /// Used when data consistency is required (e.g., cache corruption suspected).
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InvalidateCacheAsync(string userId, FeedId feedId);
}
