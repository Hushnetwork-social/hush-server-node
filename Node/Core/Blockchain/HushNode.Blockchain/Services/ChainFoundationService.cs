using Microsoft.Extensions.Logging;
using Olimpo;
using HushNode.Blockchain.Workflows;
using HushNode.Events;
using HushNode.Blockchain.Storage;
using HushNode.Blockchain.Storage.Model;
using HushShared.Caching;

namespace HushNode.Blockchain.Services;

public class ChainFoundationService(
    IBlockAssemblerWorkflow blockAssemblerWorkflow,
    IBlockchainStorageService blockchainStorageService,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator,
    ILogger<ChainFoundationService> logger) : IChainFoundationService
{
    private readonly ILogger<ChainFoundationService> _logger = logger;
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow = blockAssemblerWorkflow;
    private readonly IBlockchainStorageService _blockchainStorageService = blockchainStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task InitializeChain()
    {
        this._logger.LogInformation("Initializion Blockchain...");

        var blockchainState = await this._blockchainStorageService.RetrieveCurrentBlockchainStateAsync();

        Func<Task> handler = blockchainState switch
        {
            GenesisBlockchainState => GenerateGenesisBlock,
            _ => BlockchainInitializationFinished
        };
        
        this._blockchainCache
            .SetPreviousBlockId(blockchainState.PreviousBlockId)
            .SetCurrentBlockId(blockchainState.CurrentBlockId)
            .SetNextBlockId(blockchainState.NextBlockId)
            .SetBlockIndex(blockchainState.BlockIndex);

        await handler.Invoke();
    }

    private async Task BlockchainInitializationFinished()
    {
        this._logger.LogInformation("Blockchain initialization finished...");
        await this._eventAggregator.PublishAsync(new BlockchainInitializedEvent());
    }

    private async Task GenerateGenesisBlock()
    {
        this._logger.LogInformation("Generating Genesis Block...");
        await this._blockAssemblerWorkflow.AssembleGenesisBlockAsync();

        this._logger.LogInformation("Blockchain initialization finished...");
        await this._eventAggregator.PublishAsync(new BlockchainInitializedEvent());
    }
}
