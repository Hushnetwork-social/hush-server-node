namespace HushNode.Caching;

/// <summary>
/// Service for caching identity display names in a global Redis Hash (FEAT-065 E2).
/// Key: {prefix}identities:display_names, Fields: {publicAddress} → displayName.
/// Eliminates N+1 identity lookups in the sync hot path by batching via HMGET.
/// </summary>
public interface IIdentityDisplayNameCacheService
{
    /// <summary>
    /// Batch lookup display names for multiple addresses via HMGET.
    /// Returns a dictionary where null values indicate cache misses (caller should query PostgreSQL).
    /// </summary>
    /// <param name="addresses">Public signing addresses to look up.</param>
    /// <returns>
    /// Dictionary mapping each address to its display name, or null if not cached.
    /// Returns null on Redis failure (graceful degradation).
    /// </returns>
    Task<IReadOnlyDictionary<string, string?>?> GetDisplayNamesAsync(IEnumerable<string> addresses);

    /// <summary>
    /// Sets a single display name via HSET. Used on identity creation or update.
    /// </summary>
    /// <param name="address">The public signing address.</param>
    /// <param name="displayName">The display name to cache.</param>
    /// <returns>True on success, false on error.</returns>
    Task<bool> SetDisplayNameAsync(string address, string displayName);

    /// <summary>
    /// Batch set multiple display names via HMSET. Used to populate after cache miss.
    /// </summary>
    /// <param name="displayNames">Dictionary mapping addresses to display names.</param>
    /// <returns>True on success, false on error.</returns>
    Task<bool> SetMultipleDisplayNamesAsync(IReadOnlyDictionary<string, string> displayNames);
}
