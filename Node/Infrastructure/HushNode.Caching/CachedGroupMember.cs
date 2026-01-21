namespace HushNode.Caching;

/// <summary>
/// Represents a cached group member with display name.
/// Used by IGroupMembersCacheService to avoid N+1 identity lookups.
/// </summary>
public record CachedGroupMember(
    string PublicAddress,
    string DisplayName,
    int ParticipantType,
    long JoinedAtBlock,
    long? LeftAtBlock = null);

/// <summary>
/// Container for cached group members.
/// Stored as JSON in Redis.
/// </summary>
public record CachedGroupMembers(IReadOnlyList<CachedGroupMember> Members);
