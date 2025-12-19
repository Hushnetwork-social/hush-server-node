namespace HushNode.Notifications;

/// <summary>
/// Service for tracking unread message counts per user per feed.
/// Uses Redis for fast O(1) operations.
/// </summary>
public interface IUnreadTrackingService
{
    /// <summary>
    /// Increment unread count for a user's feed.
    /// Called when a new message is received.
    /// </summary>
    /// <param name="userId">The recipient user ID.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>The new unread count after incrementing.</returns>
    Task<int> IncrementUnreadAsync(string userId, string feedId);

    /// <summary>
    /// Mark all messages in a feed as read (reset count to 0).
    /// Called when user clicks/selects a feed.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="feedId">The feed ID.</param>
    Task MarkFeedAsReadAsync(string userId, string feedId);

    /// <summary>
    /// Get unread counts for all feeds of a user.
    /// Used on client reconnect to sync all badges.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>Dictionary of feedId -> unread count.</returns>
    Task<Dictionary<string, int>> GetUnreadCountsAsync(string userId);

    /// <summary>
    /// Get unread count for a specific feed.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <returns>The unread count (0 if none).</returns>
    Task<int> GetUnreadCountAsync(string userId, string feedId);
}
