using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushNode.Notifications;
using HushNode.PushNotifications;
using HushNode.PushNotifications.Models;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Feeds.gRPC;

public sealed class SocialPostNotificationService(
    IFeedsStorageService feedsStorageService,
    IIdentityService identityService,
    IConnectionTracker connectionTracker,
    INotificationService notificationService,
    IPushDeliveryService pushDeliveryService,
    ILogger<SocialPostNotificationService> logger) : ISocialPostNotificationService
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityService _identityService = identityService;
    private readonly IConnectionTracker _connectionTracker = connectionTracker;
    private readonly INotificationService _notificationService = notificationService;
    private readonly IPushDeliveryService _pushDeliveryService = pushDeliveryService;
    private readonly ILogger<SocialPostNotificationService> _logger = logger;

    public async Task NotifyPostCreatedAsync(
        string authorPublicAddress,
        string content,
        bool isPrivate,
        string postId,
        IReadOnlyCollection<string> authorizedPrivateViewers)
    {
        var recipients = isPrivate
            ? authorizedPrivateViewers.Where(address => address != authorPublicAddress).Distinct(StringComparer.Ordinal).ToArray()
            : await ResolveOpenPostRecipientsAsync(authorPublicAddress);

        if (recipients.Length == 0)
        {
            return;
        }

        var senderName = await ResolveSenderNameAsync(authorPublicAddress);
        var inAppPreview = isPrivate ? "New private post" : Truncate(content, 255);
        var pushBody = isPrivate ? "New private post" : Truncate(content, 120);

        foreach (var recipient in recipients)
        {
            try
            {
                var isOnline = await _connectionTracker.IsUserOnlineAsync(recipient);
                if (isOnline)
                {
                    await _notificationService.PublishNewMessageAsync(
                        recipient,
                        feedId: "social",
                        senderName,
                        inAppPreview,
                        feedName: "HushSocial");
                    continue;
                }

                var pushPayload = new PushPayload(
                    Title: senderName,
                    Body: pushBody,
                    FeedId: "social",
                    Data: new Dictionary<string, string>
                    {
                        ["type"] = "social_post",
                        ["postId"] = postId,
                        ["visibility"] = isPrivate ? "private" : "open"
                    });

                await _pushDeliveryService.SendPushAsync(recipient, pushPayload);
            }
            catch (Exception ex)
            {
                var recipientPreview = SafeRecipientPreview(recipient);
                _logger.LogWarning(
                    ex,
                    "Failed to deliver social post notification to {Recipient}",
                    recipientPreview);
            }
        }
    }

    private async Task<string[]> ResolveOpenPostRecipientsAsync(string authorPublicAddress)
    {
        var innerCircle = await _feedsStorageService.GetInnerCircleByOwnerAsync(authorPublicAddress);
        if (innerCircle == null)
        {
            return [];
        }

        var participants = await _feedsStorageService.GetActiveParticipantsAsync(innerCircle.FeedId);
        return participants
            .Select(participant => participant.ParticipantPublicAddress)
            .Where(address => address != authorPublicAddress)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<string> ResolveSenderNameAsync(string authorPublicAddress)
    {
        var profile = await _identityService.RetrieveIdentityAsync(authorPublicAddress);
        if (profile is Profile identity && !string.IsNullOrWhiteSpace(identity.Alias))
        {
            return identity.Alias;
        }

        return authorPublicAddress.Length > 10
            ? authorPublicAddress[..10] + "..."
            : authorPublicAddress;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 3)] + "...";
    }

    private static string SafeRecipientPreview(string? recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return "<empty>";
        }

        var trimmed = recipient.Trim();
        return trimmed.Length <= 20 ? trimmed : trimmed[..20];
    }
}
