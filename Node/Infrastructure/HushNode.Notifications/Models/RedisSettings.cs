namespace HushNode.Notifications.Models;

/// <summary>
/// Configuration settings for Redis connection.
/// </summary>
public class RedisSettings
{
    /// <summary>
    /// Configuration section name in appsettings.
    /// </summary>
    public const string SectionName = "Redis";

    /// <summary>
    /// Redis connection string (e.g., "localhost:6379" or "redis:6379,password=xxx").
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Prefix for all Redis keys (e.g., "HushFeeds:").
    /// </summary>
    public string InstanceName { get; set; } = "HushFeeds:";

    /// <summary>
    /// Number of times to retry connecting to Redis.
    /// </summary>
    public int ConnectRetry { get; set; } = 3;

    /// <summary>
    /// Connection timeout in milliseconds.
    /// </summary>
    public int ConnectTimeout { get; set; } = 5000;

    /// <summary>
    /// Sync operation timeout in milliseconds.
    /// </summary>
    public int SyncTimeout { get; set; } = 5000;

    /// <summary>
    /// Whether Redis is enabled. If false, a no-op implementation is used.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
