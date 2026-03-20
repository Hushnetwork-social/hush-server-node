namespace HushNode.Feeds.gRPC;

public interface ISocialPostNotificationService
{
    Task NotifyPostCreatedAsync(Guid postId);
}
