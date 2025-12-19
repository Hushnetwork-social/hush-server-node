using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Notifications;

/// <summary>
/// Redis-backed implementation of unread message tracking.
/// Uses Redis INCR/GET/SET for O(1) operations on unread counts.
/// </summary>
public class UnreadTrackingService : IUnreadTrackingService
{
    private readonly RedisConnectionManager _redis;
    private readonly ILogger<UnreadTrackingService> _logger;

    public UnreadTrackingService(
        RedisConnectionManager redis,
        ILogger<UnreadTrackingService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> IncrementUnreadAsync(string userId, string feedId)
    {
        try
        {
            var key = _redis.GetUnreadKey(userId, feedId);
            var newValue = await _redis.Database.StringIncrementAsync(key);

            _logger.LogDebug(
                "Incremented unread count for user {UserId}, feed {FeedId} to {Count}",
                userId, feedId, newValue);

            return (int)newValue;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while incrementing unread count");
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task MarkFeedAsReadAsync(string userId, string feedId)
    {
        try
        {
            var key = _redis.GetUnreadKey(userId, feedId);
            await _redis.Database.KeyDeleteAsync(key);

            _logger.LogDebug(
                "Marked feed as read for user {UserId}, feed {FeedId}",
                userId, feedId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while marking feed as read");
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, int>> GetUnreadCountsAsync(string userId)
    {
        var result = new Dictionary<string, int>();

        try
        {
            var pattern = _redis.GetUnreadPattern(userId);
            var server = GetServer();

            if (server == null)
            {
                _logger.LogWarning("No Redis server available for SCAN operation");
                return result;
            }

            // Use SCAN to find all unread keys for this user
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                var value = await _redis.Database.StringGetAsync(key);
                if (value.HasValue && int.TryParse(value, out var count) && count > 0)
                {
                    // Extract feedId from key: "HushFeeds:unread:{userId}:{feedId}"
                    var keyString = key.ToString();
                    var feedId = ExtractFeedId(keyString, userId);
                    if (!string.IsNullOrEmpty(feedId))
                    {
                        result[feedId] = count;
                    }
                }
            }

            _logger.LogDebug(
                "Retrieved {Count} unread counts for user {UserId}",
                result.Count, userId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while getting unread counts");
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<int> GetUnreadCountAsync(string userId, string feedId)
    {
        try
        {
            var key = _redis.GetUnreadKey(userId, feedId);
            var value = await _redis.Database.StringGetAsync(key);

            if (value.HasValue && int.TryParse(value, out var count))
            {
                return count;
            }

            return 0;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while getting unread count");
            return 0;
        }
    }

    private IServer? GetServer()
    {
        try
        {
            var endpoints = _redis.Database.Multiplexer.GetEndPoints();
            if (endpoints.Length > 0)
            {
                return _redis.Database.Multiplexer.GetServer(endpoints[0]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Redis server for SCAN");
        }

        return null;
    }

    private string? ExtractFeedId(string key, string userId)
    {
        // Key format: "{prefix}unread:{userId}:{feedId}"
        var expectedPrefix = $"{_redis.KeyPrefix}unread:{userId}:";
        if (key.StartsWith(expectedPrefix))
        {
            return key.Substring(expectedPrefix.Length);
        }

        return null;
    }
}
