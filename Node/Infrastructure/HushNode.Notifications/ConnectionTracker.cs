using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HushNode.Notifications;

/// <summary>
/// Redis-backed implementation of connection tracking.
/// Uses Redis SETs to store connection IDs per user, with TTL for automatic expiry.
/// </summary>
public class ConnectionTracker : IConnectionTracker
{
    private readonly RedisConnectionManager _redis;
    private readonly ILogger<ConnectionTracker> _logger;

    /// <summary>
    /// TTL for connection keys in seconds (5 minutes).
    /// Connections auto-expire if not refreshed, handling crashed clients.
    /// </summary>
    private static readonly TimeSpan ConnectionTtl = TimeSpan.FromMinutes(5);

    public ConnectionTracker(
        RedisConnectionManager redis,
        ILogger<ConnectionTracker> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task MarkOnlineAsync(string userId, string connectionId)
    {
        try
        {
            var key = _redis.GetConnectionsKey(userId);

            // Add connection ID to the SET
            await _redis.Database.SetAddAsync(key, connectionId);

            // Refresh TTL on every connection (handles multi-device scenarios)
            await _redis.Database.KeyExpireAsync(key, ConnectionTtl);

            _logger.LogDebug(
                "Marked user {UserId} online with connection {ConnectionId}",
                userId, connectionId);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while marking user online");
        }
    }

    /// <inheritdoc />
    public async Task MarkOfflineAsync(string userId, string connectionId)
    {
        try
        {
            var key = _redis.GetConnectionsKey(userId);

            // Remove connection ID from the SET
            await _redis.Database.SetRemoveAsync(key, connectionId);

            _logger.LogDebug(
                "Marked connection {ConnectionId} offline for user {UserId}",
                connectionId, userId);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while marking user offline");
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsUserOnlineAsync(string userId)
    {
        try
        {
            var key = _redis.GetConnectionsKey(userId);

            // SCARD returns the number of members in the set
            var count = await _redis.Database.SetLengthAsync(key);

            return count > 0;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while checking user online status");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetOnlineUserCountAsync()
    {
        try
        {
            var pattern = _redis.GetConnectionsPattern();
            var server = GetServer();

            if (server == null)
            {
                _logger.LogWarning("No Redis server available for SCAN operation");
                return 0;
            }

            var count = 0;

            // Use SCAN to find all connection keys
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                // Only count keys that have at least one member
                var memberCount = await _redis.Database.SetLengthAsync(key);
                if (memberCount > 0)
                {
                    count++;
                }
            }

            _logger.LogDebug("Found {Count} online users", count);
            return count;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis connection error while getting online user count");
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
}
