using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushServerNode;

public class Worker : BackgroundService
{
    private readonly IBootstrapperManager _bootstrapperManager;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IBootstrapperManager bootstrapperManager,
        ILogger<Worker> logger)
    {
        this._bootstrapperManager = bootstrapperManager;
        this._logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this._logger.LogInformation("HushNetworkNode worker started...");
        this._bootstrapperManager.Start();

        return Task.CompletedTask;
    }
}