using Microsoft.Extensions.Logging;
using Olimpo;
using Olimpo.EntityFramework.Persistency;
using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Workflows;
using HushNode.Blockchain.Persistency.EntityFramework;
using HushNode.Blockchain.Persistency.Abstractions.Repositories;
using HushNode.Events;

namespace HushNode.Blockchain.Services;

public class ChainFoundationService(
    IBlockAssemblerWorkflow blockAssemblerWorkflow,
    IUnitOfWorkProvider<BlockchainDbContext> unitOfWorkProvider,
    IEventAggregator eventAggregator,
    ILogger<ChainFoundationService> logger) : IChainFoundationService
{
    private readonly ILogger<ChainFoundationService> _logger = logger;
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow = blockAssemblerWorkflow;
    private readonly IUnitOfWorkProvider<BlockchainDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IEventAggregator _eventAggregator = eventAggregator;

    public async Task InitializeChain()
    {
        this._logger.LogInformation("Initializion Blockchain...");

        using var unitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        var blockchainState = await unitOfWork
            .GetRepository<IBlockchainStateRepository>()
            .GetCurrentStateAsync();

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
