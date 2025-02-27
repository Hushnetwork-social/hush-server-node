using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushServerNode;

public class Worker(
    IBootstrapperManager bootstrapperManager,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly IBootstrapperManager _bootstrapperManager = bootstrapperManager;
    private readonly ILogger<Worker> _logger = logger;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this._logger.LogInformation("HushNetworkNode worker started...");
        this._bootstrapperManager.Start();

        return Task.CompletedTask;
    }
}