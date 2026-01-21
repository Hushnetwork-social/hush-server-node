using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HushNode.Caching;
using HushNode.Notifications;
using Olimpo;

namespace HushServerNode;

public class Worker(
    IBootstrapperManager bootstrapperManager,
    NotificationEventHandler notificationEventHandler,
    FeedParticipantsCacheEventHandler feedParticipantsCacheEventHandler,
    ILogger<Worker> logger) : IHostedService
{
    private readonly IBootstrapperManager _bootstrapperManager = bootstrapperManager;
    // NotificationEventHandler is injected to ensure it's instantiated and subscribed to EventAggregator
    private readonly NotificationEventHandler _notificationEventHandler = notificationEventHandler;
    // FeedParticipantsCacheEventHandler is injected to ensure it's instantiated and subscribed to EventAggregator (FEAT-050)
    private readonly FeedParticipantsCacheEventHandler _feedParticipantsCacheEventHandler = feedParticipantsCacheEventHandler;
    private readonly ILogger<Worker> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        this._logger.LogInformation("HushNetworkNode worker started...");
        await this._bootstrapperManager.Start();
        this._logger.LogInformation("All bootstrappers completed.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        this._logger.LogInformation("HushNetworkNode worker stopping...");
        return Task.CompletedTask;
    }
}
