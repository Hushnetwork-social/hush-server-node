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
    /// <param name="feedName">For group feeds: the group name. Null for 1:1 chats.</param>
    Task PublishNewMessageAsync(
        string recipientUserId,
        string feedId,
        string senderName,
        string messagePreview,
        string? feedName = null);

    /// <summary>
    /// Publish a "messages read" event to all connected devices.
    /// This updates badges on other devices when user reads on one device.
    /// FEAT-063: Includes upToBlockIndex so receiving devices can calculate remaining unreads.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="feedId">The feed ID that was marked as read.</param>
    /// <param name="upToBlockIndex">The block index up to which messages were read.</param>
    Task PublishMessagesReadAsync(string userId, string feedId, long upToBlockIndex);
}
