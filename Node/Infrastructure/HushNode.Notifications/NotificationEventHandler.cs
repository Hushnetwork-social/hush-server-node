using HushNode.Events;
using HushNode.Feeds.Storage;
using HushNode.Identity;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Notifications;

/// <summary>
/// Handles feed message events and triggers notifications via Redis Pub/Sub.
/// Subscribes to NewFeedMessageCreatedEvent using the EventAggregator pattern.
/// </summary>
public class NotificationEventHandler : IHandleAsync<NewFeedMessageCreatedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IUnreadTrackingService _unreadTrackingService;
    private readonly IFeedsStorageService _feedsStorageService;
    private readonly IIdentityService _identityService;
    private readonly ILogger<NotificationEventHandler> _logger;

    public NotificationEventHandler(
        INotificationService notificationService,
        IUnreadTrackingService unreadTrackingService,
        IFeedsStorageService feedsStorageService,
        IIdentityService identityService,
        IEventAggregator eventAggregator,
        ILogger<NotificationEventHandler> logger)
    {
        _notificationService = notificationService;
        _unreadTrackingService = unreadTrackingService;
        _feedsStorageService = feedsStorageService;
        _identityService = identityService;
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
                // Regular feed - notify all participants
                await NotifyFeedParticipantsAsync(
                    feed.Participants.Select(p => p.ParticipantPublicAddress),
                    feedMessage,
                    senderName,
                    messagePreview);
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

                await NotifyFeedParticipantsAsync(
                    activeParticipants,
                    feedMessage,
                    senderName,
                    messagePreview);
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
    /// </summary>
    private async Task NotifyFeedParticipantsAsync(
        IEnumerable<string> participantAddresses,
        FeedMessage feedMessage,
        string senderName,
        string messagePreview)
    {
        foreach (var participantAddress in participantAddresses)
        {
            // Skip the message sender - they don't need notification for their own message
            if (participantAddress == feedMessage.IssuerPublicAddress)
            {
                continue;
            }

            // Increment unread count for recipient
            await _unreadTrackingService.IncrementUnreadAsync(participantAddress, feedMessage.FeedId.ToString());

            // Publish notification event via Redis Pub/Sub
            await _notificationService.PublishNewMessageAsync(
                participantAddress,
                feedMessage.FeedId.ToString(),
                senderName,
                messagePreview);

            _logger.LogDebug(
                "Notification sent to recipient: UserId={UserId}, FeedId={FeedId}",
                participantAddress.Substring(0, Math.Min(20, participantAddress.Length)),
                feedMessage.FeedId);
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
