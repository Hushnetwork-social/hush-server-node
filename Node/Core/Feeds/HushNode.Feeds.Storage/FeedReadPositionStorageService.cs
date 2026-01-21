using HushNode.Caching;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.Storage;

/// <summary>
/// Service for managing user read positions in feeds.
/// Orchestrates cache and repository operations using cache-aside (read) and write-through (write) patterns.
/// </summary>
public class FeedReadPositionStorageService(
    IUnitOfWorkProvider<FeedsDbContext> unitOfWorkProvider,
    IFeedReadPositionCacheService cacheService,
    ILogger<FeedReadPositionStorageService> logger)
    : IFeedReadPositionStorageService
{
    private readonly IUnitOfWorkProvider<FeedsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IFeedReadPositionCacheService _cacheService = cacheService;
    private readonly ILogger<FeedReadPositionStorageService> _logger = logger;

    /// <summary>
    /// Default value for unread feeds.
    /// </summary>
    private static readonly BlockIndex DefaultReadPosition = new(0);

    /// <inheritdoc />
    public async Task<BlockIndex> GetReadPositionAsync(string userId, FeedId feedId)
    {
        if (string.IsNullOrEmpty(userId))
            return DefaultReadPosition;

        // Step 1: Try cache first (cache-aside pattern)
        var cachedValue = await this._cacheService.GetReadPositionAsync(userId, feedId);
        if (cachedValue != null)
        {
            this._logger.LogDebug(
                "Read position cache hit for user={UserId} feed={FeedId} value={BlockIndex}",
                userId,
                feedId,
                cachedValue.Value);
            return cachedValue;
        }

        // Step 2: Cache miss - query database
        var dbValue = await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedReadPositionRepository>()
            .GetReadPositionAsync(userId, feedId);

        if (dbValue == null)
        {
            this._logger.LogDebug(
                "No read position found for user={UserId} feed={FeedId}, returning default 0",
                userId,
                feedId);
            return DefaultReadPosition;
        }

        // Step 3: Populate cache for future reads (fire and forget, graceful degradation)
        _ = this._cacheService.SetReadPositionAsync(userId, feedId, dbValue);

        this._logger.LogDebug(
            "Read position from database for user={UserId} feed={FeedId} value={BlockIndex}",
            userId,
            feedId,
            dbValue.Value);

        return dbValue;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<FeedId, BlockIndex>> GetReadPositionsForUserAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return new Dictionary<FeedId, BlockIndex>();

        // Step 1: Try cache first
        var cachedPositions = await this._cacheService.GetReadPositionsForUserAsync(userId);
        if (cachedPositions != null && cachedPositions.Count > 0)
        {
            this._logger.LogDebug(
                "Read positions cache hit for user={UserId}, count={Count}",
                userId,
                cachedPositions.Count);
            return cachedPositions;
        }

        // Step 2: Cache miss - query database
        var dbPositions = await this._unitOfWorkProvider
            .CreateReadOnly()
            .GetRepository<IFeedReadPositionRepository>()
            .GetReadPositionsForUserAsync(userId);

        this._logger.LogDebug(
            "Read positions from database for user={UserId}, count={Count}",
            userId,
            dbPositions.Count);

        // Step 3: Populate cache for individual positions
        foreach (var (feedId, blockIndex) in dbPositions)
        {
            _ = this._cacheService.SetReadPositionAsync(userId, feedId, blockIndex);
        }

        return dbPositions;
    }

    /// <inheritdoc />
    public async Task<bool> MarkFeedAsReadAsync(string userId, FeedId feedId, BlockIndex blockIndex)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        // Step 1: Write to database first (source of truth)
        using var writableUnitOfWork = this._unitOfWorkProvider.CreateWritable();
        var repository = writableUnitOfWork.GetRepository<IFeedReadPositionRepository>();

        var updated = await repository.UpsertReadPositionAsync(userId, feedId, blockIndex);
        await writableUnitOfWork.CommitAsync();

        if (updated)
        {
            this._logger.LogDebug(
                "Marked feed as read in database: user={UserId} feed={FeedId} blockIndex={BlockIndex}",
                userId,
                feedId,
                blockIndex.Value);

            // Step 2: Update cache (write-through, graceful degradation)
            var cacheUpdated = await this._cacheService.SetReadPositionAsync(userId, feedId, blockIndex);
            if (!cacheUpdated)
            {
                this._logger.LogWarning(
                    "Failed to update read position cache for user={UserId} feed={FeedId}. Database is consistent.",
                    userId,
                    feedId);
            }
        }
        else
        {
            this._logger.LogDebug(
                "Read position not updated (max wins): user={UserId} feed={FeedId} blockIndex={BlockIndex}",
                userId,
                feedId,
                blockIndex.Value);
        }

        return updated;
    }
}
