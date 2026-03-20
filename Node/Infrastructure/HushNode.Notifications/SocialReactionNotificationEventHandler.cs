using HushNode.Events;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Notifications;

public sealed class SocialReactionNotificationEventHandler : IHandleAsync<SocialReactionCreatedEvent>
{
    private readonly ISocialNotificationRoutingService _socialNotificationRoutingService;

    public SocialReactionNotificationEventHandler(
        ISocialNotificationRoutingService socialNotificationRoutingService,
        IEventAggregator eventAggregator,
        ILogger<SocialReactionNotificationEventHandler> logger)
    {
        _socialNotificationRoutingService = socialNotificationRoutingService;

        eventAggregator.Subscribe(this);
        logger.LogInformation("SocialReactionNotificationEventHandler subscribed to SocialReactionCreatedEvent");
    }

    public async Task HandleAsync(SocialReactionCreatedEvent message)
    {
        await _socialNotificationRoutingService.RouteReactionCreatedAsync(
            message.ActorPublicAddress,
            message.FeedId,
            message.TargetMessageId);
    }
}
