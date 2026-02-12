using System.Text.Json;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Caching;

/// <summary>
/// Service for caching per-user feed metadata in Redis (FEAT-060).
/// Currently handles lastBlockIndex only. FEAT-065 will extend with full metadata.
/// Implements graceful degradation: cache failures return null/false and are logged.
/// </summary>
public class FeedMetadataCacheService : IFeedMetadataCacheService
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<FeedMetadataCacheService> _logger;

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

    public FeedMetadataCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        ILogger<FeedMetadataCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<FeedId, BlockIndex>?> GetAllLastBlockIndexesAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        var key = GetKey(userId);

        try
        {
            var entries = await _database.HashGetAllAsync(key);

            if (entries.Length == 0)
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug(
                    "Cache miss for feed metadata hash user={UserId}",
                    userId);
                return null;
            }

            var result = new Dictionary<FeedId, BlockIndex>();
            foreach (var entry in entries)
            {
                var lastBlockIndex = ParseLastBlockIndex(entry.Value);
                if (lastBlockIndex.HasValue)
                {
                    var feedId = FeedIdHandler.CreateFromString(entry.Name!);
                    result[feedId] = new BlockIndex(lastBlockIndex.Value);
                }
            }

            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug(
                "Cache hit for feed metadata hash user={UserId} count={Count}",
                userId,
                result.Count);
            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to read feed metadata hash for user={UserId}. Returning null.",
                userId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetLastBlockIndexAsync(string userId, FeedId feedId, BlockIndex lastBlockIndex)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        var key = GetKey(userId);

        try
        {
            var json = SerializeLastBlockIndex(lastBlockIndex.Value);
            await _database.HashSetAsync(key, feedId.ToString(), json);
            await _database.KeyExpireAsync(key, FeedMetadataCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Set feed metadata user={UserId} feed={FeedId} lastBlockIndex={LastBlockIndex}",
                userId,
                feedId,
                lastBlockIndex.Value);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to set feed metadata for user={UserId} feed={FeedId}. Continuing without cache.",
                userId,
                feedId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetMultipleLastBlockIndexesAsync(string userId, IReadOnlyDictionary<FeedId, BlockIndex> blockIndexes)
    {
        if (string.IsNullOrEmpty(userId) || blockIndexes == null || blockIndexes.Count == 0)
            return false;

        var key = GetKey(userId);

        try
        {
            var entries = blockIndexes
                .Select(p => new HashEntry(p.Key.ToString(), SerializeLastBlockIndex(p.Value.Value)))
                .ToArray();

            await _database.HashSetAsync(key, entries);
            await _database.KeyExpireAsync(key, FeedMetadataCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Bulk set feed metadata hash user={UserId} count={Count}",
                userId,
                blockIndexes.Count);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to bulk set feed metadata for user={UserId}. Continuing without cache.",
                userId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveFeedMetaAsync(string userId, FeedId feedId)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        var key = GetKey(userId);

        try
        {
            var removed = await _database.HashDeleteAsync(key, feedId.ToString());
            _logger.LogDebug(
                "Removed feed metadata user={UserId} feed={FeedId} removed={Removed}",
                userId,
                feedId,
                removed);
            return removed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to remove feed metadata for user={UserId} feed={FeedId}. Continuing without cache.",
                userId,
                feedId);
            return false;
        }
    }

    /// <summary>
    /// Gets the Redis HASH key for a user's feed metadata, including the instance prefix.
    /// </summary>
    private string GetKey(string userId)
    {
        return $"{_keyPrefix}{FeedMetadataCacheConstants.GetFeedMetaHashKey(userId)}";
    }

    /// <summary>
    /// Serializes a lastBlockIndex value to JSON format: {"lastBlockIndex": N}.
    /// </summary>
    private static string SerializeLastBlockIndex(long lastBlockIndex)
    {
        return JsonSerializer.Serialize(new { lastBlockIndex });
    }

    /// <summary>
    /// Parses a lastBlockIndex from JSON format: {"lastBlockIndex": N}.
    /// Returns null if parsing fails.
    /// </summary>
    private static long? ParseLastBlockIndex(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("lastBlockIndex", out var prop) && prop.TryGetInt64(out var value))
                return value;
            return null;
        }
        catch
        {
            return null;
        }
    }
}
