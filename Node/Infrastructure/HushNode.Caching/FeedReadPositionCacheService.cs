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
    private long _migrationOperations;

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
    /// Gets the total number of migration operations (old STRING keys → HASH).
    /// </summary>
    public long MigrationOperations => Interlocked.Read(ref _migrationOperations);

    /// <summary>
    /// Lua script for atomic max-wins compare-and-set on HASH fields.
    /// Only updates the field if the new value is greater than the current value.
    /// </summary>
    private const string MaxWinsLuaScript = @"
local current = tonumber(redis.call('HGET', KEYS[1], ARGV[1]))
local new = tonumber(ARGV[2])
if current == nil or new > current then
    redis.call('HSET', KEYS[1], ARGV[1], ARGV[2])
    return 1
end
return 0";

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

        // Step 1: Try new HASH first (FEAT-060)
        var hashResult = await GetAllReadPositionsAsync(userId);
        if (hashResult != null && hashResult.Count > 0)
            return hashResult;

        // Step 2: Try SCAN old individual STRING keys (migration fallback)
        var pattern = $"{_keyPrefix}{FeedReadPositionCacheConstants.GetReadPositionScanPattern(userId)}";

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

            if (result.Count > 0)
            {
                // Step 3: Migrate old keys to new HASH
                _logger.LogInformation(
                    "Migrating {Count} read positions from individual keys to HASH for user={UserId}",
                    result.Count,
                    userId);
                await SetAllReadPositionsAsync(userId, result);
                Interlocked.Increment(ref _migrationOperations);
                return result;
            }

            // Step 4: No old keys found — cache miss (caller falls back to PostgreSQL)
            _logger.LogDebug(
                "No legacy read position keys found for user={UserId}",
                userId);
            return null;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to scan legacy read positions for user={UserId}. Returning null.",
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

    // --- FEAT-060: HASH-based methods ---

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<FeedId, BlockIndex>?> GetAllReadPositionsAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        var key = GetHashKey(userId);

        try
        {
            var entries = await _database.HashGetAllAsync(key);

            if (entries.Length == 0)
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug(
                    "Cache miss for read positions hash user={UserId}",
                    userId);
                return null;
            }

            var result = new Dictionary<FeedId, BlockIndex>();
            foreach (var entry in entries)
            {
                if (long.TryParse(entry.Value, out var blockIndexValue))
                {
                    var feedId = FeedIdHandler.CreateFromString(entry.Name!);
                    result[feedId] = new BlockIndex(blockIndexValue);
                }
            }

            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug(
                "Cache hit for read positions hash user={UserId} count={Count}",
                userId,
                result.Count);
            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to read read positions hash for user={UserId}. Returning null.",
                userId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetReadPositionWithMaxWinsAsync(string userId, FeedId feedId, BlockIndex blockIndex)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        var key = GetHashKey(userId);

        try
        {
            var result = await _database.ScriptEvaluateAsync(
                MaxWinsLuaScript,
                new RedisKey[] { key },
                new RedisValue[] { feedId.ToString(), blockIndex.Value.ToString() });

            var updated = (int)result == 1;

            if (updated)
            {
                // Refresh TTL on successful write
                await _database.KeyExpireAsync(key, FeedReadPositionCacheConstants.CacheTtl);
            }

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Max-wins set read position user={UserId} feed={FeedId} value={BlockIndex} updated={Updated}",
                userId,
                feedId,
                blockIndex.Value,
                updated);
            return updated;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to set read position with max-wins for user={UserId} feed={FeedId}. Continuing without cache.",
                userId,
                feedId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetAllReadPositionsAsync(string userId, IReadOnlyDictionary<FeedId, BlockIndex> positions)
    {
        if (string.IsNullOrEmpty(userId) || positions == null || positions.Count == 0)
            return false;

        var key = GetHashKey(userId);

        try
        {
            var entries = positions
                .Select(p => new HashEntry(p.Key.ToString(), p.Value.Value.ToString()))
                .ToArray();

            await _database.HashSetAsync(key, entries);
            await _database.KeyExpireAsync(key, FeedReadPositionCacheConstants.CacheTtl);

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Bulk set read positions hash user={UserId} count={Count}",
                userId,
                positions.Count);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to bulk set read positions for user={UserId}. Continuing without cache.",
                userId);
            return false;
        }
    }

    /// <summary>
    /// Gets the Redis key for a user's read position (legacy individual STRING key), including the instance prefix.
    /// </summary>
    private string GetKey(string userId, FeedId feedId)
    {
        return $"{_keyPrefix}{FeedReadPositionCacheConstants.GetReadPositionKey(userId, feedId)}";
    }

    /// <summary>
    /// Gets the Redis HASH key for a user's read positions, including the instance prefix.
    /// </summary>
    private string GetHashKey(string userId)
    {
        return $"{_keyPrefix}{FeedReadPositionCacheConstants.GetReadPositionsHashKey(userId)}";
    }
}
