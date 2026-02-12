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

    // --- FEAT-060: HASH-based methods ---

    /// <summary>
    /// Gets all read positions for a user from the Redis HASH (FEAT-060).
    /// Single round-trip via HGETALL, O(hash_size) instead of O(keyspace) SCAN.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>
    /// Dictionary mapping feed IDs to their read positions, or null on cache miss/error.
    /// </returns>
    Task<IReadOnlyDictionary<FeedId, BlockIndex>?> GetAllReadPositionsAsync(string userId);

    /// <summary>
    /// Atomically sets a read position using max-wins semantics (FEAT-060).
    /// Only updates the Hash field if the new blockIndex is greater than the current value.
    /// Uses a Lua script for atomic compare-and-set.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="blockIndex">The block index up to which the user has read.</param>
    /// <returns>True if the value was updated (new > current), false otherwise or on error.</returns>
    Task<bool> SetReadPositionWithMaxWinsAsync(string userId, FeedId feedId, BlockIndex blockIndex);

    /// <summary>
    /// Bulk-sets all read positions for a user via HMSET (FEAT-060).
    /// Used for migration from legacy individual keys and cache-miss repopulation from PostgreSQL.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="positions">Dictionary mapping feed IDs to their read positions.</param>
    /// <returns>True if cache was updated successfully, false on error.</returns>
    Task<bool> SetAllReadPositionsAsync(string userId, IReadOnlyDictionary<FeedId, BlockIndex> positions);
}
