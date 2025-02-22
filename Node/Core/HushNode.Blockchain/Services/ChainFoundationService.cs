using Microsoft.Extensions.Logging;
using Olimpo;
using HushNode.Blockchain.Events;
using HushNode.Blockchain.Persistency.Abstractions;
using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Workflows;

namespace HushNode.Blockchain.Services;

public class ChainFoundationService : IChainFoundationService
{
    private readonly ILogger<ChainFoundationService> _logger;
    private readonly IBlockAssemblerWorkflow _blockAssemblerWorkflow;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IEventAggregator _eventAggregator;
    private readonly IUnitOfWork _unitOfWork;

    public ChainFoundationService(
        IBlockAssemblerWorkflow blockAssemblerWorkflow,
        IUnitOfWorkFactory unitOfWorkFactory,
        IEventAggregator eventAggregator,
        ILogger<ChainFoundationService> logger)
    {
        this._blockAssemblerWorkflow = blockAssemblerWorkflow;
        this._unitOfWorkFactory = unitOfWorkFactory;
        this._eventAggregator = eventAggregator;
        this._logger = logger;

        this._unitOfWork = this._unitOfWorkFactory.Create();
    }

    public async Task InitializeChain()
    {
        this._logger.LogInformation("Initializion Blockchain...");

        var blockchainState = await this._unitOfWork.BlockStateRepository.GetCurrentStateAsync();

        Func<BlockchainState, Task> handler = blockchainState switch
        {
            GenesisBlockchainState => GenerateGenesisBlock,
            _ => BlockchainInitializationFinished
        };
        
        await handler.Invoke(blockchainState);
    }

    private async Task BlockchainInitializationFinished(BlockchainState blockchainState)
    {
        await this._eventAggregator.PublishAsync(new BlockchainInitializedEvent());
    }

    private async Task GenerateGenesisBlock(BlockchainState genesisBlockchainState)
    {
        await this._blockAssemblerWorkflow.AssembleGenesisBlockAsync(genesisBlockchainState);
        await this._eventAggregator.PublishAsync(new BlockchainInitializedEvent());
    }
}
