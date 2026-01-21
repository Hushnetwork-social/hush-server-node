using Microsoft.EntityFrameworkCore;
using Olimpo.EntityFramework.Persistency;
using HushShared.Feeds.Model;
using HushShared.Blockchain.BlockModel;

namespace HushNode.Feeds.Storage;

/// <summary>
/// Repository implementation for managing user read positions in feeds.
/// Implements "max wins" semantics: when concurrent updates occur, the higher blockIndex always wins.
/// </summary>
public class FeedReadPositionRepository : RepositoryBase<FeedsDbContext>, IFeedReadPositionRepository
{
    /// <inheritdoc />
    public async Task<BlockIndex?> GetReadPositionAsync(string userId, FeedId feedId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        var entity = await this.Context.FeedReadPositions
            .FirstOrDefaultAsync(x => x.UserId == userId && x.FeedId == feedId);

        return entity?.LastReadBlockIndex;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<FeedId, BlockIndex>> GetReadPositionsForUserAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return new Dictionary<FeedId, BlockIndex>();

        var positions = await this.Context.FeedReadPositions
            .Where(x => x.UserId == userId)
            .ToListAsync();

        return positions.ToDictionary(
            x => x.FeedId,
            x => x.LastReadBlockIndex);
    }

    /// <inheritdoc />
    public async Task<bool> UpsertReadPositionAsync(string userId, FeedId feedId, BlockIndex blockIndex)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        var now = DateTime.UtcNow;

        // Try to find existing record
        var existing = await this.Context.FeedReadPositions
            .FirstOrDefaultAsync(x => x.UserId == userId && x.FeedId == feedId);

        if (existing == null)
        {
            // Insert new record
            var newEntity = new FeedReadPositionEntity(
                UserId: userId,
                FeedId: feedId,
                LastReadBlockIndex: blockIndex,
                UpdatedAt: now);

            await this.Context.FeedReadPositions.AddAsync(newEntity);
            return true;
        }

        // "Max wins" semantics: only update if new value is greater
        if (blockIndex.Value > existing.LastReadBlockIndex.Value)
        {
            // Since FeedReadPositionEntity is a record (immutable), we remove and add
            // to update the values. This approach works with both PostgreSQL and in-memory.
            this.Context.FeedReadPositions.Remove(existing);
            var updatedEntity = new FeedReadPositionEntity(
                UserId: userId,
                FeedId: feedId,
                LastReadBlockIndex: blockIndex,
                UpdatedAt: now);
            await this.Context.FeedReadPositions.AddAsync(updatedEntity);
            return true;
        }

        // New value is not greater, no change made
        return false;
    }
}
