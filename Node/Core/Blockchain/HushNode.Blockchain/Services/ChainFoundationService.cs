using Microsoft.Extensions.Logging;
using Olimpo;
using HushNode.Blockchain.Workflows;
using HushNode.Events;
using HushNode.Blockchain.Model;
using HushNode.Blockchain.Storage;

namespace HushNode.Blockchain.Services;

public class ChainFoundationService(
    IBlockAssemblerWorkflow blockAssemblerWorkflow,
    IBlockchainStorageService blockchainStorageService,
    IEventAggregator eventAggregator,
    ILogger<ChainFoundationService> logger) : IChainFoundationService
{
    private readonly ILogger<ChainFoundationService> _logger = logger;
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow = blockAssemblerWorkflow;
    private readonly IBlockchainStorageService _blockchainStorageService = blockchainStorageService;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task InitializeChain()
    {
        this._logger.LogInformation("Initializion Blockchain...");

        var blockchainState = await this._blockchainStorageService.RetrieveCurrentBlockchainStateAsync();

        Func<BlockchainState, Task> handler = blockchainState switch
        {
            GenesisBlockchainState => GenerateGenesisBlock,
            _ => BlockchainInitializationFinished
        };
        
        await handler.Invoke(blockchainState);
    }

    private async Task BlockchainInitializationFinished(BlockchainState blockchainState)
    {
        this._logger.LogInformation("Blockchain initialization finished...");
        await this._eventAggregator.PublishAsync(new BlockchainInitializedEvent());
    }

    private async Task GenerateGenesisBlock(BlockchainState genesisBlockchainState)
    {
        this._logger.LogInformation("Generating Genesis Block...");
        await this._blockAssemblerWorkflow.AssembleGenesisBlockAsync(genesisBlockchainState);

        this._logger.LogInformation("Blockchain initialization finished...");
        await this._eventAggregator.PublishAsync(new BlockchainInitializedEvent());
    }
}
