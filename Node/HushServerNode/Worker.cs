using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HushNode.Notifications;
using Olimpo;

namespace HushServerNode;

public class Worker(
    IBootstrapperManager bootstrapperManager,
    NotificationEventHandler notificationEventHandler,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly IBootstrapperManager _bootstrapperManager = bootstrapperManager;
    // NotificationEventHandler is injected to ensure it's instantiated and subscribed to EventAggregator
    private readonly NotificationEventHandler _notificationEventHandler = notificationEventHandler;
    private readonly ILogger<Worker> _logger = logger;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this._logger.LogInformation("HushNetworkNode worker started...");
        this._bootstrapperManager.Start();

        return Task.CompletedTask;
    }
}