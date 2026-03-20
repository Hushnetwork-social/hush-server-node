namespace HushNode.Notifications.Models;

public sealed class SocialNotificationPreferenceUpdate
{
    public bool? OpenActivityEnabled { get; init; }

    public bool? CloseActivityEnabled { get; init; }

    public IReadOnlyList<SocialCircleMuteState>? CircleMutes { get; init; }
}
