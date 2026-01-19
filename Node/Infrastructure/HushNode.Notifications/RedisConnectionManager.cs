using HushNode.Notifications.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HushNode.Notifications;

/// <summary>
/// Manages the Redis connection and provides access to Redis database and pub/sub.
/// Implements the singleton pattern via DI registration.
/// </summary>
public class RedisConnectionManager : IDisposable
{
    private readonly ILogger<RedisConnectionManager> _logger;
    private readonly RedisSettings _settings;
    private readonly Lazy<ConnectionMultiplexer> _connection;
    private bool _disposed;

    public RedisConnectionManager(
        IOptions<RedisSettings> settings,
        ILogger<RedisConnectionManager> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _connection = new Lazy<ConnectionMultiplexer>(CreateConnection);
    }

    /// <summary>
    /// Gets the underlying connection multiplexer for advanced operations.
    /// </summary>
    public virtual IConnectionMultiplexer Connection => _connection.Value;

    /// <summary>
    /// Gets the Redis database for key-value operations.
    /// </summary>
    public virtual IDatabase Database => _connection.Value.GetDatabase();

    /// <summary>
    /// Gets the Redis subscriber for pub/sub operations.
    /// </summary>
    public virtual ISubscriber Subscriber => _connection.Value.GetSubscriber();

    /// <summary>
    /// Gets the key prefix for all Redis keys.
    /// </summary>
    public virtual string KeyPrefix => _settings.InstanceName;

    /// <summary>
    /// Whether Redis is connected and available.
    /// </summary>
    public bool IsConnected => _connection.IsValueCreated && _connection.Value.IsConnected;

    /// <summary>
    /// Gets the Redis channel for user events.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>The Redis channel for this user's events.</returns>
    public RedisChannel GetUserChannel(string userId)
        => new($"{_settings.InstanceName}user:{userId}:events", RedisChannel.PatternMode.Literal);

    /// <summary>
    /// Gets the Redis key for unread count.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>The Redis key for this unread count.</returns>
    public virtual string GetUnreadKey(string userId, string feedId)
        => $"{_settings.InstanceName}unread:{userId}:{feedId}";

    /// <summary>
    /// Gets the pattern for scanning all unread keys for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>The Redis key pattern.</returns>
    public virtual string GetUnreadPattern(string userId)
        => $"{_settings.InstanceName}unread:{userId}:*";

    /// <summary>
    /// Gets the Redis key for user connections (SET of connection IDs).
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>The Redis key for this user's connections.</returns>
    public virtual string GetConnectionsKey(string userId)
        => $"{_settings.InstanceName}connections:{userId}";

    /// <summary>
    /// Gets the pattern for scanning all connection keys.
    /// </summary>
    /// <returns>The Redis key pattern for all connection keys.</returns>
    public virtual string GetConnectionsPattern()
        => $"{_settings.InstanceName}connections:*";

    private ConnectionMultiplexer CreateConnection()
    {
        _logger.LogInformation("Connecting to Redis at {ConnectionString}", _settings.ConnectionString);

        var options = ConfigurationOptions.Parse(_settings.ConnectionString);
        options.ConnectRetry = _settings.ConnectRetry;
        options.ConnectTimeout = _settings.ConnectTimeout;
        options.SyncTimeout = _settings.SyncTimeout;
        options.AbortOnConnectFail = false;

        var connection = ConnectionMultiplexer.Connect(options);

        connection.ConnectionFailed += (sender, args) =>
        {
            _logger.LogWarning(
                "Redis connection failed: {FailureType} - {Exception}",
                args.FailureType,
                args.Exception?.Message);
        };

        connection.ConnectionRestored += (sender, args) =>
        {
            _logger.LogInformation("Redis connection restored");
        };

        _logger.LogInformation("Connected to Redis successfully");
        return connection;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection.IsValueCreated)
        {
            _connection.Value.Dispose();
            _logger.LogInformation("Redis connection disposed");
        }
    }
}
