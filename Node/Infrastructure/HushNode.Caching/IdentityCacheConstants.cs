namespace HushNode.Caching;

/// <summary>
/// Constants for the identity cache (FEAT-048).
/// </summary>
public static class IdentityCacheConstants
{
    /// <summary>
    /// Time-to-live for identity cache entries.
    /// TTL is refreshed on every read operation.
    /// Frequently accessed identities stay cached; inactive identities expire after 7 days.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets the Redis key for an identity's profile cache.
    /// Pattern: identity:{publicSigningAddress}
    /// </summary>
    /// <param name="publicSigningAddress">The public signing address of the identity.</param>
    /// <returns>The Redis key for this identity's cache entry.</returns>
    public static string GetIdentityKey(string publicSigningAddress) => $"identity:{publicSigningAddress}";
}
