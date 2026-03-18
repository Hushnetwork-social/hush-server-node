namespace HushNode.Caching;

/// <summary>
/// Constants for the feed participants and key generations cache (FEAT-050).
/// </summary>
public static class FeedParticipantsCacheConstants
{
    /// <summary>
    /// Time-to-live for feed participants cache entries.
    /// Membership lists can change more frequently, so keep this shorter.
    /// </summary>
    public static readonly TimeSpan ParticipantsCacheTtl = TimeSpan.FromHours(1);

    /// <summary>
    /// Time-to-live for key generations cache entries.
    /// Key generations are append-only and explicitly invalidated on membership changes,
    /// so a long TTL is safe and keeps PostgreSQL out of the hot read path.
    /// TTL is refreshed on cache hit.
    /// </summary>
    public static readonly TimeSpan KeyGenerationsCacheTtl = TimeSpan.FromDays(30);

    /// <summary>
    /// Key prefix for participants cache entries.
    /// </summary>
    private const string ParticipantsPrefix = "feed:";

    /// <summary>
    /// Key suffix for participants cache entries.
    /// </summary>
    private const string ParticipantsSuffix = ":participants";

    /// <summary>
    /// Key suffix for key generations cache entries.
    /// </summary>
    private const string KeysSuffix = ":keys";

    /// <summary>
    /// Gets the Redis key for a feed's participants cache.
    /// Pattern: feed:{feedId}:participants
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>The Redis key for this feed's participants cache.</returns>
    public static string GetParticipantsKey(string feedId) => $"{ParticipantsPrefix}{feedId}{ParticipantsSuffix}";

    /// <summary>
    /// Gets the Redis key for a feed's key generations cache.
    /// Pattern: feed:{feedId}:keys
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>The Redis key for this feed's key generations cache.</returns>
    public static string GetKeyGenerationsKey(string feedId) => $"{ParticipantsPrefix}{feedId}{KeysSuffix}";
}
