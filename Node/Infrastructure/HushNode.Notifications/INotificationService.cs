using HushNode.Notifications.Models;

namespace HushNode.Notifications;

/// <summary>
/// Service for publishing and subscribing to real-time notification events.
/// Uses Redis Pub/Sub for event distribution across multiple server instances.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Subscribe to real-time events for a user.
    /// Returns an IAsyncEnumerable that yields events as they occur.
    /// </summary>
    /// <param name="userId">The user ID to subscribe for.</param>
    /// <param name="cancellationToken">Cancellation token to stop the subscription.</param>
    /// <returns>Async enumerable of feed events.</returns>
    IAsyncEnumerable<FeedEvent> SubscribeToEventsAsync(
        string userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Publish a new message event to all connected devices of the recipient.
    /// This will trigger notifications on clients (except on reconnect sync).
    /// </summary>
    /// <param name="recipientUserId">The recipient user ID.</param>
    /// <param name="feedId">The feed ID.</param>
    /// <param name="senderName">Display name of the sender.</param>
    /// <param name="messagePreview">Preview of the message (max 255 chars).</param>
    Task PublishNewMessageAsync(
        string recipientUserId,
        string feedId,
        string senderName,
        string messagePreview);

    /// <summary>
    /// Publish a "messages read" event to all connected devices.
    /// This clears badges on other devices when user reads on one device.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="feedId">The feed ID that was marked as read.</param>
    Task PublishMessagesReadAsync(string userId, string feedId);
}
