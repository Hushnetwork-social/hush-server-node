namespace HushNode.Notifications.Models;

/// <summary>
/// Types of events that can be pushed to clients via the notification stream.
/// </summary>
public enum EventType
{
    /// <summary>
    /// A new message arrived in a feed.
    /// </summary>
    NewMessage,

    /// <summary>
    /// Messages were marked as read (from another device).
    /// </summary>
    MessagesRead,

    /// <summary>
    /// Full sync of unread counts (sent on reconnect).
    /// Does NOT trigger notifications - only updates badges.
    /// </summary>
    UnreadCountSync
}
