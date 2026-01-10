using HushNode.Events;
using HushNode.Feeds.Storage;
using HushNode.Identity;
using HushNode.PushNotifications;
using HushNode.PushNotifications.Models;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Notifications;

/// <summary>
/// Handles feed message events and triggers notifications via Redis Pub/Sub or Push.
/// Subscribes to NewFeedMessageCreatedEvent using the EventAggregator pattern.
/// Routes notifications based on user online status:
/// - Online users: gRPC via Redis Pub/Sub
/// - Offline users: Push notifications via FCM
/// </summary>
public class NotificationEventHandler : IHandleAsync<NewFeedMessageCreatedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IUnreadTrackingService _unreadTrackingService;
    private readonly IFeedsStorageService _feedsStorageService;
    private readonly IIdentityService _identityService;
    private readonly IConnectionTracker _connectionTracker;
    private readonly IPushDeliveryService _pushDeliveryService;
    private readonly ILogger<NotificationEventHandler> _logger;

    public NotificationEventHandler(
        INotificationService notificationService,
        IUnreadTrackingService unreadTrackingService,
        IFeedsStorageService feedsStorageService,
        IIdentityService identityService,
        IConnectionTracker connectionTracker,
        IPushDeliveryService pushDeliveryService,
        IEventAggregator eventAggregator,
        ILogger<NotificationEventHandler> logger)
    {
        _notificationService = notificationService;
        _unreadTrackingService = unreadTrackingService;
        _feedsStorageService = feedsStorageService;
        _identityService = identityService;
        _connectionTracker = connectionTracker;
        _pushDeliveryService = pushDeliveryService;
        _logger = logger;

        // Subscribe to events via EventAggregator
        eventAggregator.Subscribe(this);

        _logger.LogInformation("NotificationEventHandler subscribed to NewFeedMessageCreatedEvent");
    }

    /// <summary>
    /// Handles new feed message events by notifying all feed participants except the sender.
    /// </summary>
    public async Task HandleAsync(NewFeedMessageCreatedEvent message)
    {
        var feedMessage = message.FeedMessage;

        _logger.LogDebug(
            "Handling new feed message: FeedId={FeedId}, MessageId={MessageId}, Issuer={Issuer}",
            feedMessage.FeedId,
            feedMessage.FeedMessageId,
            feedMessage.IssuerPublicAddress?.Substring(0, Math.Min(20, feedMessage.IssuerPublicAddress?.Length ?? 0)));

        try
        {
            // Get sender display name
            var senderName = await GetDisplayNameAsync(feedMessage.IssuerPublicAddress ?? string.Empty);

            // Truncate message preview
            var messagePreview = TruncateMessage(feedMessage.MessageContent, 255);

            // Try to get the feed as a regular Feed first (Personal, Chat)
            var feed = await _feedsStorageService.GetFeedByIdAsync(feedMessage.FeedId);
            if (feed != null)
            {
                // Regular feed (1:1 Chat) - notify all participants
                // feedName is null for 1:1 chats, so notification shows sender name
                await NotifyFeedParticipantsAsync(
                    feed.Participants.Select(p => p.ParticipantPublicAddress),
                    feedMessage,
                    senderName,
                    messagePreview,
                    feedName: null);
                return;
            }

            // Feed not found in Feeds table - try GroupFeeds table
            var groupFeed = await _feedsStorageService.GetGroupFeedAsync(feedMessage.FeedId);
            if (groupFeed != null)
            {
                // Group feed - notify active participants (not left, not banned)
                var activeParticipants = groupFeed.Participants
                    .Where(p => p.LeftAtBlock == null &&
                                p.ParticipantType != ParticipantType.Banned)
                    .Select(p => p.ParticipantPublicAddress);

                // Pass group title so notification shows "GroupName" + "Sender: New message"
                await NotifyFeedParticipantsAsync(
                    activeParticipants,
                    feedMessage,
                    senderName,
                    messagePreview,
                    feedName: groupFeed.Title);
                return;
            }

            _logger.LogWarning("Feed not found for notification: FeedId={FeedId}", feedMessage.FeedId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling feed message notification: FeedId={FeedId}", feedMessage.FeedId);
        }
    }

    /// <summary>
    /// Notifies all participants except the message sender.
    /// Routes to gRPC (online users) or Push (offline users) based on connection status.
    /// </summary>
    /// <param name="feedName">The feed/group name. Null for 1:1 chats.</param>
    private async Task NotifyFeedParticipantsAsync(
        IEnumerable<string> participantAddresses,
        FeedMessage feedMessage,
        string senderName,
        string messagePreview,
        string? feedName)
    {
        var feedId = feedMessage.FeedId.ToString();
        var senderAddress = feedMessage.IssuerPublicAddress ?? string.Empty;

        foreach (var participantAddress in participantAddresses)
        {
            // Skip the message sender - they don't need notification for their own message
            if (participantAddress == senderAddress)
            {
                continue;
            }

            // Increment unread count for recipient (always, regardless of delivery method)
            await _unreadTrackingService.IncrementUnreadAsync(participantAddress, feedId);

            // Route notification based on user's online status
            await RouteNotificationAsync(
                participantAddress,
                feedId,
                senderName,
                senderAddress,
                messagePreview,
                feedName);
        }
    }

    /// <summary>
    /// Routes a notification to the appropriate delivery channel based on user's online status.
    /// </summary>
    /// <param name="recipientAddress">The recipient's public signing address.</param>
    /// <param name="feedId">The feed ID where the message was sent.</param>
    /// <param name="senderName">The sender's display name.</param>
    /// <param name="senderAddress">The sender's public signing address.</param>
    /// <param name="messagePreview">The message preview (for gRPC: 255 chars).</param>
    /// <param name="feedName">The feed/group name. Null for 1:1 chats.</param>
    private async Task RouteNotificationAsync(
        string recipientAddress,
        string feedId,
        string senderName,
        string senderAddress,
        string messagePreview,
        string? feedName)
    {
        var userIdTruncated = recipientAddress.Substring(0, Math.Min(20, recipientAddress.Length));

        try
        {
            var isOnline = await _connectionTracker.IsUserOnlineAsync(recipientAddress);

            if (isOnline)
            {
                // User is online - deliver via gRPC (Redis Pub/Sub)
                _logger.LogInformation(
                    "User {UserId} is online, routing notification via gRPC for feed {FeedId}",
                    userIdTruncated,
                    feedId);

                await _notificationService.PublishNewMessageAsync(
                    recipientAddress,
                    feedId,
                    senderName,
                    messagePreview);
            }
            else
            {
                // User is offline - deliver via Push notification
                _logger.LogInformation(
                    "User {UserId} is offline, routing notification via push for feed {FeedId}",
                    userIdTruncated,
                    feedId);

                // Format notification differently for group vs 1:1 chats
                // Group: Title = "Group Name", Body = "Sender: New message"
                // 1:1:   Title = "Sender Name", Body = "New message"
                var isGroupMessage = !string.IsNullOrEmpty(feedName);
                var pushPayload = new PushPayload(
                    Title: isGroupMessage ? feedName! : senderName,
                    Body: isGroupMessage ? $"{senderName}: New message" : "New message",
                    FeedId: feedId,
                    Data: new Dictionary<string, string>
                    {
                        ["type"] = "new_message",
                        ["feedId"] = feedId,
                        ["senderId"] = senderAddress
                    });

                await _pushDeliveryService.SendPushAsync(recipientAddress, pushPayload);
            }

            _logger.LogDebug(
                "Notification delivered to {UserId} via {DeliveryMethod} for feed {FeedId}",
                userIdTruncated,
                isOnline ? "gRPC" : "push",
                feedId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to route notification to {UserId} for feed {FeedId}",
                userIdTruncated,
                feedId);
        }
    }

    private async Task<string> GetDisplayNameAsync(string publicSigningAddress)
    {
        if (string.IsNullOrEmpty(publicSigningAddress))
        {
            return "Unknown";
        }

        try
        {
            var identity = await _identityService.RetrieveIdentityAsync(publicSigningAddress);

            if (identity is Profile profile)
            {
                return profile.Alias;
            }

            // Fallback: use truncated public address
            return publicSigningAddress.Length > 10
                ? publicSigningAddress.Substring(0, 10) + "..."
                : publicSigningAddress;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get display name for {Address}", publicSigningAddress);
            return publicSigningAddress.Length > 10
                ? publicSigningAddress.Substring(0, 10) + "..."
                : publicSigningAddress;
        }
    }

    private static string TruncateMessage(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        if (content.Length <= maxLength) return content;
        return content.Substring(0, maxLength - 3) + "...";
    }
}
