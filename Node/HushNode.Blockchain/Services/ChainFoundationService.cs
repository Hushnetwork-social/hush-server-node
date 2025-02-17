using Microsoft.Extensions.Logging;

namespace HushNode.Blockchain.Services;

public class ChainFoundationService(ILogger<ChainFoundationService> logger) : IChainFoundationService
{
    private readonly ILogger<ChainFoundationService> _logger = logger;

    public Task InitializeChain()
    {
        this._logger.LogInformation("Initializion Blockchain...");

        return Task.CompletedTask;
    }
}
