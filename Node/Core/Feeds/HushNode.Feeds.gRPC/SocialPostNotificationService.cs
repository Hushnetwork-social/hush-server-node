using HushNode.Notifications;

namespace HushNode.Feeds.gRPC;

public sealed class SocialPostNotificationService(
    ISocialNotificationRoutingService socialNotificationRoutingService) : ISocialPostNotificationService
{
    private readonly ISocialNotificationRoutingService _socialNotificationRoutingService = socialNotificationRoutingService;

    public Task NotifyPostCreatedAsync(Guid postId) =>
        _socialNotificationRoutingService.RoutePostCreatedAsync(postId);
}
