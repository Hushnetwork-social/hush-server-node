using System.Text.Json;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Caching;

/// <summary>
/// Service for caching feed messages in Redis.
/// Implements write-through and cache-aside patterns for FEAT-046.
/// </summary>
public class FeedMessageCacheService : IFeedMessageCacheService
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<FeedMessageCacheService> _logger;

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
    /// Creates a new instance of FeedMessageCacheService.
    /// </summary>
    /// <param name="connectionMultiplexer">Redis connection multiplexer.</param>
    /// <param name="keyPrefix">Prefix for all Redis keys (e.g., "HushFeeds:").</param>
    /// <param name="logger">Logger instance.</param>
    public FeedMessageCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        ILogger<FeedMessageCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AddMessageAsync(FeedId feedId, FeedMessage message)
    {
        var key = GetKey(feedId);

        try
        {
            var serialized = SerializeMessage(message);

            // Atomic: LPUSH (prepend to list) + LTRIM (keep last N) + EXPIRE (set TTL)
            // Messages are stored newest-first (LPUSH prepends)
            var transaction = _database.CreateTransaction();

            _ = transaction.ListLeftPushAsync(key, serialized);
            _ = transaction.ListTrimAsync(key, 0, FeedMessageCacheConstants.MaxMessagesPerFeed - 1);
            _ = transaction.KeyExpireAsync(key, FeedMessageCacheConstants.CacheTtl);

            await transaction.ExecuteAsync();

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Added message {MessageId} to cache for feed {FeedId}",
                message.FeedMessageId,
                feedId);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to add message {MessageId} to cache for feed {FeedId}. Continuing without cache.",
                message.FeedMessageId,
                feedId);
            // Log and continue - cache failure should not break the write path
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<FeedMessage>?> GetMessagesAsync(FeedId feedId, BlockIndex? sinceBlockIndex = null)
    {
        var key = GetKey(feedId);

        try
        {
            // Check if key exists (cache miss detection)
            if (!await _database.KeyExistsAsync(key))
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug("Cache miss for feed {FeedId}", feedId);
                return null;
            }

            // Get all cached messages (LRANGE 0 -1)
            var values = await _database.ListRangeAsync(key, 0, -1);

            if (values.Length == 0)
            {
                Interlocked.Increment(ref _cacheHits);
                _logger.LogDebug("Cache hit for feed {FeedId} - empty list", feedId);
                return Enumerable.Empty<FeedMessage>();
            }

            // Deserialize messages
            var messages = new List<FeedMessage>(values.Length);
            foreach (var value in values)
            {
                var message = DeserializeMessage(value!);
                if (message != null)
                {
                    messages.Add(message);
                }
            }

            // Server-side filtering by BlockIndex if requested
            IEnumerable<FeedMessage> result = messages;
            if (sinceBlockIndex != null)
            {
                result = messages.Where(m => m.BlockIndex > sinceBlockIndex);
            }

            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug(
                "Cache hit for feed {FeedId} - {MessageCount} messages (filtered from {TotalCount})",
                feedId,
                result.Count(),
                messages.Count);

            return result;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to read messages from cache for feed {FeedId}. Returning null (cache miss).",
                feedId);
            // Return null on error - caller should fall back to database
            return null;
        }
    }

    /// <inheritdoc />
    public async Task InvalidateCacheAsync(FeedId feedId)
    {
        var key = GetKey(feedId);

        try
        {
            await _database.KeyDeleteAsync(key);
            _logger.LogDebug("Invalidated cache for feed {FeedId}", feedId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to invalidate cache for feed {FeedId}. Cache may contain stale data.",
                feedId);
            // Log and continue - invalidation failure is not critical
        }
    }

    /// <inheritdoc />
    public async Task PopulateCacheAsync(FeedId feedId, IEnumerable<FeedMessage> messages)
    {
        var key = GetKey(feedId);
        var messageList = messages.ToList();

        if (messageList.Count == 0)
        {
            _logger.LogDebug("No messages to populate cache for feed {FeedId}", feedId);
            return;
        }

        try
        {
            // Messages should be ordered oldest-to-newest for RPUSH
            // This maintains the same order as LPUSH (newest at index 0)
            var orderedMessages = messageList
                .OrderBy(m => m.BlockIndex.Value)
                .ThenBy(m => m.Timestamp.Value)
                .ToList();

            // Serialize all messages
            var serialized = orderedMessages
                .Select(m => (RedisValue)SerializeMessage(m))
                .ToArray();

            // Atomic: DEL (clean slate) + RPUSH (add all) + LTRIM (ensure max) + EXPIRE (set TTL)
            var transaction = _database.CreateTransaction();

            _ = transaction.KeyDeleteAsync(key);
            _ = transaction.ListRightPushAsync(key, serialized);
            _ = transaction.ListTrimAsync(key, -FeedMessageCacheConstants.MaxMessagesPerFeed, -1);
            _ = transaction.KeyExpireAsync(key, FeedMessageCacheConstants.CacheTtl);

            await transaction.ExecuteAsync();

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Populated cache for feed {FeedId} with {MessageCount} messages",
                feedId,
                messageList.Count);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to populate cache for feed {FeedId}. Cache will remain empty.",
                feedId);
            // Log and continue - population failure should not break the read path
        }
    }

    /// <summary>
    /// Gets the Redis key for a feed's message cache, including the instance prefix.
    /// </summary>
    private string GetKey(FeedId feedId)
    {
        return $"{_keyPrefix}{FeedMessageCacheConstants.GetFeedMessagesKey(feedId)}";
    }

    /// <summary>
    /// Serializes a FeedMessage to JSON string.
    /// </summary>
    private static string SerializeMessage(FeedMessage message)
    {
        return JsonSerializer.Serialize(message);
    }

    /// <summary>
    /// Deserializes a JSON string to FeedMessage.
    /// </summary>
    private FeedMessage? DeserializeMessage(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<FeedMessage>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize FeedMessage from cache: {Json}", json);
            return null;
        }
    }
}
