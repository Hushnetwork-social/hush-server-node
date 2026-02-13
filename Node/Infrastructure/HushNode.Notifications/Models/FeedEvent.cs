namespace HushNode.Notifications.Models;

/// <summary>
/// Represents an event pushed to clients via the notification stream.
/// </summary>
public class FeedEvent
{
    /// <summary>
    /// The type of event.
    /// </summary>
    public EventType Type { get; set; }

    /// <summary>
    /// The feed this event relates to.
    /// </summary>
    public string FeedId { get; set; } = string.Empty;

    /// <summary>
    /// For NewMessage events: the sender's display name.
    /// </summary>
    public string? SenderName { get; set; }

    /// <summary>
    /// For NewMessage events: message preview (max 255 chars).
    /// </summary>
    public string? MessagePreview { get; set; }

    /// <summary>
    /// Current unread count for this feed.
    /// </summary>
    public int? UnreadCount { get; set; }

    /// <summary>
    /// For UnreadCountSync events: all unread counts (feedId -> count).
    /// </summary>
    public Dictionary<string, int>? AllCounts { get; set; }

    /// <summary>
    /// UTC timestamp when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// FEAT-063: For MessagesRead events - block index up to which user has read.
    /// </summary>
    public long UpToBlockIndex { get; set; }
}
