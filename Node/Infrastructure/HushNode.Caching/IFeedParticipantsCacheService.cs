using HushShared.Feeds.Model;

namespace HushNode.Caching;

/// <summary>
/// Service for caching feed participants and key generations in Redis (FEAT-050).
/// Part A: Feed participants stored as Redis SET.
/// Part B: Key generations stored as JSON blob.
/// </summary>
public interface IFeedParticipantsCacheService
{
    #region Part A: Feed Participants

    /// <summary>
    /// Gets all cached participant addresses for a feed.
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>
    /// The cached participant addresses, or null if cache miss (key doesn't exist).
    /// Returns empty collection if cache exists but feed has no participants.
    /// </returns>
    Task<IReadOnlyList<string>?> GetParticipantsAsync(FeedId feedId);

    /// <summary>
    /// Stores all participant addresses for a feed in the cache.
    /// Replaces any existing cached participants for this feed.
    /// Used for cache population after a cache miss.
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="participantAddresses">The participant public signing addresses.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetParticipantsAsync(FeedId feedId, IEnumerable<string> participantAddresses);

    /// <summary>
    /// Adds a single participant to the feed's cache (in-place update).
    /// Uses Redis SADD to add to the SET.
    /// Refreshes TTL on the cache entry.
    /// Only adds if cache already exists (doesn't create partial cache).
    /// Idempotent: adding an existing participant is a no-op.
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="participantAddress">The participant's public signing address.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddParticipantAsync(FeedId feedId, string participantAddress);

    /// <summary>
    /// Removes a single participant from the feed's cache (in-place update).
    /// Uses Redis SREM to remove from the SET.
    /// Idempotent: removing a non-existent participant is a no-op.
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="participantAddress">The participant's public signing address.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveParticipantAsync(FeedId feedId, string participantAddress);

    #endregion

    #region Part B: Key Generations

    /// <summary>
    /// Gets all cached key generations for a feed.
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>
    /// The cached key generations, or null if cache miss (key doesn't exist).
    /// </returns>
    Task<CachedKeyGenerations?> GetKeyGenerationsAsync(FeedId feedId);

    /// <summary>
    /// Stores key generations for a feed in the cache.
    /// Replaces any existing cached key generations for this feed.
    /// Used for cache population after a cache miss.
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="keyGenerations">The key generations to cache.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetKeyGenerationsAsync(FeedId feedId, CachedKeyGenerations keyGenerations);

    /// <summary>
    /// Invalidates (deletes) the key generations cache for a feed.
    /// Called when membership changes, as key generations become stale.
    /// Next read will repopulate from database.
    /// </summary>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InvalidateKeyGenerationsAsync(FeedId feedId);

    #endregion
}
