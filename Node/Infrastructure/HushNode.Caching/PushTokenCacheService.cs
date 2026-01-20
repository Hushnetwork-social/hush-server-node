using System.Text.Json;
using HushNode.Interfaces.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Caching;

/// <summary>
/// Service for caching push notification device tokens in Redis.
/// Implements write-through and cache-aside patterns for FEAT-047.
/// Uses Redis HASH to store multiple tokens per user.
/// </summary>
public class PushTokenCacheService : IPushTokenCacheService
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly ILogger<PushTokenCacheService> _logger;

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
    /// Creates a new instance of PushTokenCacheService.
    /// </summary>
    /// <param name="connectionMultiplexer">Redis connection multiplexer.</param>
    /// <param name="keyPrefix">Prefix for all Redis keys (e.g., "HushFeeds:").</param>
    /// <param name="logger">Logger instance.</param>
    public PushTokenCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        string keyPrefix,
        ILogger<PushTokenCacheService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<DeviceToken>?> GetTokensAsync(string userId)
    {
        var key = GetKey(userId);

        try
        {
            // Check if key exists (cache miss detection)
            if (!await _database.KeyExistsAsync(key))
            {
                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug("Cache miss for user {UserId}", TruncateUserId(userId));
                return null;
            }

            // Get all tokens from HASH (HGETALL)
            var entries = await _database.HashGetAllAsync(key);

            if (entries.Length == 0)
            {
                Interlocked.Increment(ref _cacheHits);
                _logger.LogDebug("Cache hit for user {UserId} - empty hash", TruncateUserId(userId));
                return Enumerable.Empty<DeviceToken>();
            }

            // Deserialize tokens
            var tokens = new List<DeviceToken>(entries.Length);
            foreach (var entry in entries)
            {
                var token = DeserializeToken(entry.Value!);
                if (token != null)
                {
                    tokens.Add(token);
                }
            }

            Interlocked.Increment(ref _cacheHits);
            _logger.LogDebug(
                "Cache hit for user {UserId} - {TokenCount} tokens",
                TruncateUserId(userId),
                tokens.Count);

            return tokens;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _readErrors);
            _logger.LogWarning(
                ex,
                "Failed to read tokens from cache for user {UserId}. Returning null (cache miss).",
                TruncateUserId(userId));
            // Return null on error - caller should fall back to database
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetTokensAsync(string userId, IEnumerable<DeviceToken> tokens)
    {
        var tokenList = tokens.ToList();

        if (tokenList.Count == 0)
        {
            _logger.LogDebug("No tokens to cache for user {UserId}", TruncateUserId(userId));
            return;
        }

        var key = GetKey(userId);

        try
        {
            // Prepare hash entries: field = tokenId, value = serialized JSON
            var hashEntries = tokenList
                .Select(t => new HashEntry(t.Id, SerializeToken(t)))
                .ToArray();

            // Atomic: DEL (clean slate) + HSET (add all) + EXPIRE (set TTL)
            var transaction = _database.CreateTransaction();

            _ = transaction.KeyDeleteAsync(key);
            _ = transaction.HashSetAsync(key, hashEntries);
            _ = transaction.KeyExpireAsync(key, PushTokenCacheConstants.CacheTtl);

            await transaction.ExecuteAsync();

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Set {TokenCount} tokens in cache for user {UserId}",
                tokenList.Count,
                TruncateUserId(userId));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to set tokens in cache for user {UserId}. Continuing without cache.",
                TruncateUserId(userId));
            // Log and continue - cache failure should not break the write path
        }
    }

    /// <inheritdoc />
    public async Task AddOrUpdateTokenAsync(string userId, DeviceToken token)
    {
        var key = GetKey(userId);

        try
        {
            var serialized = SerializeToken(token);

            // Atomic: HSET (add/update single token) + EXPIRE (refresh TTL)
            var transaction = _database.CreateTransaction();

            _ = transaction.HashSetAsync(key, token.Id, serialized);
            _ = transaction.KeyExpireAsync(key, PushTokenCacheConstants.CacheTtl);

            await transaction.ExecuteAsync();

            Interlocked.Increment(ref _writeOperations);
            _logger.LogDebug(
                "Added/updated token {TokenId} in cache for user {UserId}",
                token.Id,
                TruncateUserId(userId));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _writeErrors);
            _logger.LogWarning(
                ex,
                "Failed to add/update token {TokenId} in cache for user {UserId}. Continuing without cache.",
                token.Id,
                TruncateUserId(userId));
            // Log and continue - cache failure should not break the write path
        }
    }

    /// <inheritdoc />
    public async Task RemoveTokenAsync(string userId, string tokenId)
    {
        var key = GetKey(userId);

        try
        {
            // HDEL removes a single field from the hash
            await _database.HashDeleteAsync(key, tokenId);

            _logger.LogDebug(
                "Removed token {TokenId} from cache for user {UserId}",
                tokenId,
                TruncateUserId(userId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to remove token {TokenId} from cache for user {UserId}.",
                tokenId,
                TruncateUserId(userId));
            // Log and continue - removal failure is not critical
        }
    }

    /// <inheritdoc />
    public async Task InvalidateUserCacheAsync(string userId)
    {
        var key = GetKey(userId);

        try
        {
            await _database.KeyDeleteAsync(key);
            _logger.LogDebug("Invalidated cache for user {UserId}", TruncateUserId(userId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to invalidate cache for user {UserId}. Cache may contain stale data.",
                TruncateUserId(userId));
            // Log and continue - invalidation failure is not critical
        }
    }

    /// <summary>
    /// Gets the Redis key for a user's push tokens cache, including the instance prefix.
    /// </summary>
    private string GetKey(string userId)
    {
        return $"{_keyPrefix}{PushTokenCacheConstants.GetUserKey(userId)}";
    }

    /// <summary>
    /// Serializes a DeviceToken to JSON string.
    /// </summary>
    private static string SerializeToken(DeviceToken token)
    {
        return JsonSerializer.Serialize(token);
    }

    /// <summary>
    /// Deserializes a JSON string to DeviceToken.
    /// </summary>
    private DeviceToken? DeserializeToken(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DeviceToken>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize DeviceToken from cache: {Json}", json);
            return null;
        }
    }

    /// <summary>
    /// Truncates userId for logging (privacy).
    /// </summary>
    private static string TruncateUserId(string userId)
    {
        return userId.Length > 20 ? userId.Substring(0, 20) + "..." : userId;
    }
}
