namespace HushNode.Caching;

/// <summary>
/// Constants for the identity display name cache (FEAT-065 E2).
/// Stores display names in a single global Redis Hash.
/// No TTL — entries are updated on identity change events.
/// </summary>
public static class IdentityDisplayNameCacheConstants
{
    /// <summary>
    /// The Redis Hash key for the global identity display names cache.
    /// Pattern: identities:display_names (single global Hash, not per-user).
    /// </summary>
    public const string DisplayNamesHashKey = "identities:display_names";

    /// <summary>
    /// Gets the full Redis key including the instance prefix.
    /// </summary>
    /// <param name="prefix">The Redis instance name prefix.</param>
    /// <returns>The prefixed Redis Hash key.</returns>
    public static string GetDisplayNamesKey(string prefix) => $"{prefix}{DisplayNamesHashKey}";
}
