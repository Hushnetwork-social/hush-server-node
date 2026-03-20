namespace HushNode.Notifications.Models;

public sealed class SocialNotificationItem
{
    public string NotificationId { get; set; } = string.Empty;

    public string RecipientUserId { get; set; } = string.Empty;

    public SocialNotificationKind Kind { get; set; }

    public SocialNotificationVisibilityClass VisibilityClass { get; set; }

    public SocialNotificationTargetType TargetType { get; set; }

    public string TargetId { get; set; } = string.Empty;

    public string PostId { get; set; } = string.Empty;

    public string ParentCommentId { get; set; } = string.Empty;

    public string ActorUserId { get; set; } = string.Empty;

    public string ActorDisplayName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    public bool IsPrivatePreviewSuppressed { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string DeepLinkPath { get; set; } = string.Empty;

    public List<string> MatchedCircleIds { get; set; } = [];
}
