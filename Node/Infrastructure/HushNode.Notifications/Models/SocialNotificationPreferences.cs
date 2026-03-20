namespace HushNode.Notifications.Models;

public sealed class SocialNotificationPreferences
{
    public bool OpenActivityEnabled { get; set; } = true;

    public bool CloseActivityEnabled { get; set; } = true;

    public List<SocialCircleMuteState> CircleMutes { get; set; } = [];

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
