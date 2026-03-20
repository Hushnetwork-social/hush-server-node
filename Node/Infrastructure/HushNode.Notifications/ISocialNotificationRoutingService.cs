using HushShared.Feeds.Model;

namespace HushNode.Notifications;

public interface ISocialNotificationRoutingService
{
    Task RoutePostCreatedAsync(Guid postId, CancellationToken cancellationToken = default);

    Task RouteThreadMessageCreatedAsync(FeedMessage feedMessage, CancellationToken cancellationToken = default);

    Task RouteReactionCreatedAsync(
        string actorPublicAddress,
        FeedId feedId,
        FeedMessageId targetMessageId,
        CancellationToken cancellationToken = default);
}
