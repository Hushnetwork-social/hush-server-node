using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;
using HushShared.Blockchain.BlockModel;

namespace HushNode.Feeds.Storage;

/// <summary>
/// Repository for managing user read positions in feeds.
/// Used for cross-device read sync - tracks the last block index each user has read per feed.
/// </summary>
public interface IFeedReadPositionRepository : IRepository
{
    /// <summary>
    /// Get the read position for a specific user and feed.
    /// </summary>
    /// <param name="userId">User's public signing address.</param>
    /// <param name="feedId">Feed identifier.</param>
    /// <returns>The last read block index, or null if no record exists.</returns>
    Task<BlockIndex?> GetReadPositionAsync(string userId, FeedId feedId);

    /// <summary>
    /// Get all read positions for a user across all feeds.
    /// Used for bulk feed sync when retrieving feeds for an address.
    /// </summary>
    /// <param name="userId">User's public signing address.</param>
    /// <returns>Dictionary mapping FeedId to LastReadBlockIndex.</returns>
    Task<IReadOnlyDictionary<FeedId, BlockIndex>> GetReadPositionsForUserAsync(string userId);

    /// <summary>
    /// Upsert a read position using "max wins" semantics.
    /// If the new blockIndex is greater than the existing one, the record is updated.
    /// If the new blockIndex is less than or equal to the existing one, no change is made.
    /// If no record exists, a new one is created.
    /// </summary>
    /// <param name="userId">User's public signing address.</param>
    /// <param name="feedId">Feed identifier.</param>
    /// <param name="blockIndex">The block index up to which the user has read.</param>
    /// <returns>True if the record was inserted or updated, false if the value was unchanged (max wins).</returns>
    Task<bool> UpsertReadPositionAsync(string userId, FeedId feedId, BlockIndex blockIndex);
}
