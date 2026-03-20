using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushNode.Notifications.Models;
using HushNode.PushNotifications;
using HushNode.PushNotifications.Models;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Notifications;

public sealed class SocialNotificationRoutingService(
    IFeedsStorageService feedsStorageService,
    IFeedMessageStorageService feedMessageStorageService,
    IIdentityService identityService,
    IConnectionTracker connectionTracker,
    INotificationService notificationService,
    IPushDeliveryService pushDeliveryService,
    ISocialNotificationStateService socialNotificationStateService,
    ILogger<SocialNotificationRoutingService> logger) : ISocialNotificationRoutingService
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IIdentityService _identityService = identityService;
    private readonly IConnectionTracker _connectionTracker = connectionTracker;
    private readonly INotificationService _notificationService = notificationService;
    private readonly IPushDeliveryService _pushDeliveryService = pushDeliveryService;
    private readonly ISocialNotificationStateService _socialNotificationStateService = socialNotificationStateService;
    private readonly ILogger<SocialNotificationRoutingService> _logger = logger;

    public async Task RoutePostCreatedAsync(Guid postId, CancellationToken cancellationToken = default)
    {
        var post = await _feedsStorageService.GetSocialPostAsync(postId);
        if (post == null)
        {
            return;
        }

        var actorDisplayName = await ResolveDisplayNameAsync(post.AuthorPublicAddress);
        var visibilityClass = MapVisibilityClass(post);

        if (post.AudienceVisibility == SocialPostVisibility.Open)
        {
            var innerCircle = await _feedsStorageService.GetInnerCircleByOwnerAsync(post.AuthorPublicAddress);
            if (innerCircle == null)
            {
                return;
            }

            var participants = await _feedsStorageService.GetActiveParticipantsAsync(innerCircle.FeedId);
            foreach (var participant in participants
                         .Select(x => x.ParticipantPublicAddress)
                         .Where(x => !string.Equals(x, post.AuthorPublicAddress, StringComparison.Ordinal))
                         .Distinct(StringComparer.Ordinal))
            {
                var followState = await _feedsStorageService.GetSocialFollowStateAsync(participant, post.AuthorPublicAddress);
                if (!followState.IsFollowing)
                {
                    continue;
                }

                await DeliverAsync(
                    post,
                    participant,
                    SocialNotificationKind.NewPost,
                    visibilityClass,
                    SocialNotificationTargetType.Post,
                    post.PostId.ToString("D"),
                    parentCommentId: string.Empty,
                    post.AuthorPublicAddress,
                    actorDisplayName,
                    openBody: BuildOpenPostBody(post.Content),
                    privateBody: "New private post",
                    matchedCircleIds: [],
                    cancellationToken);
            }

            return;
        }

        var recipients = await ResolvePrivateRecipientsAsync(post);
        foreach (var entry in recipients)
        {
            await DeliverAsync(
                post,
                entry.Key,
                SocialNotificationKind.NewPost,
                visibilityClass,
                SocialNotificationTargetType.Post,
                post.PostId.ToString("D"),
                parentCommentId: string.Empty,
                post.AuthorPublicAddress,
                actorDisplayName,
                openBody: BuildOpenPostBody(post.Content),
                privateBody: "New private post",
                entry.Value,
                cancellationToken);
        }
    }

    public async Task RouteThreadMessageCreatedAsync(FeedMessage feedMessage, CancellationToken cancellationToken = default)
    {
        var post = await _feedsStorageService.GetSocialPostAsync(feedMessage.FeedId.Value);
        if (post == null)
        {
            return;
        }

        var actorPublicAddress = feedMessage.IssuerPublicAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(actorPublicAddress))
        {
            return;
        }

        var actorDisplayName = await ResolveDisplayNameAsync(actorPublicAddress);
        var visibilityClass = MapVisibilityClass(post);

        if (feedMessage.ReplyToMessageId == null)
        {
            var commentRecipientCircleIds = await ResolveMatchedCircleIdsAsync(post, post.AuthorPublicAddress);
            await DeliverAsync(
                post,
                post.AuthorPublicAddress,
                SocialNotificationKind.Comment,
                visibilityClass,
                SocialNotificationTargetType.Comment,
                feedMessage.FeedMessageId.ToString(),
                parentCommentId: string.Empty,
                actorPublicAddress,
                actorDisplayName,
                openBody: "commented on your post",
                privateBody: "commented on your private post",
                commentRecipientCircleIds,
                cancellationToken);
            return;
        }

        var parentMessage = await _feedMessageStorageService.GetFeedMessageByIdAsync(feedMessage.ReplyToMessageId.Value);
        if (parentMessage == null || parentMessage.FeedId != post.AsFeedId())
        {
            var fallbackRecipientCircleIds = await ResolveMatchedCircleIdsAsync(post, post.AuthorPublicAddress);
            await DeliverAsync(
                post,
                post.AuthorPublicAddress,
                SocialNotificationKind.Reply,
                visibilityClass,
                SocialNotificationTargetType.Reply,
                feedMessage.FeedMessageId.ToString(),
                feedMessage.ReplyToMessageId.Value.ToString(),
                actorPublicAddress,
                actorDisplayName,
                openBody: "replied on your post",
                privateBody: "replied in your private post",
                fallbackRecipientCircleIds,
                cancellationToken);
            return;
        }

        var recipientMap = new Dictionary<string, SocialReplyRecipientContext>(StringComparer.Ordinal);
        var postOwnerCircleIds = await ResolveMatchedCircleIdsAsync(post, post.AuthorPublicAddress);
        recipientMap[post.AuthorPublicAddress] = new SocialReplyRecipientContext(
            "replied on your post",
            "replied in your private post",
            postOwnerCircleIds);

        var parentOwnerCircleIds = await ResolveMatchedCircleIdsAsync(post, parentMessage.IssuerPublicAddress);
        recipientMap[parentMessage.IssuerPublicAddress] = new SocialReplyRecipientContext(
            "replied to your comment",
            "replied to your private comment",
            parentOwnerCircleIds);

        foreach (var recipient in recipientMap)
        {
            await DeliverAsync(
                post,
                recipient.Key,
                SocialNotificationKind.Reply,
                visibilityClass,
                SocialNotificationTargetType.Reply,
                feedMessage.FeedMessageId.ToString(),
                feedMessage.ReplyToMessageId.Value.ToString(),
                actorPublicAddress,
                actorDisplayName,
                recipient.Value.OpenBody,
                recipient.Value.PrivateBody,
                recipient.Value.MatchedCircleIds,
                cancellationToken);
        }
    }

    public async Task RouteReactionCreatedAsync(
        string actorPublicAddress,
        FeedId feedId,
        FeedMessageId targetMessageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorPublicAddress))
        {
            return;
        }

        var actorDisplayName = await ResolveDisplayNameAsync(actorPublicAddress);

        var postTarget = await _feedsStorageService.GetSocialPostByReactionScopeIdAsync(targetMessageId.Value);
        if (postTarget != null)
        {
            await DeliverAsync(
                postTarget,
                postTarget.AuthorPublicAddress,
                SocialNotificationKind.Reaction,
                MapVisibilityClass(postTarget),
                SocialNotificationTargetType.Post,
                postTarget.PostId.ToString("D"),
                parentCommentId: string.Empty,
                actorPublicAddress,
                actorDisplayName,
                openBody: "reacted to your post",
                privateBody: "reacted to your private post",
                await ResolveMatchedCircleIdsAsync(postTarget, postTarget.AuthorPublicAddress),
                cancellationToken);
            return;
        }

        var socialPost = await _feedsStorageService.GetSocialPostAsync(feedId.Value);
        if (socialPost == null)
        {
            return;
        }

        var targetMessage = await _feedMessageStorageService.GetFeedMessageByIdAsync(targetMessageId);
        if (targetMessage == null || targetMessage.FeedId != feedId)
        {
            return;
        }

        var targetType = targetMessage.ReplyToMessageId == null
            ? SocialNotificationTargetType.Comment
            : SocialNotificationTargetType.Reply;
        var openBody = targetType == SocialNotificationTargetType.Comment
            ? "reacted to your comment"
            : "reacted to your reply";
        var privateBody = targetType == SocialNotificationTargetType.Comment
            ? "reacted to your private comment"
            : "reacted to your private reply";

        await DeliverAsync(
            socialPost,
            targetMessage.IssuerPublicAddress,
            SocialNotificationKind.Reaction,
            MapVisibilityClass(socialPost),
            targetType,
            targetMessageId.ToString(),
            parentCommentId: targetMessage.ReplyToMessageId?.ToString() ?? string.Empty,
            actorPublicAddress,
            actorDisplayName,
            openBody,
            privateBody,
            await ResolveMatchedCircleIdsAsync(socialPost, targetMessage.IssuerPublicAddress),
            cancellationToken);
    }

    private async Task DeliverAsync(
        SocialPostEntity post,
        string recipientPublicAddress,
        SocialNotificationKind kind,
        SocialNotificationVisibilityClass visibilityClass,
        SocialNotificationTargetType targetType,
        string targetId,
        string parentCommentId,
        string actorPublicAddress,
        string actorDisplayName,
        string openBody,
        string privateBody,
        IReadOnlyList<string> matchedCircleIds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipientPublicAddress) ||
            string.Equals(recipientPublicAddress, actorPublicAddress, StringComparison.Ordinal))
        {
            return;
        }

        var preferences = await _socialNotificationStateService.GetPreferencesAsync(recipientPublicAddress, cancellationToken);
        if (visibilityClass == SocialNotificationVisibilityClass.Open && !preferences.OpenActivityEnabled)
        {
            return;
        }

        if (visibilityClass == SocialNotificationVisibilityClass.Close)
        {
            if (!preferences.CloseActivityEnabled)
            {
                return;
            }

            if (!await HasCurrentPrivateAccessAsync(post, recipientPublicAddress))
            {
                return;
            }

            if (matchedCircleIds.Count > 0 && AreAllMatchedCirclesMuted(preferences, matchedCircleIds))
            {
                return;
            }
        }

        var body = visibilityClass == SocialNotificationVisibilityClass.Close ? privateBody : openBody;
        var item = new SocialNotificationItem
        {
            NotificationId = Guid.NewGuid().ToString("D"),
            RecipientUserId = recipientPublicAddress,
            Kind = kind,
            VisibilityClass = visibilityClass,
            TargetType = targetType,
            TargetId = targetId,
            PostId = post.PostId.ToString("D"),
            ParentCommentId = parentCommentId,
            ActorUserId = actorPublicAddress,
            ActorDisplayName = actorDisplayName,
            Title = actorDisplayName,
            Body = body,
            IsRead = false,
            IsPrivatePreviewSuppressed = visibilityClass == SocialNotificationVisibilityClass.Close,
            CreatedAtUtc = DateTime.UtcNow,
            DeepLinkPath = BuildDeepLink(post.PostId, targetId),
            MatchedCircleIds = matchedCircleIds.ToList()
        };

        await _socialNotificationStateService.StoreNotificationAsync(item, cancellationToken);

        try
        {
            var isOnline = await _connectionTracker.IsUserOnlineAsync(recipientPublicAddress);
            if (isOnline)
            {
                await _notificationService.PublishNewMessageAsync(
                    recipientPublicAddress,
                    "social",
                    actorDisplayName,
                    body,
                    "HushSocial");
                return;
            }

            await _pushDeliveryService.SendPushAsync(
                recipientPublicAddress,
                BuildPushPayload(item));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to deliver FEAT-091 social notification to {Recipient}",
                SafeRecipientPreview(recipientPublicAddress));
        }
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> ResolvePrivateRecipientsAsync(SocialPostEntity post)
    {
        var recipientMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var audienceCircle in post.AudienceCircles)
        {
            var participants = await _feedsStorageService.GetActiveParticipantsAsync(audienceCircle.CircleFeedId);
            foreach (var participant in participants)
            {
                var address = participant.ParticipantPublicAddress;
                if (string.Equals(address, post.AuthorPublicAddress, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!recipientMap.TryGetValue(address, out var circles))
                {
                    circles = new HashSet<string>(StringComparer.Ordinal);
                    recipientMap[address] = circles;
                }

                circles.Add(audienceCircle.CircleFeedId.ToString());
            }
        }

        return recipientMap.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<string>)x.Value.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
    }

    private async Task<IReadOnlyList<string>> ResolveMatchedCircleIdsAsync(SocialPostEntity post, string recipientPublicAddress)
    {
        if (post.AudienceVisibility != SocialPostVisibility.Private)
        {
            return [];
        }

        if (string.Equals(post.AuthorPublicAddress, recipientPublicAddress, StringComparison.Ordinal))
        {
            return post.AudienceCircles
                .Select(x => x.CircleFeedId.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
        }

        var matched = new List<string>();
        foreach (var circle in post.AudienceCircles)
        {
            var participants = await _feedsStorageService.GetActiveParticipantsAsync(circle.CircleFeedId);
            if (participants.Any(x => string.Equals(x.ParticipantPublicAddress, recipientPublicAddress, StringComparison.Ordinal)))
            {
                matched.Add(circle.CircleFeedId.ToString());
            }
        }

        return matched
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<bool> HasCurrentPrivateAccessAsync(SocialPostEntity post, string recipientPublicAddress)
    {
        if (post.AudienceVisibility != SocialPostVisibility.Private)
        {
            return true;
        }

        if (string.Equals(post.AuthorPublicAddress, recipientPublicAddress, StringComparison.Ordinal))
        {
            return true;
        }

        var matchedCircleIds = await ResolveMatchedCircleIdsAsync(post, recipientPublicAddress);
        return matchedCircleIds.Count > 0;
    }

    private async Task<string> ResolveDisplayNameAsync(string publicAddress)
    {
        var profile = await _identityService.RetrieveIdentityAsync(publicAddress);
        if (profile is Profile identity && !string.IsNullOrWhiteSpace(identity.Alias))
        {
            return identity.Alias;
        }

        return publicAddress.Length > 10
            ? publicAddress[..10] + "..."
            : publicAddress;
    }

    private static SocialNotificationVisibilityClass MapVisibilityClass(SocialPostEntity post) =>
        post.AudienceVisibility == SocialPostVisibility.Private
            ? SocialNotificationVisibilityClass.Close
            : SocialNotificationVisibilityClass.Open;

    private static bool AreAllMatchedCirclesMuted(
        SocialNotificationPreferences preferences,
        IReadOnlyList<string> matchedCircleIds)
    {
        if (matchedCircleIds.Count == 0)
        {
            return false;
        }

        var muted = preferences.CircleMutes
            .Where(x => x.IsMuted)
            .Select(x => x.CircleId)
            .ToHashSet(StringComparer.Ordinal);

        return matchedCircleIds.All(muted.Contains);
    }

    private static string BuildOpenPostBody(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "shared a new post";
        }

        return content.Length <= 120 ? content : content[..117] + "...";
    }

    private static string BuildDeepLink(Guid postId, string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId) || string.Equals(targetId, postId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            return $"/social/post/{postId:D}";
        }

        return $"/social/post/{postId:D}?focus={targetId}";
    }

    private static PushPayload BuildPushPayload(SocialNotificationItem item)
    {
        return new PushPayload(
            Title: item.ActorDisplayName,
            Body: item.Body,
            FeedId: "social",
            Data: new Dictionary<string, string>
            {
                ["type"] = "social_notification",
                ["kind"] = item.Kind.ToString().ToLowerInvariant(),
                ["postId"] = item.PostId,
                ["targetId"] = item.TargetId,
                ["visibility"] = item.VisibilityClass == SocialNotificationVisibilityClass.Close ? "private" : "open"
            });
    }

    private static string SafeRecipientPreview(string recipient)
    {
        var trimmed = recipient.Trim();
        return trimmed.Length <= 20 ? trimmed : trimmed[..20];
    }

    private sealed record SocialReplyRecipientContext(
        string OpenBody,
        string PrivateBody,
        IReadOnlyList<string> MatchedCircleIds);
}

internal static class SocialNotificationRoutingExtensions
{
    public static FeedId AsFeedId(this SocialPostEntity post) => new(post.PostId);
}
