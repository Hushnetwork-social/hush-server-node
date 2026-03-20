namespace HushNode.Notifications.Models;

public sealed class SocialCircleMuteState
{
    public string CircleId { get; set; } = string.Empty;

    public bool IsMuted { get; set; }
}
