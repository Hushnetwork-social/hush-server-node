using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Caching;

/// <summary>
/// Service for caching user read positions in Redis.
/// Implements graceful degradation: cache failures return null/false and are logged.
/// </summary>
public class FeedReadPositionCacheService : IFeedReadPositionCacheService
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<FeedReadPositionCacheService> _logger;

    // Cache metrics (simple counters for observability)
    private long _cacheHits;
    private long _cacheMisses;
    private long _writeOperations;
    private long _writeErrors;
    private long _readErrors;

    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    public long CacheHits => Interlocked.Read(ref _cacheHits);

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    public long CacheMisses => Interlocked.Read(ref _cacheMisses);

    /// <summary>
    /// Gets the total number of write operations.
    /// </summary>
    public long WriteOperations => Interlocked.Read(ref _writeOperations);

    /// <summary>
    /// Gets the total number of write errors.
    /// </summary>
    public long WriteErrors => Interlocked.Read(ref _writeErrors);

    /// <summary>
    /// Gets the total number of read errors.
    /// </summary>
    public long ReadErrors => Interlocked.Read(ref _readErrors);

    /// <summary>
    /// Creates a new instance of FeedReadPositionCacheService.
    /// </summary>
    /// <param name="connectionMultiplexer">Redis connection multiplexer.</param>
    /// <param name="keyPrefix">Prefix for all Redis keys (e.g., "HushFeeds:").</param>
    /// <param name="logger">Logger instance.</param>
    public FeedReadPositionCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        ILogger<FeedReadPositionCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BlockIndex?> GetReadPositionAsync(string userId, FeedId feedId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        var key = GetKey(userId, feedId);

        try
        {
            var value = await _database.StringGetAsync(key);

            if (!value.HasValue)
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug(
                    "Cache miss for read position user={UserId} feed={FeedId}",
                    userId,
                    feedId);
                return null;
            }

            if (long.TryParse(value, out var blockIndexValue))
            {
                Interlocked.Increment(ref _cacheHits);
                _logger.LogDebug(
                    "Cache hit for read position user={UserId} feed={FeedId} value={BlockIndex}",
                    userId,
                    feedId,
                    blockIndexValue);
                return new BlockIndex(blockIndexValue);
            }

            // Invalid value in cache - treat as miss
            _logger.LogWarning(
                "Invalid value in cache for read position user={UserId} feed={FeedId} value={Value}",
                userId,
                feedId,
                value);
            Interlocked.Increment(ref _cacheMisses);
            return null;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to read from cache for read position user={UserId} feed={FeedId}. Returning null (cache miss).",
                userId,
                feedId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<FeedId, BlockIndex>?> GetReadPositionsForUserAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        // Note: This uses SCAN which is O(n) on the Redis keyspace.
        // For production with many users, consider maintaining a separate set of feed IDs per user.
        var pattern = $"{_keyPrefix}user:{userId}:read:*";

        try
        {
            var result = new Dictionary<FeedId, BlockIndex>();
            var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());

            await foreach (var key in server.KeysAsync(database: _database.Database, pattern: pattern))
            {
                var value = await _database.StringGetAsync(key);
                if (value.HasValue && long.TryParse(value, out var blockIndexValue))
                {
                    // Extract feedId from key: {prefix}user:{userId}:read:{feedId}
                    var keyString = key.ToString();
                    var feedIdStart = keyString.LastIndexOf(":read:", StringComparison.Ordinal) + 6;
                    if (feedIdStart > 5 && feedIdStart < keyString.Length)
                    {
                        var feedIdString = keyString[feedIdStart..];
                        var feedId = FeedIdHandler.CreateFromString(feedIdString);
                        result[feedId] = new BlockIndex(blockIndexValue);
                    }
                }
            }

            _logger.LogDebug(
                "Retrieved {Count} cached read positions for user={UserId}",
                result.Count,
                userId);

            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to read cached read positions for user={UserId}. Returning null.",
                userId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetReadPositionAsync(string userId, FeedId feedId, BlockIndex blockIndex)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        var key = GetKey(userId, feedId);

        try
        {
            await _database.StringSetAsync(
                key,
                blockIndex.Value.ToString(),
                FeedReadPositionCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Set read position in cache user={UserId} feed={FeedId} value={BlockIndex}",
                userId,
                feedId,
                blockIndex.Value);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to set read position in cache user={UserId} feed={FeedId}. Continuing without cache.",
                userId,
                feedId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task InvalidateCacheAsync(string userId, FeedId feedId)
    {
        if (string.IsNullOrEmpty(userId))
            return;

        var key = GetKey(userId, feedId);

        try
        {
            await _database.KeyDeleteAsync(key);
            _logger.LogDebug(
                "Invalidated cache for read position user={UserId} feed={FeedId}",
                userId,
                feedId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to invalidate cache for read position user={UserId} feed={FeedId}. Cache may contain stale data.",
                userId,
                feedId);
        }
    }

    /// <summary>
    /// Gets the Redis key for a user's read position, including the instance prefix.
    /// </summary>
    private string GetKey(string userId, FeedId feedId)
    {
        return $"{_keyPrefix}{FeedReadPositionCacheConstants.GetReadPositionKey(userId, feedId)}";
    }
}
