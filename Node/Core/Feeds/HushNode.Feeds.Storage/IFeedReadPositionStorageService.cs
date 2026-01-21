using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Storage;

/// <summary>
/// Service for managing user read positions in feeds.
/// Orchestrates cache and repository operations using cache-aside (read) and write-through (write) patterns.
/// This is the single entry point for all read position operations.
/// </summary>
public interface IFeedReadPositionStorageService
{
    /// <summary>
    /// Gets the read position for a user in a feed.
    /// Uses cache-aside pattern: tries cache first, falls back to database on miss.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>
    /// The block index up to which the user has read, or 0 if no read position exists.
    /// Returns 0 (not null) as the default value for unread feeds.
    /// </returns>
    Task<BlockIndex> GetReadPositionAsync(string userId, FeedId feedId);

    /// <summary>
    /// Gets all read positions for a user across all their feeds.
    /// Uses cache-aside pattern with database fallback.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <returns>
    /// Dictionary mapping feed IDs to their read positions.
    /// Feeds without read positions are not included (assume 0).
    /// </returns>
    Task<IReadOnlyDictionary<FeedId, BlockIndex>> GetReadPositionsForUserAsync(string userId);

    /// <summary>
    /// Marks a feed as read up to the specified block index.
    /// Uses write-through pattern: writes to database first (source of truth), then updates cache.
    /// Implements "max wins" semantics: only updates if new blockIndex > current blockIndex.
    /// </summary>
    /// <param name="userId">The user's public signing address.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="blockIndex">The block index up to which the user has read.</param>
    /// <returns>
    /// True if the position was updated (new value was greater than current),
    /// False if no update occurred (new value was less than or equal to current).
    /// Note: Cache failures do not affect the return value; they are logged and ignored (graceful degradation).
    /// </returns>
    Task<bool> MarkFeedAsReadAsync(string userId, FeedId feedId, BlockIndex blockIndex);
}
