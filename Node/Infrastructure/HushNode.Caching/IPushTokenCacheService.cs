using HushNode.Interfaces.Models;

namespace HushNode.Caching;

/// <summary>
/// Service for caching push notification device tokens in Redis.
/// Implements write-through and cache-aside patterns (FEAT-047).
/// </summary>
public interface IPushTokenCacheService
{
    /// <summary>
    /// Gets all cached tokens for a user.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>
    /// The cached tokens, or null if cache miss (key doesn't exist).
    /// Returns empty collection if cache exists but user has no tokens.
    /// </returns>
    Task<IEnumerable<DeviceToken>?> GetTokensAsync(string userId);

    /// <summary>
    /// Stores all tokens for a user in the cache.
    /// Replaces any existing cached tokens for this user.
    /// Used for cache population after a cache miss.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="tokens">The tokens to cache.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetTokensAsync(string userId, IEnumerable<DeviceToken> tokens);

    /// <summary>
    /// Adds a new token or updates an existing token in the user's cache.
    /// Uses HSET to add/update a single field in the user's HASH.
    /// Refreshes TTL on the cache entry.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="token">The token to add or update.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddOrUpdateTokenAsync(string userId, DeviceToken token);

    /// <summary>
    /// Removes a single token from the user's cache.
    /// Uses HDEL to remove a single field from the user's HASH.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="tokenId">The ID of the token to remove.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveTokenAsync(string userId, string tokenId);

    /// <summary>
    /// Invalidates (deletes) the entire cache for a user.
    /// Removes all tokens for the specified user from Redis.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InvalidateUserCacheAsync(string userId);
}
