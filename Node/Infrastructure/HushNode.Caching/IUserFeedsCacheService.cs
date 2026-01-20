using HushShared.Feeds.Model;

namespace HushNode.Caching;

/// <summary>
/// Service for caching user feed lists in Redis.
/// Implements cache-aside pattern with in-place updates (FEAT-049).
/// Uses Redis SET to store feed IDs per user.
/// </summary>
public interface IUserFeedsCacheService
{
    /// <summary>
    /// Gets all cached feed IDs for a user.
    /// </summary>
    /// <param name="userPublicAddress">The user's public signing address.</param>
    /// <returns>
    /// The cached feed IDs, or null if cache miss (key doesn't exist).
    /// Returns empty collection if cache exists but user has no feeds.
    /// </returns>
    Task<IReadOnlyList<FeedId>?> GetUserFeedsAsync(string userPublicAddress);

    /// <summary>
    /// Stores all feed IDs for a user in the cache.
    /// Replaces any existing cached feeds for this user.
    /// Used for cache population after a cache miss.
    /// </summary>
    /// <param name="userPublicAddress">The user's public signing address.</param>
    /// <param name="feedIds">The feed IDs to cache.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetUserFeedsAsync(string userPublicAddress, IEnumerable<FeedId> feedIds);

    /// <summary>
    /// Adds a single feed ID to the user's cache (in-place update).
    /// Uses Redis SADD to add to the SET.
    /// Refreshes TTL on the cache entry.
    /// Idempotent: adding an existing feed ID is a no-op.
    /// </summary>
    /// <param name="userPublicAddress">The user's public signing address.</param>
    /// <param name="feedId">The feed ID to add.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddFeedToUserCacheAsync(string userPublicAddress, FeedId feedId);

    /// <summary>
    /// Removes a single feed ID from the user's cache (in-place update).
    /// Uses Redis SREM to remove from the SET.
    /// Idempotent: removing a non-existent feed ID is a no-op.
    /// </summary>
    /// <param name="userPublicAddress">The user's public signing address.</param>
    /// <param name="feedId">The feed ID to remove.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveFeedFromUserCacheAsync(string userPublicAddress, FeedId feedId);
}
