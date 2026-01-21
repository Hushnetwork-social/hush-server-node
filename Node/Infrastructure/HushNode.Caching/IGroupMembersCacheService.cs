using HushShared.Feeds.Model;

namespace HushNode.Caching;

/// <summary>
/// Service for caching group members with display names in Redis.
/// Solves the N+1 identity lookup problem in GetGroupMembers.
/// Cache is invalidated when:
/// - A member's display name changes (via IdentityUpdatedEvent)
/// - A member joins or leaves the group
/// </summary>
public interface IGroupMembersCacheService
{
    /// <summary>
    /// Gets cached group members for a feed.
    /// </summary>
    /// <param name="feedId">The group feed ID.</param>
    /// <returns>
    /// The cached members with display names, or null if cache miss.
    /// </returns>
    Task<CachedGroupMembers?> GetGroupMembersAsync(FeedId feedId);

    /// <summary>
    /// Stores group members in the cache.
    /// Called after a cache miss to populate the cache from DB.
    /// </summary>
    /// <param name="feedId">The group feed ID.</param>
    /// <param name="members">The members with display names to cache.</param>
    Task SetGroupMembersAsync(FeedId feedId, CachedGroupMembers members);

    /// <summary>
    /// Invalidates the group members cache for a specific feed.
    /// Called when a member joins, leaves, or changes role.
    /// </summary>
    /// <param name="feedId">The group feed ID.</param>
    Task InvalidateGroupMembersAsync(FeedId feedId);

    /// <summary>
    /// Invalidates group members cache for all feeds where a user is a participant.
    /// Called when a user's display name changes.
    /// </summary>
    /// <param name="publicSigningAddress">The user's public signing address.</param>
    /// <param name="feedIds">The feed IDs where this user is a participant.</param>
    Task InvalidateGroupMembersForUserAsync(string publicSigningAddress, IEnumerable<FeedId> feedIds);
}
