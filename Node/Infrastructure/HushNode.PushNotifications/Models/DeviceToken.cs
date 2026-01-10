namespace HushNode.PushNotifications.Models;

/// <summary>
/// Represents a device token for push notifications.
/// Each user can have multiple device tokens (one per device).
/// </summary>
public class DeviceToken
{
    /// <summary>
    /// Unique identifier for the device token record (UUID/GUID as string).
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The user's public signing address (identity reference).
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The platform type (Android, iOS, Web).
    /// </summary>
    public PushPlatform Platform { get; set; } = PushPlatform.Unknown;

    /// <summary>
    /// The FCM device token (unique across all users).
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Optional human-readable device name (e.g., "John's Phone").
    /// </summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// Timestamp when the token was first registered.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the token was last used or updated.
    /// Used for stale token cleanup (tokens older than 60 days are deactivated).
    /// </summary>
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this token is currently active.
    /// Tokens are deactivated (not deleted) when unregistered or stale.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
