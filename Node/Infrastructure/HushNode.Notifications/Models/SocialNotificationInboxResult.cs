namespace HushNode.Notifications.Models;

public sealed class SocialNotificationInboxResult
{
    public IReadOnlyList<SocialNotificationItem> Items { get; init; } = [];

    public bool HasMore { get; init; }
}
