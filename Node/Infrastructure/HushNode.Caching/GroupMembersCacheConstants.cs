namespace HushNode.Caching;

/// <summary>
/// Constants for the group members cache.
/// </summary>
public static class GroupMembersCacheConstants
{
    /// <summary>
    /// Time-to-live for group members cache entries.
    /// 1 hour is appropriate as membership changes are infrequent.
    /// TTL is refreshed on cache hit.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    /// <summary>
    /// Key prefix for group members cache entries.
    /// </summary>
    private const string GroupMembersPrefix = "group:";

    /// <summary>
    /// Key suffix for group members cache entries.
    /// </summary>
    private const string GroupMembersSuffix = ":members";

    /// <summary>
    /// Gets the Redis key for a feed's group members cache.
    /// Pattern: group:{feedId}:members
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>The Redis key for this feed's group members cache.</returns>
    public static string GetGroupMembersKey(string feedId) => $"{GroupMembersPrefix}{feedId}{GroupMembersSuffix}";
}
