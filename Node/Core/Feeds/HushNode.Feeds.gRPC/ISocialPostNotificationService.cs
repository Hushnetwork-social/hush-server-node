namespace HushNode.Feeds.gRPC;

public interface ISocialPostNotificationService
{
    Task NotifyPostCreatedAsync(
        string authorPublicAddress,
        string content,
        bool isPrivate,
        string postId,
        IReadOnlyCollection<string> authorizedPrivateViewers);
}
