namespace HushNode.Caching;

/// <summary>
/// Constants for the feed participants and key generations cache (FEAT-050).
/// </summary>
public static class FeedParticipantsCacheConstants
{
    /// <summary>
    /// Time-to-live for feed participants and key generations cache entries.
    /// 1 hour is appropriate as membership changes are infrequent.
    /// TTL is refreshed on cache hit.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

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
