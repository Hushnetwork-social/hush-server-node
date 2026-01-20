using HushShared.Identity.Model;

namespace HushNode.Caching;

/// <summary>
/// Service for caching identity profiles in Redis.
/// Implements cache-aside pattern with TTL refresh on read (FEAT-048).
/// </summary>
public interface IIdentityCacheService
{
    /// <summary>
    /// Gets a cached identity profile by public signing address.
    /// Returns null on cache miss or Redis error (caller should fall back to database).
    /// TTL is refreshed on successful cache hit.
    /// </summary>
    /// <param name="publicSigningAddress">The public signing address of the identity.</param>
    /// <returns>The cached Profile, or null if not cached.</returns>
    Task<Profile?> GetIdentityAsync(string publicSigningAddress);

    /// <summary>
    /// Caches an identity profile with 7-day TTL.
    /// </summary>
    /// <param name="publicSigningAddress">The public signing address of the identity.</param>
    /// <param name="profile">The profile to cache.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetIdentityAsync(string publicSigningAddress, Profile profile);

    /// <summary>
    /// Invalidates (removes) a cached identity.
    /// This method is idempotent - no error if the key doesn't exist.
    /// </summary>
    /// <param name="publicSigningAddress">The public signing address of the identity to invalidate.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InvalidateCacheAsync(string publicSigningAddress);
}
